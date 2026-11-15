using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwirStreamAI;

public sealed class SearchPrioritySnapshot
{
    readonly IReadOnlyDictionary<string,double> scores;
    public bool Enabled { get; }
    public int ExampleCount { get; }
    internal SearchPrioritySnapshot(bool enabled,int examples,Dictionary<string,double> scores)
    { Enabled=enabled;ExampleCount=examples;this.scores=new System.Collections.ObjectModel.ReadOnlyDictionary<string,double>(scores); }
    public double Score(Station station) => Enabled && scores.TryGetValue(SearchLearning.CandidateKey(station),out var value) ? value : 0;
}

public record SearchObservation(DateTimeOffset CheckedAt,bool Decoded);
public sealed class SearchLearningState
{
    public int Schema { get; set; }=1;
    public int Examples { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public double[] Weights { get; set; }=new double[512];
    public Dictionary<string,SearchObservation> Observed { get; set; }=[];
}

// A per-profile online logistic model predicts useful discovery destinations. Its
// labels are actual player outcomes, never HTTP status or catalogue membership.
// TasteNet supplies content preferences; this model changes crawl order only.
public sealed class SearchLearning
{
    readonly string path;
    readonly LibraryData preferences;
    readonly IntentNet categoryModel;
    readonly object gate=new();
    SearchLearningState state=new();
    public bool Enabled => preferences.LearnPreferences;
    public int ExampleCount { get { lock(gate)return state.Examples; } }
    public string Notice { get; private set; }="";
    public SearchLearning(string profileFolder,LibraryData data,IntentNet categoryModel)
    {
        Directory.CreateDirectory(profileFolder);path=Path.Combine(profileFolder,"search-learning.json");preferences=data;this.categoryModel=categoryModel;
        try
        {
            if(!File.Exists(path))return;
            if(new FileInfo(path).Length>2_000_000)throw new InvalidDataException("Za duży zapis uczenia.");
            var saved=JsonSerializer.Deserialize<SearchLearningState>(File.ReadAllText(path))??throw new InvalidDataException("Pusty zapis uczenia.");
            if(saved.Schema!=1 || saved.Examples is <0 or >1_000_000 || saved.Weights is not {Length:512} || saved.Weights.Any(w=>!double.IsFinite(w)||Math.Abs(w)>6) || saved.Observed==null || saved.Observed.Count>4000 || saved.UpdatedAt>DateTimeOffset.UtcNow.AddMinutes(1) || saved.Observed.Any(p=>p.Key.Length!=64||!p.Key.All(Uri.IsHexDigit)||p.Value==null||p.Value.CheckedAt>DateTimeOffset.UtcNow.AddMinutes(1)))throw new InvalidDataException("Nieprawidłowy zapis uczenia.");
            saved.Observed=saved.Observed.Where(p=>DateTimeOffset.UtcNow-p.Value.CheckedAt<TimeSpan.FromDays(30)).ToDictionary();state=saved;
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { Notice="Nie wczytano uszkodzonego zapisu uczenia wyszukiwania. Biblioteka i inne profile pozostają bez zmian."; }
    }
    static uint Hash(string text){uint value=2166136261;foreach(char c in text)value=unchecked((value^c)*16777619);return value;}
    static string Domain(Station station)
    {
        string url=PublicWeb.Allowed(station.Homepage)?station.Homepage:station.Url;
        string host=TextTools.IsWeb(url)?new Uri(url).Host.ToLowerInvariant():"";
        return host.StartsWith("www.",StringComparison.Ordinal)?host[4..]:host;
    }
    internal static string CandidateKey(Station station)=>SourceRanker.ChannelKey(station)+"|"+Domain(station);
    static double[] Features(Station station,TasteEngine classifier)
    {
        var features=new double[512];void Add(string key,double value)=>features[Hash(key)%512]+=value;
        Add("site:"+Domain(station),1);Add("channel:"+SourceRanker.ChannelKey(station),.8);
        Add("country:"+station.Country,.3);Add("topic:"+classifier.Categorize(station).Code,.35);Add("kind:"+station.Kind,.15);
        double norm=Math.Sqrt(features.Sum(v=>v*v));for(int i=0;i<features.Length;i++)features[i]/=norm;return features;
    }
    static double Probability(double[] features,double[] weights,double decay=1)
    {double dot=0;for(int i=0;i<features.Length;i++)dot+=features[i]*weights[i]*decay;return 1/(1+Math.Exp(-Math.Clamp(dot,-20,20)));}
    static double Decay(DateTimeOffset updated,DateTimeOffset now)=>updated==default?0:Math.Exp(-Math.Max(0,(now-updated).TotalDays)/30);
    LibraryData PreferencesCopy()=>new(){LearnPreferences=preferences.LearnPreferences,PreferredTopics=(preferences.PreferredTopics??[]).ToArray(),TasteWeights=preferences.TasteWeights is {Length:512}?preferences.TasteWeights.ToArray():new double[512],TasteExamples=preferences.TasteExamples};
    public SearchPrioritySnapshot Snapshot(IEnumerable<Station> candidates)
    {
        var data=PreferencesCopy();if(!data.LearnPreferences)return new(false,0,[]);
        double[] weights;int examples;DateTimeOffset updated;
        lock(gate){weights=state.Weights.ToArray();examples=state.Examples;updated=state.UpdatedAt;}
        var taste=new TasteEngine(data,categoryModel);var scores=new Dictionary<string,double>();double decay=Decay(updated,DateTimeOffset.UtcNow);
        foreach(var station in candidates)
        {
            string key=CandidateKey(station);if(scores.ContainsKey(key))continue;
            double preference=Math.Clamp(taste.Score(station).Bonus,-12,20);
            double availability=examples==0?0:(Probability(Features(station,taste),weights,decay)-.5)*24*Math.Min(1,examples/6d);
            scores[key]=Math.Clamp(preference+availability,-24,32);
        }
        return new(true,examples,scores);
    }
    public bool Record(Station station,Health verifiedResult)
    {
        var now=DateTimeOffset.UtcNow;
        if(!Enabled || station.BrowserOnly || station.Kind=="Test" || !SharedPlaylists.AllowedStream(station.Url) || verifiedResult.StartupMs<=0 || verifiedResult.CheckedAt>now.AddMinutes(1) || verifiedResult.CheckedAt<now.AddMinutes(-10))return false;
        string id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SourceRanker.ChannelKey(station)+"|"+station.Url))).ToLowerInvariant();
        lock(gate)
        {
            if(!Enabled)return false;
            if(state.Observed.TryGetValue(id,out var last) && (verifiedResult.CheckedAt<=last.CheckedAt || last.Decoded==verifiedResult.Decoded&&now-last.CheckedAt<TimeSpan.FromMinutes(30)))return false;
            var classifier=new TasteEngine(new LibraryData(),categoryModel);var features=Features(station,classifier);
            double decay=state.Examples==0?1:Decay(state.UpdatedAt,now);
            double error=(verifiedResult.Decoded?1d:0d)-Probability(features,state.Weights,decay);
            for(int i=0;i<state.Weights.Length;i++)state.Weights[i]=Math.Clamp(state.Weights[i]*decay*.9995+.55*error*features[i],-6,6);
            state.Examples=Math.Min(1_000_000,state.Examples+1);state.UpdatedAt=now;state.Observed[id]=new(verifiedResult.CheckedAt,verifiedResult.Decoded);
            state.Observed=state.Observed.Where(p=>now-p.Value.CheckedAt<TimeSpan.FromDays(30)).OrderByDescending(p=>p.Value.CheckedAt).Take(4000).ToDictionary();Save();return true;
        }
    }
    void Save()
    {
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temp,JsonSerializer.Serialize(state),new UTF8Encoding(false));File.Move(temp,path,true);Notice="";}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Notice="Zmiany uczenia działają w tej sesji, ale nie udało się ich zapisać w profilu.";}
        finally {try{if(File.Exists(temp))File.Delete(temp);}catch(IOException){}}
    }
    public void Reset(){lock(gate){state=new();Save();}}
}
