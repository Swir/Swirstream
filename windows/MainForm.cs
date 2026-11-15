using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

public sealed partial class MainForm : Form
{
    static readonly Color Bg = Color.FromArgb(10, 13, 22), Card = Color.FromArgb(18, 23, 37), Edge = Color.FromArgb(38, 47, 67),
        Muted = Color.FromArgb(157, 170, 192), Cyan = Color.FromArgb(66, 230, 217), Purple = Color.FromArgb(151, 123, 255);
    LibraryStore store;
    readonly TextBox search = Input("Szukaj w gotowej bazie: nazwa kanału lub fragment");
    readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed, Width = 102, FlatStyle = FlatStyle.Flat, BackColor = Edge, ForeColor = Color.White };
    readonly ListView results = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
        BackColor = Card, ForeColor = Color.White, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.None };
    readonly Label resultTitle = Label("ODKRYWAJ TRANSMISJE", 11, Color.White, true);
    readonly Label details = Label("Wybierz stację, aby zobaczyć jej źródło.", 9, Muted);
    readonly Label status = Label("Gotowy • wybierz katalog lub otwórz M3U", 9, Muted);
    readonly Label now = Label("Twoje miejsce na live.", 17, Color.White, true);
    readonly Label playback = Label("TV, radio i transmisje w jednym oknie", 10, Muted);
    readonly Label volumeText = Label("65%", 9, Muted);
    readonly VideoSurface video = new() { Dock = DockStyle.Fill };
    readonly TrackBar volume = new() { Minimum = 0, Maximum = 100, Value = 65, TickStyle = TickStyle.None, Width = 120, Height = 30, BackColor = Card };
    readonly TrackBar seek = new() { Minimum = 0, Maximum = 1000, Value = 0, TickStyle = TickStyle.None, Dock = DockStyle.Fill, Height = 24, BackColor = Card, Enabled = false };
    readonly Label clock = Label("LIVE", 9, Muted);
    readonly System.Windows.Forms.Timer timer = new() { Interval = 400 };
    readonly FlowLayoutPanel nav = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(0, 8, 0, 0) };
    SwirPlayer? player;
    PlayerStartup? playerStartup;
    Station? pendingStation;
    readonly TaskCompletionSource playerReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task PlayerReady => playerReady.Task;
    string playerError = "";
    CancellationTokenSource? searchCancel;
    long searchGeneration;
    Station? current;
    List<Station> shown = [];
    bool historySaved, seeking, closing, currentEncrypted;
    readonly PlaybackProgress playbackProgress = new();
    string playbackFailure = "";
    DateTime playbackRequested;
    Form? fullScreen;
    TableLayoutPanel playerLayout = null!;
    public MainForm(string dataPath, IntentNet categoryModel, bool autoDiscover = true, int startupDelayMs = 0)
    {
        SuspendLayout();
        this.categoryModel = categoryModel; profiles = new(dataPath); store = new(profiles.Folder(profiles.Index.ActiveId));InitPrivateStore();
        Text = "SwirStream AI 0.9.7 — by Swir"; BackColor = Bg; ForeColor = Color.White;
        Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "assets", "swirstream-v070.ico"));
        Font = new Font("Segoe UI", 10); Size = new(1440, 940); MinimumSize = new(1200, 780);
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi; KeyPreview = true;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new(22, 12, 22, 12), BackColor = Bg }; root.SuspendLayout();
        root.ColumnStyles.Add(new(SizeType.Absolute, 204)); root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 114)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 92)); root.RowStyles.Add(new(SizeType.Absolute, 28));
        Controls.Add(root);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 }; header.SuspendLayout();
        header.RowStyles.Add(new(SizeType.Absolute,64));header.RowStyles.Add(new(SizeType.Percent,100));
        header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.Absolute, 200)); header.ColumnStyles.Add(new(SizeType.Absolute, 85));
        var brand = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        using var brandIcon = new Icon(Path.Combine(AppContext.BaseDirectory,"assets","swirstream-v070.ico"),64,64);
        brand.Controls.Add(new PictureBox { Image = brandIcon.ToBitmap(), Size = new(54,54), SizeMode = PictureBoxSizeMode.Zoom, Margin = new(0,0,12,0) });
        brand.Controls.Add(Label("SWIRSTREAM  AI", 23, Color.White, true, width:340, height:54));
        header.Controls.Add(brand, 0, 0); header.Controls.Add(Label("●  TASTENET / LOKALNIE", 9, Cyan, true), 1, 0); header.Controls.Add(Label("by Swir", 12, Purple, true), 2, 0);
        var modules=CreateProviderModuleBar();header.Controls.Add(modules,0,1);header.SetColumnSpan(modules,3);
        root.Controls.Add(header, 0, 0); root.SetColumnSpan(header, 2);
        root.Controls.Add(nav, 0, 1);
        nav.Controls.Add(Label("TWOJE TRANSMISJE", 8, Muted, true, 164, 30));
        AddNav("◉   Radio", () => SearchKind("Radio"), Cyan);
        AddNav("▣   Telewizja", () => SearchKind("TV"));
        AddNav("↗   Wszystkie live", () => SearchKind("Live"));
        nav.Controls.Add(Label("BIBLIOTEKA", 8, Muted, true, 164, 30));
        AddNav("★   Ulubione", () => ShowLocal("ULUBIONE", store.Data.Favorites));
        AddNav("◷   Historia", () => ShowLocal("HISTORIA", store.Data.History.Select(v => v.Station with { Group = v.PlayedAt == DateTimeOffset.MinValue ? "Ze starej wersji • brak daty" : v.PlayedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm") }).ToList()));
        AddNav("≡   Moja playlista", () => ShowLocal("MOJA PLAYLISTA", store.Data.Playlist));
        AddNav("↓   Znalezione playlisty", ShowFoundPlaylists, Cyan);
        AddNav("＋   Import M3U", ImportPlaylist);
        AddNav("Moje źródła", ShowPrivateSources, Cyan);
        AddNav("↗   Otwórz adres", OpenUrl);
        nav.Controls.Add(Label("SPRAWDŹ ODTWARZACZ", 8, Muted, true, 164, 30));
        AddNav("▷   Test lokalny", () => { PlayDemo(); return Task.CompletedTask; });
        AddNav("?   Jak to działa", () => { Help(); return Task.CompletedTask; });
        AddNav("Przenieś bibliotekę", ImportLegacy);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = new(8, 0, 0, 0) }; body.SuspendLayout();
        body.ColumnStyles.Add(new(SizeType.Percent, 56)); body.ColumnStyles.Add(new(SizeType.Percent, 44));
        body.RowStyles.Add(new(SizeType.Absolute, 56)); body.RowStyles.Add(new(SizeType.Absolute, 42)); body.RowStyles.Add(new(SizeType.Percent, 100)); root.Controls.Add(body, 1, 1);
        var searchBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Padding = new(0, 6, 0, 8) }; searchBar.SuspendLayout();
        searchBar.ColumnStyles.Add(new(SizeType.Percent, 100)); searchBar.ColumnStyles.Add(new(SizeType.Absolute, 108)); searchBar.ColumnStyles.Add(new(SizeType.Absolute, 82)); searchBar.ColumnStyles.Add(new(SizeType.Absolute, 142)); searchBar.ColumnStyles.Add(new(SizeType.Absolute, 142));
        search.Dock = DockStyle.Fill; kind.Items.AddRange(["Radio", "TV", "Live"]); kind.SelectedIndex = 1;
        kind.DrawItem += (_, e) => { using var fill = new SolidBrush(Edge); e.Graphics.FillRectangle(fill, e.Bounds); if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, kind.Items[e.Index]!.ToString(), Font, e.Bounds, Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left); };
        searchBar.Controls.Add(search, 0, 0); searchBar.Controls.Add(kind, 1, 0);
        searchBar.Controls.Add(Button("Szukaj", SearchSubmit, true, 76), 2, 0);
        searchBar.Controls.Add(Button("Szukaj w sieci", () => SmartSearch(true, deep: true), false, 136), 3, 0);
        searchBar.Controls.Add(Button("Odśwież bazę", () => SmartSearch(true, wholeCatalog: true), false, 136), 4, 0);
        body.Controls.Add(searchBar, 0, 0); body.SetColumnSpan(searchBar, 2);
        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Run(SearchSubmit); } };
        playerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Card, ColumnCount = 1, RowCount = 6, Margin = new(0, 0, 12, 0), Padding = new(16) }; playerLayout.SuspendLayout();
        foreach (float h in new float[] { 30, -1, 44, 29, 45, 45 }) playerLayout.RowStyles.Add(new(h < 0 ? SizeType.Percent : SizeType.Absolute, h < 0 ? 100 : h));
        playerLayout.Controls.Add(Label("SWIR PLAYER  /  LIVE", 9, Cyan, true), 0, 0); playerLayout.Controls.Add(video, 0, 1);
        playerLayout.Controls.Add(now, 0, 2); playerLayout.Controls.Add(playback, 0, 3);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; buttons.SuspendLayout();
        buttons.Controls.Add(Button("|◀", () => Step(-1), width: 44)); buttons.Controls.Add(Button("▶", PlaySelected, true, 48));
        buttons.Controls.Add(Button("Ⅱ", () => { if (player?.State == 4) player.Resume(); else player?.Pause(); return Task.CompletedTask; }, width: 44));
        buttons.Controls.Add(Button("■", Stop, width: 44)); buttons.Controls.Add(Button("▶|", () => Step(1), width: 44));
        buttons.Controls.Add(Button("★", () => Favorite(true), width: 44)); buttons.Controls.Add(Button("Pełny ekran", () => { ToggleFullScreen(); return Task.CompletedTask; }, width: 110));
        playerLayout.Controls.Add(buttons, 0, 4);
        var audio = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; audio.SuspendLayout();
        audio.Controls.Add(Button("Dźwięk", () => { if (player != null) player.Muted = !player.Muted; return Task.CompletedTask; }, width: 76));
        volume.Value = store.Data.Volume; volumeText.Text = volume.Value + "%"; volumeText.Size = new(44, 30); audio.Controls.Add(volume); audio.Controls.Add(volumeText);
        clock.Size = new(115, 30); audio.Controls.Add(clock); seek.Width = 105; seek.Dock = DockStyle.None; audio.Controls.Add(seek);
        volume.ValueChanged += (_, _) => { if (player != null) player.Volume = volume.Value; volumeText.Text = volume.Value + "%"; store.Data.Volume = volume.Value; };
        seek.MouseDown += (_, _) => seeking = true; seek.MouseUp += (_, _) => { if (player?.Seekable == true) player.Position = seek.Value / 1000f; seeking = false; };
        playerLayout.Controls.Add(audio, 0, 5); body.Controls.Add(playerLayout, 0, 2);
        var listPanel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Card, Padding = new(14), ColumnCount = 1, RowCount = 5 }; listPanel.SuspendLayout();
        listPanel.RowStyles.Add(new(SizeType.Absolute, 34)); listPanel.RowStyles.Add(new(SizeType.Percent, 100)); listPanel.RowStyles.Add(new(SizeType.Absolute, 32)); listPanel.RowStyles.Add(new(SizeType.Absolute, 92)); listPanel.RowStyles.Add(new(SizeType.Absolute, 116));
        results.Columns.Add("Stacja", 250); results.Columns.Add("Stan", 142);
        results.OwnerDraw = true;
        results.DrawSubItem += DrawStation;
        results.Resize += (_, _) => { if (results.Columns.Count > 0) results.Columns[0].Width = Math.Max(100, results.ClientSize.Width - 162); };
        results.DoubleClick += (_, _) => Run(PlaySelected);
        results.SelectedIndexChanged += (_, _) => { var s = Selected; details.Text = s == null ? "Wybierz stację z listy." : SourceDetail(s); };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Odtwórz", null, (_, _) => Run(PlaySelected)); menu.Items.Add("Dodaj do ulubionych", null, (_, _) => Run(() => Favorite(true)));
        menu.Items.Add("Nie dla mnie — ukryj kanał", null, (_, _) => Run(HideChannel));
        menu.Items.Add("Usuń z ulubionych", null, (_, _) => Run(() => Favorite(false))); menu.Items.Add("Kopiuj adres transmisji", null, (_, _) => { if (Selected is { } s) Clipboard.SetText(s.Url); });
        results.ContextMenuStrip = menu;
        listPanel.Controls.Add(resultTitle, 0, 0); listPanel.Controls.Add(results, 0, 1);
        var pages = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        previousPage = Button("◀", () => ChangePage(-1), width: 42); nextPage = Button("▶", () => ChangePage(1), width: 42);
        previousPage.Height = nextPage.Height = 28;
        pageCaption = Label("1 / 1", 9, Muted, width: 55, height: 30);
        previousPage.Enabled = nextPage.Enabled = false; pages.Controls.AddRange([previousPage, pageCaption, nextPage]);
        var epgButton = Button("Program TV", ShowEpg, width: 100); epgButton.Height = 28; pages.Controls.Add(epgButton);
        epgStatus = Label("EPG: w tle", 8, Muted, width: 190, height: 30); pages.Controls.Add(epgStatus);
        listPanel.Controls.Add(pages, 0, 2); listPanel.Controls.Add(details, 0, 3);
        var listActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true }; listActions.SuspendLayout();
        listActions.Controls.Add(Button("Kanały",ShowCurrentChannels,width:74)); listActions.Controls.Add(Button("Warianty", ShowAlternatives, width: 90)); listActions.Controls.Add(Button("Wszystkie", ShowAllCandidates, width: 90));
        listActions.Controls.Add(Button("Działające", ShowWorking, width: 100)); listActions.Controls.Add(Button("Szukaj dalej", SearchMore, width: 110));
        listActions.Controls.Add(Button("Nie dla mnie", HideChannel, width: 110));
        listActions.Controls.Add(Button("Więcej źródeł", SearchChannelSources, width: 116));
        listActions.Controls.Add(Button("Eksport M3U", ExportPlaylist, width: 110)); listActions.Controls.Add(Button("Usuń ★", () => Favorite(false), width: 90));
        listPanel.Controls.Add(listActions, 0, 4); body.Controls.Add(listPanel, 1, 2); InitDiscovery(dataPath, body);
        InitProfilePanel(root); InitEpg(); InitFoundPlaylists();
        root.Controls.Add(Label("0.9.7  /  WINDOWS x64", 8, Muted), 0, 2); root.Controls.Add(status, 0, 3); root.SetColumnSpan(status, 2);
        InitScanOverlay(root);
        search.TextChanged += (_, _) =>
        {
            if(changingSearch)return;
            bool privateInput=XtreamLink.PrivateInput(search.Text);search.UseSystemPasswordChar=privateInput;
            if(privateInput){lastQuery="";status.Text="Wklejony link pozostaje prywatny. Enter lub Wklej link otworzy import.";return;}
            if(!scanBusy)Run(FilterCatalog);
        };
        if (autoDiscover) BeginScan(true);
        Shown += async (_, _) =>
        {
            StartupDiagnostics.Mark("shownMs");
            status.Text = startupMessage.Length > 0 ? startupMessage : store.Warning ?? "Przygotowuję odtwarzacz w tle…";
            if (candidates.Count > 0) ShowRecommendations(); else ShowStations("TWOJE KANAŁY", []);
            timer.Start();
            // Let Windows paint the entire window before native initialization and discovery.
            await Task.Delay(75);
            if (closing) { playerReady.TrySetResult(); return; }
            var initialization = InitializePlayer(startupDelayMs);
            if (autoDiscover && searchGeneration == 0) Run(() => SmartSearch(false, initial: true, wholeCatalog: true));
            await initialization;
            if (epgEnabled && candidates.Count > 0) Run(() => RefreshEpg(false));
        };
        timer.Tick += (_, _) => TickPlayback();
        FormClosing += (_, _) => { closing = true; privateSources?.InvalidatePending();privatePlayCancel?.Cancel(); pendingStation = null; timer.Stop(); searchCancel?.Cancel(); fullScreen?.Close(); playerStartup?.Dispose(); try { store.Save(); } catch (Exception ex) { MessageBox.Show("Nie zapisano biblioteki: " + ex.Message); } };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.F11) { ToggleFullScreen(); e.Handled = true; } if (e.KeyCode == Keys.Escape && fullScreen != null) fullScreen.Close(); };
        ResumeTree(this);
    }
    static void ResumeTree(Control control) { foreach (Control child in control.Controls) ResumeTree(child); control.ResumeLayout(false); }
    async Task InitializePlayer(int delayMs)
    {
        StartupDiagnostics.Mark("engineStartedMs");
        IntPtr target = video.Handle; // Read a WinForms handle only on its owning thread.
        playerStartup = new(() => { if (delayMs > 0) Thread.Sleep(delayMs); return new SwirPlayer(target); });
        try
        {
            var ready = await playerStartup.Ready;
            if (closing) return;
            player = ready; player.Volume = volume.Value; StartupDiagnostics.Mark("engineReadyMs");
            if (searchGeneration == 0) status.Text = store.Warning ?? "Gotowy • " + player.Version;
            if (pendingStation is { } station) { pendingStation = null; Play(station); }
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex)
        {
            StartupDiagnostics.Mark("engineFailedMs"); playerError = ex.Message;
            if (!closing) { pendingStation = null; playback.Text = "Odtwarzacz niedostępny"; status.Text = "Swir Player: " + ex.Message; }
        }
        finally { playerReady.TrySetResult(); }
    }
    static TextBox Input(string hint) => new() { PlaceholderText = hint, BackColor = Color.FromArgb(28, 35, 52), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11) };
    static Label Label(string text, float size, Color color, bool bold = false, int width = 0, int height = 0) => new()
    { Text = text, ForeColor = color, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Dock = width == 0 ? DockStyle.Fill : DockStyle.None, Size = new(width, height), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    Button Button(string text, Func<Task> action, bool accent = false, int width = 150)
    {
        var b = new Button { Text = text, Width = width, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = accent ? Cyan : Edge, ForeColor = accent ? Bg : Color.White, Cursor = Cursors.Hand, Margin = new(2, 2, 4, 2) };
        b.FlatAppearance.BorderSize = 0; b.Click += (_, _) => Run(action); return b;
    }
    void AddNav(string text, Func<Task> action, Color? color = null)
    {
        var b = Button(text, action, width: 164); b.Height = 34; b.TextAlign = ContentAlignment.MiddleLeft; b.BackColor = Card;
        if (color != null) b.ForeColor = color.Value; nav.Controls.Add(b);
    }
    async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closing) { status.Text = "Błąd: " + ex.Message; MessageBox.Show(this, ex.Message, "SwirStream AI", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    }
    Station? Selected => results.SelectedItems.Count > 0 ? results.SelectedItems[0].Tag as Station : null;
    string DemoPath => Path.Combine(AppContext.BaseDirectory, "assets", "local-test.mp4");
    void ShowStations(string title, List<Station> stations)
    {
        if(listMode!="local")title="Katalog publiczny • "+title;
        var previous = Selected; var previousUrl = previous?.Url; int topIndex = results.TopItem?.Index ?? 0;
        stations = stations.Where(s => !s.BrowserOnly).ToList();
        shown = stations; results.BeginUpdate(); results.Items.Clear();
        foreach (var s in stations) { var item = new ListViewItem(s.Name) { Tag = s, ToolTipText = PrivateStation.DisplayAddress(s) }; item.SubItems.Add(ranker == null ? s.Kind : SourceBadge(s)); results.Items.Add(item); }
        results.EndUpdate(); resultTitle.Text = title + "  /  " + stations.Count;
        if (epg != null) RefreshEpgTooltips();
        if (results.Items.Count > 0)
        {
            (results.Items.Cast<ListViewItem>().FirstOrDefault(i => (i.Tag as Station)?.Url == previousUrl)
                ?? results.Items.Cast<ListViewItem>().FirstOrDefault(i => previous != null && i.Tag is Station s && SourceRanker.ChannelKey(s) == SourceRanker.ChannelKey(previous)) ?? results.Items[0]).Selected = true;
            results.TopItem = results.Items[Math.Min(topIndex, results.Items.Count - 1)];
        }
        else details.Text = listMode=="local"&&localProviderMessage.Length>0?localProviderMessage:"Brak wyników. Spróbuj innej nazwy lub gatunku.";
    }
    Task ShowLocal(string title, List<Station> list)
    {
        searchCancel?.Cancel(); EndScan(searchGeneration); searchGeneration++;
        localProviderMessage="";localTitle = title; localStations = list; localView="all"; resultPage = 0;
        changingSearch = true; search.Clear(); changingSearch = false;
        ShowLibrary(); return Task.CompletedTask;
    }
    Task SearchKind(string value) { kind.SelectedItem = value; listMode = "top"; return candidates.Count > 0 ? FilterCatalog() : SmartSearch(false, wholeCatalog: true); }
    public void PlayDemo() => Play(new("SwirStream • test lokalny", DemoPath, "Test", "Obraz + dźwięk", "Dołączony plik testowy"));
    void Play(Station station, bool isFallback = false,ProviderPlayback? resolved=null)
    {
        if (station.BrowserOnly)
        {
            status.Text = "Ta zapisana pozycja jest stroną WWW, nie transmisją. Użyj Szukaj w sieci, aby znaleźć adres odtwarzania."; return;
        }
        if (player == null)
        {
            if (playerError.Length > 0) throw new InvalidOperationException(playerError);
            pendingStation = station; now.Text = station.Name; playback.Text = "Przygotowuję odtwarzacz — za chwilę uruchomię kanał…"; return;
        }
        if(PrivateStation.IsPrivate(station)&&resolved==null){Run(()=>PlayPrivate(station,isFallback));return;}
        if(resolved==null)privatePlayCancel?.Cancel();
        watchedSeconds = 0; learnedWatch = false; watchTick = DateTime.UtcNow;
        playbackProgress.Reset(); playbackFailure = "";
        if (!isFallback) attempted.Clear(); attempted.Add(station.Url); expectedPlayback = false; lastProgress = DateTime.UtcNow;
        var media=resolved?.Station??station;
        currentEncrypted=resolved?.ClearKeyHex.Length>0;
        player.Play(media.Url, media.UserAgent, media.Referrer,quality.Text,resolved?.ClearKeyHex??""); player.Volume = volume.Value; expectedPlayback = true; current = station; historySaved = false; playbackRequested = DateTime.UtcNow;
        now.Text = station.Name; playback.Text = "Łączenie…"; video.Active = true; video.Invalidate();
    }
    Task PlaySelected() { if (Selected != null) Play(Selected); else if (player?.State == 4) player.Resume(); else status.Text = "Najpierw wybierz stację."; return Task.CompletedTask; }
    Task Stop() { privatePlayCancel?.Cancel();pendingStation = null; expectedPlayback = false; player?.Stop(); playbackProgress.Reset(); playbackFailure = ""; playback.Text = "Zatrzymano"; video.Active = false; video.Invalidate(); return Task.CompletedTask; }
    Task Step(int delta)
    {
        if (shown.Count == 0) return Task.CompletedTask;
        int idx = current == null ? -1 : shown.FindIndex(s => s.Url == current.Url);
        idx = (idx + delta + shown.Count) % shown.Count; results.SelectedItems.Clear(); results.Items[idx].Selected = true; results.Items[idx].EnsureVisible(); Play(shown[idx]); return Task.CompletedTask;
    }
    Task Favorite(bool add)
    {
        var s = Selected ?? current; if (s == null) { status.Text = "Wybierz stację."; return Task.CompletedTask; }
        bool wasFavorite = store.Data.Favorites.Any(x => x.Url == s.Url);
        store.Favorite(s, add); if (add && !wasFavorite && taste.Learn(s, true, "favorite")) { store.Save(); UpdateTasteStatus(); } status.Text = (add ? "Dodano do ulubionych: " : "Usunięto z ulubionych: ") + s.Name;
        if (listMode == "local" && localTitle == "ULUBIONE") { localStations = store.Data.Favorites; ShowLibrary(); } return Task.CompletedTask;
    }
    async Task ImportPlaylist()
    {
        using var dialog = new OpenFileDialog { Filter = "Playlisty M3U|*.m3u;*.m3u8|Wszystkie pliki|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var source=new ProviderSource(Guid.NewGuid().ToString("N"),Path.GetFileNameWithoutExtension(dialog.FileName),PrivateSourceKind.M3uFile,dialog.FileName);
        var repository=privateSources;using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        bool loaded=await SaveConnectSource(source,timeout.Token,message=>status.Text=message);
        if(!loaded&&ReferenceEquals(repository,privateSources))await ShowPrivateSources();
    }
    async Task ImportLegacy()
    {
        using var dialog = new OpenFileDialog { Filter = "Biblioteka SwirStream (library / settings)|*.json", InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SwirStreamAI") };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        int count = store.MergeLibrary(dialog.FileName); await ShowLocal("ULUBIONE", store.Data.Favorites); status.Text = $"Przeniesiono {count} wpisów z biblioteki.";
    }
    Task ExportPlaylist()
    {
        if(shown.Count>0&&shown.All(PrivateStation.IsPrivate)){status.Text="Prywatne źródła pozostają w chronionym magazynie. Eksport M3U nie ujawnia ich danych dostępu.";return Task.CompletedTask;}
        using var dialog = new SaveFileDialog { Filter = "Playlista M3U|*.m3u", FileName = "SwirStream-playlista.m3u" };
        if (dialog.ShowDialog(this) == DialogResult.OK) {File.WriteAllText(dialog.FileName, M3u.Export(shown), new System.Text.UTF8Encoding(true));status.Text="Wyeksportowano publiczne pozycje. Prywatne źródła pominięto.";}
        return Task.CompletedTask;
    }
    Task OpenUrl()=>SmartImport();
    async Task OpenAddress(string url)
    {
        await AddOwnAddress(url);
    }
    string? Prompt(string caption)
    {
        using var f = new Form { Text = "Otwórz transmisję", Width = 630, Height = 185, BackColor = Card, ForeColor = Color.White, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var t = Input("https://…"); t.UseSystemPasswordChar=true; t.SetBounds(18, 48, 575, 30); var label = Label(caption, 10, Color.White, width: 580, height: 30); label.Location = new(18, 12);
        var ok = new Button { Text = "Otwórz", DialogResult = DialogResult.OK, Bounds = new(480, 90, 110, 32) };
        f.Controls.AddRange([label, t, ok]); f.AcceptButton = ok; return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
    }
    void TickPlayback()
    {
        if (player == null || current == null) return;
        var stats = player.Stats; int state = player.State;
        var observation = ObservePlayback(state, stats, Environment.TickCount64);
        if (state == 4 || observation.Advancing) lastProgress = DateTime.UtcNow;
        if (expectedPlayback && state != 4 && (state == 7 || (state == 6 && player.Length <= 0) ||
            (!historySaved && playbackProgress.StartupElapsedMs > (currentEncrypted ? 35000 : 15000)) ||
            (state == 3 && DateTime.UtcNow - lastProgress > TimeSpan.FromSeconds(20))))
        {
            string reason = currentEncrypted && (state == 7 || !historySaved)
                ? SwirPlayer.EncryptedDecodeFailureMessage
                : "Transmisja nie dostarcza poprawnego obrazu lub dźwięku. Odśwież źródło albo wybierz inny wariant.";
            TryFallback(reason); return;
        }
        seek.Enabled = player.Seekable && player.Length > 0;
        if (!seeking) seek.Value = Math.Clamp((int)(player.Position * 1000), 0, 1000);
        clock.Text = player.Length > 0 ? TimeSpan.FromMilliseconds(Math.Max(0, player.Time)).ToString(@"mm\:ss") + " / " + TimeSpan.FromMilliseconds(player.Length).ToString(@"mm\:ss") : "LIVE";
        if (state is 5 or 6 or 7) { video.Active = false; video.Invalidate(); }
    }
    PlaybackProgressState ObservePlayback(int state, SwirMediaStats stats, long nowMs)
    {
        if (current == null) return default;
        var observation = playbackProgress.Observe(state, stats, current.Kind, currentEncrypted, nowMs);
        if (state == 3)
        {
            bool decoded = observation.Confirmed;
            playback.Text = decoded ? "● Odtwarzanie potwierdzone • " + current.Kind : "Odbieranie transmisji…";
            playback.ForeColor = decoded ? Cyan : Muted;
            if (decoded && observation.Advancing && !historySaved) { historySaved = true; try { store.Played(current); RecordSuccess(); } catch (Exception ex) { status.Text = "Nie zapisano historii: " + ex.Message; } }
        }
        else { playback.ForeColor = Muted; playback.Text = playbackFailure.Length > 0 && state is 5 or 6 or 7 ? playbackFailure : state switch { 1 => "Łączenie…", 2 => "Buforowanie…", 4 => "Pauza", 5 => "Zatrzymano", 6 => "Koniec transmisji", 7 => currentEncrypted ? SwirPlayer.EncryptedDecodeFailureMessage : "Nie można odtworzyć — źródło niedostępne lub nieobsługiwane", _ => "Gotowy" }; }
        TrackWatching(state, observation.Confirmed && observation.Advancing);
        return observation;
    }
    void ToggleFullScreen()
    {
        if (fullScreen != null) { fullScreen.Close(); return; }
        fullScreen = new Form { Text = "SwirStream — pełny ekran", BackColor = Color.Black, FormBorderStyle = FormBorderStyle.None, WindowState = FormWindowState.Maximized, KeyPreview = true };
        var fullLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
        fullLayout.RowStyles.Add(new(SizeType.Absolute, 36)); fullLayout.RowStyles.Add(new(SizeType.Percent, 100));
        fullScreen.Controls.Add(fullLayout); playerLayout.Controls.Remove(video); fullLayout.Controls.Add(video, 0, 1);
        var exit = new Button { Text = "Wróć • Esc / F11", Dock = DockStyle.Top, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Card, ForeColor = Color.White };
        fullLayout.Controls.Add(exit, 0, 0); exit.Click += (_, _) => fullScreen?.Close();
        fullScreen.KeyDown += (_, e) => { if (e.KeyCode is Keys.Escape or Keys.F11) fullScreen?.Close(); };
        fullScreen.FormClosing += (_, _) => { fullLayout.Controls.Remove(video); playerLayout.Controls.Add(video, 0, 1); fullScreen = null; };
        fullScreen.Show(this);
    }
    void Help() => MessageBox.Show(this,
        "EPG pod kanałem pokazuje obecną i następną audycję. Program TV otwiera opisy i ręczne dopasowanie. Źródła EPG pozwalają wskazać własny XMLTV. Brak ramówki nie oznacza braku transmisji.\n\nSzukaj w sieci / Głębokie szukanie: większy zakres, do 120 stron / 3 min. Kolejne przebiegi wybierają mniej sprawdzone strony. Lista zawiera adresy transmisji. Strony WWW nie są odtwarzane ani otwierane automatycznie. Znalezione playlisty można wczytać lub pobrać jako M3U.\n\n" +
        "Na początku zaczekaj na zakończenie pierwszego wyszukiwania. Potem wpisuj nazwy w polu Szukaj: filtruje ono gotową bazę bez zmieniania jej w tle. Szukaj w sieci dobiera źródła wskazanego kanału, Odśwież bazę ponawia pełne wyszukiwanie. Strzałki pod listą pokazują kolejne strony wszystkich zebranych kanałów.\n\nTasteNet rozpoznaje tematykę. Dopasowanie uczy się lokalnie z ulubionych, oglądania i Nie dla mnie. AI ustala kolejność propozycji; nie usuwa kanału z powodu niskiej oceny. Jawne filtry kraju, tematyki i ukryte kanały nadal obowiązują.\n\nDziałające = niedawno potwierdzone dekodowanie. Program sprawdza wybrane propozycje, a niesprawdzone i niedostępne adresy pozostają w bazie. Warianty pokazują inne źródła kanału.\n\nDwuklik = odtwarzanie. F11 = pełny ekran. Esc = powrót. Odśwież bazę nie aktualizuje EXE ani silnika.",
        "SwirStream AI — by Swir", MessageBoxButtons.OK, MessageBoxIcon.Information);
    public (int State, SwirMediaStats Stats, int Volume, bool Muted, int History, int Favorites) Diagnostics => (player?.State ?? -1, player?.Stats ?? default, volume.Value, player?.Muted ?? false, store.Data.History.Count, store.Data.Favorites.Count);
    public void SavePreview(string path)
    {
        Control target = scanBusy && scanOverlay.Visible ? scanOverlay : this;
        using var bmp = new Bitmap(target.Width, target.Height); target.DrawToBitmap(bmp, new(0, 0, target.Width, target.Height)); bmp.Save(path);
    }
}

sealed class VideoSurface : Panel
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active { get; set; }
    public VideoSurface() { BackColor = Color.FromArgb(6, 9, 16); DoubleBuffered = false; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (Active) return;
        using var pen = new Pen(Color.FromArgb(22, 35, 48));
        for (int x = 0; x < Width; x += 40) e.Graphics.DrawLine(pen, x, 0, x, Height);
        for (int y = 0; y < Height; y += 40) e.Graphics.DrawLine(pen, 0, y, Width, y);
        using var font = new Font("Segoe UI", 26, FontStyle.Bold); using var small = new Font("Segoe UI", 10);
        TextRenderer.DrawText(e.Graphics, "SWIR / LIVE", font, new Rectangle(0, Height / 2 - 36, Width, 50), Color.FromArgb(66, 230, 217), TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(e.Graphics, "Wybierz transmisję. Resztą zajmie się player.", small, new Rectangle(0, Height / 2 + 22, Width, 30), Color.FromArgb(157, 170, 192), TextFormatFlags.HorizontalCenter);
    }
}
