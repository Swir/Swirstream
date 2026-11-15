using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

public sealed partial class MainForm
{
    SmartCatalog smartCatalog = null!;
    SourceRanker ranker = null!;
    string dataFolder = "", feedDetails = "Jeszcze nie pobrano katalogów.";
    readonly ComboBox quality = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 158, FlatStyle = FlatStyle.Flat, BackColor = Edge, ForeColor = Color.White };
    readonly CheckBox verify = new() { Text = "Sprawdzaj źródła", AutoSize = true, ForeColor = Cyan, Margin = new(8, 8, 4, 0) };
    readonly CheckBox webSearch = new() { Text = "Internet", AutoSize = true, ForeColor = Cyan, Margin = new(8, 8, 4, 0) };
    readonly CheckBox fallback = new() { Text = "Zapasowe źródła", AutoSize = true, ForeColor = Color.White, Margin = new(8, 8, 4, 0) };
    Button countriesButton = null!;
    List<Station> candidates = [];
    List<Station> knownChannels = [];
    DateTimeOffset catalogDataAt = DateTimeOffset.MinValue;
    HashSet<string> attempted = [];
    string listMode = "top", lastQuery = "";
    bool expectedPlayback;
    DateTime lastProgress;
    void InitDiscovery(string folder, TableLayoutPanel body)
    {
        dataFolder = folder; smartCatalog = new(folder); ranker = new(folder);
        store.Data.SelectedCountries ??= ["PL"];
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        countriesButton = Button(CountryCaption(), SelectCountries, width: 150); filters.Controls.Add(countriesButton);
        quality.Items.AddRange(["Stabilność", "Najwyższa jakość", "Oszczędzaj transfer"]); quality.SelectedItem = store.Data.QualityMode;
        if (quality.SelectedIndex < 0) quality.SelectedIndex = 0;
        quality.SelectedIndexChanged += (_, _) => { store.Data.QualityMode = quality.Text; store.Save(); if (!applyingProfile && candidates.Count > 0) RefreshDiscoveryList(); };
        verify.Checked = store.Data.AutoVerify; fallback.Checked = store.Data.AutoFallback;
        webSearch.Checked = store.Data.SearchWeb;
        webSearch.CheckedChanged += (_, _) => { store.Data.SearchWeb = webSearch.Checked; store.Save(); };
        verify.CheckedChanged += (_, _) => { store.Data.AutoVerify = verify.Checked; store.Save(); };
        fallback.CheckedChanged += (_, _) => { store.Data.AutoFallback = fallback.Checked; store.Save(); };
        filters.Controls.Add(quality); filters.Controls.Add(webSearch); filters.Controls.Add(verify); filters.Controls.Add(fallback);
        filters.Controls.Add(Button("Anuluj", () => { searchCancel?.Cancel(); status.Text = "Przerwano wyszukiwanie / analizę."; return Task.CompletedTask; }, width: 74));
        body.Controls.Add(filters, 0, 1); body.SetColumnSpan(filters, 2);
        AddNav("Głębokie szukanie", () => SmartSearch(true, deep: true), Cyan);
        AddNav("Kopia biblioteki", BackupLibrary);
        AddNav("Źródła i diagnostyka", DiagnosticsDialog);
    }
    string CountryCaption() => store.Data.SelectedCountries.Length == 0 ? "Kraje: cały świat" : "Kraje: " + string.Join(", ", store.Data.SelectedCountries);
    Task SelectCountries()
    {
        using var dialog = new Form { Text = "Gdzie szukać? Zaznacz kraje", Size = new(480, 600), BackColor = Card, ForeColor = Color.White, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var box = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = Card, ForeColor = Color.White, BorderStyle = BorderStyle.None, Font = Font };
        var filter = Input("Filtruj kraje…"); filter.Dock = DockStyle.Top;
        var info = new Label { Text = "Maks. 12 krajów. Pusta lista = cały świat.", Dock = DockStyle.Top, Height = 35 };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45 };
        var chosen = store.Data.SelectedCountries.ToHashSet(); var all = Countries.Load(); bool updating = false;
        void Fill() { updating = true; box.Items.Clear(); foreach (var c in all.Where(c => TextTools.Normalize(c.ToString()).Contains(TextTools.Normalize(filter.Text)))) box.Items.Add(c, chosen.Contains(c.Code)); updating = false; }
        box.ItemCheck += (_, e) => { if (updating) return; var code = ((Country)box.Items[e.Index]).Code; if (e.NewValue == CheckState.Checked) { if (chosen.Count >= 12) { e.NewValue = CheckState.Unchecked; return; } chosen.Add(code); } else chosen.Remove(code); info.Text = $"Wybrano {chosen.Count}/12 • pusta lista = cały świat"; };
        filter.TextChanged += (_, _) => Fill(); Fill();
        var save = new Button { Text = "Zastosuj", DialogResult = DialogResult.OK, Width = 120, Height = 32 };
        var clear = new Button { Text = "Cały świat", Width = 120, Height = 32 }; clear.Click += (_, _) => { chosen.Clear(); Fill(); info.Text = "Cały świat"; };
        actions.Controls.AddRange([save, clear]); dialog.Controls.Add(box); dialog.Controls.Add(filter); dialog.Controls.Add(info); dialog.Controls.Add(actions);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            store.Data.SelectedCountries = chosen.Order().ToArray(); store.Save(); countriesButton.Text = CountryCaption(); Run(FilterCatalog);
        }
        return Task.CompletedTask;
    }
    void ShowRecommendations()
    {
        listMode = "top";
        var visible = VisibleCandidates(); int channels = visible.Select(SourceRanker.ChannelKey).Distinct().Count();
        var top = channels == 1 ? ranker.Rank(visible, quality.Text) : ranker.Top(visible, quality.Text, int.MaxValue);
        ShowPage($"{channels} kanałów • {visible.Count} źródeł • {WorkingCount(visible)} sprawdzonych", top.Select(r => r.Station).ToList());
    }
    int WorkingCount(IEnumerable<Station>? source = null) => taste.Filter(source ?? candidates).Where(s => !s.BrowserOnly && ranker.Results.TryGetValue(s.Url, out var h) && DiscoveryAnalyzer.FreshSuccess(h)).Select(SourceRanker.ChannelKey).Distinct().Count();
    async Task ShowCurrentChannels(){if(listMode=="local"){localView="all";resultPage=0;ShowLibrary();}else if(candidates.Count==0)await SmartSearch(false);else ShowRecommendations();}
    Task ShowWorking() { if(listMode=="local"){localView="working";resultPage=0;ShowLibrary();return Task.CompletedTask;} listMode = "working"; ShowPage("POTWIERDZONE DEKODOWANIE • ostatnie 10 min", ranker.Top(VisibleCandidates().Where(s => ranker.Results.TryGetValue(s.Url, out var h) && DiscoveryAnalyzer.FreshSuccess(h)), quality.Text, int.MaxValue).Select(r => r.Station).ToList()); return Task.CompletedTask; }
    Task ShowAllCandidates() { if(listMode=="local"){localView="all";resultPage=0;ShowLibrary();return Task.CompletedTask;} listMode = "all"; ShowPage("WSZYSTKIE ZNALEZIONE ŹRÓDŁA", ranker.Rank(VisibleCandidates(), quality.Text).Select(r => r.Station).ToList()); return Task.CompletedTask; }
    void RefreshDiscoveryList() { if (listMode == "local") ShowLibrary(); else if (listMode == "all") _ = ShowAllCandidates(); else if (listMode == "working") _ = ShowWorking(); else if (listMode == "alternatives") _ = ShowAlternatives(); else ShowRecommendations(); }
    Task ShowAlternatives()
    {
        var station = Selected ?? current; if (station == null) return Task.CompletedTask;
        if(listMode=="local")
        {if(localView!="alternatives"){alternativeKey=SourceRanker.ChannelKey(station);resultPage=0;}localView="alternatives";ShowLibrary();return Task.CompletedTask;}
        if (listMode != "alternatives") { alternativeKey = SourceRanker.ChannelKey(station); resultPage = 0; }
        listMode = "alternatives"; ShowPage("WARIANTY KANAŁU", ranker.Rank(VisibleCandidates().Where(s => SourceRanker.ChannelKey(s) == alternativeKey), quality.Text).Select(r => r.Station).ToList()); return Task.CompletedTask;
    }
    string SourceBadge(Station station)
    {
        if (station.BrowserOnly) return "WWW • " + station.Country;
        var ranked = ranker.Rank([station], quality.Text)[0]; var h = ranked.Health;
        bool fresh = h != null && DateTimeOffset.UtcNow - h.CheckedAt >= TimeSpan.Zero && DateTimeOffset.UtcNow - h.CheckedAt < TimeSpan.FromMinutes(10);
        return (fresh ? (h!.Decoded ? "✓ " : "× ") : "? ") + station.Country + " • " + (fresh ? h!.Decoded ? "działa" : "nie działa" : "do testu");
    }
    string SourceDetail(Station station)
    {
        if (station.BrowserOnly) return station.AccessNote + "\nStrona WWW nie jest adresem odtwarzania. EPG nie potwierdza dostępności transmisji.";
        var r = ranker.Rank([station], quality.Text)[0];
        var pool=PrivateStation.IsPrivate(station)?privateSources.Stations(station.PrivateSourceId):listMode=="local"?localStations:candidates;
        int variants = pool.Count(s => SourceRanker.ChannelKey(s) == SourceRanker.ChannelKey(station));
        return $"{station.Country} • {station.Source} • źródeł kanału: {variants}\n{r.Reason}\n{station.Homepage}\n{PrivateStation.DisplayAddress(station)}";
    }
    async Task<int> Analyze(long generation, CancellationToken ct, int? target = null, int budget = 32, bool final = true, List<Station>? source = null)
    {
        var batch = source ?? VisibleCandidates();
        var analyzer = new DiscoveryAnalyzer(ranker, async (station, token) => {
            ScanProgress(generation, $"3/3 • Sprawdzam: {station.Name} • potwierdzono {WorkingCount(batch)} kanałów");
            if(PrivateStation.IsPrivate(station)){var media=await privateSources.Resolve(station,token);return await StreamProbe.Run(media.Station,Path.Combine(dataFolder,"probe-temp"),token,media.ClearKeyHex);}
            return await StreamProbe.Run(station, Path.Combine(dataFolder, "probe-temp"), token);
        });
        int count = await analyzer.Run(taste.CandidatesToVerify(batch), quality.Text, (n, station, health) => {
            if (generation != searchGeneration || closing) return;
            if(searchLearning.Record(station,health))UpdateTasteStatus();
            if (!scanBusy) RefreshBadges();
        }, ct, target ?? 8, budget);
        if (generation == searchGeneration && !closing && final)
        {
            RefreshBadges(); status.Text = $"Potwierdzono {WorkingCount(batch)} kanałów • {count} nowych prób • {candidates.Count} źródeł. Szukaj dalej = kolejne próby.";
            feedDetails += $"\nWeryfikacja: {count} nowych prób, {WorkingCount()} różnych kanałów działało w ostatnich 10 minutach.";
            SaveDiscoveryReport();
        }
        return count;
    }
    void SaveDiscoveryReport()
    {
        File.WriteAllText(Path.Combine(dataFolder, "last-discovery.json"), JsonSerializer.Serialize(new {
            version = "0.9.7", query = XtreamLink.PrivateInput(lastQuery)?"":lastQuery, countries = store.Data.SelectedCountries, mode = kind.Text, profile = profiles.Index.ActiveId, topics = store.Data.PreferredTopics, completedAt = DateTimeOffset.UtcNow, catalogDataAt,
            details = feedDetails, sources = candidates.Where(s=>!PrivateStation.IsPrivate(s)&&!PublicPlaylistPrivacy.ContainsCredentials(s.Url)&&!PublicPlaylistPrivacy.ContainsCredentials(s.Referrer)&&!PublicPlaylistPrivacy.ContainsCredentials(s.Homepage)).Select(s => new { station = s, health = ranker.Results.GetValueOrDefault(s.Url) })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    async Task SearchMore()
    {
        if (scanBusy) return;
        if (listMode!="local"&&candidates.Count == 0) { await SmartSearch(true); return; }
        searchCancel?.Cancel(); searchCancel = new CancellationTokenSource(TimeSpan.FromMinutes(7)); long generation = ++searchGeneration;
        try { var batch=listMode=="local"?ChannelSearch.FilterLocal(lastQuery,localStations):null;await Analyze(generation, searchCancel.Token, WorkingCount(batch) + 8,source:batch); }
        catch (OperationCanceledException) { if (!closing && generation == searchGeneration) status.Text = "Przerwano. Zachowano sprawdzone wyniki."; }
    }
    async Task SmartSearch(bool refresh, bool initial = false, bool wholeCatalog = false, bool deep = false, bool playlistSearch = false)
    {
        if(!wholeCatalog&&await RedirectPrivateSearch(search.Text))return;
        searchCancel?.Cancel(); using var operation = new CancellationTokenSource(TimeSpan.FromMinutes(8)); searchCancel = operation;
        var ct = operation.Token; long generation = ++searchGeneration;
        string rawQuery = wholeCatalog ? "" : search.Text.Trim();
        var parsed = playlistSearch || ChannelSearch.Exact(rawQuery, knownChannels).Count > 0 || candidates.Any(s => ChannelSearch.Matches(rawQuery, s))
            ? (Query: rawQuery, Codes: store.Data.SelectedCountries) : Countries.Extract(rawQuery, store.Data.SelectedCountries);
        string searchMode = kind.Text;
        if (!parsed.Codes.SequenceEqual(store.Data.SelectedCountries)) { store.Data.SelectedCountries = parsed.Codes; store.Save(); countriesButton.Text = CountryCaption(); }
        bool useWeb = playlistSearch || webSearch.Checked, useLists = playlistSearch || store.Data.SearchPlaylists && searchMode != "Radio", useIndex = store.Data.SearchIndex && searchMode != "Radio", testPlayback = verify.Checked;
        BeginScan(initial);
        string scope = Scope(searchMode, parsed.Codes), notes = "Zapytanie: " + parsed.Query + " • " + searchMode + "\n";
        // Work in a private batch. The visible, searchable catalog changes only once,
        // after all providers finish and this generation is still current.
        List<Station> discovered = [];
        Task<SearchResult>? directoryTask = null;
        var coverage = new DiscoveryCoverage(profiles.Folder(profiles.Index.ActiveId));
        try
        {
            async Task<SearchResult> Safe(Func<Task<SearchResult>> job, string name)
            {
                try { return await job(); }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { return new([], "BŁĄD " + name + ": " + ex.Message); }
            }
            directoryTask = searchMode != "Radio"
                ? Safe(() => smartCatalog.Broadcasters(refresh, ct), "spisu kanałów") : Task.FromResult(new SearchResult([], ""));
            var result = await Safe(() => smartCatalog.Search(parsed.Query, searchMode, parsed.Codes, refresh, ct), "katalogów");
            if (generation != searchGeneration || closing) return;
            discovered = result.Stations; notes += smartCatalog.Notice + "\n" + result.Detail;
            var index = await directoryTask;
            if (generation != searchGeneration || closing) return;
            var directory = searchMode == "Radio" ? discovered.Where(s => TextTools.IsWeb(s.Homepage)).Select(s => s with { Url = s.Homepage }).ToList() : index.Stations;
            notes += "\n" + index.Detail;
            var ids = directory.Where(s => s.ChannelId.Length > 0).GroupBy(s => s.ChannelId).ToDictionary(g => g.Key, g => g.First());
            discovered = ChannelSearch.Filter(parsed.Query, discovered.Select(s => ids.TryGetValue(s.ChannelId, out var meta) ? s with { Group = meta.Group.Length > 0 ? meta.Group : s.Group, Homepage = meta.Homepage, Aliases = ChannelSearch.Names(meta).Concat(ChannelSearch.Names(s)).Distinct().ToArray() } : s), directory);
            var exact = ChannelSearch.Exact(parsed.Query, directory);
            if (exact.Count > 0) notes += "\nRozpoznano nazwę/alias: " + string.Join(", ", exact.Take(10).Select(s => s.Name + " (" + s.Country + ")")) + ". Filtry krajów pozostają aktywne.";
            string webQuery = parsed.Query;
            void Progress(string text) => ScanProgress(generation, "2/3 • " + text);
            Progress($"Łączę źródła • zebrano {discovered.Count} adresów");
            static string Notes(WebResult r) => string.Join("\n", r.Evidence.Select(e => $"{e.CheckedAt.ToLocalTime():HH:mm:ss} [{e.Stage}] {e.Url} — {e.Detail}"));
            var jobs = new List<DiscoveryJob>();
            var catalogSnapshot = discovered.ToList();
            var profileLearning=searchLearning;
            SearchPrioritySnapshot? prioritySnapshot=useWeb?await Task.Run(()=>profileLearning.Snapshot(directory.Where(s=>(parsed.Codes.Length==0||parsed.Codes.Contains(s.Country))&&PublicWeb.Allowed(s.Url))),ct):null;
            ct.ThrowIfCancellationRequested();if(generation!=searchGeneration||closing)return;
            if (useIndex) jobs.Add(new("Indeks adresów", async token =>
            {
                var more = await smartCatalog.ExtraStreams(parsed.Query, parsed.Codes, directory, refresh, token);
                return new("Indeks adresów", more.Stations, more.Detail);
            }));
            if (useWeb) jobs.Add(new("Strony nadawców", async token =>
            {
                var more = await new WebDiscovery().Search(webQuery, searchMode, parsed.Codes, directory, catalogSnapshot, smartCatalog.WebSearchTemplate, Progress, token, deep ? WebSearchPlan.Deep : WebSearchPlan.Normal, coverage, prioritySnapshot);
                return new("Strony nadawców", more.Stations, Notes(more));
            }));
            if (useLists) jobs.Add(new("Publiczne IPTV", async token =>
            {
                var playlistSeeds = deep ? smartCatalog.SharedPlaylistUrls.Concat(["https://iptv-org.github.io/iptv/index.m3u"]).Distinct().ToArray() : smartCatalog.SharedPlaylistUrls;
                var more = await new SharedPlaylists().Search(parsed.Query, parsed.Codes, directory, smartCatalog.WebSearchTemplate, playlistSeeds, Progress, token, deep, foundPlaylists.Save);
                return new("Publiczne IPTV", more.Stations, Notes(more));
            }));
            var discovery = DiscoveryPipeline.Run(jobs, batch =>
            {
                if (generation != searchGeneration || closing) return;
                var incoming = ChannelSearch.Filter(parsed.Query, batch.Stations.Where(s => parsed.Codes.Length == 0 || parsed.Codes.Contains(s.Country)), directory);
                discovered = MergeStations(incoming, discovered);
                notes += "\n" + batch.Detail;
                Progress($"{batch.Name}: zakończono • zebrano {discovered.Count} adresów");
            }, ct);
            await discovery;
            if (generation != searchGeneration || closing) return;
            int attempts = testPlayback && discovered.Count > 0 ? await Analyze(generation, ct, final: false, source: discovered) : 0;
            ct.ThrowIfCancellationRequested(); if (generation != searchGeneration || closing) return;
            if (discovered.Count > 0)
            {
                bool keepPrevious = catalogScope == scope && catalogDataAt >= DateTimeOffset.UtcNow.AddDays(-7);
                var dataAt = result.DataAt ?? DateTimeOffset.UtcNow;
                if (keepPrevious && candidates.Count > 0 && catalogDataAt < dataAt) dataAt = catalogDataAt;
                candidates = MergeStations(discovered, keepPrevious ? candidates : []);
                catalogDataAt = dataAt;
                
                catalogScope = scope;
            }
            if (directory.Count > 0) knownChannels = directory;
            feedDetails = notes + $"\nZakończono: {discovered.Count} znalezionych adresów, {attempts} prób; bazę pokazano po zakończeniu.";
            if (!wholeCatalog && parsed.Query != rawQuery && !XtreamLink.PrivateInput(search.Text))
            {
                changingSearch = true; search.Text = parsed.Query; changingSearch = false;
            }
            lastQuery = XtreamLink.PrivateInput(search.Text)?"":ChannelSearch.LocalQuery(search.Text, candidates); resultPage = 0; ShowRecommendations(); SaveDiscoveryReport();
            if (epgEnabled) Run(() => RefreshEpg(false));
            status.Text = discovered.Count == 0 ? "Nie znaleziono nowych źródeł. Poprzednia baza została zachowana. Sprawdź filtry lub Szukaj w sieci."
                : $"Wyszukiwanie zakończone • {candidates.Select(SourceRanker.ChannelKey).Distinct().Count()} kanałów • {candidates.Count} źródeł • {WorkingCount()} potwierdzonych. Wpisz nazwę lub wyszukaj ponownie.";
        }
        catch (OperationCanceledException) { if (generation == searchGeneration && !closing) status.Text = "Wyszukiwanie / analiza przerwane. Zachowano dotychczasowe wyniki."; }
        catch (Exception ex) when (generation != searchGeneration || closing) { _ = ex; }
        finally
        {
            try { if (directoryTask != null) await directoryTask; } catch (OperationCanceledException) { }
            coverage.Save();
            EndScan(generation); if (ReferenceEquals(searchCancel, operation)) searchCancel = null;
        }
    }
    async Task SearchChannelSources()
    {
        var selected = Selected ?? current;
        if (selected == null) { status.Text = "Najpierw wybierz kanał z listy."; return; }
        var meta = knownChannels.FirstOrDefault(s => s.ChannelId.Length > 0 && s.ChannelId == selected.ChannelId);
        changingSearch = true;
        search.Text = ChannelSearch.ProgramVariant(selected.Feed).Length == 0 && meta != null ? meta.Name : Regex.Replace(selected.Name, @"\s*\([^)]*\)|\s*\[[^]]*\]", "").Trim();
        changingSearch = false;
        kind.SelectedItem = selected.Kind == "Radio" ? "Radio" : "TV";
        if (selected.Country.Length > 0) { store.Data.SelectedCountries = [selected.Country]; countriesButton.Text = CountryCaption(); }
        store.Save(); await SmartSearch(true);
    }
    void RecordSuccess()
    {
        if (current != null)
        {
            var health=new Health(DateTimeOffset.UtcNow,true,(int)(DateTime.UtcNow-playbackRequested).TotalMilliseconds);
            ranker.Record(current,health);if(searchLearning.Record(current,health))UpdateTasteStatus();
            RefreshBadges();
        }
    }
    void TryFallback(string reason = "")
    {
        if (!expectedPlayback || current == null) return; expectedPlayback = false;
        // Stop and retain the useful error before any optional profile write.
        // A full disk or locked profile must not leave a failed stream running.
        try { RetainPlaybackFailure(reason); }
        catch { status.Text = "Nie udało się zakończyć odtwarzania źródła."; return; }
        try
        {
            var health=new Health(DateTimeOffset.UtcNow,false,12000,Note:"Odtwarzanie przerwane / brak danych");
            ranker.Record(current,health);if(searchLearning.Record(current,health))UpdateTasteStatus();
            RefreshBadges();
        }
        catch { status.Text = "Wynik próby źródła nie został zapisany."; }
        try
        {
            var next = fallback.Checked && attempted.Count < 3 ? ranker.Alternative(candidates, current, attempted, quality.Text) : null;
            if (next != null) { Play(next, true); status.Text = "Automatycznie przełączono na inne źródło tego samego kanału."; }
        }
        catch (Exception ex) { status.Text = "Nie udało się przełączyć źródła: " + ex.Message; }
    }
    void RetainPlaybackFailure(string reason)
    {
        try { if (player?.State != 7) player?.Stop(); }
        finally
        {
            playbackFailure = reason.Length > 0 ? reason : "Brak działającego wariantu. Odśwież katalog lub wybierz inną stację.";
            playback.Text = playbackFailure; video.Active = false; video.Invalidate();
        }
    }
    Task BackupLibrary()
    {
        using var d = new SaveFileDialog { Filter = "Biblioteka SwirStream|*.json", FileName = "SwirStream-biblioteka-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json" };
        if (d.ShowDialog(this) == DialogResult.OK) { store.Save(); File.Copy(store.FilePath, d.FileName, true); status.Text = "Zapisano kopię biblioteki."; }
        return Task.CompletedTask;
    }
    Task DiagnosticsDialog()
    {
        using var dialog = new Form { Text = "Źródła, daty i diagnostyka", Size = new(800, 580), BackColor = Card, ForeColor = Color.White, StartPosition = FormStartPosition.CenterParent };
        var content = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, BackColor = Card, ForeColor = Color.White, Font = new Font("Consolas", 10), Text = "SwirStream AI 0.9.7\r\nEPG: " + epg.Notice.Replace("\n", "\r\n") + "\r\nSilnik " + player?.Version + "\r\n\r\n" + feedDetails.Replace("\n", "\r\n") + "\r\n\r\nKopia katalogu: maks. 6 h; awaryjna: maks. 7 dni.\r\nTest źródła: ważny w rankingu przez 6 h.\r\nOcena 0–100 jest heurystyką, nie gwarancją jakości ani praw nadawcy.\r\n\r\nTasteNet: lokalna sieć rozpoznająca 10 tematyk.\r\nDopasowanie uczy się z wyborów w bieżącym profilu. Brak panelu asystenta.\r\nPoszukiwacz wykonuje wyszukiwanie Bing RSS i DuckDuckGo, odczyt publicznych stron i próby Swir Player.\r\nWWW: zwykłe 60 s / 24 strony; głębokie 180 s / 120 stron / 3 poziomy. Kolejne skany wybierają najdawniej sprawdzane strony. Próby: do 32 adresów, cel 8 kanałów.\r\nStrony wymagające logowania, JavaScript lub DRM mogą nie udostępnić adresu.\r\nBing RSS i DuckDuckGo są publicznymi interfejsami bez gwarancji dostępności; awarie w raporcie.\r\nProgram nie ma serwera aktualizacji. Aktualizacje EXE i silnika wymagają nowego wydania.\r\nŹródła można zmienić bez kompilacji w data/sources.json.\r\nBiblioteka: " + dataFolder };
        dialog.Controls.Add(content); dialog.ShowDialog(this); return Task.CompletedTask;
    }
    public async Task DiscoveryUiTest(string localFile)
    {
        candidates = [new("Kanał testowy (720p)", localFile, "TV", Country: "PL", ChannelId: "Test.pl"), new("Kanał testowy (1080p)", "https://example.org/test", "TV", Country: "PL", ChannelId: "Test.pl")];
        var result = await StreamProbe.Run(candidates[0], Path.Combine(dataFolder, "probe-temp"), CancellationToken.None);
        if (!result.Decoded) throw new Exception("Sonda nie zdekodowała lokalnego źródła."); ranker.Record(candidates[0], result);
        ShowRecommendations(); if (shown.Count != 2 || shown[0].Url != localFile) throw new Exception("TOP nie wybrał potwierdzonego wariantu.");
        await ShowAlternatives(); if (shown.Count != 2) throw new Exception("Brak wariantów kanału.");
        candidates[1] = candidates[1] with { Url = "http://127.0.0.1:1/unavailable" };
        fallback.Checked = true; Play(candidates[1]);
        for (int i = 0; i < 60 && !(current?.Url == localFile && player?.Stats.VideoProgressSamples > 3); i++) await Task.Delay(200);
        if (current?.Url != localFile || player?.Stats.VideoProgressSamples <= 3 || attempted.Count != 2) throw new Exception("Automatyczne źródło zapasowe nie rozpoczęło odtwarzania.");
        await Stop();
        ShowRecommendations();
    }
    public async Task DeepSearchUiTest()
    {
        var previousCatalog = smartCatalog;
        bool previousVerify = verify.Checked, previousWeb = webSearch.Checked, previousLists = store.Data.SearchPlaylists, previousIndex = store.Data.SearchIndex;
        string oldQuery = search.Text; var oldCountries = store.Data.SelectedCountries;
        try
        {
            var cfg = new SourceSettings { TvTemplates = ["https://fixture.example/{path}"], ChannelDirectoryUrls = ["https://fixture.example/channels.json"], StreamDirectoryUrls = ["https://fixture.example/streams.json"] };
            Task<(string, DateTimeOffset?)> Fetch(string url, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                string body = url.EndsWith("channels.json") ? JsonSerializer.Serialize(Enumerable.Range(0,10).Select(i => new { id="Alpha"+i+".pl", name=i==0?"Main Alpha":"Channel "+i, alt_names=i==0?new[]{"Alias Alpha"}:Array.Empty<string>(),country="PL",website="",categories=new[]{"news"} }))
                    : url.EndsWith("streams.json") ? "[{\"channel\":\"Alpha0.pl\",\"title\":\"Main Alpha\",\"url\":\"https://cdn.example/alpha.m3u8\",\"feed\":\"HD\"}]"
                    : "#EXTM3U\n#EXTINF:-1 tvg-id=\"Alpha0.pl@HD\",Main Alpha\nhttps://cdn.example/alpha.m3u8";
                return Task.FromResult((body,(DateTimeOffset?)DateTimeOffset.UtcNow));
            }
            smartCatalog = new(Path.Combine(dataFolder,"ui-deep-fixture"),cfg,Fetch);
            verify.Checked = false; webSearch.Checked = false; store.Data.SearchPlaylists = false; store.Data.SearchIndex = true;
            store.Data.SelectedCountries = ["PL"]; kind.SelectedItem = "TV"; search.Text = "Alias Alpha";
            await SmartSearch(true);
            if (shown.Count != 1 || shown[0].ChannelId != "Alpha0.pl" || !shown[0].Source.StartsWith("Indeks")) throw new Exception("Alias did not reach the new index route in the UI.");
            await SearchChannelSources();
            if (search.Text != "Main Alpha" || shown.Count != 1 || store.Data.SelectedCountries.Single() != "PL") throw new Exception("Selected-channel search lost identity or country.");
        }
        finally
        {
            smartCatalog = previousCatalog; verify.Checked = previousVerify; webSearch.Checked = previousWeb; store.Data.SearchPlaylists = previousLists; store.Data.SearchIndex = previousIndex;
            search.Text = oldQuery; store.Data.SelectedCountries = oldCountries; countriesButton.Text = CountryCaption();
        }
    }
}
