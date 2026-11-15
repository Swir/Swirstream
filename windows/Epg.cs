using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SwirStreamAI;

public record EpgProgramme(DateTimeOffset Start, DateTimeOffset Stop, string Title, string Description);
public record EpgChannel(string Id, string[] Names, List<EpgProgramme> Programmes);
public record EpgFeed(int Schema, string Country, string Source, DateTimeOffset Downloaded, DateTimeOffset? Modified, List<EpgChannel> Channels);
public record EpgMatch(EpgChannel Channel, EpgFeed Feed, string Method);
public record EpgSlot(EpgProgramme? Now, EpgProgramme? Next, EpgMatch? Match);
public sealed class EpgSettings
{
    public string[] Templates { get; set; } = ["https://iptv-epg.org/files/epg-{country}.xml.gz", "https://free-epg.de/api/epg/{country}.xml.gz"];
    public Dictionary<string, string> Sources { get; set; } = [];
    public Dictionary<string, string> Matches { get; set; } = [];
}

public sealed class EpgService
{
    const string SourcesPurpose = "epg-sources-v1", CachePurpose = "epg-custom-cache-v1";
    record ProtectedSources(int Schema, Dictionary<string, string> Sources);
    record PublicSettings(string[] Templates, Dictionary<string, string> Matches);
    readonly string folder, settingsPath, privateSettingsPath;
    bool privateSettingsLocked, recoveredPrivateSettings;
    readonly ConcurrentDictionary<string, EpgFeed> feeds = new();
    readonly ConcurrentDictionary<string, string> notices = new();
    readonly Func<string, CancellationToken, Task<(byte[], DateTimeOffset?)>> fetch;
    public EpgSettings Settings { get; private set; } = new();
    public string Notice => string.Join("\n", notices.OrderBy(x => x.Key).Select(x => x.Key + ": " + x.Value));
    public IEnumerable<EpgFeed> Feeds => feeds.Values;
    public EpgService(string data, Func<string, CancellationToken, Task<(byte[], DateTimeOffset?)>>? fetch = null)
    {
        folder = Path.Combine(data, "epg-cache"); Directory.CreateDirectory(folder); settingsPath = Path.Combine(data, "epg-settings.json");
        privateSettingsPath = Path.Combine(data, "epg-private-sources.dpapi");
        this.fetch = fetch ?? PublicWeb.FetchEpg;
        try
        {
            if (File.Exists(settingsPath)) Settings = JsonSerializer.Deserialize<EpgSettings>(File.ReadAllText(settingsPath)) ?? throw new JsonException();
            if (Settings.Templates is not { Length: <= 4 } || Settings.Templates.Any(x => x == null || !PublicWeb.Allowed(x.Replace("{country}", "pl"))) || Settings.Sources == null || Settings.Matches == null) throw new JsonException();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { Settings = new(); notices["Ustawienia"] = "Nie można odczytać ustawień EPG; użyto domyślnych źródeł."; }
        var legacySources = Settings.Sources.ToArray();
        if (File.Exists(privateSettingsPath) || File.Exists(privateSettingsPath + ".bak"))
        {
            try { MergeProtectedSources(privateSettingsPath); }
            catch (Exception ex) when (StorageError(ex))
            {
                try { MergeProtectedSources(privateSettingsPath + ".bak"); recoveredPrivateSettings = true; notices["Ochrona"] = "Odczytano chronioną kopię ustawień EPG."; }
                catch (Exception backup) when (StorageError(backup))
                { privateSettingsLocked = true; notices["Ochrona"] = "Własne źródła EPG są niedostępne dla tego konta Windows albo zapis jest uszkodzony. Nie nadpisano plików."; }
            }
        }
        if (legacySources.Length > 0 && !privateSettingsLocked)
        {
            try { SaveSettings(); }
            catch (Exception ex) when (StorageError(ex))
            { notices["Ochrona"] = "Nie udało się zabezpieczyć wcześniejszych ustawień EPG. Oryginalny zapis pozostał bez zmian; ponów zapis ustawień."; }
        }
        // Only a successful protected write may remove a previous plaintext cache.
        foreach (var source in legacySources.Concat(Settings.Sources).Distinct())
        {
            try { MigrateCache(source.Key, source.Value); }
            catch (Exception ex) when (StorageError(ex))
            { notices["Ochrona cache"] = "Nie udało się zabezpieczyć wcześniejszego cache EPG. Oryginalny zapis zachowano; ponów odświeżenie."; }
        }
    }
    public void SaveSettings()
    {
        if (privateSettingsLocked) throw new InvalidOperationException("Chronione ustawienia EPG są niedostępne. Nie nadpisano plików; odzyskaj je na pierwotnym koncie Windows.");
        if (Settings.Sources.Count > 0 || File.Exists(privateSettingsPath) || File.Exists(privateSettingsPath + ".bak"))
        {
            var sources = new ProtectedSources(1, Settings.Sources.ToDictionary());
            ValidateSources(sources);
            PrivateData.Write(privateSettingsPath, sources, SourcesPurpose, backup: !recoveredPrivateSettings);
            // A public-settings migration commits only after an actual decrypt/readback.
            var saved = PrivateData.Read<ProtectedSources>(privateSettingsPath, SourcesPurpose, 4_000_000);
            ValidateSources(saved);
            if (!saved.Sources.OrderBy(x => x.Key).SequenceEqual(sources.Sources.OrderBy(x => x.Key))) throw new InvalidDataException("Nie potwierdzono chronionego zapisu EPG.");
            recoveredPrivateSettings = false;
        }
        string temp = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(new PublicSettings(Settings.Templates, Settings.Matches), new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, settingsPath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static bool StorageError(Exception ex) => ex is IOException or InvalidDataException or JsonException or CryptographicException or UnauthorizedAccessException;
    static void ValidateSources(ProtectedSources sources)
    {
        if (sources.Schema != 1 || sources.Sources is not { Count: <= 256 } || sources.Sources.Any(x => !Regex.IsMatch(x.Key, "^[A-Z]{2}$") || x.Value is not { Length: > 0 and <= 8192 })) throw new InvalidDataException("Nieprawidłowy chroniony zapis EPG.");
    }
    void MergeProtectedSources(string path)
    {
        var sources = PrivateData.Read<ProtectedSources>(path, SourcesPurpose, 4_000_000); ValidateSources(sources);
        // The protected commit is newer if an interruption left old public settings behind.
        Settings.Sources = sources.Sources.ToDictionary();
    }
    static string CacheKey(string country, string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(country + url)));
    static string PrivateLabel(string country, string url) => "Własne źródło EPG • " + CacheKey(country, url)[..12];
    static bool Structure(EpgFeed? feed) => feed is { Schema: 1, Channels.Count: <= 30000 } && feed.Channels.All(c => c != null && c.Id != null && c.Names != null && c.Names.All(n => n != null) && c.Programmes != null && c.Programmes.All(p => p != null && p.Title != null && p.Description != null && p.Stop > p.Start));
    void MigrateCache(string country, string url)
    {
        string old = Path.Combine(folder, CacheKey(country, url) + ".json");
        if (!File.Exists(old)) return;
        if (new FileInfo(old).Length > 40_000_000) throw new InvalidDataException("Wcześniejszy cache EPG jest zbyt duży.");
        var feed = JsonSerializer.Deserialize<EpgFeed>(File.ReadAllText(old));
        if (!Structure(feed) || feed!.Country != country || feed.Source != url) throw new InvalidDataException("Nieprawidłowy wcześniejszy cache EPG.");
        string protectedPath = Path.Combine(folder, CacheKey(country, url) + ".dpapi");
        PrivateData.Write(protectedPath, feed with { Source = PrivateLabel(country, url) }, CachePurpose, backup: false);
        var saved = PrivateData.Read<EpgFeed>(protectedPath, CachePurpose, 45_000_000);
        if (!Structure(saved) || saved.Country != country || saved.Source != PrivateLabel(country, url) || saved.Downloaded != feed.Downloaded) throw new InvalidDataException("Nie potwierdzono chronionego cache EPG.");
        File.Delete(old);
    }
    public static string MappingKey(Station station) => SourceRanker.ChannelKey(station) + "@" + station.Feed;
    public static bool TryTime(string text, out DateTimeOffset result)
    {
        var m = Regex.Match(text, @"^(\d{14})\s*([+-]\d{4}|Z)?$"); result = default;
        if (!m.Success) return false;
        string offset = m.Groups[2].Value;
        if (offset.Length == 0 || offset == "Z") offset = "+0000"; // XMLTV specifies UTC when the zone is omitted.
        return DateTimeOffset.TryParseExact(m.Groups[1].Value + " " + offset.Insert(3, ":"), "yyyyMMddHHmmss zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }
    public static EpgFeed Parse(byte[] bytes, string country, string source, DateTimeOffset? modified, DateTimeOffset now, CancellationToken ct = default)
    {
        if (modified > now.AddMinutes(10) || modified < now.AddDays(-2)) throw new InvalidDataException("Plik EPG ma starą lub przyszłą datę aktualizacji.");
        using var raw = new MemoryStream(bytes); using var gzip = bytes.Length > 2 && bytes[0] == 31 && bytes[1] == 139 ? new GZipStream(raw, CompressionMode.Decompress, true) : null;
        using var reader = XmlReader.Create((Stream?)gzip ?? raw, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 120_000_000, IgnoreComments = true });
        reader.MoveToContent(); if (reader.Name != "tv") throw new InvalidDataException("To nie jest ramówka XMLTV.");
        var names = new Dictionary<string, string[]>(); var programmes = new Dictionary<string, List<EpgProgramme>>(); int total = 0;
        if (!reader.IsEmptyElement) reader.Read();
        while (!reader.EOF)
        {
            ct.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.Name is not ("channel" or "programme")) { reader.Read(); continue; }
            var x = (XElement)XNode.ReadFrom(reader);
            if (x.Name.LocalName == "channel")
            {
                string id = (string?)x.Attribute("id") ?? "";
                if (id.Length > 0 && id.Length < 300 && names.Count < 30000) names[id] = x.Elements("display-name").Select(n => n.Value).Where(n => n.Length is > 0 and < 300).Distinct().Take(20).ToArray();
                continue;
            }
            if (++total > 600000) throw new InvalidDataException("Ramówka jest zbyt duża.");
            string channel = (string?)x.Attribute("channel") ?? "", title = x.Element("title")?.Value.Trim() ?? "";
            if (channel.Length == 0 || title.Length == 0 || !TryTime((string?)x.Attribute("start") ?? "", out var start) || !TryTime((string?)x.Attribute("stop") ?? "", out var stop) || stop <= start || stop - start > TimeSpan.FromDays(1)) continue;
            if (stop <= now.AddHours(-6) || start > now.AddDays(3)) continue;
            if (!programmes.TryGetValue(channel, out var list)) programmes[channel] = list = [];
            if (list.Count < 2000) list.Add(new(start, stop, title[..Math.Min(500, title.Length)], (x.Element("desc")?.Value ?? "") is var desc ? desc[..Math.Min(3000, desc.Length)] : ""));
        }
        var channels = names.Select(n => new EpgChannel(n.Key, n.Value, programmes.GetValueOrDefault(n.Key, []).DistinctBy(p => (p.Start, p.Stop, p.Title)).OrderBy(p => p.Start).ToList())).Where(c => c.Programmes.Count > 0).ToList();
        if (!channels.Any(c => c.Programmes.Any(p => p.Stop > now && p.Start < now.AddHours(24)))) throw new InvalidDataException("Brak audycji na dziś/jutro. Nie użyto starej ramówki.");
        return new(1, country, source, now, modified, channels);
    }
    static bool Valid(EpgFeed? f, DateTimeOffset now, int hours) => f is { Schema: 1, Channels: not null } && f.Downloaded <= now.AddMinutes(5) && now - f.Downloaded < TimeSpan.FromHours(hours) && (f.Modified == null || f.Modified >= now.AddDays(-2) && f.Modified <= now.AddMinutes(10)) && f.Channels.Any(c => c.Programmes.Any(p => p.Stop > now && p.Start < now.AddHours(24)));
    public async Task Refresh(IEnumerable<string> countries, bool force, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(2);
        var templates = Settings.Templates.ToArray(); var custom = Settings.Sources.ToDictionary();
        await Task.WhenAll(countries.Where(c => Regex.IsMatch(c, "^[A-Z]{2}$")).Distinct().Take(12).Select(async country =>
        {
            await gate.WaitAsync(ct);
            try
            {
                bool isPrivate = custom.TryGetValue(country, out var source);
                var urls = isPrivate ? new[] { source! } : templates.Select(t => t.Replace("{country}", country.ToLowerInvariant())).ToArray();
                var errors = new List<string>(); EpgFeed? fallback = null;
                foreach (string url in urls)
                {
                    ct.ThrowIfCancellationRequested(); var now = DateTimeOffset.UtcNow;
                    string label = isPrivate ? PrivateLabel(country, url) : url;
                    string path = Path.Combine(folder, CacheKey(country, url) + (isPrivate ? ".dpapi" : ".json"));
                    EpgFeed? cached = null;
                    try
                    {
                        if (isPrivate) MigrateCache(country, url);
                        if (File.Exists(path)) cached = isPrivate ? PrivateData.Read<EpgFeed>(path, CachePurpose, 45_000_000) : new FileInfo(path).Length < 40_000_000 ? JsonSerializer.Deserialize<EpgFeed>(await File.ReadAllTextAsync(path, ct)) : null;
                    }
                    catch (Exception ex) when (StorageError(ex)) { if (isPrivate && File.Exists(Path.Combine(folder, CacheKey(country, url) + ".json"))) notices["Ochrona cache"] = "Nie udało się zabezpieczyć wcześniejszego cache EPG; oryginalny zapis zachowano."; }
                    if (cached?.Country != country || cached?.Source != label || !Structure(cached)) cached = null;
                    if (!force && Valid(cached, now, 6)) { feeds[country] = cached!; notices[country] = $"zapis {cached!.Downloaded.ToLocalTime():dd.MM HH:mm} • {cached.Channels.Count} ramówek"; return; }
                    if (Valid(cached, now, 48)) fallback ??= cached;
                    try
                    {
                        byte[] bytes; DateTimeOffset? modified;
                        if (PublicWeb.Allowed(url)) (bytes, modified) = await fetch(url, ct);
                        else if (Path.IsPathFullyQualified(url) && File.Exists(url))
                        { if (new FileInfo(url).Length > 120_000_000) throw new IOException("Plik XMLTV przekracza 120 MB."); bytes = await File.ReadAllBytesAsync(url, ct); modified = File.GetLastWriteTimeUtc(url); }
                        else throw new IOException("Nieprawidłowy adres lub plik EPG.");
                        var feed = await Task.Run(() => Parse(bytes, country, label, modified, DateTimeOffset.UtcNow, ct), ct);
                        if (isPrivate) { ct.ThrowIfCancellationRequested(); PrivateData.Write(path, feed, CachePurpose, backup: false); }
                        else
                        {
                            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            try { await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(feed), ct); ct.ThrowIfCancellationRequested(); File.Move(tmp, path, true); }
                            finally { if (File.Exists(tmp)) File.Delete(tmp); }
                        }
                        feeds[country] = feed;
                        notices[country] = $"{feed.Channels.Count} ramówek • pobrano {feed.Downloaded.ToLocalTime():dd.MM HH:mm} • " + (modified.HasValue ? $"źródło {modified.Value.ToLocalTime():dd.MM HH:mm}" : "brak daty źródła; sprawdzono daty audycji") + " • " + label + (errors.Count > 0 ? " • użyto źródła zapasowego: " + string.Join("; ", errors) : ""); return;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    { errors.Add(label + ": " + (isPrivate ? ex is InvalidDataException or XmlException ? "Nieprawidłowa lub nieaktualna ramówka XMLTV." : "Nie udało się pobrać lub zabezpieczyć ramówki (połączenie, uprawnienia lub zapis)." : ex.Message)); }
                }
                if (fallback != null) feeds[country] = fallback; else if (feeds.TryGetValue(country, out var old) && !Valid(old, DateTimeOffset.UtcNow, 48)) feeds.TryRemove(country, out _);
                notices[country] = (fallback != null ? "TRYB OFFLINE • zachowano aktualne audycje z zapisu. " : "Brak aktualnego EPG. ") + string.Join("; ", errors);
            }
            finally { gate.Release(); }
        }));
    }
    static string NameKey(string name, string country)
    {
        name = Regex.Replace(name, "^" + Regex.Escape(country) + @"\s*[-|:]\s*", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(TextTools.Normalize(name), @"\b(hd|sd|fhd|uhd|4k)\b|\(\d{3,4}p\)", "");
        return Regex.Replace(name, @"[^\p{L}\p{N}+]+", "");
    }
    public EpgMatch? Match(Station s)
    {
        if (s.Kind == "Radio") return null;
        string country = s.Country.Length == 2 ? s.Country : Regex.Match(s.ChannelId, @"\.([a-z]{2})$").Groups[1].Value.ToUpperInvariant();
        if (!feeds.TryGetValue(country, out var feed) || !Valid(feed, DateTimeOffset.UtcNow, 48)) return null;
        if (Settings.Matches.TryGetValue(MappingKey(s), out var manual))
        {
            var chosen = feed.Channels.FirstOrDefault(c => c.Id == manual); return chosen == null ? null : new(chosen, feed, "ręczne dopasowanie");
        }
        // Regional/language/time-shift feeds must not inherit the parent schedule silently.
        if (ChannelSearch.ProgramVariant(s.Feed).Length > 0 || Regex.IsMatch(s.Name, @"\+\s*\d")) return null;
        var ids = feed.Channels.Where(c => c.Id == s.ChannelId).ToList();
        if (ids.Count == 1) return new(ids[0], feed, "identyfikator kanału");
        var keys = ChannelSearch.Names(s).Select(n => NameKey(n, country)).Where(n => n.Length > 0).ToHashSet();
        var named = feed.Channels.Where(c => c.Names.Any(n => keys.Contains(NameKey(n, country)))).Take(2).ToList();
        return named.Count == 1 ? new(named[0], feed, "jednoznaczna nazwa w kraju " + country) : null;
    }
    public EpgSlot Get(Station s, DateTimeOffset? at = null)
    {
        var match = Match(s); var time = at ?? DateTimeOffset.UtcNow;
        if (match == null) return new(null, null, null);
        var current = match.Channel.Programmes.Where(p => p.Start <= time && time < p.Stop).ToList();
        // Conflicting/overlapping listings do not justify inventing a current programme.
        var now = current.Count == 1 ? current[0] : null;
        var next = match.Channel.Programmes.FirstOrDefault(p => p.Start >= (now?.Stop ?? time) && p.Start > time);
        return new(now, next, match);
    }
    public static string Line(EpgProgramme? p, string prefix) => p == null ? prefix + "brak danych EPG" : $"{prefix}{p.Start.ToLocalTime():HH:mm}–{p.Stop.ToLocalTime():HH:mm}  {p.Title}";
}
