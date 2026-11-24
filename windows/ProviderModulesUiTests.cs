using System.Text;
namespace SwirStreamAI;

public sealed partial class MainForm
{
    public async Task ProviderModulesUiTest(Action<string,bool,object?> check,string screenshot)
    {
        static IEnumerable<Control> All(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(All(c)));
        void Capture(Form f,string suffix){f.Refresh();using var image=new Bitmap(f.Width,f.Height);f.DrawToBitmap(image,new(0,0,f.Width,f.Height));image.Save(Path.ChangeExtension(screenshot,suffix+".png"));}
        string original=profiles.Index.ActiveId;var test=profiles.Add("Test modułów 0.12.0");await SwitchProfile(test.Id,false);
        string folder=profiles.Folder(test.Id);bool fail=false,wait=false;var received=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string m3u="#EXTM3U x-tvg-url=\"https://fixture.example/guide.xml\"\n#EXTINF:-1 tvg-id=\"one.pl\" tvg-country=\"PL\",Kanał Pierwszy\nhttps://fixture.example/one.m3u8\n#EXTINF:-1 tvg-id=\"two.de\" tvg-country=\"DE\",Kanał Drugi\nhttps://fixture.example/two.m3u8\n";
        var client=new PrivateProviderClient(async(request,ct)=>{if(fail)throw new IOException("Controlled failure");if(wait){received.TrySetResult();await release.Task.WaitAsync(ct);}ct.ThrowIfCancellationRequested();return new ProviderDocument(Encoding.UTF8.GetBytes(request.Url.EndsWith("guide.xml")?"<tv><channel id=\"one.pl\"><display-name>Kanał Pierwszy</display-name></channel></tv>":m3u),request.Url,Modified:DateTimeOffset.UtcNow.AddHours(-1));});
        privateSources=new(folder,client);
        var first=new ProviderSource(Guid.NewGuid().ToString("N"),"Moja playlista",PrivateSourceKind.M3u,"https://fixture.example/channels.m3u");
        try
        {
            var initialSize=Size;Size=MinimumSize;PerformLayout();
            var modules=All(this).OfType<Button>().Where(b=>new[]{"module-Playlists","module-Xtream","module-Mac","module-Drm"}.Contains(b.Name)).ToArray();
            check("Four module buttons visible at minimum window size without scrolling",modules.Length==4&&modules.All(b=>b.Visible&&ClientRectangle.Contains(RectangleToClient(b.RectangleToScreen(b.ClientRectangle)))),modules.Select(b=>b.Text).ToArray());
            Size=initialSize;PerformLayout();Capture(this,".main");
            bool connected=await SaveConnectSource(first,CancellationToken.None);
            check("Save and connect immediately shows downloaded channels in named module",connected&&shown.Count==2&&localTitle.StartsWith("Playlisty URL •")&&store.Data.Playlist.Count==2,null);
            Capture(this,".main-channels");
            check("Playlist handoff retains EPG URL in protected catalogue",privateSources.Sources.Single().Catalog?.EpgUrl=="https://fixture.example/guide.xml",null);
            var date=privateSources.Sources.Single().Catalog!.DownloadedAt;var displayed=shown.ToArray();fail=true;
            bool failed=await SaveConnectSource(first,CancellationToken.None);
            check("Failed quick connect preserves previous channel list and success date",!failed&&shown.SequenceEqual(displayed)&&privateSources.Sources.Single().Catalog!.DownloadedAt==date,null);fail=false;
            wait=true;using(var cancel=new CancellationTokenSource())
            {var pending=SaveConnectSource(first,cancel.Token);await received.Task.WaitAsync(TimeSpan.FromSeconds(2));cancel.Cancel();try{await pending;}catch(OperationCanceledException){}
            check("Cancelled quick connect preserves channels and dates",shown.SequenceEqual(displayed)&&privateSources.Sources.Single().Catalog!.DownloadedAt==date,null);}wait=false;
            search.Text="DRUGI";await FilterCatalog();check("Dedicated module channel search finds imported name locally",shown.Count==1&&shown[0].Name=="Kanał Drugi",null);search.Text="";await FilterCatalog();
            string localFile=Path.Combine(folder,"fixture.m3u");File.WriteAllText(localFile,m3u);
            var fixtures=new[]{first,new ProviderSource(Guid.NewGuid().ToString("N"),"Konto Xtream",PrivateSourceKind.Xtream,"https://fixture.example","fixture","fixture"),new ProviderSource(Guid.NewGuid().ToString("N"),"Portal MAC",PrivateSourceKind.Portal,"https://fixture.example/stalker_portal/",Mac:"00:1A:79:11:22:33"),new ProviderSource(Guid.NewGuid().ToString("N"),"Licencja ClearKey",PrivateSourceKind.ClearKeyDash,"https://fixture.example/dash.mpd",KeyId:"00112233445566778899aabbccddeeff",KeyHex:"ffeeddccbbaa99887766554433221100"),new ProviderSource(Guid.NewGuid().ToString("N"),"Plik M3U",PrivateSourceKind.M3uFile,localFile),new ProviderSource(Guid.NewGuid().ToString("N"),"Adres transmisji",PrivateSourceKind.DirectUrl,"https://fixture.example/live.m3u8")};
            foreach(var source in fixtures)privateSources.Put(source);
            privateSources.ImportSaved("Starsza biblioteka",[new Station("Migracja","https://fixture.example/old.m3u8")]);
            var before=privateSources.Sources.Select(s=>s.Source).ToArray();
            foreach(var module in new[]{ProviderModule.Playlists,ProviderModule.Xtream,ProviderModule.Mac,ProviderModule.Drm,ProviderModule.All})
            {
                privateDialogPreview=form=>{try{var list=All(form).OfType<ListView>().Single(l=>l.Name=="provider-sources");var actual=list.Items.Cast<ListViewItem>().Select(i=>((ProviderState)i.Tag!).Source).ToArray();check("Module "+module+" shows its existing sources only",actual.Length==before.Count(s=>ModuleContains(module,s))&&actual.All(s=>ModuleContains(module,s)),actual.Length);Capture(form,"."+module);if(module==ProviderModule.Playlists){var filter=All(form).OfType<TextBox>().Single(t=>t.Name=="provider-source-search");filter.Text="STARSZA";check("Migrated saved channels remain accessible and searchable",list.Items.Count==1&&list.Items[0].Text=="Starsza biblioteka",null);}}finally{form.Close();}};
                await ShowProviderModule(module);
            }
            foreach(var source in fixtures.Take(4))
            {
                var savedBeforeEditor=privateSources.Sources.Single(s=>s.Source.Id==source.Id).Source;
                privateDialogPreview=form=>{try{var fields=All(form).OfType<TextBox>().ToArray();bool specific=source.Kind switch{PrivateSourceKind.Xtream=>fields.Any(t=>t.Name=="source-username")&&fields.Any(t=>t.Name=="source-password")&&!fields.Any(t=>t.Name=="source-mac"),PrivateSourceKind.Portal=>fields.Any(t=>t.Name=="source-mac")&&!fields.Any(t=>t.Name=="source-password"),PrivateSourceKind.ClearKeyDash=>fields.Any(t=>t.Name=="source-kid")&&fields.Any(t=>t.Name=="source-license")&&fields.Any(t=>t.Name=="source-key"),_=>fields.Length==2};check("Dedicated "+source.Kind+" form has only relevant fields and masks secrets",specific&&!All(form).OfType<ComboBox>().Any()&&fields.Where(t=>t.Name!="source-name").All(t=>t.UseSystemPasswordChar),null);Capture(form,".editor-"+source.Kind);}finally{form.Close();}};
                var cancelled=EditOwnSource(source,source.Kind);check("Closing "+source.Kind+" editor without save does not change source",cancelled==null&&privateSources.Sources.Single(s=>s.Source.Id==source.Id).Source==savedBeforeEditor,null);
            }
            first=first with{DiscoveredPublic=true};privateSources.Put(first);
            privateDialogPreview=form=>{check("Found URL opens draft for existing public subscription rather than duplicate",All(form).OfType<TextBox>().Single(t=>t.Name=="source-name").Text==first.Name,null);form.Close();};
            int count=privateSources.Sources.Count;var selectedCountries=store.Data.SelectedCountries.ToArray();await OpenProviderDraft(PrivateSourceKind.M3u,first.Url,"Different found label");
            check("Unapproved public playlist draft does not save or change countries",privateSources.Sources.Count==count&&store.Data.SelectedCountries.SequenceEqual(selectedCountries),null);
            var publicList=PlaylistRepository.Create(new("https://fixture.example/public.m3u",m3u,"application/x-mpegurl"),[],[],"Kanał");foundPlaylists.Save(publicList);
            search.Text="Kanał Pierwszy";privateDialogPreview=form=>{try{check("Found playlist view offers in-app subscription and honest verification",All(form).OfType<Button>().Any(b=>b.Text=="Dodaj do Playlist URL")&&All(form).OfType<TextBox>().Any(t=>t.Text.Contains("Sprawdzone = odtwarzanie potwierdzone")),null);Capture(form,".found");}finally{form.Close();}};await ShowFoundPlaylists();privateDialogPreview=null;
            var learned=searchLearning.Snapshot(publicList.Stations);var order=PlaylistRecommendations.Order([publicList],"Kanał Pierwszy",["PL"],learned,new HashSet<string>());
            check("Playlist recommendation counts unique matching channel without fabricated playback",order[0].MatchingChannels==1&&order[0].VerifiedChannels==0,null);
            var duplicated=publicList with{Id="duplicates",Stations=Enumerable.Range(0,12).Select(i=>publicList.Stations[0] with{Url="https://fixture.example/"+i+".m3u8"}).ToList()};
            check("Duplicate stream addresses do not inflate recommendation channel count",PlaylistRecommendations.Order([duplicated],"",[],learned,new HashSet<string>())[0].MatchingChannels==1,null);
            store.Data.LearnPreferences=false;var neutral=searchLearning.Snapshot(publicList.Stations);
            var neutralRank=PlaylistRecommendations.Order([publicList,duplicated],"",[],neutral,new HashSet<string>());
            check("Disabled learning is neutral and does not hide playlists or renew dates",!neutral.Enabled&&neutralRank.Count==2&&neutralRank.All(r=>r.Preference==0)&&publicList.FetchedAt==foundPlaylists.Items.Single(p=>p.Id==publicList.Id).FetchedAt,null);
            wait=true;received=new(TaskCreationOptions.RunContinuationsAsynchronously);var vaultA=privateSources;var pendingSwitch=SaveConnectSource(first,CancellationToken.None);await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await SwitchProfile(original,false);release.TrySetResult();bool stale=await pendingSwitch;
            check("Async result after profile switch never loads or writes another profile",!stale&&privateSources.Sources.All(s=>s.Source.Id!=first.Id)&&shown.Count==0&&vaultA.Sources.Single(s=>s.Source.Id==first.Id).Catalog!=null,null);
            check("Profile switch clears previous private now-playing label",now.Text=="Twoje miejsce na live.",null);
        }
        finally{release.TrySetResult();privateDialogPreview=null;if(profiles.Index.ActiveId!=original)await SwitchProfile(original,false);}
    }
}
