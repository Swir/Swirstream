using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

public record Country(string Code, string Name)
{
    public override string ToString() => (UiLanguage.IsEnglish ? WorldMarkets.Name(Code) : Name) + " (" + Code + ")";
    public bool Matches(string text)
    {
        string query = TextTools.Normalize(text.Trim());
        return query.Length == 0 || TextTools.Normalize(Name+" "+WorldMarkets.Name(Code)+" "+Code+(Code=="GB"?" UK":"")).Contains(query)
            || Countries.NativeNames(Code).Contains(query);
    }
}
public static class Countries
{
    static readonly Lazy<Dictionary<string,string>> LocalNames = new(() =>
    {
        var names = new Dictionary<string,HashSet<string>>();
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            RegionInfo region;
            try { region = new RegionInfo(culture.Name); } catch (ArgumentException) { continue; }
            string code = region.TwoLetterISORegionName;
            if (!names.TryGetValue(code, out var values)) names[code] = values = [];
            values.Add(TextTools.Normalize(region.NativeName));
        }
        return names.ToDictionary(item => item.Key, item => string.Join(" ", item.Value));
    });
    internal static string NativeNames(string code) => LocalNames.Value.GetValueOrDefault(code,"");
    public static List<Country> Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "countries.json")));
        return doc.RootElement.EnumerateArray().Select(x => {
            string code = x.GetProperty("code").GetString()!, name = x.GetProperty("name").GetString()!;
            return new Country(WorldMarkets.Canonical(code), code == "UK" ? "Wielka Brytania" : name);
        }).DistinctBy(c => c.Code).OrderBy(c => c.ToString(), StringComparer.Create(UiLanguage.IsEnglish ? CultureInfo.GetCultureInfo("en") : CultureInfo.GetCultureInfo("pl"),true)).ToList();
    }
    public static (string Query, string[] Codes) Extract(string text, string[] selected)
    {
        string q = TextTools.Normalize(text); var codes = new List<string>();
        var aliases = new Dictionary<string, string> { ["polsk\\w*"]="PL", ["niemieck\\w*|niemiec|niemcy"]="DE", ["norwes\\w*|norwegii"]="NO", ["francusk\\w*|francji"]="FR", ["brytyjsk\\w*|wielkiej brytanii"]="GB", ["amerykansk\\w*|usa"]="US", ["czesk\\w*|czech"]="CZ", ["ukrainsk\\w*|ukrainy"]="UA", ["wlosk\\w*|wloch"]="IT", ["hiszpansk\\w*|hiszpanii"]="ES" };
        foreach (var (pattern, code) in aliases) if (Regex.IsMatch(q, @"\b(?:" + pattern + @")\b")) { codes.Add(code); q = Regex.Replace(q, @"\b(?:" + pattern + @")\b", " "); }
        q = Regex.Replace(q, @"\b(z|i|oraz|po|w|najlepsze|najlepszy|stabilne|stabilny|polecane|kanaly|telewizji|stacje)\b", " ");
        q = Regex.Replace(q, @"\b(znajdz|wyszukaj|szukaj|poszukaj|przeszukaj|pokaz|polec|mi|dla|mnie|kilka|wszystkie|dzialajace|dzialajacych|dobrych|najlepszych|zrodla|zrodel|opcje|opcji|streamy|streamow|transmisje|transmisji|sieci|sieciowe|internecie|internet|internetu|tv|telewizja|telewizje|telewizyjne|radio|radia|na|zywo|kanalow)\b", " ");
        q = Regex.Replace(q, @"\b(informacyjne|wiadomosci)\b", "news"); q = Regex.Replace(q, @"\b(muzyczne|muzyczna)\b", "music");
        q = Regex.Replace(q, @"\b(sportowe)\b", "sport"); q = Regex.Replace(q, @"\b(dzieciece|bajki)\b", "kids");
        return (Regex.Replace(q, @"\s+", " ").Trim(), codes.Count > 0 ? codes.Distinct().ToArray() : selected);
    }
}
public sealed class SourceSettings
{
    public int Schema { get; set; } = 1;
    public string[] TvTemplates { get; set; } = ["https://iptv-org.github.io/iptv/{path}", "https://raw.githubusercontent.com/iptv-org/iptv/gh-pages/{path}"];
    public string[] RadioMirrors { get; set; } = Catalog.RadioServers;
    public string[] ChannelDirectoryUrls { get; set; } = ["https://iptv-org.github.io/api/channels.json", "https://raw.githubusercontent.com/iptv-org/api/gh-pages/channels.json"];
    public string[] StreamDirectoryUrls { get; set; } = ["https://iptv-org.github.io/api/streams.json", "https://raw.githubusercontent.com/iptv-org/api/gh-pages/streams.json"];
    public string WebSearchTemplate { get; set; } = "https://www.bing.com/search?format=rss&q={query}";
    public string[] SharedPlaylistUrls { get; set; } = ["https://raw.githubusercontent.com/Free-TV/IPTV/master/playlist.m3u8"];
    [System.Text.Json.Serialization.JsonIgnore] public string Notice { get; set; } = "";
    public static SourceSettings Load(string folder)
    {
        var path = Path.Combine(folder, "sources.json");
        if (!File.Exists(path)) File.WriteAllText(path, JsonSerializer.Serialize(new SourceSettings(), new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            var settings = JsonSerializer.Deserialize<SourceSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Pusty plik");
            if (settings.Schema != 1 || settings.SharedPlaylistUrls is not { Length: <= 8 } || settings.SharedPlaylistUrls.Any(t => t == null || !PublicWeb.Allowed(t)) || settings.TvTemplates is not { Length: > 0 and <= 5 } || settings.RadioMirrors is not { Length: > 0 and <= 8 } || settings.ChannelDirectoryUrls is not { Length: > 0 and <= 5 } ||
                settings.StreamDirectoryUrls is not { Length: > 0 and <= 5 } || settings.StreamDirectoryUrls.Any(t => t == null || !PublicWeb.Allowed(t)) || settings.ChannelDirectoryUrls.Any(t => t == null || !TextTools.IsWeb(t)) || settings.WebSearchTemplate == null || !settings.WebSearchTemplate.Contains("{query}") || !PublicWeb.Allowed(settings.WebSearchTemplate.Replace("{query}", "test")) ||
                settings.TvTemplates.Any(t => t == null || !t.Contains("{path}") || !TextTools.IsWeb(t.Replace("{path}", "index.m3u"))) || settings.RadioMirrors.Any(t => !TextTools.IsWeb(t)))
                throw new InvalidDataException("Nieprawidłowe adresy katalogów.");
            // These old bootstrap names no longer resolve in the provider's
            // current public server list. Keep user-defined alternatives intact.
            settings.RadioMirrors=settings.RadioMirrors.Where(u=>u.TrimEnd('/') is not "https://nl1.api.radio-browser.info" and not "https://at1.api.radio-browser.info").ToArray();
            if(settings.RadioMirrors.Length==0)settings.RadioMirrors=Catalog.RadioServers;
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            File.Copy(path, path + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
            var defaults = new SourceSettings { Notice = "Odtworzono ustawienia źródeł. Uszkodzony sources.json zachowano jako .damaged." };
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true }));
            return defaults;
        }
    }
}
public record FeedCache(int Schema, DateTimeOffset DownloadedAt, DateTimeOffset? ModifiedAt, string Source, List<Station> Stations);
public sealed class SmartCatalog
{
    readonly string cacheFolder;
    public string Notice => settings.Notice;
    public Action<FoundPlaylist>? PlaylistFound { get; set; }
    readonly SourceSettings settings;
    readonly Func<string, CancellationToken, Task<(string Body, DateTimeOffset? Modified)>> fetch;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    public SmartCatalog(string folder, SourceSettings? settings = null, Func<string, CancellationToken, Task<(string, DateTimeOffset?)>>? fetch = null)
    {
        Directory.CreateDirectory(folder); cacheFolder = Path.Combine(folder, "catalog-cache"); Directory.CreateDirectory(cacheFolder);
        this.settings = settings ?? SourceSettings.Load(folder); this.fetch = fetch ?? Fetch;
    }
    static async Task<(string, DateTimeOffset?)> Fetch(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.UserAgent.ParseAdd("SwirStreamAI/0.11.0");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 20_000_000) throw new IOException("Katalog przekracza 20 MB.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token); using var memory = new MemoryStream();
        var chunk = new byte[16384]; int n;
        while ((n = await stream.ReadAsync(chunk, deadline.Token)) > 0) { if (memory.Length + n > 20_000_000) throw new IOException("Za duży katalog."); memory.Write(chunk, 0, n); }
        return (Encoding.UTF8.GetString(memory.ToArray()), response.Content.Headers.LastModified);
    }
    static bool PublicStation(Station s)=>!PublicPlaylistPrivacy.ContainsCredentials(s.Url)&&!PublicPlaylistPrivacy.ContainsCredentials(s.Referrer)&&!PublicPlaylistPrivacy.ContainsCredentials(s.Homepage)&&!PublicPlaylistPrivacy.ContainsCredentials(s.Source);
    static bool Usable(FeedCache c, DateTimeOffset now, TimeSpan maxAge) => c.Schema == 1 && c.Stations != null && !PublicPlaylistPrivacy.ContainsCredentials(c.Source) && c.Stations.All(PublicStation) && c.DownloadedAt <= now.AddMinutes(5) && now - c.DownloadedAt <= maxAge && (!c.ModifiedAt.HasValue || c.ModifiedAt <= now.AddMinutes(10) && now - c.ModifiedAt <= TimeSpan.FromDays(7));
    static DateTimeOffset DataAt(FeedCache c) => c.ModifiedAt.HasValue && c.ModifiedAt.Value < c.DownloadedAt ? c.ModifiedAt.Value : c.DownloadedAt;
    async Task<SearchResult> Feed(string key, string[] urls, Func<string, List<Station>> parse, bool refresh, CancellationToken ct)
    {
        string path = Path.Combine(cacheFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key + string.Join('|', urls)))) + ".json");
        FeedCache? cached = null; var now = DateTimeOffset.UtcNow;
        try { if (File.Exists(path)) cached = JsonSerializer.Deserialize<FeedCache>(await File.ReadAllTextAsync(path, ct)); } catch (Exception ex) when (ex is JsonException or IOException) { }
        if(cached?.Stations!=null&&(!cached.Stations.All(PublicStation)||PublicPlaylistPrivacy.ContainsCredentials(cached.Source)))
        {
            var safe=cached.Stations.Where(PublicStation).ToList();
            if(PublicPlaylistPrivacy.ContainsCredentials(cached.Source)||safe.Count==0){cached=null;File.Delete(path);}
            else{cached=cached with{Stations=safe};await File.WriteAllTextAsync(path,JsonSerializer.Serialize(cached),ct);}
        }
        if (!refresh && cached != null && Usable(cached, now, TimeSpan.FromHours(6))) return new(cached.Stations, $"{key}: zapis {cached.DownloadedAt.ToLocalTime():dd.MM HH:mm}", DataAt(cached));
        var errors = new List<string>();
        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if(PublicPlaylistPrivacy.ContainsCredentials(url))throw new InvalidDataException("Publiczny katalog nie przyjmuje adresu z danymi konta.");
                var (body, modified) = await fetch(url, ct);
                if (modified > now.AddMinutes(10) || modified < now.AddDays(-7)) throw new IOException("Nieaktualna data źródła (>7 dni) lub data z przyszłości.");
                string publicBody=body;int removed=0;
                if(body.TrimStart('\uFEFF',' ','\r','\n').StartsWith("#EXTM3U")){var clean=PublicPlaylistPrivacy.SanitizeM3u(url,body);publicBody=clean.Body;removed=clean.RemovedEntries;}
                var parsedStations=parse(publicBody);var stations=parsedStations.Where(PublicStation).ToList();removed+=parsedStations.Count-stations.Count;
                if(parsedStations.Count>0&&stations.Count==0)throw new InvalidDataException("Katalog nie zawiera bezpiecznych publicznych wpisów.");
                var entry = new FeedCache(1, now, modified, url, stations);
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(entry), ct); ct.ThrowIfCancellationRequested(); File.Move(temp, path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                foreach (var old in new DirectoryInfo(cacheFolder).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(512)) try { old.Delete(); } catch (IOException) { }
                string playlistNotice = removed>0?" • pominięto wpisy zawierające dane konta: "+removed:"";
                if (PlaylistFound != null && body.TrimStart('\uFEFF',' ','\r','\n').StartsWith("#EXTM3U") && !M3u.IsHls(body))
                {
                    try { PlaylistFound(PlaylistRepository.Create(new(url, body, "application/x-mpegurl", modified), stations, [], "", now)); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    { playlistNotice += " • nie zapisano znalezionej playlisty: " + ex.Message; }
                }
                return new(stations, $"{key}: pobrano {now.ToLocalTime():HH:mm}" + (modified.HasValue ? $", źródło {modified.Value.ToLocalTime():dd.MM HH:mm}" : ", źródło nie podaje daty aktualizacji") + (url == urls[0] ? "" : " • serwer zapasowy") + playlistNotice, DataAt(entry));
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException or InvalidOperationException)
            { ct.ThrowIfCancellationRequested(); errors.Add(new Uri(url).Host + ": " + ex.Message); }
        }
        if (cached != null && Usable(cached, now, TimeSpan.FromDays(7))) return new(cached.Stations, $"{key}: TRYB AWARYJNY • zapis {cached.DownloadedAt.ToLocalTime():dd.MM HH:mm} • katalog offline", DataAt(cached));
        throw new IOException(key + ": brak aktualnego katalogu. " + string.Join("; ", errors));
    }
    public async Task<SearchResult> Search(string query, string mode, string[] countries, bool refresh, CancellationToken ct)
    {
        countries = countries.Where(c => Regex.IsMatch(c, "^[A-Z]{2}$")).Select(WorldMarkets.Canonical).Distinct().ToArray();
        bool globalTv = countries.Length > 12;
        var regions = countries.Length == 0 ? [""] : globalTv ? new[] { "" }.Concat(countries).ToArray() : countries;
        using var gate = new SemaphoreSlim(3); var jobs = new List<Task<SearchResult>>();
        foreach (var country in regions)
        {
            if (mode != "Radio" && (!globalTv || country.Length == 0)) jobs.Add(Job(async () => {
                string path = country.Length == 0 ? "index.m3u" : "countries/" + WorldMarkets.TvCatalogCode(country) + ".m3u";
                var result = await Feed("TV " + country + " (kraj kanału)", settings.TvTemplates.Select(t => t.Replace("{path}", path)).ToArray(), body => {
                    if (!body.TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("#EXTM3U") || M3u.IsHls(body)) throw new InvalidDataException("Nieprawidłowy format katalogu TV.");
                    return M3u.Parse(body, "https://iptv-org.github.io/iptv/" + path).Select(s => s with { Kind = "TV", Country = s.Country.Length > 0 ? s.Country : country, Source = "iptv-org" }).ToList();
                }, refresh, ct);
                return result with { Stations = result.Stations.Where(s => (countries.Length == 0 || countries.Contains(s.Country)) && ChannelSearch.Matches(query, s)).ToList() };
            }));
            if (mode != "TV" && (!globalTv || country.Length > 0)) jobs.Add(Job(async () => {
                string tail = "/json/stations/search?hidebroken=true&limit=200&order=clickcount&reverse=true" + (country.Length > 0 ? "&countrycode=" + country : "");
                var list = new List<Station>(); var notes = new List<string>(); var dates = new List<DateTimeOffset>();
                foreach (string filter in query.Length == 0 ? new[] { "" } : new[] { "&name=", "&tag=" })
                {
                    var result = await Feed("Radio " + country + " " + query + filter, settings.RadioMirrors.Select(m => m.TrimEnd('/') + tail + filter + Uri.EscapeDataString(query)).ToArray(), body => ParseRadio(body, country), refresh, ct);
                    list.AddRange(result.Stations); notes.Add(result.Detail); if (result.DataAt.HasValue) dates.Add(result.DataAt.Value);
                }
                return new(list.DistinctBy(s => s.Url).ToList(), string.Join(" / ", notes), dates.Count == 0 ? null : dates.Min());
            }));
        }
        async Task<SearchResult> Job(Func<Task<SearchResult>> job) { await gate.WaitAsync(ct); try { return await job(); } catch (Exception e) when (e is not OperationCanceledException) { return new([], "BŁĄD: " + e.Message); } finally { gate.Release(); } }
        var results = await Task.WhenAll(jobs); ct.ThrowIfCancellationRequested();
        if (results.All(r => r.Detail.StartsWith("BŁĄD:"))) throw new IOException(string.Join("\n", results.Select(r => r.Detail)));
        return new(results.SelectMany(r => r.Stations).DistinctBy(s => (s.Url, s.Country)).Take(20000).ToList(), string.Join("\n", results.Select(r => r.Detail)), results.Where(r => r.DataAt.HasValue).Select(r => r.DataAt).Min());
    }
    public Task<SearchResult> Broadcasters(bool refresh, CancellationToken ct) => Feed("Spis kanałów, aliasów i stron v3", settings.ChannelDirectoryUrls, body => {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Nieprawidłowy spis nadawców.");
        var list = new List<Station>();var replacements=new Dictionary<string,string>();
        foreach (var x in doc.RootElement.EnumerateArray())
        {
            string Get(string key) => x.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
            if (x.TryGetProperty("is_nsfw", out var nsfw) && nsfw.ValueKind == JsonValueKind.True) continue;
            if(Get("id").Length>0&&Get("replaced_by").Length>0)replacements[Get("id")]=Get("replaced_by");
            if (Regex.IsMatch(Get("country"), "^[A-Z]{2}$") && Get("id").Length > 0 && Get("name").Length > 0)
                list.Add(new(Get("name"), TextTools.IsWeb(Get("website")) ? Get("website") : "", "TV", Group: x.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array ? string.Join(',', categories.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString())) : "", Source: "Spis nadawców", Homepage: TextTools.IsWeb(Get("website")) ? Get("website") : "", Country: Get("country"), ChannelId: Get("id"), Aliases: x.TryGetProperty("alt_names", out var aliases) && aliases.ValueKind == JsonValueKind.Array ? aliases.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!).Take(30).ToArray() : []));
        }
        if (list.Count < 10) throw new JsonException("Niepełny spis nadawców.");
        return ChannelSearch.AddReplacementAliases(list,replacements);
    }, refresh, ct);
    public async Task<SearchResult> ExtraStreams(string query, string[] countries, IReadOnlyList<Station> directory, bool refresh, CancellationToken ct)
    {
        var result = await Feed("Indeks adresów transmisji v1", settings.StreamDirectoryUrls, ChannelSearch.ParseStreamIndex, refresh, ct);
        var matched = ChannelSearch.JoinStreamIndex(result.Stations, directory, countries, query);
        return result with { Stations = matched, Detail = result.Detail + $" • {matched.Count} pasujących adresów; wspólny dostawca iptv-org, nie niezależny dowód działania." };
    }
    public string WebSearchTemplate => settings.WebSearchTemplate;
    public string[] SharedPlaylistUrls => settings.SharedPlaylistUrls;
    public static List<Station> ParseRadio(string body, string country)
    {
        using var doc = JsonDocument.Parse(body); if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Nieprawidłowy katalog radia.");
        return doc.RootElement.EnumerateArray().Select(s => {
            string Get(string key) => s.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
            return new Station(Get("name").Trim(), TextTools.IsWeb(Get("url_resolved")) ? Get("url_resolved") : Get("url"), "Radio", Get("tags"), "Radio Browser", Get("homepage"), Country: Get("countrycode"), ChannelId: "radio:" + Get("stationuuid"));
        }).Where(s => TextTools.IsWeb(s.Url) && (country.Length == 0 || s.Country == country)).ToList();
    }
}

