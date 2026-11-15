using System.Net;
using System.Text.Json;

namespace SwirStreamAI;
static class WebDiscoveryTests
{
    public static int Run(string report, bool network)
    {
        var checks = new List<object>(); bool success = true;
        void Check(string name, bool ok, object? detail = null) { checks.Add(new { name, ok, detail }); success &= ok; }
        string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "web-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            Check("Polish request becomes country-filtered search", Countries.Extract("znajdź mi kilka działających źródeł TV z Polski", ["DE"]) is var plan && plan.Query == "" && plan.Codes.SequenceEqual(new[] { "PL" }));
            string xml = "<rss><channel><item><title>News PL live</title><link>https://news.example/live</link></item><item><title>Local</title><link>http://127.0.0.1/private</link></item></channel></rss>";
            Check("Search RSS returns real public result links", WebDiscovery.ParseSearch(xml).Count == 1);
            bool rejected = false; try { WebDiscovery.ParseSearch("<html>Captcha</html>"); } catch { rejected = true; } Check("Engine block is an error, not a fake success", rejected);
            rejected = false; try { WebDiscovery.ParseSearch("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///test'>]><rss>&x;</rss>"); } catch { rejected = true; } Check("XML external entities rejected", rejected);
            Check("Public crawler rejects local and private networks", !PublicWeb.Allowed("http://localhost/") && !PublicWeb.Allowed("https://10.1.2.3/") && !PublicWeb.IsPublic(IPAddress.Parse("::ffff:192.168.1.1")) && !PublicWeb.IsPublic(IPAddress.Parse("fc00::1")));
            var media = WebDiscovery.MediaUrls(new("https://news.example/live", "<source src='/hls/master.m3u8?token=a&amp;b=c'><script>{\"file\":\"https:\\/\\/cdn.example/live.m3u8?t=1\\u0026x=2\"}</script>", "text/html"));
            Check("Relative, HTML-escaped and JSON-escaped media URLs", media.Contains("https://news.example/hls/master.m3u8?token=a&b=c") && media.Contains("https://cdn.example/live.m3u8?t=1&x=2"), media);
            var station = new Station("News PL", "https://news.example/", "TV", Homepage: "https://news.example/", Country: "PL", ChannelId: "News.pl");
            var other = station with { Name = "Other", Url = "https://other.example/", Country = "DE", ChannelId = "Other.de" };
            Check("Country attribution comes from broadcaster directory", WebDiscovery.Identify(new("News PL live", "https://news.example/live"), [station, other])?.Country == "PL");
            Check("Lookalike domains cannot claim broadcaster identity", WebDiscovery.Identify(new("News PL", "https://news.example.attacker.example/live"), [station]) == null);
            var visited = new List<string>();
            Task<WebDocument> Fetch(string url, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); visited.Add(url);
                return Task.FromResult(new WebDocument(url, url.Contains("search.example") ? xml : url.EndsWith(".m3u8") ? "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:42\n#EXTINF:4,\nsegment.ts" : "<source src='https://cdn.example/live.m3u8'>", "text/html"));
            }
            var crawler = new WebDiscovery(Fetch);
            var vod = new WebDiscovery((u,c) => Task.FromResult(new WebDocument(u, "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:0\n#EXTINF:4,\nsegment.ts\n#EXT-X-ENDLIST", "application/vnd.apple.mpegurl")));
            Check("Archived video is never claimed to be a live TV source", !vod.IsLive("https://cdn.example/recording.m3u8", CancellationToken.None).GetAwaiter().GetResult());
            var masterVod = new WebDiscovery((u,c) => Task.FromResult(new WebDocument(u, u.Contains("master") ? "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1\nrecorded.m3u8" : "#EXTM3U\n#EXTINF:4,\nsegment.ts\n#EXT-X-ENDLIST", "application/vnd.apple.mpegurl")));
            Check("Master playlist cannot hide a VOD child", !masterVod.IsLive("https://cdn.example/master.m3u8", CancellationToken.None).GetAwaiter().GetResult());
            var found = crawler.Search("News PL", "TV", ["PL"], [station, other], [], "https://search.example/?q={query}", null, CancellationToken.None).GetAwaiter().GetResult();
            Check("End-to-end search discovers stream without catalog entry", found.Stations.Count == 1 && found.Stations[0].Url == "https://cdn.example/live.m3u8" && found.Stations[0].Source.StartsWith("WWW:") && found.Stations[0].Country == "PL", found);
            Check("Actual query, pages and media counts recorded", found.Evidence.Any(e => e.Stage == "Wyszukiwarka") && found.Evidence.Any(e => e.Stage == "Strona nadawcy"));
            var blocked = new WebDiscovery((u,c) => u.Contains("search.example") ? throw new HttpRequestException("429") : Fetch(u,c));
            var recovered = blocked.Search("News PL", "TV", ["PL"], [station], [], "https://search.example/?q={query}", null, CancellationToken.None).GetAwaiter().GetResult();
            Check("Search engine outage uses broadcaster pages and reports failure", recovered.Stations.Count == 1 && recovered.Evidence.Any(e => e.Stage == "BŁĄD wyszukiwarki"));
            var ranker = new SourceRanker(folder); int calls = 0;
            var many = Enumerable.Range(0, 20).Select(i => station with { Name = "Channel " + i.ToString("00"), Url = "https://cdn.example/" + i, ChannelId = "Channel" + i }).ToList();
            var analyzer = new DiscoveryAnalyzer(ranker, (s,c) => { calls++; return Task.FromResult(new Health(DateTimeOffset.UtcNow, int.Parse(s.Url.Split('/').Last()) >= 5, 300)); });
            int tried = analyzer.Run(many, "Stabilność", null, CancellationToken.None).GetAwaiter().GetResult();
            Check("Analyzer continues after first five failures to eight working channels", tried == 13 && many.Count(s => ranker.Results.TryGetValue(s.Url, out var h) && h.Decoded) == 8, tried);
            analyzer.Run(many, "Stabilność", null, CancellationToken.None).GetAwaiter().GetResult(); Check("Fresh verified results avoid redundant playback probes", calls == 13);
            var budgetRanker = new SourceRanker(Path.Combine(folder, "budget")); var failed = new DiscoveryAnalyzer(budgetRanker, (s,c) => Task.FromResult(new Health(DateTimeOffset.UtcNow, false, 8000)));
            Check("All failures respect attempt budget", failed.Run(many, "Stabilność", null, CancellationToken.None, 8, 6).GetAwaiter().GetResult() == 6);
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool cancelled = false; try { crawler.Search("", "TV", [], [], [], "https://search.example/{query}", null, cancel.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; } Check("Cancellation stops web work", cancelled);
            if (network)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
                var catalog = new SmartCatalog(Path.Combine(folder, "live")); var directory = catalog.Broadcasters(true, deadline.Token).GetAwaiter().GetResult();
                Check("Production broadcaster directory freshness/schema", directory.Stations.Count > 1000, new { count = directory.Stations.Count, directory.Detail });
                var liveWeb = new WebDiscovery().Search("DW English", "TV", ["DE"], directory.Stations, [], catalog.WebSearchTemplate, null, deadline.Token).GetAwaiter().GetResult();
                Check("Production search engine returned result pages", liveWeb.Evidence.Any(e => e.Stage == "Wyszukiwarka" && !e.Detail.Contains("; 0 stron")), liveWeb.Evidence);
                Check("Production WWW discovered media beyond catalog", liveWeb.Stations.Count > 0, liveWeb.Stations);
                bool decodedWeb = false;
                foreach (var s in liveWeb.Stations.Take(3)) { var h = StreamProbe.Run(s, folder, deadline.Token).GetAwaiter().GetResult(); if (h.Decoded) { decodedWeb = true; break; } }
                Check("Source extracted from real web page actually decodes", decodedWeb);
                var tv = catalog.Search("", "TV", ["PL"], true, deadline.Token).GetAwaiter().GetResult(); var liveRanker = new SourceRanker(Path.Combine(folder, "playback"));
                var actual = new DiscoveryAnalyzer(liveRanker, (s,c) => StreamProbe.Run(s, folder, c));
                int attempts = actual.Run(tv.Stations, "Stabilność", null, deadline.Token).GetAwaiter().GetResult();
                var working = tv.Stations.Where(s => liveRanker.Results.TryGetValue(s.Url, out var h) && h.Decoded).ToList();
                Check("Production PL search returns several independently decoded TV channels", working.Select(SourceRanker.ChannelKey).Distinct().Count() >= 3, new { attempts, working, catalog = tv.Detail });
            }
        }
        catch (Exception ex) { Check("Unexpected failure", false, ex.ToString()); }
        File.WriteAllText(report, JsonSerializer.Serialize(new { success, checks }, new JsonSerializerOptions { WriteIndented = true })); return success ? 0 : 1;
    }
}
