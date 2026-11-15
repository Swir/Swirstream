using System.Text;

namespace SwirStreamAI;

public sealed partial class MainForm
{
    public async Task PrivateSourcesUiTest(Action<string,bool,object?> check,string screenshot)
    {
        string original=profiles.Index.ActiveId;
        var profile=profiles.Add("Kontrola własnych źródeł");await SwitchProfile(profile.Id,false);
        string marker="PRIVATE-UI-FIXTURE-090",folder=profiles.Folder(profile.Id);
        var now=DateTimeOffset.UtcNow;
        string xml=$"<tv><channel id=\"own.pl\"><display-name>Kanał kontrolny</display-name></channel><programme channel=\"own.pl\" start=\"{now.AddMinutes(-5):yyyyMMddHHmmss} +0000\" stop=\"{now.AddMinutes(30):yyyyMMddHHmmss} +0000\"><title>Program kontrolny EPG</title></programme></tv>";
        bool fail=false;
        var client=new PrivateProviderClient((request,ct)=>
        {
            ct.ThrowIfCancellationRequested();if(fail)throw new IOException("Controlled unavailable provider");
            string body=request.Url.Contains("guide")?xml:"#EXTM3U x-tvg-url=\"https://fixture.example/guide?token="+marker+"\"\n#EXTINF:-1 tvg-id=\"own.pl\",Kanał kontrolny\nhttps://fixture.example/live.m3u8?token="+marker+"\n";
            return Task.FromResult(new ProviderDocument(Encoding.UTF8.GetBytes(body),request.Url,Modified:now.AddMinutes(-2)));
        });
        privateSources=new(folder,client);
        var source=new ProviderSource(Guid.NewGuid().ToString("N"),"Źródło kontrolne • zapis prywatny",PrivateSourceKind.M3u,"https://fixture.example/get.php?token="+marker);
        try
        {
            privateSources.Put(source);var fresh=await privateSources.Refresh(source.Id,CancellationToken.None);await LoadOwnChannels(source.Id);
            check("Own sources GUI loads encrypted references into local library",shown.Count==1&&PrivateStation.IsPrivate(shown[0])&&shown[0].Name=="Kanał kontrolny",shown.Count);
            var station=shown.Single();check("Own source EPG appears through provider guide",Slot(station).Now?.Title=="Program kontrolny EPG",null);
            store.Favorite(station,true);store.Played(station);
            check("Private row address and library JSON omit source credentials",!SourceDetail(station).Contains(marker)&&!File.ReadAllText(store.FilePath).Contains(marker)&&!File.ReadAllText(store.FilePath).Contains("Kanał kontrolny"),null);
            check("Private sources excluded from public discovery and M3U",!candidates.Any(PrivateStation.IsPrivate)&&!M3u.Export(shown).Contains("swir-private")&&!M3u.Export(shown).Contains(marker),null);
            fail=true;var failed=await privateSources.Refresh(source.Id,CancellationToken.None);
            check("Failed refresh retains visible list and original success date",failed.Catalog?.DownloadedAt==fresh.Catalog?.DownloadedAt&&privateSources.Stations(source.Id).Count==1,null);
            static IEnumerable<Control> All(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(All(c)));
            void Capture(Form form,string path){form.Refresh();using var bmp=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bmp,new(0,0,form.Width,form.Height));bmp.Save(path);}
            privateDialogPreview=form=>
            {
                try{check("Own source manager exposes status and dates without endpoint",!string.Join("\n",All(form).Select(c=>c.Text)).Contains(marker)&&All(form).OfType<TextBox>().Any(t=>t.Text.Contains("Ostatnia próba")),null);Capture(form,screenshot);}
                finally{form.Close();}
            };
            await ShowPrivateSources();
            privateDialogPreview=form=>
            {
                try{var secrets=All(form).OfType<TextBox>().Where(t=>t.Text.Contains(marker)).ToArray();check("Source editor masks endpoint by default",secrets.Length==1&&secrets.All(t=>t.UseSystemPasswordChar),null);Capture(form,Path.ChangeExtension(screenshot,".editor.png"));}
                finally{form.Close();}
            };
            EditOwnSource(source);privateDialogPreview=null;
            privateDialogPreview=form=>
            {
                try{var urls=All(form).OfType<TextBox>().Where(t=>t.Text.Contains(marker)).ToArray();check("General XMLTV editor masks custom EPG URL by default",urls.Length==1&&urls[0].UseSystemPasswordChar,null);Capture(form,Path.ChangeExtension(screenshot,".epg.png"));}
                finally{form.Close();}
            };
            await ConfigureEpg("https://fixture.example/guide?token="+marker,"PL");privateDialogPreview=null;
            string file=Path.Combine(folder,"controlled-local.m3u");File.WriteAllText(file,"#EXTM3U\n#EXTINF:-1,Lokalny film kontrolny\n"+DemoPath+"\n");
            var local=new ProviderSource(Guid.NewGuid().ToString("N"),"Lokalny test prywatnego playera",PrivateSourceKind.M3uFile,file);privateSources.Put(local);
            await privateSources.Refresh(local.Id,CancellationToken.None);var localStation=privateSources.Stations(local.Id).Single();Play(localStation);
            var wait=System.Diagnostics.Stopwatch.StartNew();while(wait.Elapsed<TimeSpan.FromSeconds(15)&&(current?.PrivateSourceId!=local.Id||player!.Stats.VideoProgressSamples<2||player.Stats.AudioProgressSamples<2))await Task.Delay(200);
            check("Private reference resolves and native player decodes video plus audio",current?.PrivateSourceId==local.Id&&player!.Stats.VideoProgressSamples>=2&&player.Stats.AudioProgressSamples>=2,null);
            await Stop();store.Data.Playlist=[localStation];store.Save();var catalogBefore=candidates.ToArray();await AnalyzeOwnPlaylist();
            check("Own playlist verification resolves protected address without changing public catalog",ranker.Results.TryGetValue(localStation.Url,out var health)&&health.Decoded&&candidates.SequenceEqual(catalogBefore),null);
            await Stop();var encrypted=privateSources;await SwitchProfile(original,false);
            check("Switching profile hides other profile own sources",privateSources.Sources.All(s=>s.Source.Id!=source.Id),null);
            var reopened=new PrivateSourceStore(folder);check("Own source survives close and reload with private metadata restored",reopened.Stations(source.Id).Single().Name=="Kanał kontrolny",null);
        }
        finally{privateDialogPreview=null;if(profiles.Index.ActiveId!=original)await SwitchProfile(original,false);}
    }
}
