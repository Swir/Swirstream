namespace SwirStreamAI;

public sealed partial class MainForm
{
    const int ResultPageSize = 100;
    int resultPage;
    string alternativeKey = "", catalogScope = "";
    string localTitle = "";
    string localView = "all";
    List<Station> localStations = [];
    bool scanBusy, changingSearch;
    Panel scanOverlay = null!;
    Control mainContent = null!;
    Label scanTitle = null!, scanDetail = null!, pageCaption = null!;
    Button previousPage = null!, nextPage = null!;
    public event Action<string>? StartupProgress;

    void InitScanOverlay(Control content)
    {
        mainContent = content;
        scanOverlay = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Visible = false, Padding = new(90) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, BackColor = Card, Padding = new(35) };
        foreach (float h in new float[] { -1, 60, 65, 75, 20, 55, -1 }) layout.RowStyles.Add(new(h < 0 ? SizeType.Percent : SizeType.Absolute, h < 0 ? 50 : h));
        layout.Controls.Add(Label("SWIRSTREAM AI  •  by Swir", 24, Cyan, true), 0, 1);
        scanTitle = Label("Proszę czekać — trwa pierwsze wyszukiwanie kanałów", 17, Color.White, true);
        scanDetail = Label("Przygotowuję bazę kanałów…", 11, Muted);
        layout.Controls.Add(scanTitle, 0, 2); layout.Controls.Add(scanDetail, 0, 3);
        layout.Controls.Add(new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 35 }, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(0, 12, 0, 0) };
        actions.Controls.Add(Button("Przerwij wyszukiwanie", () => { searchCancel?.Cancel(); scanDetail.Text = "Kończę wyszukiwanie. Dotychczasowa baza pozostanie dostępna."; return Task.CompletedTask; }, width: 220));
        layout.Controls.Add(actions, 0, 5); scanOverlay.Controls.Add(layout); Controls.Add(scanOverlay);
    }
    void BeginScan(bool initial)
    {
        scanBusy = true; mainContent.Enabled = false;
        scanTitle.Text = initial ? "Proszę czekać — trwa pierwsze wyszukiwanie kanałów" : "Proszę czekać — trwa wyszukiwanie kanałów";
        scanDetail.Text = "1/3 • Pobieram katalogi • " + CountryCaption();
        scanOverlay.Visible = true; scanOverlay.BringToFront();
        StartupProgress?.Invoke(scanDetail.Text);
    }
    void EndScan(long generation)
    {
        if (generation != searchGeneration || closing) return;
        scanBusy = false; scanOverlay.Visible = false; mainContent.Enabled = true;
    }
    void ScanProgress(long generation, string message)
    {
        if (generation != searchGeneration || closing) return;
        if (scanBusy) scanDetail.Text = message;
        status.Text = message;
        StartupProgress?.Invoke(message);
    }
    static string Scope(string kind, IEnumerable<string> countries) => kind + ":" + string.Join(',', countries.Order());
    List<Station> VisibleCandidates()
    {
        bool InScope(Station s) => (kind.Text == "Live" || s.Kind == kind.Text) && (store.Data.SelectedCountries.Length == 0 || store.Data.SelectedCountries.Contains(s.Country));
        return taste.Filter(ChannelSearch.FilterLocal(lastQuery, candidates.Where(s => !s.BrowserOnly && InScope(s)))).ToList();
    }
    Task FilterCatalog()
    {
        if(XtreamLink.PrivateInput(search.Text)){search.UseSystemPasswordChar=true;lastQuery="";status.Text="Wykryto prywatny link. Naciśnij Enter lub Wklej link, aby dodać źródło. Nie wysłano zapytania do internetu.";return Task.CompletedTask;}
        search.UseSystemPasswordChar=false;
        if (scanBusy) return Task.CompletedTask;
        resultPage = 0;
        if (listMode == "local") { localView="all"; ShowLibrary(); return Task.CompletedTask; }
        lastQuery = ChannelSearch.LocalQuery(search.Text, candidates); ShowRecommendations();
        status.Text = candidates.Count == 0 ? "Baza jest pusta. Użyj Szukaj w sieci lub Odśwież bazę."
            : $"Baza gotowa • {candidates.Select(SourceRanker.ChannelKey).Distinct().Count()} kanałów. Szukaj w sieci = dodatkowe źródła; Odśwież bazę = nowe pełne wyszukiwanie.";
        return Task.CompletedTask;
    }
    void ShowLibrary()
    {
        listMode = "local";
        lastQuery = XtreamLink.PrivateInput(search.Text)?"":ChannelSearch.LocalQuery(search.Text, localStations);
        // Imported Live streams often lack a country. Keep the library's scope.
        var rows=ChannelSearch.FilterLocal(lastQuery,localStations.Where(s=>!s.BrowserOnly));
        if(localView=="alternatives")rows=rows.Where(s=>SourceRanker.ChannelKey(s)==alternativeKey).ToList();
        else if(localView=="working")rows=rows.Where(s=>ranker.Results.TryGetValue(s.Url,out var h)&&DiscoveryAnalyzer.FreshSuccess(h)).ToList();
        ShowPage(localTitle+(localView=="alternatives"?" • WARIANTY":localView=="working"?" • POTWIERDZONE ODTWARZANIE":""),rows);
        if(localView=="working"&&rows.Count==0&&localStations.Count>0)details.Text="Brak świeżo potwierdzonych kanałów tego źródła. Kanały / Wszystkie przywraca całą zapisaną listę.";
        status.Text = localStations.Count==0&&localProviderMessage.Length>0?localProviderMessage:"Biblioteka lokalna • wyszukiwanie w: " + localTitle + " • filtry katalogu nie ograniczają biblioteki";
    }
    void ShowPage(string title, List<Station> stations)
    {
        int pages = Math.Max(1, (stations.Count + ResultPageSize - 1) / ResultPageSize);
        resultPage = Math.Clamp(resultPage, 0, pages - 1);
        ShowStations(title, stations.Skip(resultPage * ResultPageSize).Take(ResultPageSize).ToList());
        pageCaption.Text = $"{resultPage + 1} / {pages}";
        previousPage.Enabled = resultPage > 0; nextPage.Enabled = resultPage + 1 < pages;
        if (stations.Count == 0) details.Text = listMode=="local"
            ?localStations.Count==0&&localProviderMessage.Length>0?localProviderMessage:"Brak pasujących kanałów w tej liście. Sprawdź nazwę lub wyczyść wyszukiwanie."
            :"Brak pasujących kanałów. Sprawdź nazwę, kraje, tematykę i ukryte kanały albo wybierz Szukaj w sieci.";
        if (epgEnabled && epg != null && store.Data.SelectedCountries.Length == 0 && shown.Any(s => s.Kind != "Radio" && s.Country.Length == 2 && !epg.Feeds.Any(f => f.Country == s.Country))) Run(() => RefreshEpg(false));
    }
    Task ChangePage(int delta) { resultPage += delta; RefreshDiscoveryList(); return Task.CompletedTask; }
    void RefreshBadges()
    {
        // Verification changes status only. Do not remove/reorder rows under the pointer.
        foreach (ListViewItem item in results.Items) if (item.Tag is Station station) item.SubItems[1].Text = SourceBadge(station);
        if (Selected is { } selected) details.Text = SourceDetail(selected);
    }
    internal static List<Station> MergeStations(IEnumerable<Station> fresh, IEnumerable<Station> existing)
    {
        var merged = new Dictionary<string,List<Station>>();
        foreach (var station in existing.Concat(fresh))
        {
            if (!merged.TryGetValue(station.Url, out var variants)) merged[station.Url] = variants = [];
            int index = variants.FindIndex(old => old.Kind == station.Kind && old.Country == station.Country &&
                (old.ChannelId == station.ChannelId || old.ChannelId.Length == 0 || station.ChannelId.Length == 0) &&
                ChannelSearch.ProgramVariant(old.Feed) == ChannelSearch.ProgramVariant(station.Feed) &&
                (old.ChannelId.Length > 0 && old.ChannelId == station.ChannelId || ChannelSearch.SameProgram(old, station)));
            if (index < 0) { variants.Add(station); continue; }
            var old = variants[index];
            variants[index] = station with { ChannelId = station.ChannelId.Length > 0 ? station.ChannelId : old.ChannelId, Aliases = ChannelSearch.Names(station).Concat(ChannelSearch.Names(old)).Distinct().ToArray(), Feed = station.Feed.Length > 0 ? station.Feed : old.Feed, Quality = station.Quality.Length > 0 ? station.Quality : old.Quality, UserAgent = station.UserAgent.Length>0?station.UserAgent:old.UserAgent, Referrer = station.Referrer.Length>0?station.Referrer:old.Referrer };
        }
        return merged.Values.SelectMany(s => s).ToList();
    }
}
