using System.Text.Json;

namespace SwirStreamAI;

public sealed class StartupWindow : Form
{
    readonly Label phase = new() { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(165,178,200), Font = new("Segoe UI",11), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    readonly Button offline = new() { Text = "Użyj poprzedniej bazy", Width = 205, Height = 36, Enabled = false, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(38,47,67), ForeColor = Color.White };
    public event Action? UsePrevious;
    public StartupWindow()
    {
        Text = "SwirStream AI — przygotowanie kanałów"; ClientSize = new(820,470); MinimumSize = new(720,430);
        StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(10,13,22); ForeColor = Color.White;
        Icon = new(Path.Combine(AppContext.BaseDirectory,"assets","swirstream-v070.ico"));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(40), RowCount = 6, ColumnCount = 1 };
        foreach(int height in new[]{95,65,65,20,65,45}) layout.RowStyles.Add(new(SizeType.Absolute,height));
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents=false };
        using var brandIcon=new Icon(Path.Combine(AppContext.BaseDirectory,"assets","swirstream-v070.ico"),128,128);
        header.Controls.Add(new PictureBox { Image = brandIcon.ToBitmap(), Size = new(72,72), SizeMode=PictureBoxSizeMode.Zoom, Margin = new(0,0,20,0) });
        header.Controls.Add(new Label { Text = "SWIRSTREAM AI\nby Swir", Width = 520, Height = 80, Font = new("Segoe UI",24,FontStyle.Bold), ForeColor = Color.FromArgb(66,230,217) });
        layout.Controls.Add(header,0,0);
        layout.Controls.Add(new Label { Text = "Proszę czekać — trwa wyszukiwanie kanałów", Dock = DockStyle.Fill, Font = new("Segoe UI",17,FontStyle.Bold) },0,1);
        phase.Text = "Przygotowuję wyszukiwanie…"; layout.Controls.Add(phase,0,2);
        layout.Controls.Add(new ProgressBar { Dock=DockStyle.Fill, Style=ProgressBarStyle.Marquee, MarqueeAnimationSpeed=35 },0,3);
        var actions = new FlowLayoutPanel { Dock=DockStyle.Fill, Padding=new(0,14,0,0) };
        offline.Click += (_,_)=>UsePrevious?.Invoke(); actions.Controls.Add(offline);
        var cancel = new Button { Text="Anuluj i zamknij", Width=180, Height=36, FlatStyle=FlatStyle.Flat, BackColor=Color.FromArgb(38,47,67),ForeColor=Color.White };
        cancel.Click+=(_,_)=>Close(); actions.Controls.Add(cancel); layout.Controls.Add(actions,0,4);
        layout.Controls.Add(new Label { Text="Odtwarzacz otworzy się po zakończeniu skanowania. EPG pobierze się w tle.",Dock=DockStyle.Fill,Font=new("Segoe UI",9),ForeColor=Color.FromArgb(165,178,200)},0,5);
        Controls.Add(layout);
    }
    public void Progress(string text) => phase.Text=text;
    public void AllowPrevious(bool allow) => offline.Enabled=allow;
}

