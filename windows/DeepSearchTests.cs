using System.Text.Json;

namespace SwirStreamAI;

public static class DeepSearchTests
{
    public static int Run(string report, bool network)
    {
        bool success = true; var checks = new List<object>();
        void Check(string name, bool ok, object? detail = null) { checks.Add(new { name, ok, detail }); success &= ok; }
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "deep-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var one = new Station("TVP1", "https://tvp1.example/live", "TV", Country:"PL", ChannelId:"TVP1.pl", Aliases:["Jedynka"]);
            var ten = one with { Name="TVP10", ChannelId="TVP10.pl", Aliases=[] };
            var news = one with { Name="Polsat News", ChannelId="PolsatNews.pl", Aliases=[] };
            var news2 = news with { Name="Polsat News 2", ChannelId="PolsatNews2.pl" };
            List<Station> directory = [one,ten,news,news2];
            Check("Spaced channel name matches compact name", ChannelSearch.Matches("TVP 1",one));
            Check("TVP 1 does not match TVP 10", !ChannelSearch.Matches("TVP 1",ten));
            Check("Polish sport query still matches catalog sports category",ChannelSearch.Matches("sport",one with {Name="Arena",Group="sports"}));
            Check("Alternative names resolve to canonical channel", ChannelSearch.Exact("Jedynka",directory).Single().ChannelId==one.ChannelId);
            Check("Exact channel query excludes similarly named channel", ChannelSearch.Filter("Polsat News",[news,news2],directory).SequenceEqual(new[]{news}));
            Check("Non-Latin names remain distinguishable", ChannelSearch.Key("中央电视台")!="" && ChannelSearch.Matches("中央电视台",one with { Name="中央电视台" }));
            string data = JsonSerializer.Serialize(new object[] {
                new {channel="TVP1.pl",feed="HD",title="Jedynka",url="https://cdn.example/1.m3u8",quality="1080p",labels=new[]{"Geo-blocked"},user_agent="Player/1",referrer="https://tvp1.example/"},
                new {channel="TVP10.pl",feed="HD",title="TVP10",url="https://cdn.example/10.m3u8",label="Not 24/7"},
                new {channel=(string?)null,title="TVP1",url="https://cdn.example/unnamed.m3u8"},
                new {channel="Unknown.pl",title="TVP1",url="https://cdn.example/wrong-id.m3u8"},
                new {channel="TVP1.pl",title="TVP1",url="http://127.0.0.1/private"},
                new {channel="TVP1.pl",title="TVP1",url="https://youtube.com/watch?v=test"}
            });
            var parsed = ChannelSearch.ParseStreamIndex(data);
            Check("Current labels and legacy label schemas are accepted", parsed.Count==4 && parsed[0].Group=="Geo-blocked" && parsed[1].Group=="Not 24/7");
            Check("Stream headers, quality and feed are retained", parsed[0].UserAgent=="Player/1" && parsed[0].Referrer=="https://tvp1.example/" && parsed[0].Quality=="1080p" && parsed[0].Feed=="HD");
            var joined = ChannelSearch.JoinStreamIndex(parsed,directory,["PL"],"Jedynka");
            Check("Index resolves alias with authoritative country and channel", joined.Count==2 && joined.All(s=>s.ChannelId=="TVP1.pl" && s.Country=="PL"));
            Check("Unknown declared channel ID is not relabelled by similar title", !joined.Any(s=>s.Url.Contains("wrong-id")));
            Check("Selected country does not become invented channel country", ChannelSearch.JoinStreamIndex(parsed,directory,["FR"],"Jedynka").Count==0);
            var ambiguous = ChannelSearch.JoinStreamIndex(parsed.Where(s=>s.ChannelId.Length==0),[one,one with {Country="DE",ChannelId="TVP1.de"}],["PL"],"");
            Check("Ambiguous name without an ID is not assigned a country", ambiguous.Count==0);
            bool rejected = false; try { ChannelSearch.ParseStreamIndex("[{\"name\":\"wrong schema\"}]"); } catch(JsonException) { rejected=true; }
            Check("Invalid index schema is rejected",rejected);
            Check("Stream index retains public media ports",ChannelSearch.ParseStreamIndex("[{\"channel\":\"Test.pl\",\"url\":\"https://cdn.example:1935/live.m3u8\"}]").Count==1);
            var english = one with {Name="World English",ChannelId="World.de",Country="DE",Feed="EnglishHD"};
            var arabic = english with {Name="World Arabic",Feed="Arabic",Url="https://cdn.example/ar"};
            var englishSd = english with {Name="World English (480p)",Feed="EnglishSD",Url="https://cdn.example/en-sd"};
            var ranker = new SourceRanker(root);
            Check("Failover does not switch to a different language feed",ranker.Alternative([arabic,englishSd],english,[],"Stabilność")?.Url==englishSd.Url);
            Check("HD and SD of the same program remain alternatives",ChannelSearch.SameProgram(one with {Feed="HD"},one with {Feed="SD"}));
            Check("Missing feed metadata cannot switch differently named language streams",!ChannelSearch.SameProgram(english with {Feed=""},arabic with {Feed=""}));
            var roundtrip = M3u.Parse(M3u.Export([english]),"https://playlist.example/list.m3u").Single();
            Check("M3U roundtrip preserves feed identity",roundtrip.Feed==english.Feed && roundtrip.ChannelId==english.ChannelId);
            var legacyFolder=Path.Combine(root,"legacy"); Directory.CreateDirectory(legacyFolder); File.WriteAllText(Path.Combine(legacyFolder,"library.json"),"{\"Favorites\":[],\"History\":[],\"Playlist\":[]}");
            Check("Old profiles enable extra index without losing library",new LibraryStore(legacyFolder).Data.SearchIndex);
            string brokenPlayer=Path.Combine(root,"broken-player"); Directory.CreateDirectory(brokenPlayer);
            File.WriteAllText(Path.Combine(brokenPlayer,"libmpv-2.dll"),"broken"); File.WriteAllText(Path.Combine(brokenPlayer,"libmpv-2.dll.sha256"),new string('0',64));
            bool corruptIndex=false;try{SwirPlayer.VerifyEngine(brokenPlayer);}catch(InvalidDataException){corruptIndex=true;}
            Check("Corrupt Swir Player engine is rejected before native loading",corruptIndex);
            var parent=new Station("News PL","https://news.example/live","TV",Country:"PL",ChannelId:"News.pl");
            var iframeDoc=new WebDocument(parent.Url,"<iframe src='https://player.example/embed/live'></iframe><iframe src='http://127.0.0.1/player'></iframe><iframe src='https://youtube.com/embed/live'></iframe>","text/html");
            Check("Explicit embedded player accepted; private and browser-only embeds skipped",WebDiscovery.EmbeddedPlayers(iframeDoc).SequenceEqual(new[]{"https://player.example/embed/live"}));
            Task<WebDocument> EmbeddedFetch(string u,CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                string body = u.Contains("search.example") ? "<rss><channel/></rss>" : u==parent.Url ? iframeDoc.Body : u.Contains("player.example") ? "<source src='https://cdn.example/live.m3u8'>" : "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:1\n#EXTINF:4,\na.ts";
                return Task.FromResult(new WebDocument(u,body,"text/html"));
            }
            var embedded = new WebDiscovery(EmbeddedFetch).Search("News PL","TV",["PL"],[parent],[],"https://search.example/{query}",null,CancellationToken.None).GetAwaiter().GetResult();
            Check("Crawler discovers live media through explicit external player",embedded.Stations.Count==1 && embedded.Stations[0].ChannelId==parent.ChannelId && embedded.Evidence.Any(e=>e.Stage=="Osadzony odtwarzacz"));
            Check("Shared broadcaster homepage does not invent a channel identity",WebDiscovery.Identify(new("Live television","https://tvp1.example/"),[one,ten])==null);
            var alternatives = new WebDiscovery((u,c)=>u.EndsWith("dead.m3u8") ? throw new HttpRequestException("404") : Task.FromResult(new WebDocument(u,u.EndsWith("master.m3u8") ? "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1\ndead.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=2\nlive.m3u8" : "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:1\n#EXTINF:4,\na.ts","text/plain")));
            Check("Dead first HLS rendition does not hide another live rendition",alternatives.IsLive("https://cdn.example/master.m3u8",CancellationToken.None).GetAwaiter().GetResult());
            var held = new TaskCompletionSource<DiscoveryBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
            var batches = new List<DiscoveryBatch>();
            var parallel = DiscoveryPipeline.Run([
                new("slow",c=>held.Task),
                new("fast",c=>Task.FromResult(new DiscoveryBatch("fast",[one],"ok"))),
                new("broken",c=>throw new HttpRequestException("503"))
            ],b=>batches.Add(b),CancellationToken.None);
            Check("Fast results are published before slow provider completes",batches.Any(b=>b.Name=="fast") && !parallel.IsCompleted);
            held.SetResult(new("slow",[ten],"ok")); parallel.GetAwaiter().GetResult();
            Check("Provider failure does not discard other providers",batches.Count==3 && batches.Single(b=>b.Name=="broken").Detail.StartsWith("BŁĄD") && batches.Sum(b=>b.Stations.Count)==2);
            using var cancel=new CancellationTokenSource(); int published=0;
            var cancelTask=DiscoveryPipeline.Run([new("wait",async c=>{await Task.Delay(10000,c);return new("wait",[one],"");})],b=>published++,cancel.Token); cancel.Cancel();
            bool cancelled=false;try{cancelTask.GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
            Check("Cancelled search cannot publish late results",cancelled && published==0);
            int calls=0;bool invalid=false;
            var cfg=new SourceSettings{StreamDirectoryUrls=["https://index.example/streams.json"]};
            var cached=new SmartCatalog(Path.Combine(root,"cache"),cfg,(u,c)=>{calls++;return Task.FromResult((invalid?"{}":data,(DateTimeOffset?)DateTimeOffset.UtcNow));});
            var first=cached.ExtraStreams("Jedynka",["PL"],directory,true,CancellationToken.None).GetAwaiter().GetResult();
            _=cached.ExtraStreams("Jedynka",["PL"],directory,false,CancellationToken.None).GetAwaiter().GetResult();
            Check("Extra index uses fresh cache",calls==1 && first.Stations.Count==2);
            invalid=true;var recovered=cached.ExtraStreams("Jedynka",["PL"],directory,true,CancellationToken.None).GetAwaiter().GetResult();
            Check("Invalid index cannot replace correct cache",recovered.Stations.Count==2 && recovered.Detail.Contains("TRYB AWARYJNY"));
            var stale=new SmartCatalog(Path.Combine(root,"stale"),cfg,(u,c)=>Task.FromResult((data,(DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(-8))));
            bool expired=false;try{stale.ExtraStreams("",[],directory,true,CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){expired=true;}
            Check("Stale index is refused",expired);
            if(network)
            {
                using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var catalog=new SmartCatalog(Path.Combine(root,"network"));
                var actual=catalog.Broadcasters(true,deadline.Token).GetAwaiter().GetResult();
                Check("Production directory includes aliases and channels without websites",actual.Stations.Count>20000 && actual.Stations.Any(s=>s.Aliases?.Contains("ARD")==true) && actual.Stations.Any(s=>s.Url.Length==0),new{count=actual.Stations.Count,actual.Detail});
                var ard=catalog.ExtraStreams("ARD",["DE"],actual.Stations,true,deadline.Token).GetAwaiter().GetResult();
                Check("Production alias search finds Das Erste stream addresses",ard.Stations.Count>0 && ard.Stations.All(s=>s.ChannelId=="DasErste.de"),ard);
                var attempts=new List<object>();int decodedCountries=0;
                foreach(var (query,country) in new[]{("DW English","DE"),("France 24 English","FR")})
                {
                    var result=catalog.ExtraStreams(query,[country],actual.Stations,false,deadline.Token).GetAwaiter().GetResult();
                    string expectedId=country=="DE"?"DW.de":"France24.fr";
                    Check("Production specific language query: "+query,result.Stations.Count>1 && result.Stations.All(s=>s.Country==country && s.ChannelId==expectedId && (s.Name.Contains("English",StringComparison.OrdinalIgnoreCase)||ChannelSearch.ProgramVariant(s.Feed)=="english")),result);
                    foreach(var station in result.Stations.OrderByDescending(s=>s.Url.Contains("akamaized.net")||s.Url.Contains("france24.com")).Take(3))
                    {
                        var health=StreamProbe.Run(station,root,deadline.Token).GetAwaiter().GetResult(); attempts.Add(new{station.Name,station.Url,station.Country,station.Feed,health});
                        if(health.Decoded){decodedCountries++;break;}
                    }
                }
                Check("New index route actually decodes TV from two countries",decodedCountries==2,attempts);
            }
        }
        catch(Exception ex){Check("Unexpected failure",false,ex.ToString());}
        File.WriteAllText(report,JsonSerializer.Serialize(new{success,checks},new JsonSerializerOptions{WriteIndented=true})); return success?0:1;
    }
}
