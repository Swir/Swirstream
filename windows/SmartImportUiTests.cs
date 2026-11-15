using System.Text;
using System.Text.Json;
namespace SwirStreamAI;

public sealed partial class MainForm
{
    public async Task SmartImportUiTest(Action<string,bool,object?> check,string screenshot)
    {
        static IEnumerable<Control> All(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(All(c)));
        void Capture(Form f,string suffix){f.Refresh();using var bitmap=new Bitmap(f.Width,f.Height);f.DrawToBitmap(bitmap,new(0,0,f.Width,f.Height));bitmap.Save(Path.ChangeExtension(screenshot,suffix+".png"));}
        string original=profiles.Index.ActiveId;var profile=profiles.Add("Test wklejania");await SwitchProfile(profile.Id,false);
        var oldCatalog=smartCatalog;bool oldWeb=webSearch.Checked,oldVerify=verify.Checked;
        string link="https://fixture.example:8443/tenant/get.php?username=test%2Buser&password=fixture-secret%20value&type=m3u_plus";
        int providerCalls=0,publicCalls=0;var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var released=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            privateSources=new(profiles.Folder(profile.Id),new PrivateProviderClient((request,ct)=>{
                providerCalls++;ct.ThrowIfCancellationRequested();
                string body=request.Url.Contains("get_live_categories")?"[{\"category_id\":\"1\",\"category_name\":\"Sport\"}]":request.Url.Contains("get_live_streams")?"[{\"stream_id\":1,\"name\":\"Kanał Sport\",\"category_id\":\"1\"},{\"stream_id\":2,\"name\":\"Kanał Film\"}]":request.Url.Contains("xmltv.php")?"<tv><channel id=\"one\"><display-name>Kanał Sport</display-name></channel></tv>":"{\"user_info\":{\"auth\":1,\"status\":\"Active\"}}";
                return Task.FromResult(new ProviderDocument(Encoding.UTF8.GetBytes(body),request.Url));}));
            check("Prominent paste-link action is visible beside four provider modules",All(this).OfType<Button>().Any(b=>b.Name=="smart-import"&&b.Visible),null);
            privateDialogPreview=form=>{
                var fields=All(form).OfType<TextBox>().ToArray();
                if(fields.Any(t=>t.Name=="smart-url"))
                {
                    check("Smart paste previews type and host without exposing credentials or fetching",fields.Single(t=>t.Name=="smart-url").UseSystemPasswordChar&&All(form).Single(c=>c.Name=="smart-summary").Text.Contains("fixture.example:8443")&&!All(form).Single(c=>c.Name=="smart-summary").Text.Contains("fixture-secret")&&providerCalls==0,null);
                    Capture(form,".paste");All(form).OfType<Button>().Single(b=>b.Name=="smart-next").PerformClick();
                }
                else if(fields.Any(t=>t.Name=="source-username"))
                {
                    check("Smart paste fills correct Xtream server, login and masked password",fields.Single(t=>t.Name=="source-url").Text=="https://fixture.example:8443/tenant/"&&fields.Single(t=>t.Name=="source-username").Text=="test+user"&&fields.Single(t=>t.Name=="source-password").Text=="fixture-secret value"&&fields.Single(t=>t.Name=="source-password").UseSystemPasswordChar,null);
                    Capture(form,".xtream");All(form).OfType<Button>().Single(b=>b.Name=="source-save").PerformClick();
                }
            };
            await SmartImport(link);privateDialogPreview=null;
            check("Paste, approve, connect shows actual parsed channels inside Xtream module",shown.Count==2&&localTitle.StartsWith("Xtream •")&&privateSources.Sources.Count==1&&providerCalls>=3,null);
            var selectedPrivate=shown[0];results.Items[0].Selected=true;await ShowAlternatives();
            check("Imported channel variants remain in private library and report its real source count",listMode=="local"&&shown.Count==1&&shown[0].Url==selectedPrivate.Url&&SourceDetail(selectedPrivate).Contains("źródeł kanału: 1"),null);
            await ShowAllCandidates();check("All returns the complete imported list rather than public candidates",shown.Count==2&&shown.All(PrivateStation.IsPrivate)&&listMode=="local",null);
            ranker.Record(selectedPrivate,new Health(DateTimeOffset.UtcNow,true,10));await ShowWorking();
            check("Working view is scoped to locally verified imported channels",shown.Count==1&&shown[0].Url==selectedPrivate.Url&&listMode=="local",null);
            await ShowCurrentChannels();check("Channels restores imported library without starting a public search",shown.Count==2&&shown.All(PrivateStation.IsPrivate)&&!scanBusy,null);
            int before=providerCalls;search.Text="KANAŁ FILM";await SearchSubmit();
            check("Imported uppercase channel search remains local and keeps matching result",shown.Count==1&&shown[0].Name=="Kanał Film"&&providerCalls==before,null);
            var first=privateSources.Sources.Single().Source;privateSources.Put(first with{Name="Moje konto"});
            var same=SmartDraft(link.Replace("fixture-secret%20value","new-password"),"Automatycznie");
            check("Reimport same server and user reuses source ID and custom name with candidate password",same.Id==first.Id&&same.Name=="Moje konto"&&same.Password=="new-password"&&privateSources.Sources.Single().Source.Password=="fixture-secret value",null);
            string exact=link+"&routing=own-value";var manual=SmartDraft(exact,"Dokładny URL M3U");
            check("Explicit own M3U keeps entire credential URL and routing parameters",manual.Kind==PrivateSourceKind.M3u&&manual.Url==exact&&!manual.DiscoveredPublic,null);
            privateDialogPreview=form=>{var fields=All(form).OfType<TextBox>().ToArray();fields.Single(t=>t.Name=="source-url").Text=link;All(form).OfType<Button>().Single(b=>b.Name=="source-save").PerformClick();};
            var fullServer=EditOwnSource(null,PrivateSourceKind.Xtream);privateDialogPreview=null;
            check("Full account URL directly in server field normalizes at save",fullServer?.Url=="https://fixture.example:8443/tenant/"&&fullServer.Username=="test+user"&&fullServer.Password=="fixture-secret value",null);
            var cfg=new SourceSettings{TvTemplates=["https://fixture.example/{path}"],ChannelDirectoryUrls=["https://fixture.example/channels.json"]};
            async Task<(string,DateTimeOffset?)> Fetch(string url,CancellationToken ct)
            {
                publicCalls++;started.TrySetResult();await released.Task.WaitAsync(ct);
                string body=url.EndsWith("channels.json")?JsonSerializer.Serialize(Enumerable.Range(0,10).Select(i=>new{id="Safe"+i+".pl",name="Safe "+i,country="PL",website="",categories=new[]{"news"}})):"#EXTM3U\n#EXTINF:-1 tvg-id=\"Safe0.pl\",Safe 0\nhttps://fixture.example/safe.m3u8";
                return(body,DateTimeOffset.UtcNow);
            }
            smartCatalog=new(Path.Combine(dataFolder,"smart-paste-public-fixture"),cfg,Fetch);webSearch.Checked=false;verify.Checked=false;store.Data.SearchPlaylists=false;store.Data.SearchIndex=false;store.Data.SelectedCountries=["PL"];kind.SelectedItem="TV";
            privateDialogPreview=form=>form.Close();search.Text=link;await SmartSearch(true);
            check("Credential URL submitted to public search routes locally before any transport",publicCalls==0&&lastQuery.Length==0&&!search.UseSystemPasswordChar,null);
            search.Text="https://fixture.example/get.php?%75sername%00=u&%70assword%00=fixture-secret";await SmartSearch(true);
            check("Malformed credential URL also never reaches public transport",publicCalls==0&&lastQuery.Length==0,null);privateDialogPreview=null;
            search.Text="Safe";var scan=SmartSearch(true);await started.Task.WaitAsync(TimeSpan.FromSeconds(3));search.Text=link;
            check("Pasting private link during active scan masks input immediately",scanBusy&&search.UseSystemPasswordChar&&lastQuery.Length==0,null);
            released.TrySetResult();await scan;SaveDiscoveryReport();
            string report=File.ReadAllText(Path.Combine(dataFolder,"last-discovery.json"));
            check("Completing earlier scan cannot persist later credential paste in report",!report.Contains("fixture-secret")&&!report.Contains("username=")&&lastQuery.Length==0&&candidates.Any(s=>s.Name=="Safe 0"),null);
            search.Clear();await LoadOwnChannels(first.Id);Capture(this,".main");
            await StalePrivatePlaybackTest(check);
        }
        finally{released.TrySetResult();privateDialogPreview=null;smartCatalog=oldCatalog;webSearch.Checked=oldWeb;verify.Checked=oldVerify;await SwitchProfile(original,false);}
    }
    async Task StalePrivatePlaybackTest(Action<string,bool,object?> check)
    {
        var original=privateSources;var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client=new PrivateProviderClient(async(request,ct)=>{if(request.Method=="POST"){started.TrySetResult();await release.Task;throw new IOException("controlled delayed failure");}return new ProviderDocument(Encoding.UTF8.GetBytes("<MPD xmlns:cenc=\"urn:mpeg:cenc:2013\"><Period><ContentProtection cenc:default_KID=\"00112233445566778899aabbccddeeff\"/></Period></MPD>"),request.Url);});
        privateSources=new(Path.Combine(dataFolder,"stale-play-fixture"),client);
        var a=new ProviderSource(Guid.NewGuid().ToString("N"),"Delayed",PrivateSourceKind.ClearKeyDash,"https://fixture.example/dash.mpd",LicenseUrl:"https://fixture.example/license",KeyId:"00112233445566778899aabbccddeeff");
        await privateSources.Connect(a,CancellationToken.None);var pending=PlayPrivate(privateSources.Stations(a.Id).Single(),false);await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            string file=Path.Combine(dataFolder,"stale-play-local.m3u");File.WriteAllText(file,"#EXTM3U\n#EXTINF:-1,Current playback\n"+Path.Combine(AppContext.BaseDirectory,"assets","local-test.mp4"));
            var b=new ProviderSource(Guid.NewGuid().ToString("N"),"Current",PrivateSourceKind.M3uFile,file);await privateSources.Connect(b,CancellationToken.None);await PlayPrivate(privateSources.Stations(b.Id).Single(),false);timer.Stop();playback.Text="Current request remains active";release.TrySetResult();await pending;
            check("Late private playback failure cannot overwrite newer playback state",expectedPlayback&&playback.Text=="Current request remains active"&&current?.PrivateSourceId==b.Id,null);
        }
        finally{release.TrySetResult();await pending;player?.Stop();expectedPlayback=false;timer.Start();privateSources=original;}
    }
}
