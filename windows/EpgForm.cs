namespace SwirStreamAI;

public sealed partial class MainForm
{
    EpgService epg = null!;
    readonly System.Windows.Forms.Timer epgTimer = new() { Interval = 30000 };
    CancellationTokenSource epgCancel = new();
    bool epgBusy;
    bool epgRefreshAgain;
    string? epgPendingCountry;
    bool epgEnabled = true;
    DateTimeOffset epgLastAttempt = DateTimeOffset.MinValue;
    readonly ImageList rowHeight = new() { ImageSize = new(1, 70), ColorDepth = ColorDepth.Depth32Bit };
    Label epgStatus = null!;
    public void SetAutomaticEpg(bool enabled) { epgEnabled = enabled; if (enabled && !closing) epgTimer.Start(); else epgTimer.Stop(); }
    void InitEpg()
    {
        epg = new(dataFolder);
        results.SmallImageList = rowHeight; results.ShowItemToolTips = true;
        results.ContextMenuStrip!.Items.Add("Program TV / dopasuj EPG", null, (_, _) => Run(ShowEpg));
        AddNav("EPG • program TV", ShowEpg, Cyan);
        AddNav("Odśwież EPG", () => RefreshEpg(true));
        AddNav("Źródła EPG", ConfigureEpg);
        epgTimer.Tick += (_, _) =>
        {
            results.Invalidate(); RefreshEpgTooltips();
            if (!scanBusy && DateTimeOffset.UtcNow - epgLastAttempt > TimeSpan.FromMinutes(15)) Run(() => RefreshEpg(false));
        };
        epgTimer.Start();
        FormClosing += (_, _) => { epgCancel.Cancel(); epgTimer.Stop(); };
    }
    async Task RefreshEpg(bool force, string? requestedCountry = null)
    {
        if (closing) return;
        if (epgBusy) { if (force) { epgRefreshAgain = true; epgPendingCountry = requestedCountry; epgStatus.Text = "EPG: ponowię po bieżącym pobieraniu…"; } return; }
        var countries = (requestedCountry != null ? new[] { requestedCountry } : store.Data.SelectedCountries.Length > 0 ? store.Data.SelectedCountries : shown.Where(s => s.Kind != "Radio"&&!PrivateStation.IsPrivate(s)).Select(s => s.Country)).Where(c=>c.Length==2).Distinct().Take(12).ToArray();
        if (countries.Length == 0) return;
        epgBusy = true; epgLastAttempt = DateTimeOffset.UtcNow; epgStatus.Text = "EPG: pobieram ramówkę w tle…"; results.Invalidate();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(epgCancel.Token); timeout.CancelAfter(TimeSpan.FromMinutes(3));
            // All I/O, XML parsing and cache serialization run away from the window thread.
            await Task.Run(() => epg.Refresh(countries, force, timeout.Token), timeout.Token);
            if (closing) return;
            int matched = shown.Count(s => Slot(s).Now != null);
            epgStatus.Text = $"EPG: teraz na {matched}/{shown.Count} pozycji • Program TV = szczegóły";
        }
        catch (OperationCanceledException) { if (!closing) epgStatus.Text = "EPG: przerwano pobieranie • zachowano aktualne dane"; }
        catch (Exception ex) { if (!closing) epgStatus.Text = "EPG: " + ex.Message; }
        finally
        {
            epgBusy = false;
            if (!closing)
            {
                results.Invalidate(); RefreshEpgTooltips();
                if (epgRefreshAgain) { epgRefreshAgain = false; var pending = epgPendingCountry; epgPendingCountry = null; Run(() => RefreshEpg(true, pending)); }
            }
        }
    }
    void RefreshEpgTooltips()
    {
        int withNow = 0;
        foreach (ListViewItem item in results.Items) if (item.Tag is Station s)
        { var slot = Slot(s); if (slot.Now != null) withNow++; item.ToolTipText = s.Name + "\n" + EpgService.Line(slot.Now, "Teraz: ") + "\n" + EpgService.Line(slot.Next, "Następnie: ") + (s.BrowserOnly ? "\n" + s.AccessNote : ""); }
        if (!epgBusy) epgStatus.Text = $"EPG teraz: {withNow}/{shown.Count}";
    }
    void DrawStation(object? sender, DrawListViewSubItemEventArgs e)
    {
        bool selected = e.Item?.Selected == true;
        using var fill = new SolidBrush(selected ? Edge : Card); e.Graphics.FillRectangle(fill, e.Bounds);
        if (e.ColumnIndex != 0 || e.Item?.Tag is not Station station)
        { TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", Font, e.Bounds, selected ? Cyan : Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix); return; }
        var bounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 3, e.Bounds.Width - 8, 23);
        using var bold = new Font(Font, FontStyle.Bold); using var small = new Font("Segoe UI", 8.5f);
        TextRenderer.DrawText(e.Graphics, station.Name, bold, bounds, selected ? Cyan : Color.White, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var slot = epg==null?null:Slot(station); bounds.Y += 24; bounds.Height = 18;
        string currentLine = station.Kind == "Radio" ? "Radio • ramówka TV nie dotyczy" : slot?.Now == null && epgBusy ? "Pobieram EPG…" : EpgService.Line(slot?.Now, "Teraz: ");
        TextRenderer.DrawText(e.Graphics, currentLine, small, bounds, Cyan, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        bounds.Y += 18;
        TextRenderer.DrawText(e.Graphics, station.Kind == "Radio" ? "" : EpgService.Line(slot?.Next, "Potem: "), small, bounds, Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
    Task ShowEpg()
    {
        var station = Selected ?? current;
        if (station == null) { status.Text = "Wybierz kanał z listy, aby otworzyć jego program TV."; return Task.CompletedTask; }
        using var dialog = new Form { Text = "Program TV • " + station.Name, Size = new(860, 640), BackColor = Card, ForeColor = Color.White, Font = Font, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false };
        var guide = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Card, ForeColor = Color.White, Font = Font };
        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 90, Padding = new(6) };
        var choices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 510, BackColor = Edge, ForeColor = Color.White, DisplayMember = "Name", ValueMember = "Id" };
        var country = station.Country;
        var channels = epg.Feeds.Where(f => f.Country == country).SelectMany(f => f.Channels).OrderBy(c => c.Names.FirstOrDefault() ?? c.Id).ToList();
        choices.Items.Add(new EpgChoice("", "Automatyczne dopasowanie")); choices.Items.Add(new EpgChoice("!none", "Bez EPG dla tego kanału"));
        foreach (var c in channels) choices.Items.Add(new EpgChoice(c.Id, (c.Names.FirstOrDefault() ?? c.Id) + " • " + c.Id));
        string assigned = epg.Settings.Matches.GetValueOrDefault(EpgService.MappingKey(station), "");
        choices.SelectedItem = choices.Items.Cast<EpgChoice>().FirstOrDefault(c => c.Id == assigned) ?? choices.Items[0];
        void Fill()
        {
            var slot = Slot(station); var match = slot.Match;
            guide.Text = EpgService.Line(slot.Now, "TERAZ: ") + "\r\n" + EpgService.Line(slot.Next, "NASTĘPNIE: ") + "\r\n\r\nGodziny w strefie Windows: " + TimeZoneInfo.Local.DisplayName + "\r\n" +
                (match == null ? "Nie znaleziono jednoznacznego EPG. Możesz dopasować kanał ręcznie u góry.\r\n" : $"Dopasowanie: {match.Method} • {match.Channel.Id}\r\nŹródło: {match.Feed.Source}\r\nPobrano: {match.Feed.Downloaded.ToLocalTime():dd.MM.yyyy HH:mm}\r\n\r\n" + string.Join("\r\n\r\n", match.Channel.Programmes.Where(p => p.Stop > DateTimeOffset.UtcNow).Take(50).Select(p => $"{p.Start.ToLocalTime():dd.MM HH:mm}–{p.Stop.ToLocalTime():HH:mm} • {p.Title}\r\n{p.Description}"))) + "\r\n\r\n" + epg.Notice.Replace("\n", "\r\n");
        }
        controls.Controls.Add(new Label { Text = "Ramówka kanału (ten sam kraj):", AutoSize = true, Width = 700 }); controls.SetFlowBreak(controls.Controls[0], true);
        controls.Controls.Add(choices); controls.Controls.Add(Button("Zastosuj", () =>
        {
            string id = ((EpgChoice)choices.SelectedItem!).Id;
            if (id.Length == 0) epg.Settings.Matches.Remove(EpgService.MappingKey(station)); else epg.Settings.Matches[EpgService.MappingKey(station)] = id;
            epg.SaveSettings(); Fill(); results.Invalidate(); RefreshEpgTooltips(); return Task.CompletedTask;
        }, width: 120));
        controls.Enabled=!PrivateStation.IsPrivate(station);
        if(PrivateStation.IsPrivate(station))((Label)controls.Controls[0]).Text="EPG operatora • aktualizacja w Moich źródłach";
        dialog.Controls.Add(guide); dialog.Controls.Add(controls); Fill(); dialog.Shown += (_, _) => { guide.Select(0,0); choices.Focus(); }; dialog.ShowDialog(this); return Task.CompletedTask;
    }
    record EpgChoice(string Id, string Name) { public override string ToString() => Name; }
    Task ConfigureEpg() => ConfigureEpg(null,null);
    Task ConfigureEpg(string? suggestedUrl,string? suggestedCountry)
    {
        using var dialog = new Form { Text = "Źródła EPG • XMLTV", Size = new(740, 430), BackColor = Card, ForeColor = Color.White, Font = Font, StartPosition = FormStartPosition.CenterParent };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(20) };
        layout.Controls.Add(new Label { Text = "EPG pobiera się w tle. Możesz podać własną ramówkę XMLTV lub XMLTV.GZ.\nPusty adres przywraca źródła domyślne. Dane EPG nie zapewniają odbioru TV.", Width = 660, Height = 65 });
        var countries = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 }; countries.Items.AddRange(Countries.Load().ToArray());
        countries.SelectedItem = countries.Items.Cast<Country>().FirstOrDefault(c => c.Code == (suggestedCountry??store.Data.SelectedCountries.FirstOrDefault())) ?? countries.Items[0];
        var input = Input("Adres https://… lub plik XMLTV"); input.Width = 660; input.UseSystemPasswordChar=true;
        void LoadCountry() => input.Text = epg.Settings.Sources.GetValueOrDefault(((Country)countries.SelectedItem!).Code, "");
        countries.SelectedIndexChanged += (_, _) => LoadCountry(); LoadCountry();
        if(suggestedUrl!=null)input.Text=suggestedUrl;
        layout.Controls.Add(countries); layout.Controls.Add(input);
        var reveal=new CheckBox{Text="Pokaż adres na tym ekranie",AutoSize=true,ForeColor=Color.White};reveal.CheckedChanged+=(_,_)=>input.UseSystemPasswordChar=!reveal.Checked;layout.Controls.Add(reveal);
        layout.Controls.Add(Button("Wybierz plik…", () => { using var f = new OpenFileDialog { Filter = "Ramówka XMLTV|*.xml;*.xmltv;*.gz" }; if (f.ShowDialog(dialog) == DialogResult.OK) input.Text = f.FileName; return Task.CompletedTask; }, width: 160));
        layout.Controls.Add(Button("Zapisz i odśwież", () =>
        {
            string url = input.Text.Trim(), country = ((Country)countries.SelectedItem!).Code;
            if (url.Length > 0 && !PublicWeb.Allowed(url) && !(Path.IsPathFullyQualified(url) && File.Exists(url))) throw new IOException("Podaj publiczny adres HTTP/HTTPS albo istniejący plik XMLTV.");
            if (url.Length == 0) epg.Settings.Sources.Remove(country); else epg.Settings.Sources[country] = url;
            epg.SaveSettings(); dialog.DialogResult = DialogResult.OK; dialog.Close(); return Task.CompletedTask;
        }, width: 180));
        dialog.Controls.Add(layout);dialog.Shown+=(_,_)=>BeginInvoke(()=>privateDialogPreview?.Invoke(dialog)); if (dialog.ShowDialog(this) == DialogResult.OK) Run(() => RefreshEpg(true, ((Country)countries.SelectedItem!).Code)); return Task.CompletedTask;
    }
}
