using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SwirStreamAI;

public record WebDocument(string Url, string Body, string ContentType, DateTimeOffset? Modified = null);
public record WebHit(string Title, string Url);
public record WebEvidence(string Stage, string Url, string Detail, DateTimeOffset CheckedAt);
public record WebResult(List<Station> Stations, List<WebEvidence> Evidence);
internal record PublicWebResponse(int Status, string Location, WebDocument? Document);

// Reads public pages only. It never executes page scripts, downloads programs or uses browser credentials.
public static class PublicWeb
{
    static readonly HttpClient Http = CreateClient();
    static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, AutomaticDecompression = DecompressionMethods.All };
        handler.ConnectCallback = async (context, ct) => {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            foreach (var address in addresses.Where(IsPublic))
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); ct.ThrowIfCancellationRequested(); }
            }
            throw new HttpRequestException("Brak publicznego adresu serwera lub połączenia.");
        };
        return new(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return false;
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) return !ip.IsIPv6LinkLocal && !ip.IsIPv6Multicast && !ip.IsIPv6SiteLocal && (b[0] & 0xe0) == 0x20;
        return b[0] is not (0 or 10 or 127) && b[0] < 224 && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) && !(b[0] == 192 && b[1] == 168) && !(b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }
    public static bool Allowed(string url) => TextTools.IsWeb(url) && new Uri(url) is var u && !u.IsLoopback && u.Port is 80 or 443 && (!IPAddress.TryParse(u.Host, out var ip) || IsPublic(ip));
    public static async Task<(byte[], DateTimeOffset?)> FetchEpg(string url, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(45));
        for (int redirects = 0; redirects < 5; redirects++)
        {
            if (!Allowed(url)) throw new IOException("Niepubliczny adres EPG.");
            using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.UserAgent.ParseAdd("SwirStreamAI/0.11.0");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location != null) { url = new Uri(new Uri(url), response.Headers.Location).AbsoluteUri; continue; }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 120_000_000) throw new IOException("EPG przekracza 120 MB.");
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token); using var output = new MemoryStream(); var buffer = new byte[65536]; int n;
            while ((n = await input.ReadAsync(buffer, deadline.Token)) > 0) { if (output.Length + n > 120_000_000) throw new IOException("EPG przekracza 120 MB."); output.Write(buffer, 0, n); }
            return (output.ToArray(), response.Content.Headers.LastModified);
        }
        throw new IOException("Za dużo przekierowań EPG.");
    }
    public static Task<WebDocument> Fetch(string url, CancellationToken ct)=>FetchChecked(url,ct,null);
    // Policy is checked before every request, including redirects. Explorer uses
    // this to prohibit account URLs and portal API actions without changing the
    // separately configured private-provider transport.
    internal static async Task<WebDocument> FetchChecked(string url,CancellationToken ct,Func<string,bool>? policy,Func<string,CancellationToken,Task<PublicWebResponse>>? send=null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(18));
        for (int redirects = 0; redirects < 5; redirects++)
        {
            if (!Allowed(url)||(policy!=null&&!policy(url))) throw new IOException("Pominięto niedozwolony publiczny adres WWW.");
            deadline.Token.ThrowIfCancellationRequested();
            var response=await (send??FetchResponse)(url,deadline.Token);
            if(response.Status is >=300 and <400&&response.Location.Length>0){url=new Uri(new Uri(url),response.Location).AbsoluteUri;continue;}
            if(response.Status is <200 or >=300||response.Document==null)throw new IOException("Źródło nie zwróciło publicznego dokumentu.");
            return response.Document;
        }
        throw new IOException("Za dużo przekierowań.");
    }
    static async Task<PublicWebResponse> FetchResponse(string url,CancellationToken ct)
    {
            using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.UserAgent.ParseAdd("SwirStreamAI/0.11.0 (+public-stream-discovery)");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location != null)return new((int)response.StatusCode,response.Headers.Location.ToString(),null);
            response.EnsureSuccessStatusCode();
            string type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!DocumentContentType(url,type)) throw new IOException("To nie jest dokument WWW ani lista transmisji.");
            if (response.Content.Headers.ContentLength > 12_000_000) throw new IOException("Dokument jest za duży.");
            await using var input = await response.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream(); var buffer = new byte[16384]; int n;
            while ((n = await input.ReadAsync(buffer, ct)) > 0) { if (output.Length + n > 12_000_000) throw new IOException("Dokument jest za duży."); output.Write(buffer, 0, n); }
            return new((int)response.StatusCode,"",new(url,DecodeDocument(url,type,output.ToArray()),type,response.Content.Headers.LastModified));
    }
    static bool DocumentContentType(string url,string type)=>type.StartsWith("text/",StringComparison.OrdinalIgnoreCase)||type.Contains("json",StringComparison.OrdinalIgnoreCase)||type.Contains("xml",StringComparison.OrdinalIgnoreCase)||type.Contains("mpegurl",StringComparison.OrdinalIgnoreCase)||type.Length==0||type.Equals("application/octet-stream",StringComparison.OrdinalIgnoreCase)&&new[]{".mpd",".m3u",".m3u8"}.Contains(Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant());
    internal static string DecodeDocument(string url,string type,byte[] bytes)
    {
        if(bytes.Length>12_000_000||!DocumentContentType(url,type))throw new IOException("Niedozwolony format lub rozmiar dokumentu.");
        string text=Encoding.UTF8.GetString(bytes),prefix=text.TrimStart('\uFEFF',' ','\t','\r','\n');
        if(type.Equals("application/octet-stream",StringComparison.OrdinalIgnoreCase)&&!(prefix.StartsWith("#EXTM3U",StringComparison.OrdinalIgnoreCase)||prefix.StartsWith("<?xml",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(prefix,@"^<MPD(?:\s|>)",RegexOptions.IgnoreCase)))throw new IOException("Adres manifestu nie zwrócił rozpoznanej treści.");
        return text;
    }
}