public record Health(DateTimeOffset CheckedAt, bool Decoded, int StartupMs, int Successes = 0, int Failures = 0, string Note = "");
public record Ranked(Station Station, double Score, string Reason, Health? Health);
public sealed class SourceRanker
{
    readonly string path;
    public Func<Station, (double Bonus, string Reason)>? Personalize { get; set; }
    public Dictionary<string, Health> Results { get; private set; } = [];
    public SourceRanker(string folder)
    {
        Directory.CreateDirectory(folder); path = Path.Combine(folder, "source-health-mpv.dpapi");
        string legacy=Path.Combine(folder,"source-health-mpv.json");
        try
        {
            if(File.Exists(path))Results=PrivateData.Read<Dictionary<string,Health>>(path,"source-health-v1");
            else if(File.Exists(legacy)){Results=JsonSerializer.Deserialize<Dictionary<string,Health>>(File.ReadAllText(legacy))??[];PrivateData.Write(path,Results,"source-health-v1");}
            if(File.Exists(legacy)&&File.Exists(path)){PrivateData.Write(legacy+".protected",File.ReadAllBytes(legacy),"source-health-legacy-v1");File.Delete(legacy);}
        }
        catch(Exception ex)when(ex is IOException or JsonException or System.Security.Cryptography.CryptographicException){Results=[];}
    }
    public void Record(Station station, Health result)
    {
        Results.TryGetValue(station.Url, out var old);
        Results[station.Url] = result with { Successes = Math.Min(20, (old?.Successes ?? 0) + (result.Decoded ? 1 : 0)), Failures = Math.Min(20, (old?.Failures ?? 0) + (result.Decoded ? 0 : 1)) };
        Results = Results.Where(p => DateTimeOffset.UtcNow - p.Value.CheckedAt < TimeSpan.FromDays(30)).OrderByDescending(p => p.Value.CheckedAt).Take(4000).ToDictionary();
        PrivateData.Write(path,Results,"source-health-v1");
    }
    public static string ChannelKey(Station s) => PrivateStation.IsPrivate(s)?"private:"+s.PrivateSourceId+":"+s.PrivateStreamId:s.Kind + ":" + (s.ChannelId.Length > 0 ? s.ChannelId : s.Country + ":" + ChannelSearch.Key(s.Name)) + (ChannelSearch.ProgramVariant(s.Feed) is {Length:>0} variant ? "@"+variant : "");
    public List<Ranked> Rank(IEnumerable<Station> stations, string quality)
    {
        return stations.DistinctBy(s => s.Url).Select(s => {
            if (s.BrowserOnly) return new Ranked(s, 0, s.AccessNote, null);
            Results.TryGetValue(s.Url, out var h); var reasons = new List<string>(); double score = 20;
            if (s.Url.StartsWith("https://")) { score += 3; reasons.Add("HTTPS"); }
            var match = Regex.Match(s.Name, @"(\d{3,4})[pi]", RegexOptions.IgnoreCase); int height = match.Success ? int.Parse(match.Groups[1].Value) : 0;
            if (height > 0) { score += quality == "Oszczędzaj transfer" ? (height <= 576 ? 18 : height <= 720 ? 10 : 0) : quality == "Najwyższa jakość" ? Math.Min(24, height / 60d) : Math.Min(6, height / 240d); reasons.Add(match.Value + " wg katalogu"); }
            if (h != null)
            {
                var age = DateTimeOffset.UtcNow - h.CheckedAt;
                if (age >= TimeSpan.Zero && age <= TimeSpan.FromHours(6)) { score += h.Decoded ? 55 : -45; reasons.Add(h.Decoded ? $"dekodowanie OK {h.CheckedAt.ToLocalTime():HH:mm}, start {h.StartupMs / 1000d:F1}s" : "ostatnia próba nieudana"); }
                else reasons.Add("stary test — sprawdź ponownie");
                if (age >= TimeSpan.Zero && age < TimeSpan.FromDays(7)) score += Math.Clamp(h.Successes - h.Failures, -10, 10);
            }
            else reasons.Add("jeszcze niesprawdzone");
            if (Personalize != null) { var personal = Personalize(s); score += personal.Bonus; if (personal.Reason.Length > 0) reasons.Add(personal.Reason); }
            return new Ranked(s, Math.Round(Math.Clamp(score, 0, 100)), string.Join(" • ", reasons), h);
        }).OrderBy(r => r.Station.BrowserOnly).ThenByDescending(r => DiscoveryAnalyzer.FreshSuccess(r.Health) ? 1 : r.Health != null && !r.Health.Decoded && DateTimeOffset.UtcNow - r.Health.CheckedAt is var age && age >= TimeSpan.Zero && age < TimeSpan.FromMinutes(10) ? -1 : 0).ThenByDescending(r => r.Score).ThenBy(r => r.Station.Name).ThenBy(r => r.Station.Url).ToList();
    }
    public List<Ranked> Top(IEnumerable<Station> stations, string quality, int count = 8) => Rank(stations, quality).DistinctBy(r => ChannelKey(r.Station)).Take(count).ToList();
    public Station? Alternative(IEnumerable<Station> stations, Station current, HashSet<string> attempted, string quality) => Rank(stations.Where(s => !s.BrowserOnly && ChannelKey(s) == ChannelKey(current) && ChannelSearch.SameProgram(s, current) && !attempted.Contains(s.Url)), quality).FirstOrDefault()?.Station;
}
public static class StreamProbe
{
    // Separate process: malformed streams or a native decoder hang cannot freeze the main window.
    public static async Task<Health> Run(Station station, string folder, CancellationToken ct,string clearKeyHex="")
    {
        if (station.BrowserOnly) return new(DateTimeOffset.UtcNow, false, 0, Note: "Strona WWW, brak adresu transmisji.");
        Directory.CreateDirectory(folder); string stem = Path.Combine(folder, Guid.NewGuid().ToString("N")), input = stem + ".in.dpapi", output = stem + ".out.json";
        ct.ThrowIfCancellationRequested();PrivateData.Write(input,new ProviderPlayback(station,clearKeyHex),"stream-probe-v1",backup:false);
        using var process = new Process { StartInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true } };
        foreach (var a in new[] { "--probe", input, "--report", output }) process.StartInfo.ArgumentList.Add(a);
        try
        {
            int limit=clearKeyHex.Length>0?32:12;
            process.Start(); using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(limit));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); ct.ThrowIfCancellationRequested(); return new(DateTimeOffset.UtcNow, false, limit*1000, Note: "Limit "+limit+" s"); }
            if(process.ExitCode!=0) return new(DateTimeOffset.UtcNow,false,0,Note:"Proces testujący zakończył się błędem: "+process.ExitCode);
            return File.Exists(output) ? JsonSerializer.Deserialize<Health>(await File.ReadAllTextAsync(output, ct)) ?? new(DateTimeOffset.UtcNow, false, 0, Note: "Brak wyniku") : new(DateTimeOffset.UtcNow, false, 0, Note: "Proces testujący zakończył się bez wyniku");
        }
        finally { foreach (string file in new[] { input, output }) try { File.Delete(file); } catch (IOException) { } }
    }
    public static int Worker(string input, string output)
    {
        var playback = PrivateData.Read<ProviderPlayback>(input,"stream-probe-v1");var station=playback.Station; var clock = Stopwatch.StartNew(); Health result;
        try
        {
            using var player = new SwirPlayer(IntPtr.Zero, true); player.Play(station.Url, station.UserAgent, station.Referrer,clearKeyHex:playback.ClearKeyHex);
            bool encrypted=playback.ClearKeyHex.Length>0;var progress=new PlaybackProgress();
            while (clock.ElapsedMilliseconds < (encrypted?28000:8000) && player.State != 7)
            {
                var stats = player.Stats; int state=player.State;
                var observation=progress.Observe(state,stats,station.Kind,encrypted,clock.ElapsedMilliseconds);
                if (observation.Confirmed&&observation.Advancing&&(station.Kind == "TV" ? stats.VideoReady && stats.VideoProgressSamples > 3 : stats.AudioReady && stats.AudioProgressSamples > 3 || stats.VideoReady && stats.VideoProgressSamples > 3))
                { result = new(DateTimeOffset.UtcNow, true, (int)clock.ElapsedMilliseconds); File.WriteAllText(output, JsonSerializer.Serialize(result)); return 0; }
                Thread.Sleep(100);
            }
            result = new(DateTimeOffset.UtcNow, false, (int)clock.ElapsedMilliseconds, Note: "Brak dekodowania w limicie");
        }
        catch (Exception ex) { result = new(DateTimeOffset.UtcNow, false, (int)clock.ElapsedMilliseconds, Note: playback.ClearKeyHex.Length>0||PrivateStation.IsPrivate(station)?"Nie udało się zweryfikować prywatnej transmisji.":ex.GetType().Name); }
        File.WriteAllText(output, JsonSerializer.Serialize(result)); return 0;
    }
}
