using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

public record Station(string Name, string Url, string Kind = "Live", string Group = "", string Source = "M3U", string Homepage = "", string UserAgent = "", string Referrer = "", string Country = "", string ChannelId = "", string[]? Aliases = null, string Quality = "", string Feed = "", bool BrowserOnly = false, string AccessNote = "",string PrivateSourceId="",string PrivateStreamId="");
public record Visit(Station Station, DateTimeOffset PlayedAt);
public sealed class LibraryData
{
    public List<Station> Favorites { get; set; } = [];
    public List<Visit> History { get; set; } = [];
    public List<Station> Playlist { get; set; } = [];
    public int Volume { get; set; } = 65;
    public string[] SelectedCountries { get; set; } = ["PL"];
    public string QualityMode { get; set; } = "Stabilność";
    public bool SearchWeb { get; set; } = true;
    public bool AutoVerify { get; set; } = true;
    public bool AutoFallback { get; set; } = true;
    public string[] PreferredTopics { get; set; } = [];
    public string TopicFilter { get; set; } = "all";
    public string MediaKind { get; set; } = "TV";
    public bool LearnPreferences { get; set; } = true;
    public bool SearchPlaylists { get; set; } = true;
    public bool SearchIndex { get; set; } = true;
    public HashSet<string> HiddenChannels { get; set; } = [];
    public double[] TasteWeights { get; set; } = new double[512];
    public int TasteExamples { get; set; }
    public Dictionary<string, DateTimeOffset> TasteSignals { get; set; } = [];
}
public static class TextTools
{
    public static string Normalize(string text) => new(text.ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    public static bool IsWeb(string text) => Uri.TryCreate(text, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && u.UserInfo.Length == 0 && !string.IsNullOrWhiteSpace(u.Host);
}
public sealed class LibraryStore
{
    readonly string path;
    public string FilePath => path;
    public LibraryData Data { get; private set; } = new();
    public Action? BeforeSave {get;set;}
    public string? Warning { get; private set; }
    public LibraryStore(string folder)
    {
        Directory.CreateDirectory(folder); path = Path.Combine(folder, "library.json");
        if (!File.Exists(path)) return;
        try
        {
            Data = JsonSerializer.Deserialize<LibraryData>(File.ReadAllText(path)) ?? throw new JsonException();
            if (Data.Favorites == null || Data.History == null || Data.Playlist == null) throw new JsonException();
            Data.Volume = Math.Clamp(Data.Volume, 0, 100);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            string backup = path + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            PrivateData.Write(backup+".dpapi",File.ReadAllBytes(path),"library-backup-v1",backup:false); Data = new();
            Warning = "Uszkodzona biblioteka. Zachowano chronioną kopię: " + backup+".dpapi";
        }
    }
    public void Save()
    {
        BeforeSave?.Invoke();
        var tmp = path + ".tmp";
        var safe=JsonSerializer.SerializeToNode(Data)!;
        safe["Playlist"]=JsonSerializer.SerializeToNode(Data.Playlist.Select(PrivateStation.ForStorage));
        safe["Favorites"]=JsonSerializer.SerializeToNode(Data.Favorites.Select(PrivateStation.ForStorage));
        safe["History"]=JsonSerializer.SerializeToNode(Data.History.Select(v=>v with{Station=PrivateStation.ForStorage(v.Station)}));
        File.WriteAllText(tmp, JsonSerializer.Serialize(safe, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(path)) PrivateData.Write(path+".bak.dpapi",File.ReadAllBytes(path),"library-backup-v1",backup:false);
        if(File.Exists(path+".bak"))
        {
            byte[] previous=File.ReadAllBytes(path+".bak");string encrypted=path+".legacy-bak.dpapi";
            PrivateData.Write(encrypted,previous,"library-backup-v1",backup:false);
            if(!PrivateData.Read<byte[]>(encrypted,"library-backup-v1").SequenceEqual(previous))throw new IOException("Nie udało się zabezpieczyć starej kopii biblioteki.");
            File.Delete(path+".bak");
        }
        File.Move(tmp, path, true);
    }
    public void HydratePrivate(Func<Station,Station> display)
    {Data.Playlist=Data.Playlist.Select(display).ToList();Data.Favorites=Data.Favorites.Select(display).ToList();Data.History=Data.History.Select(v=>v with{Station=display(v.Station)}).ToList();}
    public void Favorite(Station station, bool add)
    {
        Data.Favorites.RemoveAll(s => s.Url == station.Url);
        if (add) Data.Favorites.Add(station);
        Save();
    }
    public void Played(Station station)
    {
        Data.History.RemoveAll(v => v.Station.Url == station.Url);
        Data.History.Insert(0, new(station, DateTimeOffset.Now));
        Data.History = Data.History.Take(200).ToList(); Save();
    }
    public int ImportLegacy(string file)
    {
        if (new FileInfo(file).Length > 20 * 1024 * 1024) throw new IOException("Plik biblioteki jest za duży.");
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        List<Station> Read(string key)
        {
            if (!doc.RootElement.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array) return [];
            var stations = new List<Station>();
            foreach (var item in list.EnumerateArray())
            {
                string Get(string k) => item.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                if (item.ValueKind == JsonValueKind.Object && TextTools.IsWeb(Get("url")))
                    stations.Add(new(Get("name"), Get("url"), Get("kind"), Get("group"), Get("source"), Get("website"), Get("user_agent"), Get("referrer")));
            }
            return stations.DistinctBy(s => s.Url).Take(30000).ToList();
        }
        var favorites = Read("favorites"); var playlist = Read("own_streams"); var history = Read("history");
        if (favorites.Count + playlist.Count + history.Count == 0) throw new IOException("Nie znaleziono stacji w bibliotece starej wersji.");
        Data.Favorites = Data.Favorites.Concat(favorites).DistinctBy(s => s.Url).ToList();
        Data.Playlist = Data.Playlist.Concat(playlist).DistinctBy(s => s.Url).ToList();
        Data.History = Data.History.Concat(history.Select(s => new Visit(s, DateTimeOffset.MinValue))).DistinctBy(v => v.Station.Url).Take(200).ToList();
        Save(); return favorites.Count + playlist.Count + history.Count;
    }
    public int MergeLibrary(string file)
    {
        if (new FileInfo(file).Length > 20_000_000) throw new IOException("Za duży plik biblioteki.");
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        if (!doc.RootElement.TryGetProperty("Favorites", out _)) return ImportLegacy(file);
        var other = JsonSerializer.Deserialize<LibraryData>(doc.RootElement.GetRawText()) ?? throw new InvalidDataException("Nieprawidłowa biblioteka.");
        if (other.Favorites == null || other.Playlist == null || other.History == null) throw new InvalidDataException("Brak wymaganych list biblioteki.");
        bool Valid(Station s) => s != null && !string.IsNullOrWhiteSpace(s.Name) && (TextTools.IsWeb(s.Url) || File.Exists(s.Url));
        Data.Favorites = Data.Favorites.Concat(other.Favorites.Where(Valid)).DistinctBy(s => s.Url).Take(30000).ToList();
        Data.Playlist = Data.Playlist.Concat(other.Playlist.Where(Valid)).DistinctBy(s => s.Url).Take(30000).ToList();
        Data.History = Data.History.Concat(other.History.Where(v => v != null && Valid(v.Station))).DistinctBy(v => v.Station.Url).OrderByDescending(v => v.PlayedAt).Take(200).ToList();
        Save(); return other.Favorites.Count + other.Playlist.Count + other.History.Count;
    }
}
public static class M3u
{
    public static bool IsHls(string text) => Regex.IsMatch(text, @"(?m)^#EXT-X-(TARGETDURATION|STREAM-INF|MEDIA-SEQUENCE|VERSION):");
    public static List<Station> Parse(string text, string source, bool allowLocal = false)
    {
        if (IsHls(text)) return [new(Path.GetFileName(source), source, "Live", "HLS", source)];
        var result = new List<Station>(); string name = "", group = "", userAgent = "", referrer = "", channelId = "", country = "", feed = "";
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimStart('\uFEFF');
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = Regex.Match(line, "(?:\"[^\"]*\"|[^,])* ,", RegexOptions.IgnorePatternWhitespace);
                name = comma.Success ? line[comma.Length..].Trim() : "";
                group = Regex.Match(line, "group-title=\"([^\"]*)\"", RegexOptions.IgnoreCase).Groups[1].Value;
                var tvgName = Regex.Match(line, "tvg-name=\"([^\"]*)\"", RegexOptions.IgnoreCase).Groups[1].Value;
                if (tvgName.Length > 0) name = tvgName;
                var identity = Regex.Match(line, "tvg-id=\"([^\"]*)\"", RegexOptions.IgnoreCase).Groups[1].Value.Split('@', 2); channelId = identity[0]; feed = identity.Length == 2 ? identity[1] : "";
                country = Regex.Match(line, "tvg-country=\"([a-zA-Z]{2})\"", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant();
                userAgent = referrer = "";
            }
            else if (line.StartsWith("#EXTVLCOPT:http-user-agent=", StringComparison.OrdinalIgnoreCase)) userAgent = line.Split('=', 2)[1];
            else if (line.StartsWith("#EXTVLCOPT:http-referrer=", StringComparison.OrdinalIgnoreCase)) referrer = line.Split('=', 2)[1];
            else if (line.Length > 0 && !line.StartsWith('#'))
            {
                string url = line;
                if (!Uri.TryCreate(line, UriKind.Absolute, out _) && TextTools.IsWeb(source)) url = new Uri(new Uri(source), line).AbsoluteUri;
                else if (!TextTools.IsWeb(url) && allowLocal)
                {
                    try { url = Path.GetFullPath(line, Path.GetDirectoryName(Path.GetFullPath(source))!); } catch { url = ""; }
                }
                if (TextTools.IsWeb(url) || (allowLocal && File.Exists(url)))
                    result.Add(new(string.IsNullOrWhiteSpace(name) ? url : name, url, "Live", group, source, UserAgent: userAgent, Referrer: referrer, ChannelId: channelId, Feed: feed, Country: Regex.Match(channelId, @"\.([a-z]{2})$").Groups[1].Value.ToUpperInvariant() is var idCountry && idCountry.Length > 0 ? idCountry : country));
                name = group = userAgent = referrer = channelId = country = feed = "";
            }
        }
        return result.DistinctBy(s => s.Url).Take(30000).ToList();
    }
    public static string Export(IEnumerable<Station> stations)
    {
        string Clean(string s) => s.Replace('\r', ' ').Replace('\n', ' ');
        return "#EXTM3U\n" + string.Join("\n", stations.Where(s => !s.BrowserOnly&&!PrivateStation.IsPrivate(s)).Select(s => "#EXTINF:-1 tvg-id=\"" + Clean(s.ChannelId + (s.Feed.Length > 0 ? "@" + s.Feed : "")).Replace('"', '\'') + "\" tvg-country=\"" + Clean(s.Country).Replace('"', '\'') + "\" group-title=\"" + Clean(s.Group).Replace('"', '\'') + "\"," + Clean(s.Name) + "\n" +
            (s.UserAgent.Length > 0 ? "#EXTVLCOPT:http-user-agent=" + Clean(s.UserAgent) + "\n" : "") +
            (TextTools.IsWeb(s.Referrer) ? "#EXTVLCOPT:http-referrer=" + Clean(s.Referrer) + "\n" : "") + Clean(s.Url))) + "\n";
    }
}
public record SearchResult(List<Station> Stations, string Detail, DateTimeOffset? DataAt = null);
public sealed class Catalog
{
    public const string TvEndpoint = "https://iptv-org.github.io/iptv/index.m3u";
    public static readonly string[] RadioServers = ["https://de1.api.radio-browser.info"];
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(22), MaxResponseContentBufferSize = 20 * 1024 * 1024 };
    List<Station>? tv;
    DateTimeOffset fetchedAt;
    public Catalog() { if (!Http.DefaultRequestHeaders.UserAgent.Any()) Http.DefaultRequestHeaders.UserAgent.ParseAdd("SwirStreamAI/0.1.0"); }
    public static async Task<string> Download(string url, CancellationToken ct)
    {
        if (!TextTools.IsWeb(url)) throw new ArgumentException("Dozwolone są adresy http:// i https://.");
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 20 * 1024 * 1024) throw new IOException("Plik jest za duży (limit 20 MB).");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(); var block = new byte[16384]; int n;
        while ((n = await stream.ReadAsync(block, ct)) > 0)
        {
            if (buffer.Length + n > 20 * 1024 * 1024) throw new IOException("Plik jest za duży (limit 20 MB).");
            buffer.Write(block, 0, n);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
    public async Task<SearchResult> SearchTv(string query, bool refresh, CancellationToken ct)
    {
        if (refresh || tv == null || DateTimeOffset.UtcNow - fetchedAt > TimeSpan.FromHours(6))
        {
            var text = await Download(TvEndpoint, ct);
            var parsed = M3u.Parse(text, TvEndpoint).Select(s => s with { Kind = "TV", Source = "iptv-org" }).ToList();
            if (parsed.Count == 0) throw new IOException("Katalog TV nie zawiera transmisji.");
            tv = parsed; fetchedAt = DateTimeOffset.UtcNow;
        }
        var q = TextTools.Normalize(query).Trim();
        // A country request uses the provider's dedicated country feed, with the remaining category filters applied.
        var pool = tv;
        if (Regex.IsMatch(q, @"\b(polskie|polska|polskiej|polski|po polsku)\b"))
        {
            pool = M3u.Parse(await Download("https://iptv-org.github.io/iptv/countries/pl.m3u", ct), "https://iptv-org.github.io/iptv/countries/pl.m3u")
                .Select(s => s with { Kind = "TV", Source = "iptv-org • Polska" }).ToList();
            q = Regex.Replace(q, @"\b(polskie|polska|polskiej|polski|po polsku)\b", " ");
        }
        q = Regex.Replace(q, @"\b(kanaly|telewizji|telewizyjne)\b", " ");
        q = Regex.Replace(q, @"\b(informacyjne|wiadomosci)\b", "news");
        q = Regex.Replace(q, @"\b(muzyczne|muzyczna)\b", "music");
        q = Regex.Replace(q, @"\b(sportowe)\b", "sport");
        q = Regex.Replace(q, @"\b(dzieciece|bajki)\b", "kids");
        var matches = pool.Where(s => q.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => TextTools.Normalize(s.Name + " " + s.Group).Contains(w))).Take(1000).ToList();
        return new(matches, $"iptv-org • pobrano {fetchedAt.ToLocalTime():dd.MM HH:mm} • dostępność potwierdza odtwarzanie");
    }
    public async Task<SearchResult> SearchRadio(string query, CancellationToken ct)
    {
        Exception? last = null;
        foreach (string server in RadioServers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string q = Uri.EscapeDataString(query.Trim());
                string baseQuery = "?hidebroken=true&limit=100&order=clickcount&reverse=true";
                var urls = string.IsNullOrWhiteSpace(query) ? new[] { server + "/json/stations/search" + baseQuery } :
                    new[] { server + "/json/stations/search" + baseQuery + "&name=" + q, server + "/json/stations/search" + baseQuery + "&tag=" + q };
                if (TextTools.Normalize(query.Trim()) is "polska" or "polskie") urls = [server + "/json/stations/search" + baseQuery + "&countrycode=PL"];
                var responses = await Task.WhenAll(urls.Select(u => Download(u, ct)));
                var stations = new List<Station>();
                foreach (var json in responses)
                {
                    using var doc = JsonDocument.Parse(json);
                    foreach (var s in doc.RootElement.EnumerateArray())
                    {
                        string Get(string key) => s.TryGetProperty(key, out var p) ? p.GetString() ?? "" : "";
                        string url = Get("url_resolved"); if (!TextTools.IsWeb(url)) url = Get("url");
                        if (TextTools.IsWeb(url)) stations.Add(new(Get("name").Trim(), url, "Radio", Get("tags"), "Radio Browser", Get("homepage")));
                    }
                }
                return new(stations.DistinctBy(s => s.Url).ToList(), $"Radio Browser • pobrano {DateTimeOffset.Now:HH:mm} • dostępność potwierdza odtwarzanie");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
            { ct.ThrowIfCancellationRequested(); last = ex; }
        }
        throw new IOException("Katalog radia jest niedostępny. Sprawdź internet lub spróbuj ponownie.", last);
    }
}
