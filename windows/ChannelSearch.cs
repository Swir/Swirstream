using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

public static class ChannelSearch
{
    static readonly Regex QualityLabels = new(@"[\[(]\s*(?:uhd|fhd|hd|sd|4k|\d{3,4}[pi]|geo-blocked|not 24/7|offline)\s*[\])]", RegexOptions.Compiled);
    static readonly Regex QualityWords = new(@"\b(?:uhd|fhd|hd|sd|4k|\d{3,4}[pi])\b", RegexOptions.Compiled);
    static readonly Regex OtherCharacters = new(@"[^\p{L}\p{Nd}+]", RegexOptions.Compiled);
    static readonly Regex CountrySuffix = new(@"\.[a-zA-Z]{2}$", RegexOptions.Compiled);
    static readonly Regex CamelBoundary = new(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);
    static readonly Regex SearchRequest = new(@"\b(?:znajdz|wyszukaj|szukaj|poszukaj|przeszukaj|pokaz|polec|kanaly|kanalow|stacje|zrodla|zrodel|transmisje|streamy)\b", RegexOptions.Compiled);
    static readonly Dictionary<string,string> Genres=new[]{
        new[]{"movies","movie","film","films","filmy","filmowe","kino"},
        new[]{"news","wiadomosci","informacje","informacyjne"},
        new[]{"music","muzyka","muzyczne","muzyczna"},
        new[]{"kids","children","dzieci","dzieciece","bajki"},
        new[]{"documentary","documentaries","dokument","dokumenty","dokumentalne"},
        new[]{"sports","sport","sportowe"}
    }.SelectMany(group=>group.Select(word=>(word,genre:group[0]))).ToDictionary(p=>p.word,p=>p.genre);
    static bool GenreMatches(string query,string group)
    {
        // Only a complete category query expands; channel names and numeric IDs
        // keep the established literal/exact behavior. Match group tokens, not substrings.
        if(!Genres.TryGetValue(TextTools.Normalize(query).Trim(),out string? genre))return false;
        return Regex.Matches(TextTools.Normalize(group),@"[\p{L}\p{Nd}]+").Any(m=>Genres.TryGetValue(m.Value,out string? value)&&value==genre);
    }
    public static string Key(string name)
    {
        name = QualityLabels.Replace(TextTools.Normalize(name), " ");
        return OtherCharacters.Replace(QualityWords.Replace(name, " "), "");
    }
    public static string IdentifierName(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return "";
        string[] parts = identifier.Split('@', 2);
        string name = CountrySuffix.Replace(parts[0], "");
        if (parts.Length > 1) name += " " + parts[1];
        return CamelBoundary.Replace(name.Replace('_', ' '), " ").Trim();
    }
    public static IEnumerable<string> Names(Station station)
    {
        IEnumerable<string> names = new[] { station.Name }.Concat(station.Aliases ?? []);
        if (!string.IsNullOrWhiteSpace(station.Feed)) names = names.Append(station.Name + " " + IdentifierName(station.Feed));
        if (station.Kind != "Radio" && !string.IsNullOrWhiteSpace(station.ChannelId)) names = names.Append(IdentifierName(station.ChannelId));
        return names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase);
    }
    public static bool Matches(string query, Station station)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        string key = Key(query);
        if (key.Length > 0 && Names(station).Any(n => Key(n) == key)) return true;
        // Letter-only prefixes make live filtering useful while typing (TVP, Pols).
        // Numeric queries stay exact/token-based so TVP 1 never becomes TVP10.
        if (key.Length >= 2 && key.All(char.IsLetter) && Names(station).Any(n => Key(n).StartsWith(key, StringComparison.Ordinal))) return true;
        if (GenreMatches(query,station.Group)) return true;
        string haystack = TextTools.Normalize(string.Join(' ', Names(station)) + " " + station.Group);
        var tokens = Regex.Matches(TextTools.Normalize(query), @"[\p{L}\p{Nd}+]+").Select(m => m.Value).ToArray();
        return tokens.Length > 0 && tokens.All(t => Regex.IsMatch(haystack, @"(?<![\p{L}\p{Nd}+])" + Regex.Escape(t) + @"(?![\p{L}\p{Nd}+])"));
    }
    public static List<Station> Exact(string query, IEnumerable<Station> directory)
    {
        string key = Key(query);
        return key.Length == 0 ? [] : directory.Where(s => Names(s).Any(n => Key(n) == key)).DistinctBy(s => (s.Kind, s.Country, s.ChannelId, ProgramVariant(s.Feed), Key(s.Name))).ToList();
    }
    // A newer online directory must not hide a saved station with a legacy ID.
    public static List<Station> FilterLocal(string query, IEnumerable<Station> candidates) => candidates.Where(s => Matches(query, s)).ToList();
    public static string LocalQuery(string raw, IEnumerable<Station> candidates)
    {
        raw = raw.Trim();
        // A missing literal station name such as "Polskie Radio" must not turn
        // into an empty query and show every station. Interpret explicit requests.
        if (!SearchRequest.IsMatch(TextTools.Normalize(raw)) || candidates.Any(s => Matches(raw, s))) return raw;
        return Countries.Extract(raw, []).Query;
    }
    public static List<Station> Filter(string query, IEnumerable<Station> candidates, IReadOnlyList<Station> directory)
    {
        // A whole genre query is intentionally broad. A channel named "Filmy"
        // in another country must not erase scoped movies-category candidates.
        if(Genres.ContainsKey(TextTools.Normalize(query).Trim()))return candidates.Where(s=>Matches(query,s)).ToList();
        var exact = Exact(query, directory);
        if (exact.Count == 0) return candidates.Where(s => Matches(query, s)).ToList();
        return candidates.Where(s => exact.Any(meta =>
            (s.ChannelId.Length > 0 ? s.ChannelId == meta.ChannelId : s.Country == meta.Country && Key(s.Name) == Key(meta.Name)) &&
            (meta.Country.Length == 0 || s.Country.Length == 0 || s.Country == meta.Country) &&
            (ProgramVariant(meta.Feed).Length == 0 || SameProgram(meta, s)) && Matches(query, s))).ToList();
    }
    /// <summary>Use publisher-declared successors only. Retain original channel IDs.</summary>
    public static List<Station> AddReplacementAliases(IReadOnlyList<Station> directory, IReadOnlyDictionary<string, string> replacements)
    {
        var ids = directory.Where(s => s.ChannelId.Length > 0).GroupBy(s => s.ChannelId).Where(g => g.Select(s => s.Country).Distinct().Count() == 1).ToDictionary(g => g.Key, g => g.First());
        var extra = new Dictionary<string, List<string>>();
        foreach (var old in directory.Where(s => replacements.ContainsKey(s.ChannelId)))
        {
            string current = old.ChannelId; var seen = new HashSet<string> { current }; bool valid = true;
            for (int hops = 0; replacements.TryGetValue(current, out string? next); hops++)
            {
                if (hops >= 16 || string.IsNullOrWhiteSpace(next) || !seen.Add(next) || !ids.TryGetValue(next, out var target) || target.Country != old.Country) { valid = false; break; }
                current = next;
            }
            if (!valid || current == old.ChannelId) continue;
            if (!extra.TryGetValue(current, out var names)) extra[current] = names = [];
            names.AddRange(Names(old));
        }
        return directory.Select(s => extra.TryGetValue(s.ChannelId, out var names) ? s with { Aliases = (s.Aliases ?? []).Concat(names).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Take(128).ToArray() } : s).ToList();
    }
    public static List<Station> ParseStreamIndex(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Indeks transmisji nie jest tablicą JSON.");
        var list = new List<Station>(); int schema = 0;
        foreach (var x in doc.RootElement.EnumerateArray())
        {
            if (x.ValueKind != JsonValueKind.Object) continue;
            string Get(string key) => x.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
            if (!x.TryGetProperty("url", out _) || !x.TryGetProperty("channel", out _)) continue;
            schema++;
            string url = Get("url");
            if (!SharedPlaylists.AllowedStream(url)) continue;
            string label = x.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array ? string.Join(", ", labels.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString())) : Get("label");
            string name = Get("title"); if (name.Length == 0) name = Get("channel").Split('.')[0];
            list.Add(new(name, url, "TV", Group: label, Source: "Indeks transmisji iptv-org", UserAgent: Get("user_agent"), Referrer: Get("referrer"), ChannelId: Get("channel"), Quality: Get("quality"), Feed: Get("feed")));
        }
        if (schema == 0 || list.Count == 0) throw new JsonException("Niepełny lub niezgodny schemat indeksu transmisji.");
        return list.DistinctBy(s => (s.Url, s.ChannelId, ProgramVariant(s.Feed))).Take(30000).ToList();
    }
    public static List<Station> JoinStreamIndex(IEnumerable<Station> streams, IReadOnlyList<Station> directory, string[] countries, string query)
    {
        var ids = directory.Where(s => s.ChannelId.Length > 0).GroupBy(s => s.ChannelId).ToDictionary(g => g.Key, g => g.First());
        var names = directory.SelectMany(s => Names(s).Select(n => (Key:Key(n), Station:s))).Where(p => p.Key.Length > 0).GroupBy(p => p.Key)
            .Where(g => g.Select(p => p.Station.ChannelId).Distinct().Count() == 1).ToDictionary(g => g.Key, g => g.First().Station);
        var result = new List<Station>();
        foreach (var stream in streams)
        {
            var meta = stream.ChannelId.Length > 0 ? ids.GetValueOrDefault(stream.ChannelId) : names.GetValueOrDefault(Key(stream.Name));
            if (meta == null || countries.Length > 0 && !countries.Contains(meta.Country)) continue;
            result.Add(stream with { Name = (stream.Name.Length > 0 ? stream.Name : meta.Name) + (stream.Quality.Length > 0 ? " (" + stream.Quality + ")" : "") + (stream.Group.Length > 0 ? " [" + stream.Group + "]" : ""), Country = meta.Country, ChannelId = meta.ChannelId, Homepage = meta.Homepage, Group = meta.Group, Aliases = Names(meta).Concat([stream.Name]).Distinct().ToArray() });
        }
        return Filter(query, result, directory).DistinctBy(s => (s.Url, s.ChannelId, ProgramVariant(s.Feed))).ToList();
    }
    public static string ProgramVariant(string feed) => Regex.Replace(TextTools.Normalize(feed), @"(?:uhd|fhd|hd|sd|4k|\d{3,4}[pi])$", "").Trim();
    public static bool SameProgram(Station a, Station b)
    {
        string left = ProgramVariant(a.Feed), right = ProgramVariant(b.Feed);
        if (left.Length > 0 && right.Length > 0) return left == right;
        return Key(a.Name) == Key(b.Name);
    }
}
