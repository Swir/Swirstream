using System.Text;

namespace SwirStreamAI;

public sealed partial class MainForm
{
    public async Task PortalScannerUiTest(Action<string,bool,object?> check,string screenshot)
    {
        static IEnumerable<Control> All(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(All(c)));
        static Button Action(Form form,string name)=>All(form).OfType<Button>().Single(b=>b.Name=="portal-scanner-"+name);
        static ListView Results(Form form)=>All(form).OfType<ListView>().Single(l=>l.Name=="portal-scanner-results");
        static PortalTxtImport Import(string text)=>PortalTxt.Parse(Encoding.UTF8.GetBytes(text));
        static async Task Until(Func<bool> predicate){var timer=System.Diagnostics.Stopwatch.StartNew();while(!predicate()&&timer.Elapsed<TimeSpan.FromSeconds(6))await Task.Delay(20);if(!predicate())throw new TimeoutException("UI fixture timed out");}
        void Capture(Form f,string suffix){f.Refresh();using var image=new Bitmap(f.Width,f.Height);f.DrawToBitmap(image,new(0,0,f.Width,f.Height));image.Save(Path.ChangeExtension(screenshot,suffix+".png"));}
        string original=profiles.Index.ActiveId;var test=profiles.Add("Test Skanera TXT");await SwitchProfile(test.Id,false);var oldEngine=portalScanner;
        const string url="https://portal.fixture.example/c/",mac="02:00:00:00:00:98",publicUrl="https://public.fixture.example/c/";
        const string genres="{\"js\":[{\"id\":1,\"title\":\"Fixture\"}]}";
        const string channels="{\"js\":{\"data\":[{\"id\":1,\"name\":\"Kanał TXT\",\"cmd\":\"ffmpeg http://localhost/ch/1\"}]}}";
        int requests=0;
        var client=new PrivateProviderClient((request,ct)=>{ct.ThrowIfCancellationRequested();requests++;return Task.FromResult(new ProviderDocument(Encoding.UTF8.GetBytes(request.Url.Contains("action=get_genres")?genres:channels),request.Url,"application/json"));});
        privateSources=new(profiles.Folder(test.Id),client);var repository=privateSources;
        portalScanner=new(client,(u,c)=>Task.FromResult(new WebDocument(u,"<title>Stalker portal</title><script src='xpcom.common.js'></script>","text/html")),gap:TimeSpan.Zero);
        try
        {
            privateDialogPreview=form=>
            {
                form.Size=form.MinimumSize;form.PerformLayout();var entry=All(form).OfType<Button>().Single(b=>b.Name=="portal-scanner-open");
                check("TXT scanner entry is visible in MAC module at minimum size",entry.Visible&&entry.Parent!.ClientRectangle.Contains(entry.Bounds)&&form.ClientRectangle.Contains(form.RectangleToClient(entry.RectangleToScreen(entry.ClientRectangle))),null);
                Capture(form,".mac-module");form.Close();
            };
            await ShowProviderModule(ProviderModule.Mac);
            var initial=Import(url+"|"+mac+"\n"+publicUrl);
            privateDialogPreview=async form=>
            {
                try
                {
                    var list=Results(form);check("TXT preview does not connect or save",requests==0&&list.Items.Count==2&&repository.Sources.Count==0,null);
                    check("Unchecked MAC cannot be added or played",!Action(form,"add").Enabled&&!Action(form,"play").Enabled,null);
                    Action(form,"start").PerformClick();await Until(()=>Action(form,"start").Enabled);
                    var rows=list.Items.Cast<ListViewItem>().Select(i=>(PortalScanRow)i.Tag!).ToArray();
                    check("UI separates catalogue and public recognition from playback",rows.Select(r=>r.Status).SequenceEqual(new[]{PortalScanStatus.Catalog,PortalScanStatus.RecognizedPortal})&&rows[0].Channels==1&&All(form).OfType<Label>().Single(l=>l.Name=="portal-scanner-details").Text.Contains("niesprawdzone"),null);
                    check("Scanning has no vault or player side effects",repository.Sources.Count==0&&current==null&&requests==2,null);
                    form.Size=form.MinimumSize;form.PerformLayout();check("All scanner controls fit minimum window",new[]{"import","start","cancel","add","play","web","export"}.All(n=>Action(form,n).Parent!.ClientRectangle.Contains(Action(form,n).Bounds)),null);Capture(form,".results");
                    list.Items[0].Selected=true;Action(form,"add").PerformClick();
                }
                catch(Exception error){check("Scanner UI catalogue flow",false,error.GetType().Name);form.Close();}
            };
            await ShowPortalScanner(initial);
            check("Selected Add refreshes and saves one protected source",repository.Sources.Count==1&&new PrivateSourceStore(profiles.Folder(test.Id)).Sources.Count==1&&requests==4&&shown.Count==1&&shown[0].Name=="Kanał TXT",requests);
            await OpenScannerEntry(initial.Entries[0],repository,false);
            check("Repeated Add reuses same source identity",repository.Sources.Count==1&&requests==6,null);
            bool editor=false;
            privateDialogPreview=form=>
            {
                if(form.Name=="portal-scanner"){Action(form,"add").PerformClick();return;}
                var field=All(form).OfType<TextBox>().Single(t=>t.Name=="source-mac");editor=true;
                check("URL-only Add opens portal editor with blank protected MAC",field.Text==""&&field.UseSystemPasswordChar&&All(form).OfType<TextBox>().Single(t=>t.Name=="source-url").Text==publicUrl,null);form.Close();
            };
            await ShowPortalScanner(Import(publicUrl));check("Cancelled URL-only handoff makes no private request",editor&&requests==6&&repository.Sources.Count==1,null);
            bool explorer=false;privateDialogPreview=form=>
            {
                if(form.Name=="portal-scanner"){Action(form,"web").PerformClick();return;}
                explorer=true;check("Public portal discovery prefilled and portal filtered",form.Name=="source-explorer"&&All(form).OfType<TextBox>().Single(t=>t.Name=="explorer-query").Text.Contains("Stalker")&&All(form).OfType<ComboBox>().Single().SelectedIndex==1,null);form.Close();
            };
            await ShowPortalScanner();check("Public discovery handoff does not authenticate",explorer&&requests==6,null);
            var releaseImport=new TaskCompletionSource<PortalTxtImport>(TaskCreationOptions.RunContinuationsAsynchronously);int importCalls=0;
            privateDialogPreview=async form=>
            {
                try
                {
                    Action(form,"import").PerformClick();await Until(()=>importCalls==1);
                    check("Import locks scan, add, play, reimport and discovery",new[]{"start","add","play","import","web","export"}.All(n=>!Action(form,n).Enabled)&&Action(form,"cancel").Enabled,null);
                    Action(form,"start").PerformClick();Action(form,"import").PerformClick();Action(form,"add").PerformClick();
                    releaseImport.SetResult(Import(publicUrl));await Until(()=>Action(form,"import").Enabled);
                    check("Delayed import replaces preview once without network or save",importCalls==1&&Results(form).Items.Count==1&&((PortalScanRow)Results(form).Items[0].Tag!).Entry.Url==publicUrl&&requests==6&&repository.Sources.Count==1,null);
                }
                catch(Exception error){check("Scanner import race fixture",false,error.GetType().Name);}finally{form.Close();}
            };
            await ShowPortalScanner(initial,_=>{importCalls++;return releaseImport.Task;});
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var late=new TaskCompletionSource<WebDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
            portalScanner=new(page:(u,c)=>{entered.TrySetResult();return late.Task;},gap:TimeSpan.Zero);
            privateDialogPreview=async form=>
            {
                try{Action(form,"start").PerformClick();await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));await SwitchProfile(original,false);await Until(()=>form.IsDisposed||!form.Visible);late.TrySetResult(new(publicUrl,"<title>stalker</title><script src='xpcom.common.js'></script>","text/html"));}
                catch(Exception error){check("Scanner profile cancellation fixture",false,error.GetType().Name);form.Close();}
            };
            await ShowPortalScanner(Import(publicUrl));await Task.Delay(100);
            check("Profile switch cancels dialog and ignores late scan result",profiles.Index.ActiveId==original&&privateSources.Sources.Count==0&&new PrivateSourceStore(profiles.Folder(test.Id)).Sources.Count==1&&requests==6,null);
            await SwitchProfile(test.Id,false);
            var addEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var releaseAdd=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            privateSources=new(profiles.Folder(test.Id),new PrivateProviderClient(async(request,ct)=>{addEntered.TrySetResult();await releaseAdd.Task;return new(Encoding.UTF8.GetBytes(request.Url.Contains("action=get_genres")?genres:channels),request.Url,"application/json");}));
            var saving=OpenScannerEntry(Import("https://new.fixture.example/c/|02:00:00:00:00:99").Entries[0],privateSources,false);
            await addEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));await SwitchProfile(original,false);releaseAdd.TrySetResult();await saving.WaitAsync(TimeSpan.FromSeconds(3));
            check("Profile change blocks late selected Add before protected commit",privateSources.Sources.Count==0&&new PrivateSourceStore(profiles.Folder(test.Id)).Sources.Count==1,null);
        }
        finally{portalScanner=oldEngine;privateDialogPreview=null;if(profiles.Index.ActiveId!=original)await SwitchProfile(original,false);}
    }
}
