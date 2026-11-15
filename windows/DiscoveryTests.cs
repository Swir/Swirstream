using System.Text.Json;

namespace SwirStreamAI;
static class DiscoveryTests
{
    public static int Run(string report, bool network)
    {
        var checks = new List<object>(); bool success = true;
        void Check(string name, bool ok, object? detail = null) { checks.Add(new { name, ok, detail }); success &= ok; }
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "discovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var countries = Countries.Load(); Check("Country catalog codes", countries.Count > 200 && countries.Any(c => c.Code == "PL") && countries.Any(c => c.Code == "NO"));
            Check("Unambiguous Polish country names", countries.Single(c => c.Code == "AF").Name == "Afganistan" && countries.Single(c => c.Code == "DE").Name == "Niemcy");
            var extracted = Countries.Extract("polskie i niemieckie kanały informacyjne", ["NO"]);
            Check("Natural language multi-country extraction", extracted.Query == "news" && extracted.Codes.SequenceEqual(new[] { "PL", "DE" }));
            Check("Checkbox countries retained for generic query", Countries.Extract("sport", ["FR", "ES"]).Codes.SequenceEqual(new[] { "FR", "ES" }));
            File.WriteAllText(Path.Combine(root, "sources.json"), "{broken"); var recoveredSettings = SourceSettings.Load(root);
            Check("Corrupt source config recovers and keeps backup", recoveredSettings.Notice.Length > 0 && Directory.GetFiles(root, "sources.json.damaged-*").Length == 1);
            var store = new LibraryStore(root); store.Data.SelectedCountries = ["PL", "NO"]; store.Data.QualityMode = "Oszczędzaj transfer"; store.Save(); store.Save();
            var backupData=JsonSerializer.Deserialize<LibraryData>(PrivateData.Read<byte[]>(Path.Combine(root,"library.json.bak.dpapi"),"library-backup-v1"));
            Check("Countries and quality persist with encrypted recoverable backup", new LibraryStore(root).Data.SelectedCountries.SequenceEqual(new[] { "PL", "NO" }) && backupData?.SelectedCountries.SequenceEqual(new[]{"PL","NO"})==true&&backupData.QualityMode=="Oszczędzaj transfer"&&!File.Exists(Path.Combine(root,"library.json.bak")));
            var imported = new LibraryStore(Path.Combine(root, "import")); store.Favorite(new("Saved", "https://example.org/saved"), true);
            imported.MergeLibrary(Path.Combine(root, "library.json")); imported.MergeLibrary(Path.Combine(root, "library.json"));
            Check("Native 0.1 library merges without duplicates", imported.Data.Favorites.Count == 1 && store.Data.Favorites.Count == 1);
            string playlist = "#EXTM3U\n#EXTINF:-1 tvg-id=\"News.pl@HD\" group-title=\"News\",News (1080p)\nhttps://example.org/hd\n#EXTINF:-1 tvg-id=\"News.pl@SD\",News (480p)\nhttps://example.org/sd";
            var stations = M3u.Parse(playlist, "https://example.org/list").Select(s => s with { Kind = "TV" }).ToList();
            Check("Channel identity preserved across quality variants", stations.Count == 2 && SourceRanker.ChannelKey(stations[0]) == SourceRanker.ChannelKey(stations[1]) && stations[0].Country == "PL");
            var ranker = new SourceRanker(root);
            File.WriteAllText(Path.Combine(root,"source-health.json"),JsonSerializer.Serialize(new Dictionary<string,Health>{{stations[0].Url,new(DateTimeOffset.UtcNow,true,100)}}));
            Check("Prior VLC checks never mark the new engine as verified",!new SourceRanker(root).Results.ContainsKey(stations[0].Url));
            Check("Bandwidth profile prefers SD before tests", ranker.Rank(stations, "Oszczędzaj transfer")[0].Station.Url.EndsWith("sd"));
            Check("Quality profile prefers HD before tests", ranker.Rank(stations, "Najwyższa jakość")[0].Station.Url.EndsWith("hd"));
            ranker.Record(stations[0], new(DateTimeOffset.UtcNow, false, 8000)); ranker.Record(stations[1], new(DateTimeOffset.UtcNow, true, 800));
            Check("Decoded source outranks failed HD", ranker.Top(stations, "Najwyższa jakość")[0].Station.Url.EndsWith("sd"));
            Check("TOP groups channel variants", ranker.Top(stations, "Stabilność").Count == 1);
            Check("Failover stays within same channel", ranker.Alternative(stations.Append(new("Other", "https://example.org/other", "TV", ChannelId: "Other.pl")), stations[0], [stations[0].Url], "Stabilność")?.Url == stations[1].Url);
            Check("Failover does not loop", ranker.Alternative(stations, stations[0], stations.Select(s => s.Url).ToHashSet(), "Stabilność") == null);
            ranker.Record(stations[0], new(DateTimeOffset.UtcNow.AddDays(-8), true, 100));
            Check("Old health never claims current playback", ranker.Rank([stations[0]], "Stabilność")[0].Reason.Contains("stary test"));
            Check("Health persists across restart", new SourceRanker(root).Results.ContainsKey(stations[1].Url));
            var cfg = new SourceSettings { TvTemplates = ["https://primary.test/{path}", "https://backup.test/{path}"], RadioMirrors = ["https://radio.test"] };
            int fetched = 0; bool offline = false, badSchema = false;
            async Task<(string, DateTimeOffset?)> Fetch(string url, CancellationToken ct)
            {
                await Task.Yield(); ct.ThrowIfCancellationRequested(); Interlocked.Increment(ref fetched);
                if (offline || url.Contains("primary.test")) throw new HttpRequestException("offline fixture");
                return (badSchema ? "<html>bad feed</html>" : url.Contains("/de.m3u") ? playlist.Replace("News.pl", "News.de") : playlist, DateTimeOffset.UtcNow.AddHours(-1));
            }
            var catalog = new SmartCatalog(Path.Combine(root, "feeds"), cfg, Fetch);
            var first = catalog.Search("news", "TV", ["PL", "DE"], true, CancellationToken.None).GetAwaiter().GetResult();
            Check("Per-country requests + mirror fallback", first.Stations.Select(s => s.Country).Distinct().Count() == 2 && first.Detail.Contains("serwer zapasowy"));
            int before = fetched; catalog.Search("news", "TV", ["PL", "DE"], false, CancellationToken.None).GetAwaiter().GetResult();
            Check("Fresh disk cache avoids network", fetched == before);
            offline = true; var emergency = catalog.Search("news", "TV", ["PL"], true, CancellationToken.None).GetAwaiter().GetResult();
            Check("Offline cache explicitly labelled", emergency.Stations.Count == 2 && emergency.Detail.Contains("TRYB AWARYJNY"));
            offline = false; badSchema = true; var invalid = catalog.Search("news", "TV", ["PL"], true, CancellationToken.None).GetAwaiter().GetResult();
            Check("Invalid schema cannot replace valid cache", invalid.Stations.Count == 2 && invalid.Detail.Contains("TRYB AWARYJNY"));
            foreach (var f in Directory.GetFiles(Path.Combine(root, "feeds", "catalog-cache"), "*.json"))
            {
                var entry = JsonSerializer.Deserialize<FeedCache>(File.ReadAllText(f))!;
                File.WriteAllText(f, JsonSerializer.Serialize(entry with { DownloadedAt = DateTimeOffset.UtcNow.AddDays(-8) }));
            }
            offline = true; bool expired = false;
            try { catalog.Search("news", "TV", ["PL"], false, CancellationToken.None).GetAwaiter().GetResult(); } catch (IOException) { expired = true; }
            Check("Cache older than seven days refused", expired);
            var oldFeed = new SmartCatalog(Path.Combine(root, "old-feed"), cfg, (u,c) => Task.FromResult((playlist, (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(-9))));
            bool oldRejected = false; try { oldFeed.Search("", "TV", ["PL"], true, CancellationToken.None).GetAwaiter().GetResult(); } catch (IOException) { oldRejected = true; }
            Check("Old provider Last-Modified rejected", oldRejected);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool stopped = false;
            try { catalog.Search("", "TV", ["PL"], true, cancelled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { stopped = true; }
            Check("Cancellation propagates without stale result", stopped);
            var local = new Station("Local", Path.Combine(AppContext.BaseDirectory, "assets", "local-test.mp4"), "TV", ChannelId: "Local");
            var probe = StreamProbe.Run(local, root, CancellationToken.None).GetAwaiter().GetResult();
            Check("Isolated Swir Player probe actually decodes", probe.Decoded, probe);
            var fail = StreamProbe.Run(local with { Url = "http://127.0.0.1:1/unavailable" }, root, CancellationToken.None).GetAwaiter().GetResult();
            Check("Failed stream probe is not a success", !fail.Decoded, fail);
            using var probeCancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)); bool probeStopped = false;
            try { StreamProbe.Run(local, root, probeCancel.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { probeStopped = true; }
            Check("Probe subprocess supports cancellation", probeStopped);
            if (network)
            {
                var live = new SmartCatalog(Path.Combine(root, "live")); using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var result = live.Search("", "TV", ["PL", "DE"], true, deadline.Token).GetAwaiter().GetResult();
                Check("Production TV multi-country catalogs", result.Stations.Any(s => s.Country == "PL") && result.Stations.Any(s => s.Country == "DE"), new { count = result.Stations.Count, result.Detail });
                var radio = live.Search("", "Radio", ["PL", "DE"], true, deadline.Token).GetAwaiter().GetResult();
                Check("Production radio country filters", radio.Stations.Count > 0 && radio.Stations.All(s => s.Country is "PL" or "DE"), new { count = radio.Stations.Count, radio.Detail });
                var tvCandidates = result.Stations.Where(s => s.Name.Contains("Das Erste") || s.Name.Contains("DW Russian")).Take(3).ToList(); bool decoded = false; var attempts = new List<object>();
                foreach (var station in tvCandidates) { var h = StreamProbe.Run(station, root, deadline.Token).GetAwaiter().GetResult(); attempts.Add(new { station.Name, station.Url, h }); if (h.Decoded) { decoded = true; break; } }
                Check("Production TV isolated playback verification", decoded, attempts);
            }
        }
        catch (Exception ex) { Check("Unexpected failure", false, ex.ToString()); }
        File.WriteAllText(report, JsonSerializer.Serialize(new { version = "0.9.7", date = DateTimeOffset.Now, success, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return success ? 0 : 1;
    }
}
