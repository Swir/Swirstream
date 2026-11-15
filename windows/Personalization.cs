using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

public record Topic(string Code, string Name) { public override string ToString() => Name; }
public record TopicPrediction(string Code, string Name, double Confidence, string Method);
public sealed class TasteEngine
{
    public static readonly Topic[] Topics = [new("news", "Wiadomości"), new("music", "Muzyka"), new("sports", "Sport"), new("movies", "Filmy i seriale"), new("kids", "Dla dzieci"), new("documentary", "Dokument i nauka"), new("entertainment", "Rozrywka"), new("lifestyle", "Styl życia"), new("religious", "Religia"), new("general", "Ogólne")];
    readonly IntentNet classifier;
    readonly LibraryData data;
    readonly Dictionary<string, TopicPrediction> cache = [];
    public TasteEngine(LibraryData data, IntentNet classifier)
    {
        this.data = data; this.classifier = classifier;
        data.PreferredTopics ??= []; data.HiddenChannels ??= []; data.TasteSignals ??= [];
        data.SelectedCountries ??= ["PL"];
        if (data.TopicFilter != "all" && data.TopicFilter != "unknown" && !Topics.Any(t => t.Code == data.TopicFilter)) data.TopicFilter = "all";
        if (data.TasteWeights is not { Length: 512 } || data.TasteWeights.Any(w => !double.IsFinite(w))) { data.TasteWeights = new double[512]; data.TasteExamples = 0; }
    }
    public TopicPrediction Categorize(Station station)
    {
        string key = station.Name + " " + station.Group;
        if (cache.TryGetValue(key, out var result)) return result;
        // Reliable explicit category tags take precedence over a model prediction.
        string group = TextTools.Normalize(station.Group);
        var exact = Topics.FirstOrDefault(t => Regex.IsMatch(group, @"(?:^|[,;|/ ])" + Regex.Escape(t.Code) + @"(?:$|[,;|/ ])"));
        if (exact != null) result = new(exact.Code, exact.Name, 1, "kategoria źródła");
        else
        {
            var prediction = classifier.Predict(key);
            var topic = Topics.FirstOrDefault(t => t.Code == prediction.Intent);
            result = prediction.Accepted && topic != null ? new(topic.Code, topic.Name, prediction.Confidence, "TasteNet") : new("unknown", "Bez kategorii", prediction.Confidence, "model nie ma pewności");
        }
        if (cache.Count >= 30000) cache.Clear(); cache[key] = result; return result;
    }
    static uint Hash(string s) { uint h = 2166136261; foreach (char c in s) h = unchecked((h ^ c) * 16777619); return h; }
    double[] Features(Station station)
    {
        var x = new double[512];
        void Add(string key, double value) => x[Hash(key) % x.Length] += value;
        Add("topic:" + Categorize(station).Code, 1); Add("country:" + station.Country, .5); Add("kind:" + station.Kind, .25); Add("channel:" + SourceRanker.ChannelKey(station), .7);
        foreach (string word in IntentNet.Words(station.Name).Where(w => w.Length > 2 && !Regex.IsMatch(w, @"^\d+[pi]?$"))) Add("name:" + word, .2);
        double norm = Math.Sqrt(x.Sum(v => v * v)); for (int i = 0; i < x.Length; i++) x[i] /= norm; return x;
    }
    double Probability(double[] x) => 1 / (1 + Math.Exp(-Math.Clamp(x.Select((v,i) => v * data.TasteWeights[i]).Sum(), -20, 20)));
    public double Preference(Station station) => Probability(Features(station));
    public (double Bonus, string Reason) Score(Station station)
    {
        var topic = Categorize(station); double bonus = 0; var reasons = new List<string> { topic.Name + " • " + topic.Method };
        if (data.PreferredTopics.Contains(topic.Code)) { bonus += 14; reasons.Add("Twoja wybrana tematyka"); }
        if (data.LearnPreferences && data.TasteExamples > 0) { double affinity = Preference(station); bonus += (affinity - .5) * 24; reasons.Add(affinity >= .55 ? "pasuje do Twoich wyborów" : affinity <= .45 ? "rzadziej wybierana tematyka" : "AI zbiera doświadczenia"); }
        return (bonus, string.Join(" • ", reasons));
    }
    public bool Learn(Station station, bool liked, string signal, DateTimeOffset? when = null)
    {
        if (!data.LearnPreferences || station.Kind == "Test") return false;
        var now = when ?? DateTimeOffset.UtcNow; string key = signal + ":" + SourceRanker.ChannelKey(station);
        if (data.TasteSignals.TryGetValue(key, out var last) && now - last < TimeSpan.FromHours(24)) return false;
        var x = Features(station); double error = (liked ? 1 : 0) - Probability(x);
        for (int i = 0; i < x.Length; i++) data.TasteWeights[i] = Math.Clamp(data.TasteWeights[i] * .9995 + .8 * error * x[i], -8, 8);
        data.TasteExamples++; data.TasteSignals[key] = now;
        data.TasteSignals = data.TasteSignals.Where(p => now - p.Value < TimeSpan.FromDays(30)).OrderByDescending(p => p.Value).Take(4000).ToDictionary();
        return true;
    }
    // Read 0.7.0 hides as well as current variant-specific keys. Do not rewrite
    // stored keys: an older base-channel hide intentionally covered every feed.
    static string LegacyHiddenKey(Station s) => s.Kind + ":" + (s.ChannelId.Length > 0 ? s.ChannelId : s.Country + ":" + Regex.Replace(TextTools.Normalize(s.Name), @"\([^)]*\)|\b(4k|uhd|fhd|hd|sd|\d{3,4}[pi])\b", "").Trim());
    bool IsHidden(Station station) => data.HiddenChannels.Contains(SourceRanker.ChannelKey(station)) || data.HiddenChannels.Contains(LegacyHiddenKey(station));
    public IEnumerable<Station> Filter(IEnumerable<Station> stations) => stations.Where(s => !IsHidden(s) && (data.TopicFilter == "all" || Categorize(s).Code == data.TopicFilter));
    public List<Station> CandidatesToVerify(IEnumerable<Station> stations)
    {
        var visible = Filter(stations.Where(s => !s.BrowserOnly)).ToList();
        if (data.TopicFilter == "all" && data.PreferredTopics.Length > 0)
        {
            var preferred = visible.Where(s => data.PreferredTopics.Contains(Categorize(s).Code)).ToList();
            if (preferred.Count > 0) return preferred;
        }
        return visible;
    }
    public string Export(IEnumerable<Station> stations)
    {
        var sorted = stations.DistinctBy(SourceRanker.ChannelKey).Select(s => s with { Group = (s.Country.Length > 0 ? s.Country : "??") + " / " + Categorize(s).Name })
            .OrderBy(s => s.Group).ThenBy(s => s.Name);
        return M3u.Export(sorted);
    }
}

