using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

public record FoundPlaylist(int Schema, string Id, string Name, string SourceUrl, DateTimeOffset FetchedAt,
    DateTimeOffset? ModifiedAt, string ContentSha256, string OriginalM3u, List<Station> Stations,
    string[] SelectedCountries, string Query, int MatchingCount, string OriginalSha256 = "", int RemovedEntries = 0, bool Sanitized = false)
{
    [JsonIgnore] public string ExportLabel => Sanitized ? "oczyszczona kopia" : "oryginał";
    [JsonIgnore] public int SourceCount => Stations.Count;
    [JsonIgnore] public int ChannelCount => Stations.Select(SourceRanker.ChannelKey).Distinct().Count();
    [JsonIgnore] public string[] EpgUrls => PlaylistRepository.EpgUrls(OriginalM3u);
    [JsonIgnore] public bool IsStale => DateTimeOffset.UtcNow - FetchedAt > TimeSpan.FromDays(7) || ModifiedAt < DateTimeOffset.UtcNow.AddDays(-7);
    [JsonIgnore] public string Freshness => $"Pobrano {FetchedAt.ToLocalTime():dd.MM.yyyy HH:mm}" + (ModifiedAt.HasValue ? $" • źródło {ModifiedAt.Value.ToLocalTime():dd.MM.yyyy HH:mm}" : " • źródło nie podaje daty aktualizacji") + (IsStale ? " • STARY ZAPIS — odśwież" : "");
}

