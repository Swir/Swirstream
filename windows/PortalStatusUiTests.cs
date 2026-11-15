using System.Text;

namespace SwirStreamAI;

public sealed partial class MainForm
{
    public async Task PortalStatusUiTest(Action<string,bool,object?> check,string screenshot)
    {
        static IEnumerable<Control> All(Control p)=>p.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(All(c)));
        string original=profiles.Index.ActiveId;var test=profiles.Add("Test stanu portalu");await SwitchProfile(test.Id,false);
        bool rejected=true,linkDenied=false;
        const string secret="PRIVATE-PORTAL-STATUS-FIXTURE";
        var client=new PrivateProviderClient((request,ct)=>
        {
            ct.ThrowIfCancellationRequested();
            string action=new Uri(request.Url).Query.TrimStart('?').Split('&').Select(x=>x.Split('=',2)).Where(x=>x[0]=="action").Select(x=>x[1]).Single();
            if(action=="get_genres")return Task.FromResult(new ProviderDocument([],request.Url,Status:401));
            string body=action switch
            {
                "handshake"=>"{\"js\":{\"token\":\""+secret+"\"}}",
                "get_profile"=>rejected?"{\"js\":{\"id\":0,\"status\":1}}":"{\"js\":{\"id\":1,\"status\":0}}",
                "get_all_channels"=>"{\"js\":{\"data\":[{\"id\":\"1\",\"name\":\"Kanał z portalu\",\"cmd\":\"ffmpeg http://localhost/ch/1\"}]}}",
                "create_link"=>"{\"js\":{\"cmd\":\"ffmpeg https://fixture.example/live.m3u8\"}}",
                _=>throw new InvalidOperationException()
            };
            return Task.FromResult(new ProviderDocument(Encoding.UTF8.GetBytes(body),request.Url,Status:action=="create_link"&&linkDenied?403:200));
        });
        privateSources=new(profiles.Folder(test.Id),client);
        var source=new ProviderSource(Guid.NewGuid().ToString("N"),"Portal kontrolny",PrivateSourceKind.Portal,"https://fixture.example/c/",Mac:"02:00:00:00:00:01");
        try
        {
            candidates=[new("Publiczny kanał","https://fixture.example/public.m3u8")];
            listMode="top";ShowStations("Wyniki",candidates);
            check("Public results identify their origin before any portal connection",resultTitle.Text.StartsWith("Katalog publiczny •"),null);
            bool connected=await SaveConnectSource(source,CancellationToken.None);
            check("Rejected portal replaces misleading public view with its own empty list",!connected&&shown.Count==0&&listMode=="local"&&localTitle.StartsWith("MAC Player •")&&resultTitle.Text.Contains("BRAK KANAŁÓW")&&candidates.Count==1,null);
            check("Profile failure is visible without exposing session data",details.Text.Contains("profil",StringComparison.OrdinalIgnoreCase)&&!details.Text.Contains(secret)&&!status.Text.Contains("fixture.example"),null);
            SavePreview(Path.ChangeExtension(screenshot,".main.png"));
            search.Text="kanał";await FilterCatalog();
            check("Searching an empty rejected portal cannot fall back to public results",shown.Count==0&&details.Text.Contains("Nie pobrano kanałów tego źródła")&&status.Text.Contains("Nie pobrano kanałów tego źródła"),new{shown.Count,details.Text});
            privateDialogPreview=form=>
            {
                try
                {
                    var list=All(form).OfType<ListView>().Single(l=>l.Name=="provider-sources");
                    var info=All(form).OfType<TextBox>().Single(t=>t.Name=="provider-status");
                    check("MAC module explicitly displays zero channels and rejected source state",list.Items.Count==1&&list.Items[0].SubItems[2].Text=="0"&&list.Items[0].SubItems[4].Text.Contains("Brak dostępu")&&info.Text.Contains("profil",StringComparison.OrdinalIgnoreCase),null);
                    form.Refresh();using var bmp=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bmp,new(0,0,form.Width,form.Height));bmp.Save(screenshot);
                }
                finally{form.Close();}
            };
            await ShowProviderModule(ProviderModule.Mac);privateDialogPreview=null;
            rejected=false;connected=await SaveConnectSource(source,CancellationToken.None);
            var state=privateSources.Sources.Single();var savedDate=state.Catalog!.DownloadedAt;
            check("Successful reconnect opens portal channels and clears the earlier catalog failure",connected&&shown.Count==1&&PrivateStation.IsPrivate(shown[0])&&state.CatalogFailure==null&&!localTitle.Contains("BRAK KANAŁÓW"),null);
            linkDenied=true;int history=store.Data.History.Count;await PlayPrivate(shown[0],false);
            state=privateSources.Sources.Single();
            check("Channel-link rejection stays visible and does not fabricate playback history",state.PlaybackFailure?.Stage=="portal_link"&&state.Catalog?.DownloadedAt==savedDate&&shown.Count==1&&store.Data.History.Count==history&&playback.Text.Contains("odrzu",StringComparison.OrdinalIgnoreCase),null);
            privateDialogPreview=form=>
            {
                try{var info=All(form).OfType<TextBox>().Single(t=>t.Name=="provider-status");check("Source module separates last playback failure from successful catalog download",info.Text.Contains("Ostatnia próba kanału")&&info.Text.Contains("Ostatni zapis kanałów")&&!info.Text.Contains(secret),null);}
                finally{form.Close();}
            };
            await ShowProviderModule(ProviderModule.Mac);privateDialogPreview=null;
            rejected=true;await SaveConnectSource(source,CancellationToken.None);
            check("Failed refresh shows only the prior private catalog with its original date",shown.Count==1&&PrivateStation.IsPrivate(shown[0])&&privateSources.Sources.Single().Catalog?.DownloadedAt==savedDate&&status.Text.Contains("zapisane kanały"),null);
            await SwitchProfile(original,false);
            check("Profile switch clears the private empty-source explanation",localProviderMessage.Length==0&&shown.Count==0,null);
        }
        finally{privateDialogPreview=null;if(profiles.Index.ActiveId!=original)await SwitchProfile(original,false);}
    }
}