public record ViewingProfile(string Id, string Name) { public override string ToString() => Name; }
public sealed class ProfileIndex
{
    public string ActiveId { get; set; } = "default";
    public List<ViewingProfile> Profiles { get; set; } = [new("default", "Mój profil")];
}
public sealed class ProfileManager
{
    readonly string root, file;
    public ProfileIndex Index { get; private set; } = new();
    public string Notice { get; private set; } = "";
    public ProfileManager(string root)
    {
        this.root = Path.GetFullPath(root); Directory.CreateDirectory(root); file = Path.Combine(root, "profiles.json");
        if (File.Exists(file)) try
        {
            Index = JsonSerializer.Deserialize<ProfileIndex>(File.ReadAllText(file)) ?? throw new JsonException();
            if (Index.Profiles is not { Count: > 0 and <= 20 } || Index.Profiles.Any(p => p == null || !Regex.IsMatch(p.Id, "^(default|[a-f0-9]{32})$") || string.IsNullOrWhiteSpace(p.Name)) || Index.Profiles.Select(p => p.Id).Distinct().Count() != Index.Profiles.Count || !Index.Profiles.Any(p => p.Id == Index.ActiveId)) throw new JsonException();
        }
        catch (JsonException) { File.Copy(file, file + ".damaged-" + DateTime.UtcNow.Ticks); Index = new(); Notice = "Odtworzono spis profili; pliki bibliotek zachowano."; }
    }
    public string Folder(string id)
    {
        if (!Index.Profiles.Any(p => p.Id == id)) throw new InvalidDataException("Nieznany profil.");
        return id == "default" ? root : Path.Combine(root, "profiles", id);
    }
    public ViewingProfile Add(string name)
    {
        name = name.Trim(); if (name.Length is < 1 or > 40 || Index.Profiles.Count >= 20) throw new ArgumentException("Nazwa: 1–40 znaków; maks. 20 profili.");
        if (Index.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Taka nazwa profilu już istnieje.");
        var p = new ViewingProfile(Guid.NewGuid().ToString("N"), name); Index.Profiles.Add(p); Save(); return p;
    }
    public void Activate(string id) { _ = Folder(id); Index.ActiveId = id; Save(); }
    public void Save() { File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(Index, new JsonSerializerOptions { WriteIndented = true })); File.Move(file + ".tmp", file, true); }
}