// Stores only playlists deliberately discovered by the public-source adapter or imported
// by the UI. A saved/downloaded playlist is never evidence of successful playback.
public sealed class PlaylistRepository
{
    readonly string folder;
    readonly object gate = new();
    readonly Dictionary<string, FoundPlaylist> items = [];
    public string Notice { get; private set; } = "";
    public IReadOnlyList<FoundPlaylist> Items { get { lock (gate) return items.Values.OrderByDescending(p => p.FetchedAt).ThenBy(p => p.Name).ToList(); } }
    static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static string IdFor(string url) => Digest(url);
    public static string[] EpgUrls(string body)
    {
        string header = body.TrimStart('\uFEFF',' ','\r','\n').Split('\n',2)[0];
        return Regex.Matches(header, "(?:x-tvg-url|url-tvg|tvg-url)\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)
            .SelectMany(m => m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(u => PublicWeb.Allowed(u) && !PublicPlaylistPrivacy.ContainsCredentials(u)).Distinct().Take(8).ToArray();
    }
    public PlaylistRepository(string dataFolder)
    {
        folder = Path.Combine(dataFolder, "found-playlists"); Directory.CreateDirectory(folder);
        foreach (var file in new DirectoryInfo(folder).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Take(40))
        {
            try
            {
                if (file.Length > 64_000_000) throw new InvalidDataException("Zapis przekracza limit.");
                var value = JsonSerializer.Deserialize<FoundPlaylist>(File.ReadAllText(file.FullName)) ?? throw new InvalidDataException("Pusty zapis.");
                if (PublicPlaylistPrivacy.ContainsCredentials(value.SourceUrl) || PublicPlaylistPrivacy.ContainsCredentials(value.OriginalM3u))
                { File.Delete(file.FullName); throw new InvalidDataException("Pominięto zapis z danymi dostępu."); }
                Validate(value);
                if (file.Name != value.Id + ".json") throw new InvalidDataException("Niezgodny identyfikator zapisu.");
                items[value.Id] = value;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException)
            { Notice = "Nie wczytano co najmniej jednego uszkodzonego zapisu playlisty. Pozostałe playlisty zachowano."; }
        }
    }
    static void Validate(FoundPlaylist value)
    {
        PublicPlaylistPrivacy.RequirePublic(value.SourceUrl, value.OriginalM3u);
        if (value.Schema != 1 || value.SourceUrl == null || !PublicWeb.Allowed(value.SourceUrl) || value.Id != IdFor(value.SourceUrl) || value.Name == null ||
            value.OriginalM3u == null || Encoding.UTF8.GetByteCount(value.OriginalM3u) > 12_000_000 || value.ContentSha256 != Digest(value.OriginalM3u) ||
            !value.OriginalM3u.TrimStart('\uFEFF',' ','\r','\n').StartsWith("#EXTM3U") || M3u.IsHls(value.OriginalM3u) ||
            value.Stations is not { Count: > 0 and <= 30000 } || value.Stations.Any(s => s == null || s.Name == null || s.Kind == null || s.Country == null || s.ChannelId == null || s.Feed == null || s.Group == null || s.Source == null || s.Homepage == null || s.UserAgent == null || s.Referrer == null || s.Quality == null || s.AccessNote == null || s.BrowserOnly || !SharedPlaylists.AllowedStream(s.Url)) ||
            value.RemovedEntries < 0 || value.RemovedEntries > 30000 || value.OriginalSha256 == null || (value.OriginalSha256.Length > 0 && !Regex.IsMatch(value.OriginalSha256, @"^[a-f0-9]{64}$")) || (value.Sanitized && value.OriginalSha256.Length != 64) ||
            value.SelectedCountries == null || value.Query == null || value.MatchingCount < 0 || value.MatchingCount > value.Stations.Count ||
            value.FetchedAt > DateTimeOffset.UtcNow.AddMinutes(10) || value.ModifiedAt > DateTimeOffset.UtcNow.AddMinutes(10))
            throw new InvalidDataException("Nieprawidłowy lub uszkodzony zapis playlisty.");
    }
    public static FoundPlaylist Create(WebDocument document, IReadOnlyList<Station> directory, string[] countries, string query, DateTimeOffset? fetchedAt = null)
    {
        if (Encoding.UTF8.GetByteCount(document.Body) > 12_000_000) throw new InvalidDataException("Playlista przekracza 12 MB.");
        var clean = PublicPlaylistPrivacy.SanitizeM3u(document.Url, document.Body);
        document = document with { Body = clean.Body };
        if (!PublicWeb.Allowed(document.Url)) throw new InvalidDataException("Niepubliczny adres playlisty.");
        if (Encoding.UTF8.GetByteCount(document.Body) > 12_000_000) throw new InvalidDataException("Playlista przekracza 12 MB.");
        var now = fetchedAt ?? DateTimeOffset.UtcNow;
        if (document.Modified < now.AddDays(-7) || document.Modified > now.AddMinutes(10)) throw new InvalidDataException("Nieaktualna lub przyszła data playlisty.");
        var stations = SharedPlaylists.Parse(document.Body, document.Url, directory, [], "");
        if (stations.Count == 0) throw new InvalidDataException("Playlista nie zawiera adresów transmisji do odtwarzania.");
        var selected = SelectStations(stations, countries, query);
        var uri = new Uri(document.Url);
        string name = uri.Host == "raw.githubusercontent.com" ? string.Join('/', uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Take(2)) + " / " + Path.GetFileName(uri.AbsolutePath)
            : uri.Host + " / " + Path.GetFileName(uri.AbsolutePath);
        var result = new FoundPlaylist(1, IdFor(document.Url), name, document.Url, now, document.Modified, Digest(document.Body), document.Body, stations,
            countries.Distinct().ToArray(), query, selected.Count, clean.OriginalSha256, clean.RemovedEntries, clean.Sanitized);
        Validate(result); return result;
    }
    public void Save(FoundPlaylist playlist)
    {
        Validate(playlist);
        lock (gate)
        {
            if (items.TryGetValue(playlist.Id, out var old) && old.FetchedAt > playlist.FetchedAt) return;
            string path = Path.Combine(folder, playlist.Id + ".json"), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, JsonSerializer.Serialize(playlist), new UTF8Encoding(false)); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            items[playlist.Id] = playlist;
            foreach (var expired in items.Values.OrderByDescending(p => p.FetchedAt).Skip(40).ToList())
            { items.Remove(expired.Id); try { File.Delete(Path.Combine(folder, expired.Id + ".json")); } catch (IOException) { } }
        }
    }
    public FoundPlaylist Get(string id) { lock (gate) return items.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("Nie znaleziono zapisanej playlisty."); }
    public static List<Station> SelectStations(IEnumerable<Station> stations, string[] countries, string query, ISet<string>? verifiedUrls = null)
    {
        var eligible = stations.Where(s => !s.BrowserOnly && SharedPlaylists.AllowedStream(s.Url) &&
            (countries.Length == 0 || countries.Contains(s.Country)) && (verifiedUrls == null || verifiedUrls.Contains(s.Url))).ToList();
        return ChannelSearch.Filter(query, eligible, eligible).DistinctBy(s => s.Url).ToList();
    }
    public List<Station> GetStations(string id, string[] countries, string query = "", ISet<string>? verifiedUrls = null) => SelectStations(Get(id).Stations, countries, query, verifiedUrls);
    public string Original(string id) => Get(id).OriginalM3u;
    public string Filtered(string id, string[] countries, string query = "", ISet<string>? verifiedUrls = null)
    {
        var value = Get(id); var stations = GetStations(id, countries, query, verifiedUrls);
        if (stations.Count == 0) throw new InvalidDataException("Brak kanałów pasujących do wybranego zakresu.");
        var output = new StringBuilder("#EXTM3U");
        if (value.EpgUrls.Length > 0) output.Append(" x-tvg-url=\"").Append(string.Join(',',value.EpgUrls).Replace("\"","%22")).Append('"');
        output.Append("\n#SWIRSTREAM-SOURCE: ").Append(value.SourceUrl.Replace('\r',' ').Replace('\n',' ')).Append("\n#SWIRSTREAM-FETCHED: ").Append(value.FetchedAt.ToString("O")).Append('\n');
        var selected = stations.ToDictionary(s => s.Url); var written = new HashSet<string>(); var metadata = new List<string>();
        foreach (string raw in value.OriginalM3u.Split('\n'))
        {
            string line = raw.Trim().TrimStart('\uFEFF');
            if (line.StartsWith("#EXTINF:",StringComparison.OrdinalIgnoreCase)) { metadata.Clear(); metadata.Add(line); continue; }
            if (line.StartsWith('#')) { if (metadata.Count > 0) metadata.Add(line); continue; }
            if (line.Length == 0) continue;
            string url = TextTools.IsWeb(line) ? line : Uri.TryCreate(new Uri(value.SourceUrl),line,out var address) ? address.AbsoluteUri : "";
            if (selected.ContainsKey(url) && written.Add(url))
            {
                if (metadata.Count == 0) output.Append(M3u.Export([selected[url]])["#EXTM3U\n".Length..]);
                else
                {
                    foreach (string entry in metadata) output.Append(entry.StartsWith("#EXTINF:",StringComparison.OrdinalIgnoreCase) ? EnrichMetadata(entry,selected[url]) : entry).Append('\n');
                    output.Append(url).Append('\n');
                }
            }
            metadata.Clear();
        }
        foreach (var station in stations.Where(s => !written.Contains(s.Url))) output.Append(M3u.Export([station])["#EXTM3U\n".Length..]);
        return output.ToString();
    }
    static string EnrichMetadata(string line, Station station)
    {
        var comma = Regex.Match(line, "(?:\"[^\"]*\"|[^,])* ,", RegexOptions.IgnorePatternWhitespace);
        if (!comma.Success) return line;
        string attributes = line[..(comma.Length-1)], title = line[(comma.Length-1)..];
        string Clean(string value) => value.Replace('\r',' ').Replace('\n',' ').Replace('"','\'');
        if (station.Country.Length > 0 && !Regex.IsMatch(attributes,@"\btvg-country\s*=",RegexOptions.IgnoreCase)) attributes += " tvg-country=\"" + Clean(station.Country) + "\"";
        if (station.ChannelId.Length > 0 && !Regex.IsMatch(attributes,@"\btvg-id\s*=",RegexOptions.IgnoreCase)) attributes += " tvg-id=\"" + Clean(station.ChannelId+(station.Feed.Length>0?"@"+station.Feed:"")) + "\"";
        return attributes+title;
    }
    public void ExportOriginal(string id, string path) => File.WriteAllText(path, Original(id), new UTF8Encoding(false));
    public void ExportFiltered(string id, string path, string[] countries, string query = "", ISet<string>? verifiedUrls = null) => File.WriteAllText(path, Filtered(id, countries, query, verifiedUrls), new UTF8Encoding(false));
    public async Task<FoundPlaylist> Refresh(string id, IReadOnlyList<Station> directory, string[] countries, string query, CancellationToken ct)
    {
        var previous = Get(id); var doc = await PublicWeb.Fetch(previous.SourceUrl, ct);
        var updated = Create(doc, directory, countries, query); ct.ThrowIfCancellationRequested(); Save(updated); return updated;
    }
}
