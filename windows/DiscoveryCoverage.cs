using System.Text.Json;

namespace SwirStreamAI;

public record WebSearchPlan(int Seconds, int Seeds, int Roots, int Pages, int Queries, int SearchSeconds, int Depth = 2)
{
    public static WebSearchPlan Normal => new(60, 8, 12, 24, 4, 12);
    public static WebSearchPlan Deep => new(180, 36, 48, 120, 8, 30, 3);
}

public sealed class DiscoveryCoverage
{
    readonly string path;
    Dictionary<string, DateTimeOffset> visits = [];
    public DiscoveryCoverage(string folder)
    {
        Directory.CreateDirectory(folder); path = Path.Combine(folder, "visited-broadcasters.json");
        try { if (File.Exists(path)) visits = JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(path)) ?? []; }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        visits = visits.Where(v => PublicWeb.Allowed(v.Key) && v.Value <= DateTimeOffset.UtcNow.AddMinutes(5)).ToDictionary();
    }
    public List<Station> Order(IEnumerable<Station> stations,SearchPrioritySnapshot? priority=null)=>Prioritize(stations,priority,visits);
    public static List<Station> Prioritize(IEnumerable<Station> stations,SearchPrioritySnapshot? priority=null,IReadOnlyDictionary<string,DateTimeOffset>? visits=null)
    {
        DateTimeOffset Last(Station s)=>visits!=null&&visits.TryGetValue(s.Url,out var time)?time:DateTimeOffset.MinValue;
        // Rotate countries as well as pages, so a small scan cannot always consume
        // its budget in the first countries of a global directory.
        var groups=stations.DistinctBy(s=>s.Url).GroupBy(s=>s.Country).OrderBy(g=>g.Max(Last)).ToList();
        List<Station> Interleave(bool personalized)
        {
            var queues=groups.Select(g=>new Queue<Station>(personalized?g.OrderByDescending(s=>priority!.Score(s)).ThenBy(Last).ThenBy(s=>s.Name):g.OrderBy(Last).ThenBy(s=>s.Name))).ToList();
            List<Station> ordered=[];while(queues.Any(q=>q.Count>0))foreach(var queue in queues)if(queue.TryDequeue(out var station))ordered.Add(station);return ordered;
        }
        var explore=Interleave(false);if(priority?.Enabled!=true)return explore;
        var preferred=new Queue<Station>(Interleave(true));var unexplored=new Queue<Station>(explore);var used=new HashSet<string>();List<Station> result=[];
        while(result.Count<explore.Count)
        {
            var queue=result.Count%4==3?unexplored:preferred;
            while(queue.TryPeek(out var duplicate)&&used.Contains(duplicate.Url))queue.Dequeue();
            if(!queue.TryDequeue(out var candidate))queue=queue==preferred?unexplored:preferred;
            if(candidate==null){while(queue.TryPeek(out var duplicate)&&used.Contains(duplicate.Url))queue.Dequeue();if(!queue.TryDequeue(out candidate))break;}
            if(used.Add(candidate.Url))result.Add(candidate);
        }
        return result;
    }
    public void Attempt(string url) { visits[url] = DateTimeOffset.UtcNow; }
    public void Save()
    {
        try
        {
            if (File.Exists(path)) foreach (var entry in JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(path)) ?? [])
                if (PublicWeb.Allowed(entry.Key) && entry.Value <= DateTimeOffset.UtcNow.AddMinutes(5) && entry.Value > visits.GetValueOrDefault(entry.Key)) visits[entry.Key] = entry.Value;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        visits = visits.OrderByDescending(v => v.Value).Take(40000).ToDictionary();
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(visits)); File.Move(path + ".tmp", path, true);
    }
    public string Describe(IEnumerable<Station> stations)
    {
        var urls = stations.Select(s => s.Url).Where(PublicWeb.Allowed).Distinct().ToList();
        return $"Historia prób: {urls.Count(u => visits.ContainsKey(u))}/{urls.Count} stron z wybranego spisu nadawców. To nie jest licznik całego internetu ani potwierdzenie odbioru.";
    }
}
