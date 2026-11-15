using System.Text;
namespace SwirStreamAI;

public sealed partial class MainForm
{
    public async Task SourceExplorerUiTest(Action<string,bool,object?> check,string screenshot)
    {
        static IEnumerable<Control> All(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(All(c)));
        void Capture(Form f,string suffix){f.Refresh();using var image=new Bitmap(f.Width,f.Height);f.DrawToBitmap(image,new(0,0,f.Width,f.Height));image.Save(Path.ChangeExtension(screenshot,suffix+".png"));}
        string original=profiles.Index.ActiveId;var test=profiles.Add("Test Eksploratora");await SwitchProfile(test.Id,false);var oldEngine=sourceExplorer;
        const string root="https://fixture.example/",portal=root+"stalker_portal/c/index.html",playlist=root+"channels.m3u";
        string m3u="#EXTM3U\n#EXTINF:-1 tvg-id=\"one.pl\" tvg-country=\"PL\",Kanał Eksploratora\nhttps://fixture.example/one.m3u8\n";
        int fetches=0;bool wait=false;var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var resume=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sourceExplorer=new(async(url,ct)=>{fetches++;if(wait){entered.TrySetResult();await resume.Task.WaitAsync(ct);}ct.ThrowIfCancellationRequested();return new(url,url==root?"<html><title>Publiczne źródła</title><a href='channels.m3u'>Lista</a><a href='stalker_portal/c/index.html'>Portal</a><a href='live.m3u8'>Live</a></html>":url==portal?"<title>Stalker portal</title><script src='xpcom.common.js'></script>":url==playlist?m3u:"#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6,\nsegment.ts", "text/html",DateTimeOffset.UtcNow.AddHours(-1));});
        privateSources=new(profiles.Folder(test.Id),new PrivateProviderClient((request,ct)=>Task.FromResult(new ProviderDocument(Encoding.UTF8.GetBytes(m3u),request.Url))));
        string[] selectedCountries=store.Data.SelectedCountries.ToArray();
        try
        {
            var initialSize=Size;Size=MinimumSize;PerformLayout();var open=All(this).OfType<Button>().Single(b=>b.Name=="source-explorer-open");
            check("Explorer entry visible alongside all four modules at minimum size",open.Visible&&ClientRectangle.Contains(RectangleToClient(open.RectangleToScreen(open.ClientRectangle)))&&All(this).OfType<Button>().Count(b=>new[]{"module-Playlists","module-Xtream","module-Mac","module-Drm"}.Contains(b.Name))==4,null);Size=initialSize;Capture(this,".main");
            bool editorSeen=false;privateDialogPreview=async form=>
            {
                try
                {
                    if(form.Name!="source-explorer")
                    {var mac=All(form).OfType<TextBox>().Single(t=>t.Name=="source-mac");check("Portal handoff prefills exact published URL and no MAC or credentials",mac.Text==""&&All(form).OfType<TextBox>().Single(t=>t.Name=="source-url").Text==portal&&mac.UseSystemPasswordChar,null);editorSeen=true;Capture(form,".portal");form.Close();return;}
                    All(form).OfType<TextBox>().Single(t=>t.Name=="explorer-query").Text=root;All(form).OfType<Button>().Single(b=>b.Name=="explorer-analyze").PerformClick();
                    var list=All(form).OfType<ListView>().Single();var timeout=System.Diagnostics.Stopwatch.StartNew();while(list.Items.Count<4&&timeout.Elapsed<TimeSpan.FromSeconds(8))await Task.Delay(25);
                    var rows=list.Items.Cast<ListViewItem>().Select(i=>(SourceCandidate)i.Tag!).ToArray();
                    check("Analyze page UI shows playlist, portal, manifest and page with honest status",rows.Length==4&&rows.Select(x=>x.Kind).Distinct().Count()==4&&rows.All(x=>!x.Status.Contains("potwierdzono odtwarzanie",StringComparison.OrdinalIgnoreCase)),rows.Select(x=>x.Kind.ToString()).ToArray());
                    Capture(form,".results");var filter=All(form).OfType<ComboBox>().Single();filter.SelectedIndex=1;
                    check("Explorer type filter retains only portal without refreshing dates",list.Items.Count==1&&((SourceCandidate)list.Items[0].Tag!).CheckedAt==rows.Single(r=>r.Kind==SourceCandidateKind.PortalCandidate).CheckedAt,null);
                    var details=All(form).OfType<TextBox>().Single(t=>t.Name=="explorer-details");check("Explorer details expose provenance and explicitly require registered MAC",details.Text.Contains(portal)&&details.Text.Contains("MAC")&&details.Text.Contains("Znaleziono na")&&details.Text.Contains("Data dokumentu"),null);
                    All(form).OfType<Button>().Single(b=>b.Name=="explorer-import").PerformClick();
                }
                catch(Exception ex){check("Explorer UI flow completes",false,ex.ToString());form.Close();}
            };
            await ShowSourceExplorer();check("Cancelling portal editor does not authenticate or save a source",editorSeen&&privateSources.Sources.Count==0&&fetches==4&&store.Data.SelectedCountries.SequenceEqual(selectedCountries),fetches);
            var saved=new SourceExplorerStore(profiles.Folder(test.Id));var before=saved.Items.ToArray();
            check("Explorer results survive reopen in same profile",before.Length==4&&before.All(x=>x.CheckedAt<=DateTimeOffset.UtcNow),null);
            int beforeInvalid=fetches;privateDialogPreview=async form=>{try{var q=All(form).OfType<TextBox>().Single(t=>t.Name=="explorer-query");q.Text="https://fixture.example/get.php?username=fixture&password=fixture-secret";All(form).OfType<Button>().Single(b=>b.Name=="explorer-analyze").PerformClick();await Task.Delay(100);check("Explorer private URL is masked and rejected before transport",q.UseSystemPasswordChar&&fetches==beforeInvalid,null);}finally{form.Close();}};await ShowSourceExplorer();
            privateDialogPreview=form=>{All(form).OfType<Button>().SingleOrDefault(b=>b.Name=="source-save")?.PerformClick();};
            await ImportExplorerCandidate(before.Single(x=>x.Kind==SourceCandidateKind.PublicPlaylist),privateSources);
            check("Explorer playlist imports inside app with public sanitation on refresh",privateSources.Sources.Count==1&&privateSources.Sources[0].Source.DiscoveredPublic&&shown.Single().Name=="Kanał Eksploratora",null);
            search.Text="EKSPLORATORA";await FilterCatalog();check("Imported explorer channel remains searchable locally",shown.Count==1&&shown[0].Name=="Kanał Eksploratora",null);
            var draft=ExplorerDraft(before.Single(x=>x.Kind==SourceCandidateKind.DirectStream));check("Clear manifest handoff uses internal direct player source without browser",draft.Kind==PrivateSourceKind.DirectUrl&&draft.Url==root+"live.m3u8",null);
            var protectedDash=before[0] with{Kind=SourceCandidateKind.DirectStream,Url=root+"protected.mpd",RequiresDrm=true};var drm=ExplorerDraft(protectedDash);
            check("Protected DASH handoff requires separately supplied licensed configuration",drm.Kind==PrivateSourceKind.ClearKeyDash&&drm.KeyHex.Length==0&&drm.LicenseUrl.Length==0,null);
            bool rejected=false;try{ExplorerDraft(protectedDash with{Url=root+"protected.m3u8"});}catch(InvalidDataException){rejected=true;}check("Protected HLS is not silently passed to clear playback",rejected,null);
            check("Smart paste recognizes explicit Stalker client but not generic c path",SmartDraft(portal,"Automatycznie").Kind==PrivateSourceKind.Portal&&SmartDraft(root+"c/","Automatycznie").Kind==PrivateSourceKind.M3u&&SmartDraft(root+"c/","MAC Player").Mac=="",null);
            wait=true;privateDialogPreview=async form=>{try{All(form).OfType<TextBox>().Single(t=>t.Name=="explorer-query").Text=root;All(form).OfType<Button>().Single(b=>b.Name=="explorer-analyze").PerformClick();await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));await SwitchProfile(original,false);resume.TrySetResult();await Task.Delay(100);}finally{form.Close();}};
            await ShowSourceExplorer();check("Late explorer result cannot write or import into another profile",new SourceExplorerStore(profiles.Folder(original)).Items.Count==0&&new SourceExplorerStore(profiles.Folder(test.Id)).Items.SequenceEqual(before),null);
        }
        finally{resume.TrySetResult();sourceExplorer=oldEngine;privateDialogPreview=null;if(profiles.Index.ActiveId!=original)await SwitchProfile(original,false);}
    }
}