public sealed class WebDiscovery
{
    readonly Func<string, CancellationToken, Task<WebDocument>> fetch;
    public WebDiscovery(Func<string, CancellationToken, Task<WebDocument>>? fetch = null) => this.fetch = fetch ?? PublicWeb.Fetch;
    public static List<WebHit> ParseSearch(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
        var doc = XDocument.Load(reader);
        if (doc.Root?.Name.LocalName != "rss") throw new InvalidDataException("Wyszukiwarka nie zwróciła wyników RSS (możliwa blokada). Katalogi nadal działają.");
        return doc.Descendants("item").Select(x => new WebHit(x.Element("title")?.Value ?? "", x.Element("link")?.Value ?? "")).Where(x => PublicWeb.Allowed(x.Url)).DistinctBy(x => x.Url).Take(10).ToList();
    }
    public static List<string> MediaUrls(WebDocument doc)
    {
        string text = WebUtility.HtmlDecode(doc.Body).Replace("\\/", "/").Replace("\\u0026", "&").Replace("\\u003d", "=").Replace("\\u003D", "=");
        var urls = Regex.Matches(text, "(?:https?://|//)[^\\s<>\"'\\\\]+?\\.(?:m3u8|mpd|mp3|aac)(?:\\?[^\\s<>\"'\\\\]*)?", RegexOptions.IgnoreCase).Select(m => m.Value).ToList();
        foreach (Match m in Regex.Matches(text, "(?:src|file|url)\\s*[=:]\\s*[\"']([^\"']+\\.(?:m3u8|mpd|mp3|aac)(?:\\?[^\"']*)?)[\"']", RegexOptions.IgnoreCase)) urls.Add(m.Groups[1].Value);
        return urls.Select(x => Uri.TryCreate(new Uri(doc.Url), x, out var u) ? u.AbsoluteUri : "").Where(PublicWeb.Allowed).Distinct().Take(12).ToList();
    }
    public static List<string> LiveLinks(WebDocument doc) =>
        BroadcasterLinks.Find(doc, false, 3, url => OnDomain(url, doc.Url));

    public static List<string> EmbeddedPlayers(WebDocument doc) =>
        BroadcasterLinks.Find(doc, true, 2, url => !Regex.IsMatch(new Uri(url).Host, "doubleclick|googleads|adservice|adsystem", RegexOptions.IgnoreCase));