public sealed class StartupGate : ApplicationContext
{
    readonly StartupWindow splash = new();
    SwirStreamAI.MainForm? player;
    bool allowClose, cancelled, usePrevious;
    readonly Func<SwirStreamAI.MainForm> create;
    readonly Func<SwirStreamAI.MainForm,Task>? scan;
    public event Action<SwirStreamAI.MainForm,StartupWindow>? ScanStarting;
    public event Action<SwirStreamAI.MainForm>? PlayerShown;
    public StartupGate(Func<SwirStreamAI.MainForm> create, Func<SwirStreamAI.MainForm,Task>? scan = null)
    {
        this.create=create;this.scan=scan;
        splash.FormClosing+=(_,e)=>
        {
            if(allowClose)return;
            e.Cancel=true;cancelled=true;splash.Progress("Kończę wyszukiwanie…");player?.CancelStartupSearch();
        };
        splash.UsePrevious+=()=>{usePrevious=true;splash.Progress("Kończę skanowanie i otwieram poprzednią bazę…");player?.CancelStartupSearch();};
        splash.Shown+=async(_,_)=>await Start();
        splash.Show();
    }
    async Task Start()
    {
        await Task.Delay(75);
        try
        {
            if(cancelled) { Finish(); return; }
            player=create(); player.SetAutomaticEpg(false);
            player.StartupProgress += splash.Progress;
            int cached=player.RestoreStartupCatalog(); splash.AllowPrevious(cached>0);
            ScanStarting?.Invoke(player,splash);
            if(scan!=null)await scan(player);else await player.PrepareStartupCatalog();
            if(cancelled) { player.Close();player.Dispose();Finish();return; }
            player.StartupProgress-=splash.Progress;
            if(usePrevious)player.StartupMessage("Otwarto poprzednią bazę. Nowe wyszukiwanie możesz uruchomić przyciskiem.");
            player.SetAutomaticEpg(scan==null); MainForm=player;
            allowClose=true;splash.Close();splash.Dispose();player.Show();PlayerShown?.Invoke(player);
        }
        catch(Exception ex)
        {
            player?.Dispose();
            if(!cancelled) MessageBox.Show(splash,"Nie udało się przygotować kanałów.\n"+ex.Message,"SwirStream AI",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            Finish();
        }
    }
    void Finish(){allowClose=true;splash.Close();splash.Dispose();ExitThread();}
}

public sealed partial class MainForm
{
    string startupMessage="";
    public void StartupMessage(string message)=>startupMessage=message;
    public void CancelStartupSearch()=>searchCancel?.Cancel();
    public async Task PrepareStartupCatalog(){await SmartSearch(false,initial:true,wholeCatalog:true);startupMessage=status.Text;}
    public int RestoreStartupCatalog()
    {
        string path=Path.Combine(dataFolder,"last-discovery.json");
        try
        {
            if(!File.Exists(path)||new FileInfo(path).Length>40_000_000)return 0;
            using var json=JsonDocument.Parse(File.ReadAllText(path));var root=json.RootElement;
            if(!(root.TryGetProperty("catalogDataAt",out var completed)||root.TryGetProperty("completedAt",out completed))||!completed.TryGetDateTimeOffset(out var date)||date>DateTimeOffset.UtcNow.AddMinutes(5)||date<DateTimeOffset.UtcNow.AddDays(-7))return 0;
            var countries=root.GetProperty("countries").Deserialize<string[]>()??[];
            if(!countries.Order().SequenceEqual(store.Data.SelectedCountries.Order()))return 0;
            var saved=root.GetProperty("sources").EnumerateArray().Select(x=>x.GetProperty("station").Deserialize<Station>()).OfType<Station>()
                .Where(s=>!s.BrowserOnly&&!string.IsNullOrWhiteSpace(s.Name)&&s.Url!=null&&s.Kind!=null&&s.ChannelId!=null&&s.Country!=null&&s.Group!=null&&s.Homepage!=null&&s.Feed!=null&&s.Quality!=null&&s.UserAgent!=null&&s.Referrer!=null&&SharedPlaylists.AllowedStream(s.Url))
                .Where(s=>!PublicPlaylistPrivacy.ContainsCredentials(s.Url)&&!PublicPlaylistPrivacy.ContainsCredentials(s.Referrer)&&!PublicPlaylistPrivacy.ContainsCredentials(s.Homepage))
                .Where(s=>(kind.Text=="Live"||s.Kind==kind.Text)&&(countries.Length==0||countries.Contains(s.Country))).Take(20000).ToList();
            var directory=saved.DistinctBy(SourceRanker.ChannelKey).ToList();
            candidates=saved;knownChannels=directory;catalogScope=Scope(kind.Text,countries);catalogDataAt=date;
            ShowRecommendations();
            return taste.Filter(candidates).Count();
        }
        catch(Exception ex)when(ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException){return 0;}
    }
    public void SetStartupTestStations(List<Station> stations)
    {candidates=stations;knownChannels=stations;catalogScope=Scope(kind.Text,store.Data.SelectedCountries);ShowRecommendations();}
}
