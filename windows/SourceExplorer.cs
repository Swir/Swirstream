using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
namespace SwirStreamAI;

public enum SourceCandidateKind { PortalCandidate, PublicPlaylist, DirectStream, ProviderPage }
public record SourceCandidate(string Id,SourceCandidateKind Kind,string Url,string Title,string EvidenceUrl,
    DateTimeOffset CheckedAt,DateTimeOffset? ModifiedAt,string EvidenceSha256,string Status,string Country="",
    int ChannelCount=0,int MatchingChannels=0,bool RequiresDrm=false)
{
    public bool Stale=>DateTimeOffset.UtcNow-CheckedAt>TimeSpan.FromDays(7);
    public string KindLabel=>Kind switch{SourceCandidateKind.PortalCandidate=>"Portal MAC",SourceCandidateKind.PublicPlaylist=>"Playlista M3U",SourceCandidateKind.DirectStream=>"Adres transmisji",_=>"Strona źródłowa"};
}
public record SourceExplorerResult(List<SourceCandidate> Candidates,List<FoundPlaylist> Playlists,List<WebEvidence> Evidence,bool Complete);

public static class SourceExplorerRules
{
    static readonly Regex Mac=new(@"\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b",RegexOptions.IgnoreCase|RegexOptions.Compiled);
    static readonly Regex Credentials=new(@"\b(?:password|passwd|hasło|haslo|authorization|cookie|bearer|username|login|mac|debug_key|access_token)[\""']?\s*[:=]\s*[\""']?[^\s<>\""']+",RegexOptions.IgnoreCase|RegexOptions.Compiled);
    static readonly Regex BackendPath=new(@"(?:^|/)(?:server|api|admin|adm|login|signin)(?:/|$)|/(?:portal|load|player_api|xmltv|get)\.php(?:/|$)",RegexOptions.IgnoreCase|RegexOptions.Compiled);
    static readonly Regex ApiQuery=new(@"(?:^|[&;])(?:action|type|jshhttprequest|jshttprequest|mac|debug|debug_key|username|user|u|password|passwd|pass|p|login|authorization|session|sessionid|access_token|api_key)(?:=|&|$)",RegexOptions.IgnoreCase|RegexOptions.Compiled);
    static readonly Regex ClientPath=new(@"/c(?:/index\.html?)?/?$",RegexOptions.IgnoreCase|RegexOptions.Compiled);
    static readonly Regex Inert=new(@"<!--[\s\S]*?-->|<(pre|code|textarea|template|noscript)\b[^>]*>[\s\S]*?</\1\s*>",RegexOptions.IgnoreCase|RegexOptions.Compiled,TimeSpan.FromSeconds(1));
    static readonly Regex Script=new("<script\\b[^>]{0,8192}\\bsrc\\s*=\\s*[\"']([^\"']+)[\"'][^>]*>",RegexOptions.IgnoreCase|RegexOptions.Compiled,TimeSpan.FromSeconds(1));
    public static string Decode(string value){for(int i=0;i<2;i++)value=Uri.UnescapeDataString(WebUtility.HtmlDecode(value));return value;}
    public static bool SensitiveText(string text)=>Mac.IsMatch(Decode(text))||Credentials.IsMatch(Decode(text))||PublicPlaylistPrivacy.ContainsCredentials(text);
    public static bool AllowedUrl(string url)
    {
        if(string.IsNullOrEmpty(url)||Regex.IsMatch(url,@"%(?![0-9a-f]{2})",RegexOptions.IgnoreCase)||!PublicWeb.Allowed(url)||PublicPlaylistPrivacy.ContainsCredentials(url))return false;
        var u=new Uri(url);if(u.Fragment.Length>0||!u.Host.Contains('.')||new[]{".local",".localhost",".internal",".lan",".home"}.Any(x=>u.Host.EndsWith(x,StringComparison.OrdinalIgnoreCase)))return false;
        string decoded=Decode(url);for(int i=0;i<2;i++)decoded=Uri.UnescapeDataString(decoded);
        if(decoded.Any(char.IsControl)||Mac.IsMatch(decoded))return false;
        var decodedPath=Decode(u.AbsolutePath);
        string query=Decode(u.Query.TrimStart('?'));
        bool signed=Regex.IsMatch(query,@"(?:^|[&;])(?:token|auth|key|signature|sig)=",RegexOptions.IgnoreCase);
        return !BackendPath.IsMatch(decodedPath)&&!ApiQuery.IsMatch(query)&&(!signed||MediaPath(url)||PlaylistPath(url));
    }
    public static bool SafeManualPage(string url)
    {
        if(!AllowedUrl(url))return false;
        // An unexplained private token belongs in own-source import, not page analysis.
        return !Regex.IsMatch(Uri.UnescapeDataString(new Uri(url).Query),@"[?&](?:token|auth|key|signature|sig)=",RegexOptions.IgnoreCase);
    }
    public static string Canonical(string url)=>new Uri(url).AbsoluteUri;
    public static string Digest(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static string Id(string url)=>Digest(Canonical(url));
    public static string ActiveHtml(string html)=>Inert.Replace(html,"");
    public static string VisibleText(string html)=>Decode(Regex.Replace(Regex.Replace(html,@"<(script|style)\b[^>]*>[\s\S]*?</\1\s*>"," ",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1)),"<[^>]*>"," ",RegexOptions.None,TimeSpan.FromSeconds(1)));
    public static bool PortalClient(WebDocument doc)
    {
        if(!AllowedUrl(doc.Url)||!ClientPath.IsMatch(new Uri(doc.Url).AbsolutePath))return false;
        string html=Regex.Replace(ActiveHtml(doc.Body),@"(<script\b[^>]*>)[\s\S]*?</script\s*>","$1</script>",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
        bool technical=Script.Matches(html).Any(m=>!Regex.IsMatch(m.Value,"\\btype\\s*=\\s*[\"'](?!module[\"']|(?:text|application)/(?:java|ecma)script[\"'])",RegexOptions.IgnoreCase)&&Uri.TryCreate(new Uri(doc.Url),Decode(m.Groups[1].Value),out var src)&&
            (src.AbsolutePath.EndsWith("/xpcom.common.js",StringComparison.OrdinalIgnoreCase)||src.AbsolutePath.EndsWith("/server/api/load_js.php",StringComparison.OrdinalIgnoreCase)));
        bool platform=Regex.IsMatch(html,@"\b(?:stalker_portal|stalker|Ministra|stb)\b",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
        return technical&&platform;
    }
    public static string Title(WebDocument doc)
    {
        var title=Decode(Regex.Match(ActiveHtml(doc.Body),@"<title\b[^>]*>([\s\S]{0,1024}?)</title\s*>",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1)).Groups[1].Value);
        title=Regex.Replace(title,"<[^>]*>|\\s+"," ").Trim();
        return title.Length==0||SensitiveText(title)?new Uri(doc.Url).Host:title[..Math.Min(140,title.Length)];
    }
    public static bool MediaPath(string url)=>Regex.IsMatch(new Uri(url).AbsolutePath,@"\.(?:m3u8|mpd|mp3|aac|ogg|mp4|ts)$",RegexOptions.IgnoreCase);
    public static bool PlaylistPath(string url)=>Regex.IsMatch(new Uri(url).AbsolutePath,@"\.m3u8?$",RegexOptions.IgnoreCase);
    public static bool ClientPage(string url)=>ClientPath.IsMatch(new Uri(url).AbsolutePath);
    public static bool ProtectedHls(string body)=>Regex.Matches(body,@"(?im)^#EXT-X-(?:SESSION-)?KEY:[^\r\n]*").Any(m=>
        Regex.IsMatch(m.Value,@"METHOD\s*=\s*[\""']?SAMPLE-AES",RegexOptions.IgnoreCase)||
        Regex.Matches(m.Value,"KEYFORMAT\\s*=\\s*(?:[\"']([^\"']+)[\"']|([^,\\s]+))",RegexOptions.IgnoreCase).Any(k=>!(k.Groups[1].Success?k.Groups[1].Value:k.Groups[2].Value).Equals("identity",StringComparison.OrdinalIgnoreCase)));
    public static List<string> Links(WebDocument doc)
    {
        // Published playlist URLs in README pre/code blocks are useful text, not
        // executable portal fingerprints. Keep them here; PortalClient still
        // uses ActiveHtml and ignores inert/code markup entirely.
        string html=Regex.Replace(doc.Body,@"<!--[\s\S]*?-->|<(textarea|template|noscript)\b[^>]*>[\s\S]*?</\1\s*>","",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));var links=new List<string>();
        bool Preferred(string url)=>MediaPath(url)||PlaylistPath(url)||ClientPage(url);
        void Add(string value,bool preferredOnly=false)
        {if(Uri.TryCreate(new Uri(doc.Url),WebUtility.HtmlDecode(value),out var u)&&AllowedUrl(u.AbsoluteUri)&&(!preferredOnly||Preferred(u.AbsoluteUri)))links.Add(u.AbsoluteUri);}
        string text=WebUtility.HtmlDecode(html).Replace("\\/","/").Replace("\\u0026","&").Replace("\\u003d","=").Replace("\\u003D","=");
        // Read preferred URLs before navigation limits. This includes plain .m3u
        // (the older media extractor only recognized HLS .m3u8).
        foreach(Match m in Regex.Matches(text,"(?:https?://|//)[^\\s<>\"'`\\\\]{1,8192}",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1)))
        {Add(m.Value,true);if(links.Count>=500)break;}
        foreach(Match m in Regex.Matches(html,"(?:src|file|url)\\s*[=:]\\s*[\"']([^\"']{1,8192})[\"']",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1)).Take(2000))Add(m.Groups[1].Value,true);
        foreach(Match m in Regex.Matches(html,"<a\\b[^>]{0,8192}?href\\s*=\\s*[\"']([^\"']+)[\"'][^>]*>([\\s\\S]{0,2048}?)</a\\s*>",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1)).Take(2000))
        {
            if(SensitiveText(VisibleText(m.Groups[2].Value)))continue;
            Add(m.Groups[1].Value);
        }
        foreach(Match m in Regex.Matches(html,"<iframe\\b[^>]{0,8192}?src\\s*=\\s*[\"']([^\"']+)[\"'][^>]*>",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1)).Take(2000))Add(m.Groups[1].Value);
        return links.Distinct().OrderByDescending(Preferred).Take(60).ToList();
    }
    public static List<WebHit> SearchHits(string body,bool duck)
    {
        if(!duck)
        {
            using var reader=XmlReader.Create(new StringReader(body),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2_000_000});var doc=XDocument.Load(reader);
            if(doc.Root?.Name.LocalName!="rss")throw new InvalidDataException("Nie rozpoznano wyników wyszukiwarki.");
            return doc.Descendants("item").Where(x=>!SensitiveText(VisibleText(string.Join(" ",x.Elements().Where(e=>e.Name.LocalName is "title" or "description").Select(e=>e.Value))))).Select(x=>new WebHit(x.Element("title")?.Value??"",x.Element("link")?.Value??"")).Where(h=>AllowedUrl(h.Url)).DistinctBy(h=>h.Url).Take(10).ToList();
        }
        // Result snippets are part of the public discovery evidence. Never crawl a hit
        // whose surrounding result advertises account/MAC credentials.
        var hits=SearchExpansion.ParseDuck(body);var unsafeUrls=new HashSet<string>();
        var starts=Regex.Matches(body,"<a\\b[^>]*class\\s*=\\s*[\"'][^\"']*\\bresult__a\\b[^\"']*[\"'][^>]*>",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
        for(int i=0;i<starts.Count;i++)
        {
            int start=starts[i].Index,end=i+1<starts.Count?starts[i+1].Index:body.Length;
            string block=body.Substring(start,Math.Min(end-start,24000));
            if(!SensitiveText(VisibleText(block)))continue;
            foreach(var hit in SearchExpansion.ParseDuck(block))unsafeUrls.Add(hit.Url);
        }
        return hits.Where(h=>!unsafeUrls.Contains(h.Url)&&!SensitiveText(h.Title)&&AllowedUrl(h.Url)).ToList();
    }
}

