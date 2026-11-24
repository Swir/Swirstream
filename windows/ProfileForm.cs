using static SwirStreamAI.UiLanguage;
namespace SwirStreamAI;

public sealed partial class MainForm
{
    readonly ProfileManager profiles;
    readonly IntentNet categoryModel;
    TasteEngine taste = null!;
    SearchLearning searchLearning = null!;
    readonly ComboBox profilePicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 185, BackColor = Edge, ForeColor = Color.White };
    readonly ComboBox topicPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, BackColor = Edge, ForeColor = Color.White };
    readonly Label tasteStatus = Label("", 9, Muted);
    bool applyingProfile, learnedWatch;
    double watchedSeconds;
    DateTime watchTick = DateTime.UtcNow;
    void InitProfilePanel(TableLayoutPanel root)
    {
        taste = new(store.Data, categoryModel); ranker.Personalize = s => taste.Score(s);
        searchLearning = new(profiles.Folder(profiles.Index.ActiveId),store.Data,categoryModel);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Card, Padding = new(12, 8, 12, 8), Margin = new(8, 12, 0, 0) };
        panel.RowStyles.Add(new(SizeType.Absolute, 36)); panel.RowStyles.Add(new(SizeType.Percent, 100));
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        controls.Controls.Add(Label(T("DLA CIEBIE"), 9, Purple, true, 92, 30)); controls.Controls.Add(profilePicker);
        controls.Controls.Add(Button(T("Nowy profil"), CreateProfile, width: 105)); controls.Controls.Add(topicPicker);
        controls.Controls.Add(Button(T("Preferencje"), EditPreferences, width: 108)); controls.Controls.Add(Button(T("Ułóż playlistę"), OrganizePlaylist, width: 120));
        panel.Controls.Add(controls, 0, 0); panel.Controls.Add(tasteStatus, 0, 1); root.Controls.Add(panel, 1, 2);
        topicPicker.Items.Add(new Topic("all", T("Każda tematyka"))); topicPicker.Items.AddRange(TasteEngine.Topics); topicPicker.Items.Add(new Topic("unknown", T("Bez kategorii")));
        FillProfiles(); ApplyProfileControls();
        profilePicker.SelectedIndexChanged += (_, _) => { if (!applyingProfile && profilePicker.SelectedItem is ViewingProfile p && p.Id != profiles.Index.ActiveId) Run(() => SwitchProfile(p.Id)); };
        topicPicker.SelectionChangeCommitted += (_, _) => { if (applyingProfile) return; store.Data.TopicFilter = (topicPicker.SelectedItem as Topic)?.Code ?? "all"; store.Save(); resultPage = 0; RefreshDiscoveryList(); };
        kind.SelectedIndexChanged += (_, _) => { if (!applyingProfile) { store.Data.MediaKind = kind.Text; store.Save(); listMode = "top"; Run(FilterCatalog); } };
        AddNav("Ukryte kanały", ManageHidden);
        AddNav("Sprawdź playlistę", AnalyzeOwnPlaylist);
    }
    void FillProfiles()
    {
        applyingProfile = true; profilePicker.Items.Clear(); profilePicker.Items.AddRange(profiles.Index.Profiles.ToArray()); profilePicker.SelectedItem = profiles.Index.Profiles.Single(p => p.Id == profiles.Index.ActiveId); applyingProfile = false;
    }
    void ApplyProfileControls()
    {
        applyingProfile = true;
        kind.SelectedItem = store.Data.MediaKind; if (kind.SelectedIndex < 0) kind.SelectedItem = "TV";
        quality.SelectedItem = store.Data.QualityMode; if (quality.SelectedIndex < 0) quality.SelectedIndex = 0;
        topicPicker.SelectedItem = topicPicker.Items.Cast<Topic>().FirstOrDefault(t => t.Code == store.Data.TopicFilter) ?? topicPicker.Items[0];
        countriesButton.Text = CountryCaption(); verify.Checked = store.Data.AutoVerify; fallback.Checked = store.Data.AutoFallback; webSearch.Checked = store.Data.SearchWeb; volume.Value = store.Data.Volume;
        applyingProfile = false; UpdateTasteStatus();
    }
    void UpdateTasteStatus() => tasteStatus.Text = $"AI tego profilu • {(store.Data.LearnPreferences ? store.Data.TasteExamples + " wyborów • " + searchLearning.ExampleCount + " prób źródeł" : "uczenie AI wyłączone")} • lista IPTV: {(store.Data.SearchPlaylists ? "włączone" : "wyłączone")}";
    async Task SwitchProfile(string id, bool searchNow = true)
    {
        searchCancel?.Cancel(); EndScan(searchGeneration); searchGeneration++; await Stop(); current = null; store.Save(); profiles.Activate(id);
        now.Text=T("Twoje miejsce na live.");playback.Text="Wybierz kanał w bieżącym profilu.";localProviderMessage="";
        store = new(profiles.Folder(id)); InitPrivateStore();taste = new(store.Data, categoryModel); searchLearning=new(profiles.Folder(id),store.Data,categoryModel); candidates.Clear(); knownChannels.Clear(); localStations = []; localTitle = ""; listMode = "top"; catalogScope = ""; ApplyProfileControls(); FillProfiles(); ShowStations("PROFIL: " + ((ViewingProfile)profilePicker.SelectedItem!).Name, []);
        if (searchNow) await SmartSearch(false, wholeCatalog: true);
    }
    Task CreateProfile()
    {
        using var dialog = new Form { Text = "Nowy profil oglądania", Size = new(420, 175), StartPosition = FormStartPosition.CenterParent, BackColor = Card, ForeColor = Color.White, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var name = Input("Nazwa, np. Sport lub Anna"); name.SetBounds(18, 20, 370, 32);
        var save = new Button { Text = "Utwórz", DialogResult = DialogResult.OK, Bounds = new(260, 68, 125, 32) }; dialog.Controls.AddRange([name, save]); dialog.AcceptButton = save;
        if (dialog.ShowDialog(this) == DialogResult.OK) { var p = profiles.Add(name.Text); FillProfiles(); Run(() => SwitchProfile(p.Id)); }
        return Task.CompletedTask;
    }
    Task EditPreferences()
    {
        using var dialog = new Form { Text = "Co chcesz oglądać?", Size = new(490, 550), StartPosition = FormStartPosition.CenterParent, BackColor = Card, ForeColor = Color.White };
        var topics = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BackColor = Card, ForeColor = Color.White, BorderStyle = BorderStyle.None, Font = Font };
        foreach (var topic in TasteEngine.Topics) topics.Items.Add(topic, store.Data.PreferredTopics.Contains(topic.Code));
        var intro = Label("Zaznaczone tematy dostaną pierwszeństwo.\nFiltr pod listą pozwala wybrać wyłącznie jeden temat.", 10, Muted); intro.Dock = DockStyle.Top; intro.Height = 58;
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 190, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var learn = new CheckBox { Text = "Ucz AI z moich wyborów i skuteczności źródeł", AutoSize = true, Checked = store.Data.LearnPreferences, ForeColor = Color.White };
        var lists = new CheckBox { Text = "Szukaj publicznych list IPTV", AutoSize = true, Checked = store.Data.SearchPlaylists, ForeColor = Color.White };
        var index = new CheckBox { Text = "Szukaj także w indeksie adresów transmisji", AutoSize = true, Checked = store.Data.SearchIndex, ForeColor = Color.White };
        var reset = new CheckBox { Text = "Wyzeruj całe dopasowanie AI tego profilu", AutoSize = true, ForeColor = Muted };
        var save = new Button { Text = T("Zapisz"), Width = 120, Height = 34, DialogResult = DialogResult.OK };
        bottom.Controls.AddRange([learn, lists, index, reset, save]); dialog.Controls.Add(topics); dialog.Controls.Add(intro); dialog.Controls.Add(bottom);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            store.Data.PreferredTopics = topics.CheckedItems.Cast<Topic>().Select(t => t.Code).ToArray(); store.Data.LearnPreferences = learn.Checked; store.Data.SearchPlaylists = lists.Checked; store.Data.SearchIndex = index.Checked;
            if (reset.Checked) ResetProfileLearning();
            store.Save(); UpdateTasteStatus(); Run(() => SmartSearch(false, wholeCatalog: true));
        }
        return Task.CompletedTask;
    }
    void ResetProfileLearning()
    {
        store.Data.TasteWeights=new double[512];store.Data.TasteSignals.Clear();store.Data.TasteExamples=0;searchLearning.Reset();
        string visits=Path.Combine(profiles.Folder(profiles.Index.ActiveId),"visited-broadcasters.json");
        if(File.Exists(visits))File.Delete(visits);
    }
    Task HideChannel()
    {
        if (Selected is not { } s) return Task.CompletedTask;
        store.Data.HiddenChannels.Add(SourceRanker.ChannelKey(s)); taste.Learn(s, false, "hide"); store.Save(); UpdateTasteStatus(); ShowRecommendations(); status.Text = "Ukryto w tym profilu: " + s.Name; return Task.CompletedTask;
    }
    Task ManageHidden()
    {
        if (store.Data.HiddenChannels.Count == 0) { status.Text = "Ten profil nie ma ukrytych kanałów."; return Task.CompletedTask; }
        using var dialog = new Form { Text = "Ukryte kanały tego profilu", Size = new(560, 450), BackColor = Card, ForeColor = Color.White, StartPosition = FormStartPosition.CenterParent };
        var list = new CheckedListBox { Dock = DockStyle.Fill, BackColor = Card, ForeColor = Color.White, CheckOnClick = true };
        foreach (string key in store.Data.HiddenChannels) list.Items.Add(key);
        var restore = new Button { Text = "Przywróć zaznaczone", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.OK };
        dialog.Controls.Add(list); dialog.Controls.Add(restore);
        if (dialog.ShowDialog(this) == DialogResult.OK) { foreach (string key in list.CheckedItems) store.Data.HiddenChannels.Remove(key); store.Save(); ShowRecommendations(); }
        return Task.CompletedTask;
    }
    Task OrganizePlaylist()
    {
        using var options = new Form { Text = "Twoja uporządkowana playlista", Size = new(470, 220), BackColor = Card, ForeColor = Color.White, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var sourcePicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new(18, 20, 410, 32) }; sourcePicker.Items.AddRange(["Bieżąca lista", "Wszystkie wyniki wyszukiwania", T("Ulubione"), "Moja playlista"]); sourcePicker.SelectedIndex = 0;
        var working = new CheckBox { Text = "Tylko odtwarzanie potwierdzone w ostatnich 10 min", AutoSize = true, Location = new(18, 62), Checked = true };
        var go = new Button { Text = "Ułóż i zapisz", DialogResult = DialogResult.OK, Bounds = new(280, 105, 145, 34) };
        options.Controls.AddRange([sourcePicker, working, go]);
        if (options.ShowDialog(this) != DialogResult.OK) return Task.CompletedTask;
        IEnumerable<Station> source = sourcePicker.SelectedIndex switch { 1 => candidates, 2 => store.Data.Favorites, 3 => store.Data.Playlist, _ => shown };
        if (working.Checked) source = source.Where(s => ranker.Results.TryGetValue(s.Url, out var h) && DiscoveryAnalyzer.FreshSuccess(h));
        var list = ranker.Top(taste.Filter(source), quality.Text, 1000).Select(r => r.Station).ToList();
        if (list.Count == 0) { status.Text = "Najpierw wyszukaj lub zaimportuj kanały."; return Task.CompletedTask; }
        using var dialog = new SaveFileDialog { Filter = "Playlista M3U|*.m3u", FileName = "SwirStream-moja-telewizja.m3u" };
        if (dialog.ShowDialog(this) == DialogResult.OK) { File.WriteAllText(dialog.FileName, taste.Export(list), new System.Text.UTF8Encoding(true)); status.Text = $"Zapisano {list.Count} kanałów, pogrupowanych według kraju i tematyki."; }
        return Task.CompletedTask;
    }
    async Task AnalyzeOwnPlaylist()
    {
        var own=store.Data.Playlist.ToList();if(own.Count==0){status.Text="Najpierw wczytaj własne źródło lub playlistę.";return;}
        await ShowLocal("MOJA PLAYLISTA",own);
        searchCancel?.Cancel(); searchCancel = new CancellationTokenSource(TimeSpan.FromMinutes(7)); long generation = ++searchGeneration;
        int tried=await Analyze(generation,searchCancel.Token,source:own,final:false);
        if(generation==searchGeneration&&!closing){RefreshBadges();status.Text=$"Własna playlista: {tried} prób • {WorkingCount(own)} kanałów z potwierdzonym dekodowaniem.";}
    }
    void TrackWatching(int state, bool confirmedProgress)
    {
        var now = DateTime.UtcNow; double elapsed = Math.Clamp((now - watchTick).TotalSeconds, 0, 2); watchTick = now;
        if (state != 3 || current == null || !confirmedProgress || learnedWatch) return;
        watchedSeconds += elapsed;
        if (watchedSeconds >= 30) { learnedWatch = true; if (taste.Learn(current, true, "watch")) { store.Save(); UpdateTasteStatus(); } }
    }
    // Exercises the same player/store paths without restoring a command assistant to the product.
    public Task TestControl(string action)
    {
        switch (action)
        {
            case "pause": player?.Pause(); break; case "resume": player?.Resume(); break;
            case "quieter": volume.Value = Math.Max(0, volume.Value - 10); break;
            case "mute": if (player != null) player.Muted = true; break;
            case "unmute": if (player != null) player.Muted = false; break;
            case "volume40": volume.Value = 40; break; case "favorite": return Favorite(true);
            case "favorites": return ShowLocal("ULUBIONE", store.Data.Favorites); case "stop": return Stop();
        }
        return Task.CompletedTask;
    }
    public async Task ProfileUiTest()
    {
        string original = profiles.Index.ActiveId; var p = profiles.Add("Test izolacji " + DateTime.UtcNow.Ticks);
        var fixture=new Station("Pamięć profilu","https://example.org/profile-test.m3u8","TV",Homepage:"https://example.org",Country:"PL",ChannelId:"Memory.pl");
        searchLearning.Record(fixture,new(DateTimeOffset.UtcNow,true,500));int originalExamples=searchLearning.ExampleCount;
        await SwitchProfile(p.Id, false); if (store.Data.Favorites.Count != 0 || store.Data.History.Count != 0) throw new Exception("Biblioteka profilu nie jest odizolowana.");
        if(searchLearning.ExampleCount!=0)throw new Exception("Uczenie wyszukiwania przeciekło do nowego profilu.");
        searchLearning.Record(fixture,new(DateTimeOffset.UtcNow,false,500));if(searchLearning.ExampleCount!=1)throw new Exception("Nowy profil nie zapisał własnej próby.");
        ResetProfileLearning();if(searchLearning.ExampleCount!=0||store.Data.TasteExamples!=0)throw new Exception("Reset nie wyzerował uczenia tego profilu.");
        await SwitchProfile(original, false); if (store.Data.Favorites.Count == 0) throw new Exception("Utracono ulubione po zmianie profilu.");
        if(searchLearning.ExampleCount!=originalExamples)throw new Exception("Reset innego profilu zmienił uczenie pierwotnego.");
        if (Controls.Cast<Control>().SelectMany(AllControls).OfType<TextBox>().Any(t => t.PlaceholderText.Contains("Napisz np."))) throw new Exception("Pozostał panel asystenta.");
    }
    static IEnumerable<Control> AllControls(Control c) => new[] { c }.Concat(c.Controls.Cast<Control>().SelectMany(AllControls));
}
