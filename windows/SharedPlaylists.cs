using System.Net;
using System.Text.RegularExpressions;

namespace SwirStreamAI;
public sealed class SharedPlaylists
{
    // Public playlist entries often use media ports such as 1935 or 8080. They are
    // candidates for playback, not permission for the HTML crawler to fetch new ports.
    public static bool AllowedStream(string url)
    {
        if (!TextTools.IsWeb(url)) return false;
        var uri = new Uri(url);
        if (uri.IsLoopback || RequiresBrowser(url) || Regex.IsMatch(uri.AbsolutePath,@"\.html?$",RegexOptions.IgnoreCase)) return false;
        if (IPAddress.TryParse(uri.Host,out var ip)) return PublicWeb.IsPublic(ip);
        return uri.Host.Contains('.') && !new[]{".localhost",".local",".localdomain",".internal",".lan",".home"}.Any(suffix=>uri.Host.EndsWith(suffix,StringComparison.OrdinalIgnoreCase));
    }
    public static bool RequiresBrowser(string url) => new Uri(url).Host is var host && new[] { "youtube.com", "youtu.be", "dailymotion.com", "twitch.tv" }.Any(d => host == d || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
    readonly Func<string, CancellationToken, Task<WebDocument>> fetch;
    public SharedPlaylists(Func<string, CancellationToken, Task<WebDocument>>? fetch = null) => this.fetch = fetch ?? PublicWeb.Fetch;
    public static string? PlaylistUrl(string url)
    {
        if (!PublicWeb.Allowed(url) || PublicPlaylistPrivacy.ContainsCredentials(url)) return null;
        var u = new Uri(url);
        if (!Regex.IsMatch(u.AbsolutePath, @"\.m3u8?$", RegexOptions.IgnoreCase)) return null;
        if (u.Host == "github.com")
        {
            var parts = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || parts[2] != "blob") return null;
            return "https://raw.githubusercontent.com/" + string.Join('/', parts.Take(2).Concat(parts.Skip(3)));
        }
        return new UriBuilder(u) { Fragment = "" }.Uri.AbsoluteUri;
    }
    static string CleanName(string s) => ChannelSearch.Key(s);
    public static List<Station> Parse(string body, string url, IReadOnlyList<Station> directory, string[] countries, string query)
    {
        body = PublicPlaylistPrivacy.SanitizeM3u(url, body).Body;
        if (!body.TrimStart('\uFEFF', ' ', '\r', '\n').StartsWith("#EXTM3U") || M3u.IsHls(body)) throw new InvalidDataException("Adres nie zawiera listy kanałów M3U.");
        var ids = directory.Where(s => s.ChannelId.Length > 0).GroupBy(s => s.ChannelId).ToDictionary(g => g.Key, g => g.First());
        var names = directory.SelectMany(s => ChannelSearch.Names(s).Select(n => (Key: CleanName(n), Station: s)))
            .Where(x => x.Key.Length > 0).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Select(x => x.Station).DistinctBy(SourceRanker.ChannelKey).ToList());
        var list = new List<Station>();
        foreach (var row in M3u.Parse(body, url))
        {
            var s = row with { Country=RegionalPlaylists.Country(url,row.Group,row.Country) };
            if (!AllowedStream(s.Url)) continue;
            Station? meta = ids.GetValueOrDefault(s.ChannelId);
            if (meta == null && names.TryGetValue(CleanName(s.Name), out var named))
            {
                var compatible = named.Where(n => s.Country.Length == 0 || n.Country == s.Country).ToList();
                if (compatible.Count == 1) meta = compatible[0];
            }
            var station = s with { Kind = "TV", Country = meta?.Country.Length > 0 ? meta.Country : s.Country, ChannelId = s.ChannelId.Length>0?s.ChannelId:meta?.ChannelId??"",
                Group = meta?.Group.Length > 0 ? meta.Group : s.Group, Source = "IPTV: " + new Uri(url).Host, Homepage = meta?.Homepage ?? "", Aliases = meta != null ? ChannelSearch.Names(meta).Concat(ChannelSearch.Names(s)).Distinct().ToArray() : s.Aliases };
            // A playlist's title or search query is never evidence of a channel's country.
            if ((countries.Length == 0 || countries.Contains(station.Country)) && ChannelSearch.Matches(query, station)) list.Add(station);
        }
        return list.DistinctBy(s => s.Url).Take(20000).ToList();
    }
    public async Task<WebResult> Search(string query, string[] countries, IReadOnlyList<Station> directory, string searchTemplate, string[] seeds, Action<string>? progress, CancellationToken ct, bool deep = false, Action<FoundPlaylist>? onPlaylist = null)
    {
        ct.ThrowIfCancellationRequested();
        var found = new List<Station>(); var notes = new List<WebEvidence>(); var urls = new List<string>(); var visited = new HashSet<string>();
        int seconds = deep ? 90 : 40;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(seconds)); var token = deadline.Token;
        void Log(string stage, string url, string detail) => notes.Add(new(stage, url, detail, DateTimeOffset.UtcNow));
        async Task ReadPlaylist(string url)
        {
            ct.ThrowIfCancellationRequested(); if (!visited.Add(url) || token.IsCancellationRequested) return;
            progress?.Invoke("Odczytuję listę IPTV: " + new Uri(url).Host);
            try
            {
                var doc = await fetch(url, token);
                var playlist = PlaylistRepository.Create(doc, directory, countries, query);
                ct.ThrowIfCancellationRequested();
                var items = PlaylistRepository.SelectStations(playlist.Stations, countries, query); found.AddRange(items);
                try { onPlaylist?.Invoke(playlist); }
                catch (Exception ex) when (ex is not OperationCanceledException) { Log("BŁĄD zapisu playlisty", url, ex.Message); }
                Log("Lista IPTV", playlist.SourceUrl, $"{items.Count} pasujących adresów / {playlist.SourceCount} adresów w playliście; " + playlist.Freshness + "; odtwarzanie jeszcze niepotwierdzone. Pominięto znane strony odtwarzaczy i niepubliczne adresy.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { Log("BŁĄD listy IPTV", url, ex.Message); }
        }
        // Fetch the known published lists before spending any budget on a search engine.
        // Sorting URLs first was insufficient: a slow/blocked search could exhaust the deadline.
        foreach (string seed in seeds.Where(u => PublicWeb.Allowed(u) && !PublicPlaylistPrivacy.ContainsCredentials(u)).Distinct().Take(deep ? 8 : 5)) await ReadPlaylist(seed);
        string region = countries.Length == 1 ? WorldMarkets.Name(countries[0]) : "";
        string q = "site:github.com public IPTV playlist.m3u " + query + " " + region;
        var queries = new List<string> { "public TV playlist m3u " + query + " " + region, q };
        if (deep)
        {
            var names = Countries.Load().ToDictionary(c => c.Code, c => c.Name);
            queries.AddRange((countries.Length == 0 ? new[] { "" } : countries).Select(c => "site:github.com free television m3u8 " + query + " " + names.GetValueOrDefault(c,c)));
        }
        using var searchDeadline = CancellationTokenSource.CreateLinkedTokenSource(token); searchDeadline.CancelAfter(TimeSpan.FromSeconds(deep ? 25 : 12));
        async Task ReadHits(List<WebHit> hits)
        {
            urls.AddRange(hits.Select(h=>PlaylistUrl(h.Url)).OfType<string>());
            foreach(var page in hits.Where(h=>new Uri(h.Url).Host=="github.com"&&!PublicPlaylistPrivacy.ContainsCredentials(h.Url)&&PlaylistUrl(h.Url)==null).Take(deep?6:2))
            {
                try{var doc=await fetch(page.Url,searchDeadline.Token);foreach(Match m in Regex.Matches(doc.Body,"(?:href|src)=[\"']([^\"']+\\.m3u8?(?:\\?[^\"']*)?)[\"']",RegexOptions.IgnoreCase))
                    if(Uri.TryCreate(new Uri(doc.Url),WebUtility.HtmlDecode(m.Groups[1].Value),out var u)&&PlaylistUrl(u.AbsoluteUri) is string candidate)urls.Add(candidate);
                    Log("Repozytorium IPTV",page.Url,"Odczytano odnośniki do playlist.");}
                catch(Exception ex) when(ex is not OperationCanceledException||!ct.IsCancellationRequested){Log("BŁĄD repozytorium IPTV",page.Url,ex.Message);}
            }
        }
        bool primaryAvailable=true,duckAvailable=true;int queryNumber=0;
        foreach (string searchQuery in queries.Distinct().Take(deep ? 6 : 2))
        {
            if (searchDeadline.IsCancellationRequested) break;
            string endpoint = searchTemplate.Replace("{query}", Uri.EscapeDataString(searchQuery)); progress?.Invoke("Szukam publicznych udostępnień IPTV…");
            queryNumber++;if(primaryAvailable)try
            {
            var response = await fetch(endpoint, searchDeadline.Token); var hits = WebDiscovery.ParseSearch(response.Body); Log("Wyszukiwanie IPTV", endpoint, $"{hits.Count} wyników dla: {searchQuery}");
            await ReadHits(hits);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) {primaryAvailable=false; Log("BŁĄD szukania IPTV", endpoint, ex.Message); }
            if(duckAvailable&&!searchDeadline.IsCancellationRequested&&(queryNumber==1||!primaryAvailable||urls.Count==0))
            {
                string fallback=SearchExpansion.DuckTemplate.Replace("{query}",Uri.EscapeDataString(searchQuery));
                try{var response=await fetch(fallback,searchDeadline.Token);var hits=SearchExpansion.ParseDuck(response.Body);Log("Wyszukiwanie IPTV",fallback,$"DuckDuckGo: {hits.Count} wyników dla: {searchQuery}");await ReadHits(hits);}
                catch(Exception ex) when(ex is not OperationCanceledException||!ct.IsCancellationRequested){duckAvailable=false;Log("BŁĄD szukania IPTV",fallback,ex.Message);}
            }
            if(!primaryAvailable&&!duckAvailable)break;
        }
        foreach (string url in urls.Distinct().Where(u => !visited.Contains(u)).Take(Math.Max(0, (deep ? 15 : 5) - visited.Count)))
        {
            ct.ThrowIfCancellationRequested(); if (token.IsCancellationRequested) { Log("Limit IPTV", "", $"Osiągnięto limit {seconds} s; zachowano wyniki częściowe."); break; }
            await ReadPlaylist(url);
        }
        ct.ThrowIfCancellationRequested();
        return new(found.DistinctBy(s => s.Url).ToList(), notes);
    }
}
