using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

// FFmpeg opens every rendition in a master before selecting tracks. Restrict the
// master to one reasonable rendition while retaining its matching media groups.
static class HlsPlaybackSource
{
    static readonly HttpClient http=new(new HttpClientHandler{AutomaticDecompression=DecompressionMethods.All}){Timeout=TimeSpan.FromSeconds(4)};
    static readonly Regex attributes=new("(?:^|,)([A-Z0-9-]+)=(\"[^\"]*\"|[^,]*)",RegexOptions.CultureInvariant);
    static readonly Regex uris=new("(?<=URI=\")[^\"]+",RegexOptions.CultureInvariant);
    public static bool Candidate(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var u)&&u.Scheme is "http" or "https"&&u.AbsolutePath.EndsWith(".m3u8",StringComparison.OrdinalIgnoreCase);
    public static async Task<string> Prepare(string url,string userAgent,string referrer,CancellationToken token,string qualityMode="Stabilność")
    {
        if(!Candidate(url))return url;
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var request=new HttpRequestMessage(HttpMethod.Get,url);
            request.Headers.TryAddWithoutValidation("User-Agent",userAgent);
            if(Uri.TryCreate(referrer,UriKind.Absolute,out var refer))request.Headers.Referrer=refer;
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode||response.Content.Headers.ContentLength>2*1024*1024)return url;
            await using var body=await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var bytes=new MemoryStream();var buffer=new byte[16384];int count;
            while((count=await body.ReadAsync(buffer,timeout.Token).ConfigureAwait(false))>0){if(bytes.Length+count>2*1024*1024)return url;bytes.Write(buffer,0,count);}
            string text=Encoding.UTF8.GetString(bytes.ToArray());
            return Reduce(text,response.RequestMessage?.RequestUri??new Uri(url),out string reduced,qualityMode)?"memory://"+reduced:url;
        }
        catch(Exception ex)when(ex is HttpRequestException or IOException or OperationCanceledException){token.ThrowIfCancellationRequested();return url;}
    }
    public static bool Reduce(string text,Uri origin,out string reduced,string qualityMode="Stabilność")
    {
        reduced="";try{return ReduceCore(text,origin,out reduced,qualityMode);}catch(ArgumentException){return false;}
    }
    static bool ReduceCore(string text,Uri origin,out string reduced,string qualityMode)
    {
        reduced="";if(!text.TrimStart('\uFEFF',' ','\r','\n').StartsWith("#EXTM3U",StringComparison.Ordinal)||text.Contains("#EXT-X-DEFINE")||text.Contains("#EXT-X-CONTENT-STEERING"))return false;
        string[] lines=text.Replace("\r","").Split('\n').Select(x=>x.Trim()).ToArray();
        var variants=new List<(string Info,string Url,Dictionary<string,string> Attributes,int Height,long Rate)>();
        for(int i=0;i<lines.Length;i++)
        {
            if(!lines[i].StartsWith("#EXT-X-STREAM-INF:",StringComparison.Ordinal))continue;
            var a=Attrs(lines[i]);int next=i+1;while(next<lines.Length&&lines[next].Length==0)next++;
            if(next>=lines.Length||lines[next].StartsWith('#')||!Web(origin,lines[next],out string child))return false;
            int height=0;var res=a.GetValueOrDefault("RESOLUTION","").Split('x');if(res.Length==2)int.TryParse(res[1],out height);
            long.TryParse(a.GetValueOrDefault("AVERAGE-BANDWIDTH",a.GetValueOrDefault("BANDWIDTH","0")),out long rate);
            variants.Add((lines[i],child,a,height,rate));
        }
        if(variants.Count==0)return false;
        var video=variants.Where(v=>v.Height>0).ToList();
        if(video.Count==0)video=variants.Where(v=>!v.Attributes.ContainsKey("CODECS")||Regex.IsMatch(v.Attributes["CODECS"],@"(?:avc|hev|hvc|vp0?9|av01|dvhe|dvh1|mp4v|theora)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)).ToList();
        if(video.Count==0)video=variants; // Audio-only HLS radio remains playable.
        var fitting=video.Where(v=>(v.Height==0||v.Height<=720)&&(v.Rate==0||v.Rate<=3500000)).ToList();
        var chosen=fitting.Count>0?fitting.OrderByDescending(v=>v.Height).ThenByDescending(v=>v.Rate).First():video.OrderBy(v=>v.Rate==0?long.MaxValue:v.Rate).ThenBy(v=>v.Height).First();
        if(qualityMode=="Najwyższa jakość")chosen=video.OrderByDescending(v=>v.Height).ThenByDescending(v=>v.Rate).First();
        else if(qualityMode=="Oszczędzaj transfer")
        {
            var reasonable=video.Where(v=>v.Height==0||v.Height>=240).ToList();if(reasonable.Count==0)reasonable=video;
            chosen=reasonable.OrderBy(v=>v.Rate==0?long.MaxValue:v.Rate).ThenBy(v=>v.Height).First();
        }
        var result=new List<string>{"#EXTM3U"};
        foreach(string line in lines)
        {
            if(line.StartsWith("#EXT-X-VERSION:")||line=="#EXT-X-INDEPENDENT-SEGMENTS"||line.StartsWith("#EXT-X-SESSION-KEY:")||line.StartsWith("#EXT-X-SESSION-DATA:")||line.StartsWith("#EXT-X-START:"))
            {if(!RewriteUris(line,origin,out var rewritten))return false;result.Add(rewritten);}
        }
        var media=lines.Where(l=>l.StartsWith("#EXT-X-MEDIA:")).Select(l=>(Line:l,Attributes:Attrs(l))).ToList();
        foreach(string type in new[]{"AUDIO","VIDEO","SUBTITLES","CLOSED-CAPTIONS"})
        {
            if(!chosen.Attributes.TryGetValue(type,out string? group)||group=="NONE")continue;
            var matching=media.Where(m=>m.Attributes.GetValueOrDefault("TYPE")==type&&m.Attributes.GetValueOrDefault("GROUP-ID")==group).ToList();
            if(matching.Count==0)return false;
            // Default audio preserves the broadcaster's language/accessibility choice.
            if(type is "AUDIO" or "VIDEO")matching=matching.OrderByDescending(m=>m.Attributes.GetValueOrDefault("DEFAULT")=="YES").ThenByDescending(m=>m.Attributes.GetValueOrDefault("AUTOSELECT")=="YES").Take(1).ToList();
            foreach(var m in matching){if(!RewriteUris(m.Line,origin,out var rewritten))return false;result.Add(rewritten);}
        }
        result.Add(chosen.Info);result.Add(chosen.Url);reduced=string.Join('\n',result)+"\n";return true;
    }
    static Dictionary<string,string> Attrs(string line)=>attributes.Matches(line[(line.IndexOf(':')+1)..]).ToDictionary(m=>m.Groups[1].Value,m=>m.Groups[2].Value.Trim('"'),StringComparer.Ordinal);
    static bool Web(Uri origin,string value,out string url){url="";if(!Uri.TryCreate(origin,value,out var u)||u.Scheme is not("http" or "https")||u.UserInfo.Length>0)return false;url=u.AbsoluteUri;return true;}
    static bool RewriteUris(string line,Uri origin,out string rewritten)
    {
        bool valid=true;rewritten=uris.Replace(line,m=>{if(Web(origin,m.Value,out string value))return value;valid=false;return m.Value;});return valid;
    }
}