public sealed class SourceExplorer
{
    public const int MaxQueries=6,MaxPages=20,MaxDepth=2,MaxResults=100,Seconds=90;
    readonly Func<string,CancellationToken,Task<WebDocument>> fetch;
    public SourceExplorer(Func<string,CancellationToken,Task<WebDocument>>? fetch=null)=>this.fetch=fetch??((url,ct)=>PublicWeb.FetchChecked(url,ct,SourceExplorerRules.AllowedUrl));
    public Task<SourceExplorerResult> Analyze(string url,string[] countries,IReadOnlyList<Station> directory,CancellationToken ct,Action<string>? progress=null)
    {
        url=XtreamLink.Clean(url);if(!SourceExplorerRules.SafeManualPage(url))throw new InvalidDataException("To nie jest publiczna strona do analizy. Własne konta, tokeny i portale z MAC dodaj przez Wklej link.");
        return Run("",url,countries,directory,ct,progress);
    }
    public Task<SourceExplorerResult> Search(string query,string[] countries,IReadOnlyList<Station> directory,CancellationToken ct,Action<string>? progress=null)
    {
        query=query.Trim();if(query.Length is <2 or >160||XtreamLink.PrivateInput(query)||SourceExplorerRules.SensitiveText(query))throw new InvalidDataException("Wpisz nazwę lub temat bez adresu i danych konta. Publiczny URL użyj w Analizuj stronę; własne konto w Wklej link.");
        return Run(query,null,countries,directory,ct,progress);
    }
    async Task<SourceExplorerResult> Run(string query,string? initial,string[] countries,IReadOnlyList<Station> directory,CancellationToken ct,Action<string>? progress)
    {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(TimeSpan.FromSeconds(Seconds));var token=budget.Token;
        var candidates=new Dictionary<string,SourceCandidate>();var playlists=new List<FoundPlaylist>();var evidence=new List<WebEvidence>();var queue=new LinkedList<(string Url,string Origin,int Depth)>();var queued=new HashSet<string>();var visited=new HashSet<string>();int skipped=0;
        void Log(string stage,string url,string note){if(url.Length==0||SourceExplorerRules.AllowedUrl(url))evidence.Add(new(stage,url,note,DateTimeOffset.UtcNow));}
        void Queue(string url,string origin,int depth){if(depth<=MaxDepth&&queued.Count<200&&SourceExplorerRules.AllowedUrl(url)&&queued.Add(SourceExplorerRules.Canonical(url))){if(depth>0&&(SourceExplorerRules.MediaPath(url)||SourceExplorerRules.PlaylistPath(url)||SourceExplorerRules.ClientPage(url)))queue.AddFirst((url,origin,depth));else queue.AddLast((url,origin,depth));}}
        var countryIndex=directory.Select(s=>(Station:s,Url:Uri.TryCreate(s.Homepage.Length>0?s.Homepage:s.Url,UriKind.Absolute,out var u)?u:null)).Where(x=>x.Url!=null&&x.Station.Country.Length==2).GroupBy(x=>x.Url!.Host).ToDictionary(g=>g.Key,g=>{var codes=g.Select(x=>x.Station.Country).Distinct().Take(2).ToArray();return codes.Length==1?codes[0]:"";});
        string Country(string url)=>countryIndex.GetValueOrDefault(new Uri(url).Host,"");
        void Add(SourceCandidateKind kind,WebDocument doc,string origin,string status,int count=0,int matches=0,bool drm=false,string? title=null,string? country=null)
        {
            if(candidates.Count>=MaxResults||!SourceExplorerRules.AllowedUrl(doc.Url)||!SourceExplorerRules.AllowedUrl(origin))return;
            string label=title??SourceExplorerRules.Title(doc);if(SourceExplorerRules.SensitiveText(label))label=new Uri(doc.Url).Host;
            var item=new SourceCandidate(SourceExplorerRules.Id(doc.Url),kind,SourceExplorerRules.Canonical(doc.Url),label,origin,DateTimeOffset.UtcNow,doc.Modified,SourceExplorerRules.Digest(doc.Body),status,country??Country(doc.Url),count,matches,drm);
            candidates[item.Id]=item;
        }
        if(initial!=null)Queue(initial,initial,0);
        else
        {
            string region=string.Join(' ',countries.Take(3));
            var queries=new[]{query+" official IPTV provider Stalker Ministra portal "+region,query+" public TV playlist M3U "+region,query+" official television live stream "+region,query+" operator portal telewizja na żywo "+region,query+" IPTV published playlist channels "+region,query+" Ministra television provider official "+region};
            using var searchBudget=CancellationTokenSource.CreateLinkedTokenSource(token);searchBudget.CancelAfter(TimeSpan.FromSeconds(25));bool bing=true,duck=true;int searchRequests=0;
            for(int i=0;i<queries.Length&&!searchBudget.IsCancellationRequested&&searchRequests<MaxQueries;i++)
            {
                ct.ThrowIfCancellationRequested();progress?.Invoke("Szukam publicznych stron źródeł • "+(i+1)+"/"+MaxQueries);
                foreach(bool fallback in (i==0||!bing||queue.Count==0)?new[]{false,true}:new[]{false})
                {
                    if(searchRequests>=MaxQueries||searchBudget.IsCancellationRequested||fallback&&!duck||!fallback&&!bing)continue;searchRequests++;
                    string endpoint=(fallback?SearchExpansion.DuckTemplate:"https://www.bing.com/search?format=rss&q={query}").Replace("{query}",Uri.EscapeDataString(queries[i]));
                    try
                    {
                        var doc=await fetch(endpoint,searchBudget.Token);var hits=SourceExplorerRules.SearchHits(doc.Body,fallback);
                        foreach(var hit in hits){if(SourceExplorerRules.SensitiveText(hit.Title)||!SourceExplorerRules.AllowedUrl(hit.Url)){skipped++;continue;}Queue(hit.Url,hit.Url,0);}
                        Log("Wyszukiwarka",endpoint,(fallback?"DuckDuckGo":"Bing")+": "+hits.Count+" wyników; bez potwierdzenia dostępu.");
                    }
                    catch(Exception ex)when(ex is not OperationCanceledException||!ct.IsCancellationRequested){if(fallback)duck=false;else bing=false;Log("Błąd wyszukiwarki","","Nie odczytano wyników ("+ex.GetType().Name+").");}
                }
                if(!bing&&!duck)break;
            }
        }
        while(queue.Count>0&&visited.Count<MaxPages&&!token.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();var item=queue.First!.Value;queue.RemoveFirst();if(!visited.Add(item.Url))continue;
            progress?.Invoke("Analizuję publiczną stronę "+visited.Count+"/"+MaxPages+" • "+new Uri(item.Url).Host);
            try
            {
                if(!SourceExplorerRules.AllowedUrl(item.Url))continue;
                var doc=await fetch(item.Url,token);ct.ThrowIfCancellationRequested();
                if(!SourceExplorerRules.AllowedUrl(doc.Url)||doc.Modified>DateTimeOffset.UtcNow.AddMinutes(10)){skipped++;continue;}
                string body=doc.Body.TrimStart('\uFEFF',' ','\r','\n');
                if(body.StartsWith("#EXTM3U",StringComparison.OrdinalIgnoreCase))
                {
                    if(M3u.IsHls(body))
                    {
                        if(PublicPlaylistPrivacy.ContainsCredentials(body)){skipped++;continue;}
                        bool drm=SourceExplorerRules.ProtectedHls(body);
                        Add(SourceCandidateKind.DirectStream,doc,item.Origin,drm?"Manifest HLS z ochroną; wymaga zgodnej konfiguracji DRM. Nie sprawdzono odtwarzania.":"Odczytano manifest HLS. Odtwarzanie niepotwierdzone.",drm:drm,title:new Uri(doc.Url).Host+" • HLS");
                    }
                    else
                    {
                        var list=PlaylistRepository.Create(doc,directory,countries,"");playlists.Add(list);
                        Add(SourceCandidateKind.PublicPlaylist,doc,item.Origin,"Odczytano playlistę"+(list.Sanitized?"; oczyszczona kopia (pominięto "+list.RemovedEntries+")":"")+". Kanały wymagają próby odtwarzania.",list.ChannelCount,PlaylistRepository.SelectStations(list.Stations,countries,"").Select(SourceRanker.ChannelKey).Distinct().Count(),title:list.Name,country:list.Stations.Select(s=>s.Country).Distinct().Count()==1?list.Stations[0].Country:"");
                    }
                    continue;
                }
                if(new Uri(doc.Url).AbsolutePath.EndsWith(".mpd",StringComparison.OrdinalIgnoreCase)||body.StartsWith("<?xml")||body.StartsWith("<MPD"))
                {
                    if(PublicPlaylistPrivacy.ContainsCredentials(body)){skipped++;continue;}
                    using var reader=XmlReader.Create(new StringReader(body),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=12_000_000});var xml=XDocument.Load(reader);
                    if(xml.Root?.Name.LocalName=="MPD")
                    {bool drm=xml.Descendants().Any(x=>x.Name.LocalName=="ContentProtection");Add(SourceCandidateKind.DirectStream,doc,item.Origin,drm?"Manifest DASH z DRM. Wymagana obsługiwana własna licencja; odtwarzanie niepotwierdzone.":"Odczytano manifest DASH. Odtwarzanie niepotwierdzone.",drm:drm,title:new Uri(doc.Url).Host+" • DASH");continue;}
                }
                if(!Regex.IsMatch(body,@"<(?:html|head|body|title|a|script)\b",RegexOptions.IgnoreCase)){Log("Pominięto format",doc.Url,"Brak publicznej strony lub rozpoznanej playlisty.");continue;}
                if(SourceExplorerRules.SensitiveText(SourceExplorerRules.VisibleText(body))){skipped++;Log("Pominięto stronę","","Strona zawierała informacje przypominające dane dostępu; nie zapisano jej treści ani linków.");continue;}
                bool portal=SourceExplorerRules.PortalClient(doc);
                Add(portal?SourceCandidateKind.PortalCandidate:SourceCandidateKind.ProviderPage,doc,item.Origin,portal?"Rozpoznano stronę klienta Stalker/Ministra. Wymagany Twój przypisany MAC; kanałów i konta nie sprawdzono.":"Odczytano stronę źródłową. Nie potwierdzono operatora ani transmisji.");
                if(portal)continue; // Do not navigate portal controls or any script/API.
                foreach(string link in SourceExplorerRules.Links(doc))
                {
                    if(Regex.IsMatch(new Uri(link).AbsolutePath,@"\.(?:mp3|aac|ogg|mp4|ts)$",RegexOptions.IgnoreCase))
                        Add(SourceCandidateKind.DirectStream,new(link,doc.Body,doc.ContentType,doc.Modified),doc.Url,"Publiczny odnośnik do mediów. Adres i odtwarzanie nie zostały sprawdzone.",title:new Uri(link).Host+" • "+Path.GetFileName(new Uri(link).AbsolutePath));
                    else if(new Uri(link).Host==new Uri(doc.Url).Host||SourceExplorerRules.PlaylistPath(link)||SourceExplorerRules.MediaPath(link)||PortalAddress.ExplicitClient(link))Queue(link,doc.Url,item.Depth+1);
                }
            }
            catch(Exception ex)when(ex is not OperationCanceledException||!ct.IsCancellationRequested){Log("Błąd odczytu",item.Url,"Nie odczytano publicznej zawartości ("+ex.GetType().Name+"). Poprzedni zapis pozostaje.");}
        }
        ct.ThrowIfCancellationRequested();
        Log("Zakres","","Odczyty: "+visited.Count+"/"+MaxPages+"; wyniki: "+candidates.Count+"; pominięte dane dostępu/niepoprawne adresy: "+skipped+(token.IsCancellationRequested?". Osiągnięto budżet czasu; wyniki częściowe.":"."));
        return new(candidates.Values.OrderBy(c=>c.Kind==SourceCandidateKind.ProviderPage?3:c.Kind==SourceCandidateKind.PortalCandidate?2:c.Kind==SourceCandidateKind.PublicPlaylist?0:1).ThenByDescending(c=>c.MatchingChannels).ThenBy(c=>c.Title).ToList(),playlists,evidence,true);
    }
}

