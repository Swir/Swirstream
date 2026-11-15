using System.Text.Json;

namespace SwirStreamAI;
public static class PublicPlaylistDiscoveryTests
{
    public static int Run(string report, bool network = false)
    {
        var checks = new List<object>(); bool success = true;
        void Check(string name, bool ok, object? detail = null) { checks.Add(new { name, ok, detail }); success &= ok; }
        const string url = "https://broadcaster.example/public/channels.m3u";
        const string body = "#EXTM3U x-tvg-url=\"https://guide.example/epg.xml\"\n#EXTINF:-1 tvg-id=\"News.pl\" tvg-country=\"PL\",News\nhttps://cdn.example/live/news/hls/master.m3u8\n";
        bool Rejected(string source, string text, DateTimeOffset? modified = null)
        { try { PlaylistRepository.Create(new(source,text,"application/x-mpegurl",modified),[],[],""); return false; } catch (InvalidDataException) { return true; } }
        async Task Test()
        {
            Check("Direct public playlist outside GitHub is eligible", SharedPlaylists.PlaylistUrl(url)==url);
            Check("GitHub blob rewriting is retained", SharedPlaylists.PlaylistUrl("https://github.com/public/tv/blob/main/list.m3u")=="https://raw.githubusercontent.com/public/tv/main/list.m3u");
            Check("Private IP, localhost and crawler nonstandard port are rejected", new[]{"http://127.0.0.1/list.m3u","http://10.0.0.1/list.m3u","http://localhost/list.m3u","https://public.example:8443/list.m3u"}.All(u=>SharedPlaylists.PlaylistUrl(u)==null));
            Check("HTML and extensionless endpoints are not inferred as playlists", SharedPlaylists.PlaylistUrl("https://public.example/channel.html")==null&&SharedPlaylists.PlaylistUrl("https://public.example/get.php")==null);
            Check("URL credentials are rejected before fetch", SharedPlaylists.PlaylistUrl(url+"?password=fixture-secret")==null&&SharedPlaylists.PlaylistUrl("https://name:password@public.example/list.m3u")==null);
            Check("Encoded access query is rejected", SharedPlaylists.PlaylistUrl(url+"?%2570assword=fixture-secret")==null);
            Check("Ordinary public paths and harmless query remain eligible", !PublicPlaylistPrivacy.ContainsCredentials(body)&&SharedPlaylists.PlaylistUrl(url+"?country=PL&format=m3u")!=null);
            Check("Normal public playlist and EPG remain available", PlaylistRepository.Create(new(url,body,"application/x-mpegurl",null),[],[],"").EpgUrls.Single()=="https://guide.example/epg.xml");
            Check("Credential-bearing stream path is rejected", Rejected(url,body.Replace("/live/news/hls/master.m3u8","/live/account/password/12345.ts")));
            Check("Credential-bearing EPG is removed without discarding channels", PlaylistRepository.Create(new(url,body.Replace("epg.xml","epg.xml?username=test&password=fixture-secret"),"text/plain"),[],[],"") is var cleanedEpg && cleanedEpg.SourceCount==1 && cleanedEpg.Sanitized && cleanedEpg.EpgUrls.Length==0);
            Check("Authorization and Cookie playlist directives are rejected", new[]{"#EXTVLCOPT:http-cookie=fixture-secret\n","#EXTHTTP:{\"Authorization\":\"Bearer fixture-secret\"}\n","#KODIPROP:inputstream.adaptive.stream_headers=Cookie%3Dfixture-secret\n"}.All(h=>Rejected(url,body.Replace("#EXTINF:",h+"#EXTINF:"))));
            Check("Declared DRM key is not exported by public discovery", PlaylistRepository.Create(new(url,body+"#KODIPROP:inputstream.adaptive.license_key=fixture-secret\n","text/plain"),[],[],"") is var cleanedKey && cleanedKey.Sanitized && !cleanedKey.OriginalM3u.Contains("fixture-secret"));
            Check("HLS manifest is not a channel playlist", Rejected(url,"#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6,\nsegment.ts\n"));
            Check("HTML and stale source are rejected", Rejected(url,"<html>Not a playlist</html>")&&Rejected(url,body,DateTimeOffset.UtcNow.AddDays(-8)));
            string folder=Path.Combine(Path.GetTempPath(),"SwirPublicPlaylistTests-"+Guid.NewGuid().ToString("N")); var repo=new PlaylistRepository(folder);
            var valid=PlaylistRepository.Create(new(url,body,"application/x-mpegurl",null),[],[],"");repo.Save(valid);
            string path=Path.Combine(folder,"found-playlists",valid.Id+".json");
            File.WriteAllText(path,JsonSerializer.Serialize(valid with {OriginalM3u=body+"https://cdn.example/a?password=fixture-secret"}));
            Check("Legacy account-bearing public cache is removed and cannot export", new PlaylistRepository(folder).Items.Count==0&&!File.Exists(path));
            var visits=new List<string>();var found=new List<FoundPlaylist>();
            string rss="<rss><channel><item><title>Public TV</title><link>"+url+"</link></item><item><title>Same list</title><link>"+url+"#copy</link></item><item><title>Private account</title><link>https://public.example/hidden.m3u?password=fixture-secret</link></item></channel></rss>";
            Task<WebDocument> Fetch(string address,CancellationToken ct){ct.ThrowIfCancellationRequested();visits.Add(address);return Task.FromResult(new WebDocument(address,address==url?body:address.Contains("bing.com")?rss:"<div class='no-results'>No results</div>","text/plain",null));}
            var result=await new SharedPlaylists(Fetch).Search("News",["PL"],[],"https://www.bing.com/search?format=rss&q={query}",[],null,CancellationToken.None,true,found.Add);
            Check("Search adopts one public result once despite duplicate fragments", found.Count==1&&result.Stations.Count==1&&visits.Count(u=>u==url)==1);
            Check("Search never fetches account-bearing result", !visits.Any(u=>u.Contains("fixture-secret")));
            var queries=visits.Where(u=>u.Contains("bing.com")).Select(Uri.UnescapeDataString).ToList();
            Check("Bounded search includes an unrestricted public playlist query", queries.Count<=6&&queries.Any(q=>q.Contains("public TV playlist m3u")&&!q.Contains("site:github.com")));
            string mixed=body+"#EXTINF:-1,Private account\nhttps://cdn.example/live/user/password/123.ts\n#EXTINF:-1,Public signed CDN\nhttps://cdn.example/signed.m3u8?token=public-token&key=public-key&signature=public-signature\n";
            var cleaned=PlaylistRepository.Create(new(url,mixed,"text/plain"),[],[],"");
            Check("One account entry does not discard public and signed CDN channels", cleaned.SourceCount==2&&cleaned.RemovedEntries==1&&cleaned.Sanitized&&cleaned.OriginalM3u.Contains("token=public-token")&&!cleaned.OriginalM3u.Contains("/user/password/"));
            Check("Sanitized export distinguishes original hash and cleaned hash", cleaned.OriginalSha256!=cleaned.ContentSha256&&cleaned.ExportLabel=="oczyszczona kopia");
            if(network)foreach(string source in new[]{"https://iptv-org.github.io/iptv/index.m3u","https://raw.githubusercontent.com/Free-TV/IPTV/master/playlist.m3u8","https://iptv-org.github.io/iptv/countries/pl.m3u"})
            {
                var downloaded=await PublicWeb.Fetch(source,CancellationToken.None);var beforeRows=M3u.Parse(downloaded.Body,source).Where(s=>SharedPlaylists.AllowedStream(s.Url)).DistinctBy(s=>s.Url).ToList();var after=PlaylistRepository.Create(downloaded,[],[],"");
                int Signed(IEnumerable<Station> rows)=>rows.Count(s=>System.Text.RegularExpressions.Regex.IsMatch(s.Url,@"[?&](?:token|key|signature|sig)=",System.Text.RegularExpressions.RegexOptions.IgnoreCase)&&!PublicPlaylistPrivacy.ContainsCredentials(s.Url));
                Check("Production consumer preserves public channels: "+new Uri(source).Host+new Uri(source).AbsolutePath,after.SourceCount>=beforeRows.Count*.95&&Signed(beforeRows)==Signed(after.Stations),new{before=beforeRows.Count,after=after.SourceCount,signedBefore=Signed(beforeRows),signedAfter=Signed(after.Stations),after.RemovedEntries,after.Sanitized,after.OriginalSha256,after.ContentSha256});
            }
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();int before=visits.Count;bool didCancel=false;
            try{await new SharedPlaylists(Fetch).Search("",[],[],"https://www.bing.com/search?q={query}",[],null,cancelled.Token);}catch(OperationCanceledException){didCancel=true;}
            Check("Cancellation performs no new fetch and retains prior results", didCancel&&visits.Count==before&&found.Count==1);
        }
        try { Test().GetAwaiter().GetResult(); } catch(Exception ex) { checks.Add(new{name="Unexpected exception",ok=false,detail=ex.GetType().Name});success=false; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);File.WriteAllText(report,JsonSerializer.Serialize(new{success,checks},new JsonSerializerOptions{WriteIndented=true}));return success?0:1;
    }
}
