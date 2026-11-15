using System.Text.Json;

namespace SwirStreamAI;
public static class SearchLearningTests
{
    public static int Run(string report)
    {
        var checks=new List<object>();bool success=true;
        void Check(string name,bool ok,object? detail=null){checks.Add(new{name,ok,detail});success&=ok;}
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"search-learning-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        async Task Test()
        {
            var model=IntentNet.Load(Path.Combine(AppContext.BaseDirectory,"model","tastenet.json"),10);
            var profiles=new ProfileManager(root);var second=profiles.Add("Drugi profil");
            var dataA=new LibraryData();var dataB=new LibraryData();
            var a=new SearchLearning(profiles.Folder("default"),dataA,model);var b=new SearchLearning(profiles.Folder(second.Id),dataB,model);
            var good=new Station("Good News","https://good.example/live.m3u8","TV","news",Homepage:"https://good.example/",Country:"PL",ChannelId:"Good.pl");
            var bad=good with {Name="Other News",Url="https://bad.example/live.m3u8",Homepage="https://bad.example/",ChannelId="Bad.pl"};
            var cold=a.Snapshot([good,bad]);
            Check("Cold model creates no imaginary success or ranking bias",cold.Score(good)==0&&cold.Score(bad)==0&&a.ExampleCount==0&&!File.Exists(Path.Combine(root,"search-learning.json")));
            for(int i=0;i<8;i++)
            {
                var positive=good with {ChannelId="Good"+i+".pl",Url="https://good.example/"+i+".m3u8"};
                var negative=bad with {ChannelId="Bad"+i+".pl",Url="https://bad.example/"+i+".m3u8"};
                a.Record(positive,new(DateTimeOffset.UtcNow,true,350));a.Record(negative,new(DateTimeOffset.UtcNow,false,8000));
                b.Record(positive,new(DateTimeOffset.UtcNow,false,8000));
            }
            var learnedA=a.Snapshot([good,bad]);var learnedB=b.Snapshot([good,bad]);
            Check("Actual successful sources gain priority over failed sources",learnedA.Score(good)>learnedA.Score(bad),new{good=learnedA.Score(good),bad=learnedA.Score(bad)});
            Check("Different profiles learn opposite results independently",learnedA.Score(good)>learnedB.Score(good)&&a.ExampleCount==16&&b.ExampleCount==8);
            var persisted=new SearchLearning(root,dataA,model).Snapshot([good,bad]);
            Check("Source model survives restart in its own profile",Math.Abs(persisted.Score(good)-learnedA.Score(good))<.001&&new SearchLearning(profiles.Folder(second.Id),dataB,model).ExampleCount==8);
            var now=DateTimeOffset.UtcNow;var result=new Health(now,true,700);
            Check("Repeated same source result cannot inflate training",a.Record(good,result)&&!a.Record(good,result)&&!a.Record(good,result with {CheckedAt=now.AddMilliseconds(1)}));
            Check("A fresh opposite playback outcome can correct an old lesson",a.Record(good,result with {CheckedAt=now.AddMilliseconds(2),Decoded=false,StartupMs=8000}));
            int before=a.ExampleCount;
            Check("HTTP-only, browser, local and stale observations do not train",!a.Record(bad,new(now,true,0))&&!a.Record(bad with {BrowserOnly=true},new(now,true,300))&&!a.Record(bad with {Url="http://127.0.0.1/live.m3u8"},new(now,true,300))&&!a.Record(bad,new(now.AddHours(-1),true,300))&&a.ExampleCount==before);
            string beforeDisabled=File.ReadAllText(Path.Combine(root,"search-learning.json"));dataA.LearnPreferences=false;dataA.PreferredTopics=["news"];
            Check("Disable removes both preference and learned-source bias",!a.Snapshot([good,bad]).Enabled&&a.Snapshot([good,bad]).Score(good)==0&&!a.Record(bad,new(now,true,300))&&File.ReadAllText(Path.Combine(root,"search-learning.json"))==beforeDisabled);
            dataA.LearnPreferences=true;dataA.PreferredTopics=[];
            a.Reset();Check("Reset removes source lessons without touching another profile",a.ExampleCount==0&&a.Snapshot([good]).Score(good)==0&&new SearchLearning(root,dataA,model).ExampleCount==0&&new SearchLearning(profiles.Folder(second.Id),dataB,model).ExampleCount==8);

            var sport=good with {Name="Sport TV",Group="sports",ChannelId="Sport.pl"};var music=bad with {Name="Music TV",Group="music",ChannelId="Music.pl"};
            var taste=new TasteEngine(dataA,model);
            for(int i=0;i<8;i++)taste.Learn(sport,true,"test-positive-"+i);
            var tasteSnapshot=a.Snapshot([sport,music]);
            Check("Existing watched/favorite TasteNet learning changes crawl priority",tasteSnapshot.Score(sport)>tasteSnapshot.Score(music));
            double frozen=tasteSnapshot.Score(sport);dataA.PreferredTopics=["music"];
            Check("Running search uses immutable preferences snapshot",tasteSnapshot.Score(sport)==frozen&&a.Snapshot([sport,music]).Score(music)>tasteSnapshot.Score(music));

            dataA.TasteWeights=new double[512];dataA.TasteExamples=0;dataA.PreferredTopics=["sports"];
            var candidates=Enumerable.Range(0,16).Select(i=>new Station((i<8?"Favored ":"Explore ")+i,$"https://station{i}.example/","TV",i<8?"sports":"music",Homepage:$"https://station{i}.example/",Country:i%2==0?"PL":"DE",ChannelId:"C"+i)).ToList();
            var coverage=new DiscoveryCoverage(Path.Combine(root,"coverage"));foreach(var station in candidates.Take(8))coverage.Attempt(station.Url);
            var priority=a.Snapshot(candidates);var ordered=coverage.Order(candidates,priority);
            Check("Search spends first slots on preferred sources",ordered.Take(3).All(s=>s.Group=="sports"));
            Check("Every fourth slot explores least-visited sources",ordered[3].Group=="music"&&ordered[7].Group=="music",ordered.Take(8).Select(s=>new{s.Name,s.Country}));
            Check("Learning only reorders; every candidate remains exactly once",ordered.Count==candidates.Count&&ordered.Select(s=>s.Url).ToHashSet().SetEquals(candidates.Select(s=>s.Url)));
            Check("Both selected countries remain represented in the limited scan",ordered.Take(8).Select(s=>s.Country).Distinct().Count()==2);
            foreach(var station in ordered.Take(8))coverage.Attempt(station.Url);coverage.Save();
            var next=new DiscoveryCoverage(Path.Combine(root,"coverage")).Order(candidates,priority);
            Check("Exploration rotates to unseen sources across restarts",next[3].Group=="music"&&!ordered.Take(8).Any(s=>s.Url==next[3].Url));
            dataA.LearnPreferences=false;
            Check("Disabled learning returns ordinary coverage order",coverage.Order(candidates,a.Snapshot(candidates)).Select(s=>s.Url).SequenceEqual(coverage.Order(candidates).Select(s=>s.Url)));
            var wide=Enumerable.Range(0,12).Select(i=>good with {Country="C"+i,Url=$"https://country{i}.example/",ChannelId="Wide"+i}).ToList();
            var countryCoverage=new DiscoveryCoverage(Path.Combine(root,"countries"));var initial=countryCoverage.Order(wide).Take(8).ToList();foreach(var station in initial)countryCoverage.Attempt(station.Url);
            Check("Countries beyond one scan budget receive next-scan priority",countryCoverage.Order(wide).Take(4).All(s=>!initial.Any(old=>old.Url==s.Url)));

            var visits=new List<string>();dataA.LearnPreferences=true;
            bool IsSearchProvider(string url)=>new Uri(url).Host is "search.example" or "html.duckduckgo.com";
            string? SearchResponse(string url)=>new Uri(url).Host switch {"search.example"=>"<rss><channel/></rss>","html.duckduckgo.com"=>"<div class='no-results'>No results</div>",_=>null};
            Task<WebDocument> Fetch(string url,CancellationToken token){token.ThrowIfCancellationRequested();visits.Add(url);return Task.FromResult(new WebDocument(url,SearchResponse(url)??"<html>No player</html>","text/html"));}
            var scoped=await new WebDiscovery(Fetch).Search("","TV",["PL"],candidates,[],"https://search.example/{query}",null,CancellationToken.None,priority:priority);
            Check("Adaptive crawl obeys explicit selected country",visits.Where(u=>!IsSearchProvider(u)).All(u=>candidates.Single(s=>s.Url==u).Country=="PL"));
            Check("HTTP documents never create source-learning examples",a.ExampleCount==0&&scoped.Stations.Count==0&&scoped.Evidence.Any(e=>e.Stage=="Uczenie wyszukiwania"));
            visits.Clear();var exactStation=candidates[10];
            await new WebDiscovery(Fetch).Search(exactStation.Name,"TV",[exactStation.Country],candidates,[],"https://search.example/{query}",null,CancellationToken.None,priority:priority);
            Check("An exact requested channel retains precedence over personal taste",visits.First(u=>!IsSearchProvider(u))==exactStation.Url);
            var local=good with {Name="Local Channel",Url="https://shared.example/",Homepage="https://shared.example/",ChannelId="Local.pl"};
            var foreign=local with {Name="Foreign Channel",Country="DE",ChannelId="Foreign.de"};
            Task<WebDocument> SharedPage(string url,CancellationToken token)=>Task.FromResult(new WebDocument(url,SearchResponse(url)??(url.EndsWith(".m3u8")?"#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:5\n#EXTINF:4,\nsegment.ts":"<title>Foreign Channel live</title><source src='https://cdn.example/foreign.m3u8'>"),"text/html"));
            var foreignResult=await new WebDiscovery(SharedPage).Search("","TV",["PL"],[local,foreign],[],"https://search.example/{query}",null,CancellationToken.None,priority:a.Snapshot([local,foreign]));
            Check("Shared broadcaster title cannot override the selected country",foreignResult.Stations.Count==0&&foreignResult.Evidence.Any(e=>e.Detail.Contains("innego kraju")));
            string privateModel=File.ReadAllText(Path.Combine(profiles.Folder(second.Id),"search-learning.json"));
            Check("Saved availability model contains hashes and weights, not raw URLs",!privateModel.Contains("https://")&&!privateModel.Contains("Good News"));
            File.WriteAllText(Path.Combine(root,"search-learning.json"),"{broken");var recovered=new SearchLearning(root,dataA,model);
            Check("Corrupt personal model recovers without losing another profile",recovered.ExampleCount==0&&recovered.Notice.Length>0&&new SearchLearning(profiles.Folder(second.Id),dataB,model).ExampleCount==8);
            File.WriteAllText(Path.Combine(root,"search-learning.json"),JsonSerializer.Serialize(new SearchLearningState{Weights=[1],Examples=1}));
            Check("Wrong model dimension cannot influence discovery",new SearchLearning(root,dataA,model).ExampleCount==0);
        }
        try{Test().GetAwaiter().GetResult();}catch(Exception ex){Check("Unexpected failure",false,ex.ToString());}
        File.WriteAllText(report,JsonSerializer.Serialize(new{success,checks},new JsonSerializerOptions{WriteIndented=true}));return success?0:1;
    }
}
