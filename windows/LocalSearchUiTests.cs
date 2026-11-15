namespace SwirStreamAI;

public sealed partial class MainForm
{
    public async Task LocalSearchUiTest(Action<string,bool,object?> check)
    {
        var oldCandidates=candidates; var oldDirectory=knownChannels; var oldCatalog=smartCatalog;
        var oldCountries=store.Data.SelectedCountries; string oldKind=kind.Text,oldQuery=search.Text,oldMode=listMode,oldTopic=store.Data.TopicFilter,oldLocalTitle=localTitle;
        var oldLocal=localStations; bool oldWeb=webSearch.Checked,oldVerify=verify.Checked,oldLists=store.Data.SearchPlaylists,oldIndex=store.Data.SearchIndex;
        int requests=0;
        try
        {
            var tv=new Station("TVP 1","https://cdn.example/tvp1.m3u8","TV",Country:"PL",ChannelId:"TVP1.pl");
            var dw=new Station("DW English","https://cdn.example/dw.m3u8","TV",Country:"DE",ChannelId:"DWEnglish.de");
            var radio=new Station("Polskie Radio Jedynka","https://cdn.example/radio","Radio",Country:"PL",ChannelId:"radio-uuid");
            candidates=[tv,tv with {Name="TVP 10",ChannelId="TVP10.pl",Url="https://cdn.example/tvp10"},dw,radio];
            knownChannels=[dw with {Name="DW",ChannelId="DW.de"}];
            store.Data.SelectedCountries=[]; store.Data.TopicFilter="all"; kind.SelectedItem="TV"; listMode="top";
            search.Text="DW"; await FilterCatalog();
            check("Local UI finds a downloaded legacy ID despite the current global directory",shown.Count==1&&shown[0].ChannelId=="DWEnglish.de",shown.Select(s=>s.ChannelId).ToArray());
            search.Text="TVP 1";
            check("Typing keeps TVP 1 distinct from TVP 10",shown.Count==1&&shown[0].ChannelId=="TVP1.pl",null);
            search.Clear(); kind.SelectedItem="Radio";
            check("Changing media type immediately filters downloaded channels",shown.Count==1&&shown[0].Kind=="Radio"&&!scanBusy,null);
            kind.SelectedItem="TV";store.Data.SelectedCountries=["PL"];await FilterCatalog();
            check("Country filter remains effective for the downloaded catalog",shown.Count==2&&shown.All(s=>s.Country=="PL"),null);
            var imported=M3u.Parse("#EXTM3U\n#EXTINF:-1,Unique Local\nhttps://cdn.example/imported.m3u8","fixture.m3u",true);
            foreach(string title in new[]{"MOJA PLAYLISTA","ULUBIONE","HISTORIA"})
            {
                await ShowLocal(title,imported);search.Text="Unique";
                check("Typing searches the active "+title+" library, including imported Live without a country",shown.Count==1&&shown[0].Name=="Unique Local"&&resultTitle.Text.StartsWith(title),shown.Select(s=>new{s.Name,s.Kind,s.Country}).ToArray());
                search.Text="Missing";check("Unmatched library search does not fall back to the catalog: "+title,shown.Count==0,null);
                search.Clear();check("Clearing restores the same library: "+title,shown.Count==1&&resultTitle.Text.StartsWith(title),null);
            }
            await ShowLocal("MOJA PLAYLISTA",Enumerable.Range(0,151).Select(i=>imported[0] with {Name="Import "+i,Url="https://cdn.example/import/"+i}).ToList());
            await ChangePage(1);check("Local library paging retains its own source",shown.Count==51&&shown.All(s=>s.Name.StartsWith("Import ")),pageCaption.Text);
            search.Text="Import 150";check("Search covers the whole library beyond the current page",shown.Count==1&&shown[0].Name=="Import 150",null);

            var cfg=new SourceSettings{TvTemplates=["https://fixture.example/{path}"],ChannelDirectoryUrls=["https://fixture.example/channels.json"]};
            smartCatalog=new(Path.Combine(dataFolder,"local-search-fixture"),cfg,(u,c)=>{requests++;return Task.FromResult((u.EndsWith("channels.json")?"[{\"id\":\"TVP1.pl\",\"name\":\"TVP 1\",\"country\":\"PL\"}]":"#EXTM3U\n#EXTINF:-1 tvg-id=\"TVP1.pl\",TVP 1\nhttps://cdn.example/tvp1.m3u8",(DateTimeOffset?)DateTimeOffset.UtcNow));});
            webSearch.Checked=false;verify.Checked=false;store.Data.SearchPlaylists=false;store.Data.SearchIndex=false;
            listMode="top";search.Text="znajdź polskie kanały TVP1";await FilterCatalog();
            check("Natural request finds a downloaded channel without network",shown.Count==1&&shown[0].ChannelId=="TVP1.pl"&&requests==0,lastQuery);
            await SmartSearch(true);
            check("Finishing a named scan preserves the interpreted query and visible channel",shown.Count==1&&shown[0].ChannelId=="TVP1.pl"&&lastQuery=="tvp1"&&search.Text=="tvp1"&&!scanBusy,new{lastQuery,requests});
            int before=requests;await ShowLocal("ULUBIONE",[radio]);search.Text="Polskie Radio";await FilterCatalog();
            check("Library filtering and catalog kind changes make no network calls",shown.Count==1&&requests==before,null);
        }
        finally
        {
            candidates=oldCandidates;knownChannels=oldDirectory;smartCatalog=oldCatalog;localStations=oldLocal;localTitle=oldLocalTitle;
            applyingProfile=true;kind.SelectedItem=oldKind;applyingProfile=false;store.Data.SelectedCountries=oldCountries;store.Data.TopicFilter=oldTopic;
            webSearch.Checked=oldWeb;verify.Checked=oldVerify;store.Data.SearchPlaylists=oldLists;store.Data.SearchIndex=oldIndex;
            changingSearch=true;search.Text=oldQuery;changingSearch=false;lastQuery=ChannelSearch.LocalQuery(oldQuery,candidates.Concat(knownChannels));listMode=oldMode;resultPage=0;RefreshDiscoveryList();
        }
    }
}
