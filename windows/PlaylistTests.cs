using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwirStreamAI;
public static class PlaylistTests
{
    // Frozen 0.5.1 matching baseline, used only to compare the very same downloaded data.
    static List<Station> Baseline051(string body,string url,IReadOnlyList<Station> directory,string[] countries)
    {
        var ids=directory.Where(s=>s.ChannelId.Length>0).GroupBy(s=>s.ChannelId).ToDictionary(g=>g.Key,g=>g.First());
        var names=directory.GroupBy(s=>ChannelSearch.Key(s.Name)).Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.First());
        var list=new List<Station>();
        foreach(var s in M3u.Parse(body,url))
        {
            if(SharedPlaylists.RequiresBrowser(s.Url))continue;
            var meta=ids.GetValueOrDefault(s.ChannelId);
            if(meta==null && names.TryGetValue(ChannelSearch.Key(s.Name),out var named) && (s.Country.Length==0||named.Country==s.Country))meta=named;
            var station=s with {Country=meta?.Country.Length>0?meta.Country:s.Country,ChannelId=meta?.ChannelId??s.ChannelId};
            if(countries.Length==0||countries.Contains(station.Country))list.Add(station);
        }
        return list.DistinctBy(s=>s.Url).ToList();
    }
    public static int Run(string report,bool network)
    {
        var checks=new List<object>();bool success=true;
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"playlist-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        void Check(string name,bool ok,object? detail=null){checks.Add(new{name,ok,detail});success&=ok;}
        async Task Test()
        {
            var pl=new Station("TVP1","https://tvp.example/","TV",Country:"PL",ChannelId:"TVP1.pl",Aliases:["Jedynka"],Homepage:"https://tvp.example/");
            var de=new Station("Das Erste","https://ard.example/","TV",Country:"DE",ChannelId:"DasErste.de",Aliases:["ARD"],Homepage:"https://ard.example/");
            var sharedPl=new Station("Channel One","https://one.example/pl","TV",Country:"PL",ChannelId:"One.pl");
            var sharedDe=sharedPl with {Country="DE",ChannelId="One.de",Url="https://one.example/de"};
            List<Station> directory=[pl,de,sharedPl,sharedDe];
            const string url="https://raw.githubusercontent.com/example/public/main/list.m3u";
            string body="#EXTM3U x-tvg-url=\"https://epg.example/pl.xml,https://epg.example/de.xml\"\n#EXTINF:-1 tvg-id=\"TVP1.pl@HD\" tvg-logo=\"https://logo.example/tvp.png\" group-title=\"Polska\",TVP 1\n#EXTVLCOPT:http-user-agent=SwirFixture\n#EXTVLCOPT:http-referrer=https://tvp.example/\nhttps://cdn.example/pl.m3u8\n#EXTINF:-1,ARD\nhttps://cdn.example/de.m3u8\n#EXTINF:-1 tvg-country=\"PL\",Channel One\nhttps://cdn.example/one.m3u8\n#EXTINF:-1,Unknown\nhttps://cdn.example/unknown.m3u8\n#EXTINF:-1,HTML\nhttps://example.org/watch.html\n#EXTINF:-1,YouTube\nhttps://youtube.com/live/test\n#EXTINF:-1,Private\nhttp://192.168.1.10/live.m3u8\n";
            var doc=new WebDocument(url,body,"text/plain",DateTimeOffset.UtcNow);
            var playlist=PlaylistRepository.Create(doc,directory,["PL"],"");
            Check("Alias ARD maps to Das Erste with confirmed country",playlist.Stations.Single(s=>s.Url.EndsWith("de.m3u8")).ChannelId=="DasErste.de");
            Check("Explicit playlist country disambiguates equal channel names",playlist.Stations.Single(s=>s.Url.EndsWith("one.m3u8")).ChannelId=="One.pl");
            Check("Selected country never assigns unknown station nationality",!PlaylistRepository.SelectStations(playlist.Stations,["PL"],"").Any(s=>s.Name=="Unknown"));
            Check("HTML, browser-only and private addresses excluded",playlist.SourceCount==4 && playlist.Stations.All(s=>!s.BrowserOnly));
            string mediaPortBody="#EXTM3U\n#EXTINF:-1 tvg-id=\"TVP1.pl\",TVP1\nhttps://cdn.example:1935/live.m3u8\n#EXTINF:-1,Local\nhttp://192.168.1.12:8080/live.m3u8\n";
            var mediaPortList=PlaylistRepository.Create(doc with {Body=mediaPortBody},directory,["PL"],"");
            Check("Public media ports retained but private media hosts rejected",mediaPortList.SourceCount==1&&mediaPortList.Stations[0].Url=="https://cdn.example:1935/live.m3u8"&&!SharedPlaylists.AllowedStream("http://192.168.1.12:8080/live.m3u8")&&!PublicWeb.Allowed("https://cdn.example:1935/document.html"));
            Check("Playlist records totals and matching source count separately",playlist.MatchingCount==2 && playlist.ChannelCount==4);
            Check("EPG URLs extracted without fetching them",playlist.EpgUrls.SequenceEqual(new[]{"https://epg.example/pl.xml","https://epg.example/de.xml"}));
            var repo=new PlaylistRepository(root);repo.Save(playlist);
            Check("Found playlist survives restart with original content",new PlaylistRepository(root).Get(playlist.Id).OriginalM3u==body);
            string original=Path.Combine(root,"download-original.m3u");repo.ExportOriginal(playlist.Id,original);
            Check("Download original writes exact M3U text",File.ReadAllText(original)==body);
            string filtered=repo.Filtered(playlist.Id,["PL"]);
            var parsed=M3u.Parse(filtered,"https://example.org/selected.m3u");
            Check("Filtered download keeps only selected country channels",parsed.Count==2 && parsed.All(s=>s.Url.EndsWith("pl.m3u8")||s.Url.EndsWith("one.m3u8")));
            Check("Filtered export preserves EPG, logos, groups and playback headers",filtered.Contains("tvg-logo=\"https://logo.example/tvp.png\"")&&filtered.Contains("x-tvg-url=")&&parsed[0].UserAgent=="SwirFixture"&&parsed[0].Referrer=="https://tvp.example/"&&parsed[0].Feed=="HD");
            Check("Filtered export records origin and fetch time",filtered.Contains("#SWIRSTREAM-SOURCE: "+url)&&filtered.Contains("#SWIRSTREAM-FETCHED:"));
            Check("Inferred identity and country survive filtered file re-import",M3u.Parse(repo.Filtered(playlist.Id,["DE"]),url).Single() is var reimported && reimported.Country=="DE" && reimported.ChannelId=="DasErste.de");
            Check("Verified-only export requires explicit playback results",repo.GetStations(playlist.Id,[],verifiedUrls:new HashSet<string>{"https://cdn.example/de.m3u8"}).Single().Country=="DE");
            bool rejected=false;try{repo.Save(playlist with {OriginalM3u=body+"#tampered"});}catch(InvalidDataException){rejected=true;}
            Check("Content digest catches damaged saved playlist",rejected&&repo.Get(playlist.Id).OriginalM3u==body);
            File.WriteAllText(Path.Combine(root,"found-playlists","damaged.json"),"{broken");var recovered=new PlaylistRepository(root);
            Check("One corrupt record does not erase other saved playlists",recovered.Items.Count==1&&recovered.Notice.Length>0);
            string wrongFile=Path.Combine(root,"found-playlists",new string('f',64)+".json");File.WriteAllText(wrongFile,JsonSerializer.Serialize(playlist with {Schema=999}));
            Check("Structurally valid JSON with unsupported schema recovers",new PlaylistRepository(root).Items.Count==1);
            var damagedHeaders=playlist with {Stations=playlist.Stations.Select(s=>s with {UserAgent=null!}).ToList()};
            File.WriteAllText(Path.Combine(root,"found-playlists",playlist.Id+".json"),JsonSerializer.Serialize(damagedHeaders));
            Check("Null playback header in persisted JSON is rejected before import",new PlaylistRepository(root).Items.Count==0);repo.Save(playlist);
            rejected=false;try{PlaylistRepository.Create(doc with {Modified=DateTimeOffset.UtcNow.AddDays(-8)},directory,[],"");}catch(InvalidDataException){rejected=true;}
            Check("Old publisher dates rejected",rejected);
            rejected=false;try{PlaylistRepository.Create(doc with {Body="#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:1\n#EXTINF:3,\na.ts"},directory,[],"");}catch(InvalidDataException){rejected=true;}
            Check("HLS segment manifest never offered as channel playlist",rejected);
            var visits=new List<string>();var records=new List<FoundPlaylist>();
            Task<WebDocument> Fetch(string address,CancellationToken token)
            {
                token.ThrowIfCancellationRequested();visits.Add(address);
                if(address.Contains("search.example"))throw new HttpRequestException("search unavailable");
                return Task.FromResult(doc with {Url=address});
            }
            var result=await new SharedPlaylists(Fetch).Search("",["PL"],directory,"https://search.example/?q={query}",[url],null,CancellationToken.None,onPlaylist:records.Add);
            Check("Published playlist is fetched before search engine request",visits[0]==url);
            Check("Search engine failure retains playlist and candidates",result.Stations.Count==2&&records.Count==1&&result.Evidence.Any(e=>e.Stage=="BŁĄD szukania IPTV"));
            records.Clear();visits.Clear();
            await new SharedPlaylists(Fetch).Search("",["PL"],directory,"https://search.example/?q={query}",[url,url,url.Replace("list.m3u","second.m3u")],null,CancellationToken.None,onPlaylist:records.Add);
            Check("Several distinct playlists are exposed without duplicate records",records.Count==2&&records.Select(p=>p.Id).Distinct().Count()==2);
            var observerFailure=await new SharedPlaylists(Fetch).Search("",["PL"],directory,"https://search.example/?q={query}",[url],null,CancellationToken.None,onPlaylist:_=>throw new InvalidOperationException("broken observer"));
            Check("Playlist observer failure never drops discovered addresses",observerFailure.Stations.Count==2&&observerFailure.Evidence.Any(e=>e.Stage=="BŁĄD zapisu playlisty"));
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();rejected=false;
            try{await new SharedPlaylists(Fetch).Search("",[],[],"https://search.example/?q={query}",[],null,cancelled.Token,onPlaylist:records.Add);}catch(OperationCanceledException){rejected=true;}
            Check("Already cancelled empty search does not quietly succeed",rejected);
            var sourceDate=DateTimeOffset.UtcNow.AddDays(-2);bool offline=false;var feedRecords=new List<FoundPlaylist>();
            var settings=new SourceSettings {TvTemplates=["https://broken.example/{path}","https://mirror.example/{path}"]};
            Task<(string,DateTimeOffset?)> CatalogFetch(string address,CancellationToken token)
            {
                if(offline||address.Contains("broken.example"))throw new HttpRequestException("provider offline");
                return Task.FromResult<(string,DateTimeOffset?)>(("#EXTM3U\n#EXTINF:-1 tvg-id=\"TVP1.pl\",TVP1\nhttps://cdn.example/catalog.m3u8\n",sourceDate));
            }
            var feedCatalog=new SmartCatalog(Path.Combine(root,"catalog"),settings,CatalogFetch){PlaylistFound=feedRecords.Add};
            var feedResult=await feedCatalog.Search("","TV",["PL"],true,CancellationToken.None);
            Check("Country catalogue exposes the actual successful mirror M3U",feedRecords.Count==1&&feedRecords[0].SourceUrl=="https://mirror.example/countries/pl.m3u"&&feedRecords[0].OriginalM3u.Contains("catalog.m3u8")&&feedRecords[0].ModifiedAt==sourceDate);
            offline=true;var fallback=await feedCatalog.Search("","TV",["PL"],true,CancellationToken.None);
            Check("Failed refresh cannot extend cached source age",fallback.Stations.Count==1&&fallback.DataAt==sourceDate&&feedResult.DataAt==sourceDate&&feedRecords.Count==1);
            offline=false;feedCatalog.PlaylistFound=_=>throw new InvalidOperationException("observer unavailable");
            var feedObserverFailure=await feedCatalog.Search("","TV",["PL"],true,CancellationToken.None);
            Check("Country catalogue save observer failure retains stream results",feedObserverFailure.Stations.Count==1&&feedObserverFailure.Detail.Contains("nie zapisano"));
            var station=pl with {Name="News",ChannelId="News.pl",Url="https://news.example/",Homepage="https://news.example/"};
            Task<WebDocument> Nested(string address,CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                string text=address.Contains("search.example")?"<rss><channel/></rss>":address==station.Url?"<a href='/live'>Live</a>":address.EndsWith("/live")?"<a href='/live/player'>Player</a>":address.EndsWith("/live/player")?"<source src='https://cdn.example/live.m3u8'>":"#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:1\n#EXTINF:3,\na.ts";
                return Task.FromResult(new WebDocument(address,text,"text/html"));
            }
            var nested=await new WebDiscovery(Nested).Search("News","TV",["PL"],[station],[],"https://search.example/{query}",null,CancellationToken.None);
            Check("Crawler follows homepage then live directory then channel player",nested.Stations.Count==1&&nested.Stations[0].ChannelId==station.ChannelId);
            var second=station with {Name="News Two",ChannelId="News2.pl"};
            Task<WebDocument> Ambiguous(string address,CancellationToken token)=>Task.FromResult(new WebDocument(address,address.Contains("search.example")?"<rss><channel/></rss>":address.EndsWith(".m3u8")?"#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:1\n#EXTINF:3,\na.ts":"<title>Television live</title><source src='https://cdn.example/live.m3u8'>","text/html"));
            var ambiguous=await new WebDiscovery(Ambiguous).Search("","TV",["PL"],[station,second],[],"https://search.example/{query}",null,CancellationToken.None);
            Check("Shared homepage does not mislabel unidentified feed as first channel",ambiguous.Stations.Count==0&&ambiguous.Evidence.Any(e=>e.Detail.Contains("tożsamości")));
            if(network)
            {
                using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(4));
                var catalog=new SmartCatalog(Path.Combine(root,"production"));var actual=await catalog.Broadcasters(true,deadline.Token);
                Check("Production directory provides countries and aliases",actual.Stations.Count>20000&&actual.Stations.Any(s=>s.Aliases?.Length>0),new{count=actual.Stations.Count,actual.Detail});
                foreach(string source in new[]{"https://raw.githubusercontent.com/Free-TV/IPTV/master/playlist.m3u8","https://iptv-org.github.io/iptv/countries/pl.m3u","https://iptv-org.github.io/iptv/countries/de.m3u"})
                {
                    var actualDoc=await PublicWeb.Fetch(source,deadline.Token);var saved=PlaylistRepository.Create(actualDoc,actual.Stations,["PL","DE","FR"],"");repo.Save(saved);
                    var rawBaseline=Baseline051(actualDoc.Body,source,actual.Stations,["PL","DE","FR"]);
                    var baseline=Baseline051(PublicPlaylistPrivacy.SanitizeM3u(source,actualDoc.Body).Body,source,actual.Stations,["PL","DE","FR"]);var improved=PlaylistRepository.SelectStations(saved.Stations,["PL","DE","FR"],"");
                    var excluded=rawBaseline.Where(s=>!baseline.Any(n=>n.Url==s.Url)).ToList();
                    Check("Production exclusions are account-bearing entries, not ordinary public CDN: "+new Uri(source).Host+new Uri(source).AbsolutePath,excluded.All(s=>PublicPlaylistPrivacy.ContainsCredentials(s.Url)||PublicPlaylistPrivacy.ContainsCredentials(s.Referrer)),new{beforePrivacy=rawBaseline.Count,publicBaseline=baseline.Count,excluded=excluded.Count});
                    var missing=baseline.Where(s=>!improved.Any(n=>n.Url==s.Url)).ToList();
                    Check("Production same-data comparison: "+new Uri(source).Host+new Uri(source).AbsolutePath,missing.Count==0,new{baseline=baseline.Count,current=improved.Count,added=improved.Where(s=>!baseline.Any(b=>b.Url==s.Url)).Select(s=>new{s.Name,s.Country,s.ChannelId}).Take(20),missing=saved.SourceUrl+" "+string.Join(';',missing.Select(s=>s.Name)),saved.FetchedAt,saved.ModifiedAt,saved.ContentSha256,saved.SourceCount,saved.ChannelCount});
                    string export=repo.Filtered(saved.Id,["PL","DE","FR"]);
                    Check("Production playlist can be exported and parsed again: "+new Uri(source).Host+Path.GetFileName(new Uri(source).AbsolutePath),M3u.Parse(export,source).Count==improved.Count);
                }
            }
        }
        try{Test().GetAwaiter().GetResult();}catch(Exception ex){Check("Unexpected failure",false,ex.ToString());}
        File.WriteAllText(report,JsonSerializer.Serialize(new{success,checks},new JsonSerializerOptions{WriteIndented=true}));return success?0:1;
    }
}