    public async Task<bool> IsLive(string url, CancellationToken ct, int depth = 0)
    {
        if (depth > 2 || Regex.IsMatch(url, "hlsvod|/vod/|\\.mp4(?:[./?]|$)", RegexOptions.IgnoreCase)) return false;
        var doc = await fetch(url, ct);
        if (doc.Body.TrimStart('\uFEFF', ' ', '\n', '\r').StartsWith("#EXTM3U"))
        {
            if (doc.Body.Contains("#EXT-X-ENDLIST") || doc.Body.Contains("#EXT-X-PLAYLIST-TYPE:VOD")) return false;
            if (doc.Body.Contains("#EXT-X-STREAM-INF:"))
            {
                var lines = doc.Body.Split('\n').Select(l => l.Trim()).ToArray();
                int variants = 0;
                for (int i = 0; i < lines.Length - 1 && variants < 3; i++) if (lines[i].StartsWith("#EXT-X-STREAM-INF:"))
                {
                    string? next = lines.Skip(i + 1).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
                    if (next == null || !Uri.TryCreate(new Uri(doc.Url), next, out var child) || !PublicWeb.Allowed(child.AbsoluteUri)) continue;
                    variants++;
                    try { if (await IsLive(child.AbsoluteUri, ct, depth + 1)) return true; }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or XmlException or OperationCanceledException) { ct.ThrowIfCancellationRequested(); }
                }
                return false;
            }
            return doc.Body.Contains("#EXT-X-MEDIA-SEQUENCE:") && doc.Body.Contains("#EXTINF:");
        }
        if (new Uri(url).AbsolutePath.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = XmlReader.Create(new StringReader(doc.Body), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var xml = XDocument.Load(reader); return xml.Root?.Name.LocalName == "MPD" && xml.Root.Attribute("type")?.Value == "dynamic";
        }
        return false;
    }
    static string Host(string url) => new Uri(url).Host.Replace("www.", "");
    static bool OnDomain(string url, string website)
    {
        if (!PublicWeb.Allowed(url) || !PublicWeb.Allowed(website)) return false;
        string a = Host(url), b = Host(website);
        return a == b || a.EndsWith("." + b, StringComparison.OrdinalIgnoreCase);
    }
    public static Station? Identify(WebHit hit, IReadOnlyList<Station> directory)
    {
        var matches = directory.Where(s => OnDomain(hit.Url, s.Url)).ToList();
        var named = matches.OrderByDescending(s => s.Name.Length).FirstOrDefault(s => ChannelSearch.Names(s).Any(n => ChannelSearch.Matches(n, new Station(hit.Title, hit.Url))));
        if (named != null) return named;
        // Country may be shared by a broadcaster domain; identity must not be guessed for a multi-channel site.
        return matches.Select(SourceRanker.ChannelKey).Distinct().Count() == 1 ? matches[0] : null;
    }
    public async Task<WebResult> Search(string query, string mode, string[] countries, IReadOnlyList<Station> directory, IReadOnlyList<Station> catalog, string template, Action<string>? progress, CancellationToken ct, WebSearchPlan? plan = null, DiscoveryCoverage? coverage = null, SearchPrioritySnapshot? priority = null)
    {
        plan ??= WebSearchPlan.Normal;
        query = TextTools.Normalize(query).Trim();
        using var webDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct); webDeadline.CancelAfter(TimeSpan.FromSeconds(plan.Seconds)); var webToken = webDeadline.Token;
        var evidence = new List<WebEvidence>(); var found = new List<Station>(); var roots = new List<(WebHit Hit, Station Station)>();
        void Log(string stage, string url, string detail) => evidence.Add(new(stage, url, detail, DateTimeOffset.UtcNow));
        var byHost=directory.Where(s=>PublicWeb.Allowed(s.Url)).GroupBy(s=>Host(s.Url)).ToDictionary(g=>g.Key,g=>g.ToList());
        var sharedSites=directory.GroupBy(s=>s.Url).Where(g=>g.Select(SourceRanker.ChannelKey).Distinct().Take(2).Count()>1).Select(g=>g.Key).ToHashSet();
        Station? IdentifyIndexed(WebHit hit)
        {
            if(!PublicWeb.Allowed(hit.Url))return null;List<Station> candidates=[];string host=Host(hit.Url);
            while(host.Length>0){if(byHost.TryGetValue(host,out var matches))candidates.AddRange(matches);int dot=host.IndexOf('.');if(dot<0)break;host=host[(dot+1)..];}
            return Identify(hit,candidates);
        }
        var exact = ChannelSearch.Exact(query, directory).Where(s => (countries.Length == 0 || countries.Contains(s.Country)) && PublicWeb.Allowed(s.Url)).OrderByDescending(s => priority?.Score(s) ?? 0).Take(2).ToList();
        var seedOptions = directory.Where(s=>PublicWeb.Allowed(s.Url)&&(countries.Length==0||countries.Contains(s.Country))&&(ChannelSearch.Matches(query,s)||s.Name.Length>=2&&ChannelSearch.Matches(s.Name,new Station(query,"")))).ToList();
        var seeds = mode == "Radio" ? catalog.Where(s => PublicWeb.Allowed(s.Homepage) && (countries.Length == 0 || countries.Contains(s.Country)) && ChannelSearch.Matches(query,s)).Select(s => s with { Url = s.Homepage }) : seedOptions.AsEnumerable();
        var orderedSeeds = coverage?.Order(seeds,priority) ?? DiscoveryCoverage.Prioritize(seeds,priority);
        var queries = SearchExpansion.Queries(query,mode,countries,mode=="Radio"?[]:exact,orderedSeeds,plan.Queries);
        using var searchDeadline = CancellationTokenSource.CreateLinkedTokenSource(webToken); searchDeadline.CancelAfter(TimeSpan.FromSeconds(plan.SearchSeconds));
        bool primaryAvailable=true,duckAvailable=true;int queryIndex=0;
        var searchUrls = new HashSet<string>(StringComparer.Ordinal);
        void AddHits(IEnumerable<WebHit> hits)
        {
            foreach(var hit in hits)
            {
                var station=IdentifyIndexed(hit);
                if(station!=null&&(countries.Length==0||countries.Contains(station.Country))&&(query.Length==0||ChannelSearch.Matches(query,station)||station.Name.Length>=2&&ChannelSearch.Matches(station.Name,new Station(query,"")))){if (searchUrls.Add(hit.Url)) roots.Add((hit,station));Log("Wynik WWW",hit.Url,hit.Title+" • "+station.Country);}
                else Log("Pominięto wynik",hit.Url,"Brak zgodnej tożsamości kraju/kanału: "+hit.Title);
            }
        }
        foreach (string q in queries.Distinct().Take(plan.Queries))
        {
            ct.ThrowIfCancellationRequested();
            if (searchDeadline.IsCancellationRequested) break;
            string url = template.Replace("{query}", Uri.EscapeDataString(q)); progress?.Invoke("Internet: szukam stron • " + q);
            int previousUrls = searchUrls.Count;
            queryIndex++;if(primaryAvailable)try
            {
                var response = await fetch(url, searchDeadline.Token); var hits = ParseSearch(response.Body); Log("Wyszukiwarka", url, $"Zapytanie: {q}; {hits.Count} stron");
                AddHits(hits);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { primaryAvailable=false;Log("BŁĄD wyszukiwarki", url, ex.Message); }
            if(duckAvailable&&!searchDeadline.IsCancellationRequested&&(queryIndex==1||!primaryAvailable||searchUrls.Count==previousUrls))
            {
                string fallback=SearchExpansion.DuckTemplate.Replace("{query}",Uri.EscapeDataString(q));
                try{var response=await fetch(fallback,searchDeadline.Token);var hits=SearchExpansion.ParseDuck(response.Body);Log("Wyszukiwarka",fallback,$"DuckDuckGo: {q}; {hits.Count} stron");AddHits(hits);}
                catch(Exception ex) when(ex is not OperationCanceledException||!ct.IsCancellationRequested){duckAvailable=false;Log("BŁĄD wyszukiwarki",fallback,ex.Message);}
            }
            if(!primaryAvailable&&!duckAvailable)break;
        }
        // Homepages provide a second route if the search engine refuses a request.
        if(priority?.Enabled==true)Log("Uczenie wyszukiwania","",$"Lokalna kolejność profilu; {priority.ExampleCount} wyników prób odtwarzania. Co czwarta strona daje pierwszeństwo najdawniej sprawdzanym źródłom. Nie usuwa to kanałów ani nie potwierdza ich działania.");
        roots=roots.OrderByDescending(r=>SearchExpansion.LivePage(r.Hit.Url)).ToList();
        roots.AddRange(exact.Concat(orderedSeeds).DistinctBy(s => s.Url).Take(plan.Seeds).Select(s => (new WebHit(s.Name, s.Url), s)));
        var visited = new HashSet<string>(); var queue = new Queue<(WebHit Hit, Station Station, int Depth)>();
        var livePages = new Queue<(WebHit Hit, Station Station, int Depth)>();
        foreach (var r in roots.DistinctBy(r => r.Hit.Url).Take(plan.Roots)) queue.Enqueue((r.Hit, r.Station, 0));
        while ((queue.Count > 0 || livePages.Count > 0) && visited.Count < plan.Pages)
        {
            if (webToken.IsCancellationRequested) { ct.ThrowIfCancellationRequested(); Log("Limit", "", $"Osiągnięto {plan.Seconds} s analizy WWW. Zachowano wyniki częściowe."); break; }
            ct.ThrowIfCancellationRequested();
            var next = livePages.Count > 0 && (queue.Count == 0 || visited.Count % 3 != 0) ? livePages : queue;
            var (hit, station, depth) = next.Dequeue();
            if (!visited.Add(hit.Url)) continue;
            if (depth == 0) coverage?.Attempt(station.Url);
            progress?.Invoke($"Internet: analizuję stronę {visited.Count}/{plan.Pages} • {Host(hit.Url)}");
            try
            {
                var doc = await fetch(hit.Url, webToken);
                var urls = MediaUrls(doc);
                string pageTitle = WebUtility.HtmlDecode(Regex.Match(doc.Body, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value);
                // A shared homepage cannot lend the first channel's identity to every embedded feed.
                // Retain the seed identity for a dedicated page, or resolve the actual child page.
                bool sharedHomepage = sharedSites.Contains(station.Url);
                Station? mediaStation = sharedHomepage ? IdentifyIndexed(new(pageTitle, doc.Url)) : station;
                // Public TVP VOD live-player endpoint. No login, token forgery or geographic bypass.
                var tvp = Regex.Match(hit.Url, @"^https://vod\.tvp\.pl/live,\d+/[^,]+,(\d+)");
                if (tvp.Success)
                {
                    string api = "https://vod.tvp.pl/api/products/" + tvp.Groups[1].Value + "/videos/playlist?platform=BROWSER&videoType=LIVE";
                    try { var data = await fetch(api, webToken); urls.AddRange(MediaUrls(data)); Log("API nadawcy", api, $"Adresy mediów: {urls.Count}"); }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { Log("BŁĄD API", api, ex.Message); }
                }
                foreach (var media in urls.Distinct())
                {
                    if (mediaStation == null) { Log("Pominięto media", media, "Wspólna strona wielu kanałów; brak jednoznacznej tożsamości kanału."); continue; }
                    if(countries.Length>0&&!countries.Contains(mediaStation.Country)){Log("Pominięto media",media,"Kanał rozpoznany na wspólnej stronie należy do innego kraju niż wybrane.");continue;}
                    try { if (!await IsLive(media, webToken)) { Log("Pominięto media", media, "Nagranie VOD albo brak potwierdzenia manifestu live."); continue; } }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { Log("BŁĄD manifestu", media, ex.Message); continue; }
                    Log("Manifest live", media, "HLS bez znacznika końca lub dynamiczny DASH; odtwarzanie sprawdzi odtwarzacz.");
                    string channel = mediaStation.ChannelId.Length > 0 ? mediaStation.ChannelId : "web:" + media;
                    found.Add(mediaStation with { Name = mediaStation.Name.Length > 0 ? mediaStation.Name : hit.Title, Url = media, Kind = mode == "Radio" ? "Radio" : "TV", Source = "WWW: " + Host(doc.Url), Homepage = doc.Url, Referrer = doc.Url, ChannelId = channel, Feed = catalog.FirstOrDefault(s => s.Url == media)?.Feed ?? "", Aliases = ChannelSearch.Names(mediaStation).Concat([hit.Title, pageTitle]).Distinct().ToArray() });
                }
                Log("Strona nadawcy", doc.Url, $"Znalezione adresy mediów: {urls.Count}; odczyt strony nie potwierdza odtwarzania" + (doc.Modified.HasValue ? "; Last-Modified: " + doc.Modified.Value.ToString("O") : "; brak daty aktualizacji"));
                if (depth < plan.Depth)
                {
                    var sameSite = LiveLinks(doc).Where(x => OnDomain(x, station.Homepage.Length > 0 ? station.Homepage : hit.Url));
                    foreach (var link in sameSite.Concat(EmbeddedPlayers(doc)).Distinct())
                    {
                        if (!OnDomain(link, doc.Url)) Log("Osadzony odtwarzacz", link, "Jawna ramka iframe na stronie: " + doc.Url + "; nie wykonano JavaScript.");
                        livePages.Enqueue((new(hit.Title, link), station, depth + 1));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { Log("BŁĄD strony", hit.Url, ex.Message); }
        }
        Log("Zakres", "", $"Próbowano odczytać {visited.Count} stron; limit {plan.Pages} / {plan.Seconds} s. " + coverage?.Describe(seeds));
        return new(found.DistinctBy(s => s.Url).ToList(), evidence);
    }
}

public sealed class DiscoveryAnalyzer
{
    readonly SourceRanker ranker;
    readonly Func<Station, CancellationToken, Task<Health>> probe;
    public DiscoveryAnalyzer(SourceRanker ranker, Func<Station, CancellationToken, Task<Health>> probe) { this.ranker = ranker; this.probe = probe; }
    public static bool FreshSuccess(Health? h) => h != null && h.Decoded && DateTimeOffset.UtcNow - h.CheckedAt is var age && age >= TimeSpan.Zero && age < TimeSpan.FromMinutes(10);
    public async Task<int> Run(IReadOnlyList<Station> stations, string quality, Action<int, Station, Health>? update, CancellationToken ct, int target = 8, int budget = 32)
    {
        var ranked = ranker.Rank(stations.Where(s => !s.BrowserOnly), quality);
        var confirmed = ranked.Where(r => FreshSuccess(r.Health)).Select(r => SourceRanker.ChannelKey(r.Station)).ToHashSet();
        var attempted = new HashSet<string>(); int probes = 0;
        // Round-robin by country; first offer different channels, then try their alternative sources.
        var groups = ranked.GroupBy(r => r.Station.Country).Select(g => new Queue<Ranked>(g.DistinctBy(r => SourceRanker.ChannelKey(r.Station)).Concat(g).DistinctBy(r => r.Station.Url))).ToList();
        while (confirmed.Count < target && probes < budget && groups.Any(g => g.Count > 0))
        {
            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested(); if (confirmed.Count >= target || probes >= budget) break;
                if (!group.TryDequeue(out var item) || !attempted.Add(item.Station.Url) || confirmed.Contains(SourceRanker.ChannelKey(item.Station))) continue;
                if (item.Health != null && DateTimeOffset.UtcNow - item.Health.CheckedAt is var age && age >= TimeSpan.Zero && age < TimeSpan.FromMinutes(10)) continue;
                var health = await probe(item.Station, ct); ct.ThrowIfCancellationRequested(); ranker.Record(item.Station, health); probes++;
                if (health.Decoded) confirmed.Add(SourceRanker.ChannelKey(item.Station)); update?.Invoke(probes, item.Station, health);
            }
        }
        return probes;
    }
}