public sealed class SourceExplorerStore
{
    readonly string path;List<SourceCandidate> items=[];bool backupRecovered,locked;
    public string Notice{get;private set;}="";
    public IReadOnlyList<SourceCandidate> Items=>items.ToArray();
    public SourceExplorerStore(string profileFolder)
    {
        Directory.CreateDirectory(profileFolder);path=Path.Combine(profileFolder,"source-explorer.dpapi");if(!File.Exists(path))return;
        try{items=Read(path);}catch(Exception ex)when(ex is IOException or CryptographicException or JsonException or InvalidDataException){try{items=Read(path+".bak");backupRecovered=true;Notice="Odczytano kopię wyników.";}catch(Exception e)when(e is IOException or CryptographicException or JsonException or InvalidDataException){locked=true;Notice="Nie odczytano wyników; chronionych plików nie nadpisano.";}}
    }
    static void Validate(SourceCandidate c)
    {
        if(c==null||c.Id==null||c.Url==null||c.EvidenceUrl==null||c.Title==null||c.Status==null||c.Country==null||!Enum.IsDefined(c.Kind)||!SourceExplorerRules.AllowedUrl(c.Url)||!SourceExplorerRules.AllowedUrl(c.EvidenceUrl)||c.Id!=SourceExplorerRules.Id(c.Url)||c.Title.Length>140||c.Status.Length>800||SourceExplorerRules.SensitiveText(c.Title)||SourceExplorerRules.SensitiveText(c.Status)||c.CheckedAt==default||c.CheckedAt>DateTimeOffset.UtcNow.AddMinutes(10)||c.ModifiedAt>DateTimeOffset.UtcNow.AddMinutes(10)||c.Country.Length>2||c.ChannelCount<0||c.MatchingChannels<0||c.MatchingChannels>c.ChannelCount||!Regex.IsMatch(c.EvidenceSha256??"","^[a-f0-9]{64}$"))throw new InvalidDataException("Nieprawidłowy zapis publicznego źródła.");
    }
    static List<SourceCandidate> Read(string file)
    {
        if(new FileInfo(file).Length>2_000_000)throw new InvalidDataException("Zapis przekracza limit.");
        var rows=PrivateData.Read<List<SourceCandidate>>(file,"source-explorer-v1");if(rows==null||rows.Count>SourceExplorer.MaxResults)throw new InvalidDataException("Nieprawidłowa liczba wyników.");foreach(var row in rows)Validate(row);return rows.DistinctBy(c=>c.Id).ToList();
    }
    public void Merge(SourceExplorerResult result)
    {
        if(!result.Complete||result.Candidates.Count==0)return;if(locked)throw new InvalidDataException(Notice);
        foreach(var row in result.Candidates)Validate(row);
        var next=items.Concat(result.Candidates).GroupBy(c=>c.Id).Select(g=>g.OrderByDescending(c=>c.CheckedAt).First()).OrderByDescending(c=>c.CheckedAt).Take(SourceExplorer.MaxResults).ToList();
        PrivateData.Write(path,next,"source-explorer-v1",backup:!backupRecovered);backupRecovered=false;items=next;
    }
}
