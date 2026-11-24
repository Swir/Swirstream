using static SwirStreamAI.UiLanguage;
namespace SwirStreamAI;

public sealed partial class MainForm
{
    PlaylistRepository foundPlaylists = null!;
    void InitFoundPlaylists()
    {
        foundPlaylists = new(dataFolder);
        smartCatalog.PlaylistFound = foundPlaylists.Save;
    }
    async Task ShowFoundPlaylists()
    {
        FoundPlaylist? providerDraft=null;bool searchAgain=false;string query=search.Text.Trim();
        var learning=searchLearning;var countries=store.Data.SelectedCountries.ToArray();
        var preferences=await Task.Run(()=>learning.Snapshot(foundPlaylists.Items.SelectMany(p=>p.Stations).Where(s=>(countries.Length==0||countries.Contains(s.Country))&&ChannelSearch.Matches(query,s)).DistinctBy(SearchLearning.CandidateKey)));
        if(closing||!ReferenceEquals(learning,searchLearning))return;
        using var dialog = new Form { Text = T("Znalezione playlisty • SwirStream AI"), Size = new(1040,700), MinimumSize = new(850,580), Font = Font, BackColor = Card, ForeColor = Color.White, StartPosition = FormStartPosition.CenterParent };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new(SizeType.Absolute,52));layout.RowStyles.Add(new(SizeType.Absolute,45));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,150));layout.RowStyles.Add(new(SizeType.Absolute,132));
        var intro = Label("Publiczne playlisty • dopasowanie, świeżość, potwierdzone źródła i preferencje AI",13,Color.White,true);
        layout.Controls.Add(intro,0,0);
        var filters = new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false };
        var chosen = new CheckBox { Text=T("Tylko wybrane kraje: ")+(store.Data.SelectedCountries.Length==0?"świat":string.Join(',',store.Data.SelectedCountries)),Checked=true,AutoSize=true,ForeColor=Cyan,Margin=new(0,8,15,0) };
        var working = new CheckBox { Text=T("Tylko ostatnio sprawdzone"),AutoSize=true,ForeColor=Color.White,Margin=new(0,8,15,0) };
        filters.Controls.AddRange([chosen,working]);layout.Controls.Add(filters,0,1);
        var list=new ListView { Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false,HideSelection=false,BackColor=Bg,ForeColor=Color.White,Font=Font,BorderStyle=BorderStyle.None };
        list.Name="found-playlists";list.Columns.Add(T("Playlista"),310);list.Columns.Add(T("Kanały"),75);list.Columns.Add("Pasuje",75);list.Columns.Add("W krajach",90);list.Columns.Add(T("Sprawdzone"),100);list.Columns.Add("Pobrano",140);
        layout.Controls.Add(list,0,2);
        var detail=new TextBox { Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Card,ForeColor=Muted,BorderStyle=BorderStyle.None,Font=Font };layout.Controls.Add(detail,0,3);
        FoundPlaylist? SelectedPlaylist()=>list.SelectedItems.Count>0?list.SelectedItems[0].Tag as FoundPlaylist:null;
        string[] CountriesFilter()=>chosen.Checked?store.Data.SelectedCountries:[];
        HashSet<string> Verified()=>ranker.Results.Where(x=>DiscoveryAnalyzer.FreshSuccess(x.Value)).Select(x=>x.Key).ToHashSet();
        List<Station> Selection(FoundPlaylist p)=>foundPlaylists.GetStations(p.Id,CountriesFilter(),verifiedUrls:working.Checked?Verified():null);
        void Describe()
        {
            var p=SelectedPlaylist();
            detail.Text=p==null?"Wyszukaj kanały z włączoną opcją list IPTV. Znalezione playlisty pojawią się tutaj.\r\n"+foundPlaylists.Notice
                :p.Freshness+"\r\nŹródło: "+p.SourceUrl+"\r\nZapytanie: "+(query.Length>0?query:"wszystkie kanały")+" • kolejność uwzględnia unikalne kanały, kraje i "+(preferences.Enabled?"lokalne preferencje AI":"neutralne preferencje (uczenie wyłączone)")+".\r\n"+Selection(p).Count+" adresów w zakresie. Dodaj do Playlist URL otwiera formularz, a zatwierdzenie pobiera pełną listę i EPG.\r\n"+
                "Sprawdzone = odtwarzanie potwierdzone w ostatnich 10 minutach. Pozostałe adresy wymagają próby."+(p.Sanitized?"\r\nOczyszczona kopia: pominięto wpisy "+p.RemovedEntries+". SHA256 otrzymanej odpowiedzi: "+p.OriginalSha256:"")+(p.EpgUrls.Length>0?"\r\nEPG z playlisty: "+string.Join(", ",p.EpgUrls)+" • przycisk Ustaw EPG pozwala wybrać ramówkę.":"");
        }
        void Fill()
        {
            string? previous=SelectedPlaylist()?.Id;var confirmed=Verified();list.BeginUpdate();list.Items.Clear();
            foreach(var recommendation in PlaylistRecommendations.Order(foundPlaylists.Items,query,CountriesFilter(),preferences,confirmed))
            {
                var p=recommendation.Playlist;
                var entries=foundPlaylists.GetStations(p.Id,CountriesFilter());
                var row=new ListViewItem(p.Name+(p.IsStale?" • stary zapis":"")){Tag=p};
                row.SubItems.Add(p.ChannelCount.ToString());row.SubItems.Add(recommendation.MatchingChannels.ToString());row.SubItems.Add(entries.Select(SourceRanker.ChannelKey).Distinct().Count().ToString());
                row.SubItems.Add(entries.Where(s=>confirmed.Contains(s.Url)).Select(SourceRanker.ChannelKey).Distinct().Count().ToString());row.SubItems.Add(p.FetchedAt.ToLocalTime().ToString("dd.MM HH:mm"));list.Items.Add(row);
            }
            list.EndUpdate();if(list.Items.Count>0)(list.Items.Cast<ListViewItem>().FirstOrDefault(i=>((FoundPlaylist)i.Tag!).Id==previous)??list.Items[0]).Selected=true;Describe();
        }
        chosen.CheckedChanged+=(_,_)=>Fill();working.CheckedChanged+=(_,_)=>Describe();list.SelectedIndexChanged+=(_,_)=>Describe();
        var actions=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=true };layout.Controls.Add(actions,0,4);
        actions.Controls.Add(Button(T("Dodaj do Playlist URL"),()=>{if(SelectedPlaylist() is {} p){providerDraft=p;dialog.Close();}return Task.CompletedTask;},true,205));
        actions.Controls.Add(Button(T("Wczytaj do programu"),()=>
        {
            if(SelectedPlaylist() is not {} p)return Task.CompletedTask;var stations=Selection(p);
            if(stations.Count==0){detail.Text="Brak kanałów pasujących do filtrów. Zmień kraje lub wyłącz Tylko sprawdzone.";return Task.CompletedTask;}
            ImportFoundStations(stations,p.Name,p.ModifiedAt is {} modified && modified<p.FetchedAt?modified:p.FetchedAt);dialog.Close();return Task.CompletedTask;
        },true,190));
        var download=Button(T("Pobierz oryginał M3U"),()=>
        {
            if(SelectedPlaylist() is not {} p)return Task.CompletedTask;
            using var save=new SaveFileDialog { Filter="Playlista M3U|*.m3u",FileName="SwirStream-playlista.m3u" };
            if(save.ShowDialog(dialog)==DialogResult.OK){foundPlaylists.ExportOriginal(p.Id,save.FileName);detail.Text="Zapisano oryginalną playlistę: "+save.FileName;}return Task.CompletedTask;
        },width:205);actions.Controls.Add(download);
        list.SelectedIndexChanged+=(_,_)=>download.Text=SelectedPlaylist() is {Sanitized:true}?"Pobierz oczyszczoną kopię":T("Pobierz oryginał M3U");
        actions.Controls.Add(Button("Zapisz wybrane M3U",()=>
        {
            if(SelectedPlaylist() is not {} p)return Task.CompletedTask;
            if(Selection(p).Count==0){detail.Text="Brak kanałów pasujących do filtrów.";return Task.CompletedTask;}
            using var save=new SaveFileDialog { Filter="Playlista M3U|*.m3u",FileName="SwirStream-wybrane.m3u" };
            if(save.ShowDialog(dialog)==DialogResult.OK){foundPlaylists.ExportFiltered(p.Id,save.FileName,CountriesFilter(),verifiedUrls:working.Checked?Verified():null);detail.Text="Zapisano wybrane kanały: "+save.FileName;}return Task.CompletedTask;
        },width:190));
        var refresh=Button(T("Odśwież playlistę"),async()=>
        {
            if(SelectedPlaylist() is not {} p)return;
            actions.Enabled=false;detail.Text=T("Pobieram aktualną wersję playlisty…");
            try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));var updated = await foundPlaylists.Refresh(p.Id,knownChannels,CountriesFilter(),"",timeout.Token);
                if(!dialog.IsDisposed)
                {
                    Fill();
                    var previous = p.Stations.Select(s=>s.Url).ToHashSet();
                    var current = updated.Stations.Select(s=>s.Url).ToHashSet();
                    detail.Text = $"Odświeżono playlistę. Dodane adresy: {current.Except(previous).Count()}, usunięte: {previous.Except(current).Count()}.\r\n" + detail.Text;
                }}
            catch(Exception ex){if(!dialog.IsDisposed)detail.Text="Nie udało się odświeżyć. Zachowano poprzednią listę.\r\n"+ex.Message;}
            finally{if(!dialog.IsDisposed)actions.Enabled=true;}
        },width:175);actions.Controls.Add(refresh);
        actions.Controls.Add(Button(T("Ustaw EPG playlisty"),()=>
        {
            if(SelectedPlaylist() is not {} p)return Task.CompletedTask;
            if(p.EpgUrls.Length==0){detail.Text="Ta playlista nie podaje adresu EPG. Program używa ramówek dla wybranych krajów.";return Task.CompletedTask;}
            string url=p.EpgUrls[0];
            if(p.EpgUrls.Length>1)
            {
                using var choose=new Form {Text="Wybierz ramówkę z playlisty",ClientSize=new(680,140),StartPosition=FormStartPosition.CenterParent,BackColor=Card,ForeColor=Color.White};
                var urls=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Left=20,Top=20,Width=640};urls.Items.AddRange(p.EpgUrls);urls.SelectedIndex=0;
                choose.Controls.Add(urls);choose.Controls.Add(new Button {Text="Wybierz",DialogResult=DialogResult.OK,Left=520,Top=80,Width=140});
                if(choose.ShowDialog(dialog)!=DialogResult.OK)return Task.CompletedTask;url=(string)urls.SelectedItem!;
            }
            string? country=Selection(p).Select(s=>s.Country).FirstOrDefault(c=>c.Length==2);
            return ConfigureEpg(url,country);
        },width:195));
        actions.Controls.Add(Button("Szukaj nowych playlist",()=>{searchAgain=true;dialog.Close();return Task.CompletedTask;},width:215));
        dialog.Controls.Add(layout);Fill();dialog.Shown+=(_,_)=>BeginInvoke(()=>privateDialogPreview?.Invoke(dialog));dialog.ShowDialog(this);
        if(providerDraft!=null&&!closing)await OpenProviderDraft(PrivateSourceKind.M3u,providerDraft.SourceUrl,providerDraft.Name);
        else if(searchAgain&&!closing)await FindProviderPlaylists();
    }
    void ImportFoundStations(List<Station> stations,string name,DateTimeOffset fetched)
    {
        candidates=MergeStations(stations.Where(s=>!s.BrowserOnly),candidates);catalogDataAt=catalogDataAt==DateTimeOffset.MinValue||fetched<catalogDataAt?fetched:catalogDataAt;
        store.Data.Playlist=MergeStations(stations,store.Data.Playlist.Where(s=>!s.BrowserOnly));store.Save();
        search.Text="";lastQuery="";kind.SelectedItem="Live";resultPage=0;
        if(stations.Any(s=>store.Data.SelectedCountries.Length>0&&!store.Data.SelectedCountries.Contains(s.Country))) { store.Data.SelectedCountries=[];countriesButton.Text=CountryCaption();store.Save(); }
        catalogScope=Scope(kind.Text,store.Data.SelectedCountries);
        ShowRecommendations();status.Text=$"Wczytano {stations.Count} adresów z playlisty {name}. Wybierz kanał, aby odtworzyć.";SaveDiscoveryReport();
        if(epgEnabled)Run(()=>RefreshEpg(false));
    }
}
