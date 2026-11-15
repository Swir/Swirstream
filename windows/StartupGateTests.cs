using System.Text.Json;

namespace SwirStreamAI;
public static class StartupGateTests
{
    public static int Run(string report,IntentNet model)
    {
        var checks=new List<object>();bool success=true;
        void Check(string name,bool ok,object? detail=null){checks.Add(new{name,ok,detail});success&=ok;}
        string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"gate-tests-"+Guid.NewGuid().ToString("N"));
        try
        {
            bool scanning=false,finished=false;
            using(var gate=new StartupGate(()=>new MainForm(folder,model,autoDiscover:false),async form=>
            {
                scanning=true;Check("Main player hidden before scan starts",!form.Visible);
                await Task.Delay(250);Check("Main player stays hidden while scan runs",!form.Visible);
                form.SetStartupTestStations([new("Kanał testowy", "https://example.org/live.m3u8","TV",Country:"PL",ChannelId:"Test.pl")]);
                finished=true;
            }))
            {
                gate.ScanStarting+=(form,splash)=>Check("Dedicated startup window is visible and main player is hidden",splash.Visible&&!form.Visible);
                gate.PlayerShown+=form=>
                {
                    Check("Main player becomes visible only after completed scan",scanning&&finished&&form.Visible);
                    Check("Prepared channel list survives main window Shown",form.StartupStationCount==1,form.StartupStationCount);
                    form.SetAutomaticEpg(true);Check("Enabling EPG after startup restarts its timer",form.AutomaticEpgRunning);form.SetAutomaticEpg(false);
                    form.BeginInvoke(new Action(()=>form.Close()));
                };
                Application.Run(gate);
            }
            bool cancelled=false,shown=false;
            using(var gate=new StartupGate(()=>new MainForm(Path.Combine(folder,"cancel"),model,autoDiscover:false),async form=>
            {
                await Task.Delay(100);cancelled=true;
            }))
            {
                gate.ScanStarting+=(form,splash)=>splash.Close();gate.PlayerShown+=form=>{shown=true;form.Close();};Application.Run(gate);
            }
            Check("Cancelling startup closes without exposing empty main player",cancelled&&!shown);

            bool providerCancelled=false,realScanFinished=false,realPlayerShown=false;
            var cancelWatch=System.Diagnostics.Stopwatch.StartNew();
            using(var gate=new StartupGate(()=>new MainForm(Path.Combine(folder,"cancel-provider"),model,autoDiscover:false),async form=>
            {
                await form.PrepareStartupCatalog();realScanFinished=true;
            }))
            {
                gate.ScanStarting+=(form,splash)=>form.ConfigureStartupCancellationFixture(
                    ()=>splash.BeginInvoke(new Action(()=>splash.Close())),()=>providerCancelled=true);
                gate.PlayerShown+=form=>{realPlayerShown=true;form.Close();};Application.Run(gate);
            }
            Check("Cancelling startup reaches in-flight provider tokens and waits for cleanup",providerCancelled&&realScanFinished&&!realPlayerShown&&cancelWatch.Elapsed<TimeSpan.FromSeconds(3),cancelWatch.ElapsedMilliseconds);

            string source=Path.Combine(folder,"cache");Directory.CreateDirectory(source);
            var station=new Station("Zapisany kanał","https://example.org/cache.m3u8","TV",Country:"PL",ChannelId:"Cached.pl");
            string cachePath=Path.Combine(source,"last-discovery.json");
            void Cache(IEnumerable<Station?> stations,DateTimeOffset? dataAt=null,DateTimeOffset? completedAt=null,bool legacy=false,string[]? countries=null)
            {
                var cache=new Dictionary<string,object?> { ["completedAt"]=completedAt??DateTimeOffset.UtcNow,["countries"]=countries??["PL"],["sources"]=stations.Select(s=>new{station=s}).ToArray() };
                if(!legacy)cache["catalogDataAt"]=dataAt??DateTimeOffset.UtcNow;
                File.WriteAllText(cachePath,JsonSerializer.Serialize(cache));
            }
            Cache([station],legacy:true);
            using(var form=new MainForm(source,model,autoDiscover:false))
            {
                form.SetAutomaticEpg(false);Check("Legacy successful catalog restores without network",form.RestoreStartupCatalog()==1);
                Cache([station],dataAt:DateTimeOffset.UtcNow.AddDays(-8));
                Check("Fresh report completion cannot rejuvenate a stale catalog",form.RestoreStartupCatalog()==0);
                Cache([station],dataAt:DateTimeOffset.UtcNow.AddHours(-2),completedAt:DateTimeOffset.UtcNow.AddDays(-8));
                Check("Catalog origin controls freshness independently of report completion",form.RestoreStartupCatalog()==1);
                Cache([station],dataAt:DateTimeOffset.UtcNow.AddHours(1));
                Check("A future catalog origin is rejected despite a current report completion",form.RestoreStartupCatalog()==0);
                Cache([station],completedAt:DateTimeOffset.UtcNow.AddDays(-8),legacy:true);
                Check("Legacy catalogs still expire after seven days",form.RestoreStartupCatalog()==0);
                Cache([station],countries:["DE"]);
                Check("A cache for different selected countries is not relabeled",form.RestoreStartupCatalog()==0);
                Cache([station with { Kind="Radio" }]);
                Check("A radio-only cache cannot enable previous TV catalog",form.RestoreStartupCatalog()==0&&form.StartupCachedUrls.Length==0);
                Cache([station,station with { Url="https://example.org/radio",Kind="Radio" },station with { Url="https://example.org/de",Country="DE" }]);
                Check("Mixed cached media restore only the active kind and country",form.RestoreStartupCatalog()==1&&form.StartupCachedUrls.SequenceEqual(new[]{station.Url}));
                Station?[] invalid=[null,station with {Name=null!},station with {Url=null!},station with {Kind=null!},station with {ChannelId=null!},station with {Country=null!},station with {Group=null!},station with {Homepage=null!},station with {Feed=null!},station with {Quality=null!},station with {UserAgent=null!},station with {Referrer=null!},station with {Name=" "},station with {BrowserOnly=true},station with {Url="http://127.0.0.1/private"}];
                Cache(invalid.Prepend(station));
                Check("Null and malformed station fields are excluded while valid rows survive",form.RestoreStartupCatalog()==1&&form.StartupCachedUrls.SequenceEqual(new[]{station.Url}));
                Cache(invalid);
                Check("An entirely invalid station cache is unavailable without crashing",form.RestoreStartupCatalog()==0&&form.StartupCachedUrls.Length==0);
                foreach(string malformed in new[]{"{}","null","{broken",JsonSerializer.Serialize(new{completedAt=DateTimeOffset.UtcNow,countries=new[]{"PL"},sources=new[]{new{station=42}}}),JsonSerializer.Serialize(new{completedAt=DateTimeOffset.UtcNow,countries=new[]{"PL"},sources=new[]{new{missingStation=true}}})})
                {
                    File.WriteAllText(cachePath,malformed);
                    Check("Malformed previous catalog is ignored: "+malformed[..Math.Min(35,malformed.Length)],form.RestoreStartupCatalog()==0);
                }
            }
            Cache([station]);
            bool previousShown=false,previousCancelled=false;
            using(var gate=new StartupGate(()=>new MainForm(source,model,autoDiscover:false),form=>form.PrepareStartupCatalog()))
            {
                gate.ScanStarting+=(form,splash)=>form.ConfigureStartupCancellationFixture(()=>splash.BeginInvoke(new Action(()=>
                {
                    var button=Descendants(splash).OfType<Button>().Single(b=>b.Text=="Użyj poprzedniej bazy");
                    Check("Previous catalog action is available only after a valid cache restores",button.Enabled);
                    button.PerformClick();
                })),()=>previousCancelled=true);
                gate.PlayerShown+=form=>
                {
                    previousShown=true;
                    Check("Using previous catalog cancels discovery and keeps cached channels visible",previousCancelled&&form.StartupStationCount==1&&form.StartupCachedUrls.SequenceEqual(new[]{station.Url}),new{previousCancelled,form.StartupStationCount,form.StartupCachedUrls});
                    form.BeginInvoke(new Action(()=>form.Close()));
                };
                Application.Run(gate);
            }
            Check("Previous catalog action opens the main player",previousShown);
        }
        catch(Exception ex){Check("Unexpected error",false,ex.ToString());}
        File.WriteAllText(report,JsonSerializer.Serialize(new{success,checks},new JsonSerializerOptions{WriteIndented=true}));return success?0:1;
    }
    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach(Control child in parent.Controls) { yield return child;foreach(var nested in Descendants(child))yield return nested; }
    }
}
public sealed partial class MainForm
{
    public int StartupStationCount=>shown.Count;
    public bool AutomaticEpgRunning=>epgTimer.Enabled;
    public string[] StartupCachedUrls=>candidates.Select(s=>s.Url).ToArray();
    public void ConfigureStartupCancellationFixture(Action started,Action cancelled)
    {
        var settings=new SourceSettings {TvTemplates=["https://fixture.example/{path}"],ChannelDirectoryUrls=["https://fixture.example/channels.json"]};
        async Task<(string,DateTimeOffset?)> Fetch(string url,CancellationToken ct)
        {
            started();
            try { await Task.Delay(TimeSpan.FromSeconds(4),ct); }
            catch(OperationCanceledException)when(ct.IsCancellationRequested) {cancelled();throw;}
            return (url.EndsWith("channels.json")?"[]":"#EXTM3U\n#EXTINF:-1,Fixture\nhttps://fixture.example/live.m3u8",DateTimeOffset.UtcNow);
        }
        smartCatalog=new(Path.Combine(dataFolder,"cancel-fixture"),settings,Fetch);
        verify.Checked=false;webSearch.Checked=false;store.Data.SearchPlaylists=false;store.Data.SearchIndex=false;
    }
}
