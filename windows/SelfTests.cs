using System.Text.Json;

namespace SwirStreamAI;

static class SelfTests
{
    public static int Run(IntentNet model, string output, bool network)
    {
        var checks = new List<object>(); bool success = true;
        void Check(string name, bool ok, object? detail = null) { checks.Add(new { name, ok, detail }); success &= ok; }
        var temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "test-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string m3u = "\uFEFF#EXTM3U\n#EXTINF:-1 group-title=\"Music, Rock\",Radio Świr\nhttps://example.org/a\n#EXTINF:-1,Duplicate\nhttps://example.org/a\n#EXTINF:-1,Other\n../b.mp3\n#EXTINF:-1,Unsafe\nvlc://quit\n";
            var parsed = M3u.Parse(m3u, "https://example.org/list/stations.m3u");
            Check("M3U metadata + BOM + dedup", parsed.Count == 2 && parsed[0].Name == "Radio Świr" && parsed[0].Group == "Music, Rock");
            Check("M3U relative HTTP URL", parsed[1].Url == "https://example.org/b.mp3");
            Check("M3U protocol rejection", M3u.Parse("file:///etc/passwd\nvlc://quit\nftp://example.org/a", "https://example.org/list.m3u").Count == 0);
            Check("M3U export roundtrip", M3u.Parse(M3u.Export(parsed), "https://example.org/list.m3u").Select(s => (s.Name, s.Url)).SequenceEqual(parsed.Select(s => (s.Name, s.Url))));
            var options = M3u.Parse("#EXTINF:-1 tvg-name=\"Test\",Other\n#EXTVLCOPT:http-user-agent=SwirTest\n#EXTVLCOPT:http-referrer=https://example.org\nhttps://example.org/live", "https://example.org/list.m3u");
            var roundTrip = M3u.Parse(M3u.Export(options), "https://example.org/list.m3u");
            Check("M3U VLC headers preserved", roundTrip[0].UserAgent == "SwirTest" && roundTrip[0].Referrer == "https://example.org" && roundTrip[0].Name == "Test");
            Check("Credentials in stream URL rejected", !TextTools.IsWeb("https://user:secret@example.org/live"));
            var hls = M3u.Parse("#EXTM3U\n#EXT-X-TARGETDURATION:5\n#EXTINF:5,\nsegment.ts", "https://example.org/live.m3u8");
            Check("HLS treated as a single stream", hls.Count == 1 && hls[0].Url.EndsWith("live.m3u8"));
            var store = new LibraryStore(temp); store.Favorite(parsed[0], true); store.Favorite(parsed[0], true);
            Check("Favorites deduplicated", new LibraryStore(temp).Data.Favorites.Count == 1);
            store.Favorite(parsed[0], false); Check("Favorites removal persisted", new LibraryStore(temp).Data.Favorites.Count == 0);
            for (int i = 0; i < 205; i++) store.Played(new("Station " + i, "https://example.org/" + i));
            Check("History bounded and latest first", new LibraryStore(temp).Data.History.Count == 200 && store.Data.History[0].Station.Name == "Station 204");
            File.WriteAllText(Path.Combine(temp, "library.json"), "{broken"); var recovered = new LibraryStore(temp);
            Check("Corrupt library backup", recovered.Warning != null && Directory.GetFiles(temp, "*.damaged-*").Length == 1);
            string legacyFile = Path.Combine(temp, "legacy.json");
            string legacyText = "{\"favorites\":[{\"name\":\"Legacy\",\"url\":\"https://example.org/legacy\",\"kind\":\"radio\"}],\"own_streams\":[{\"name\":\"Own\",\"url\":\"https://example.org/own\"}],\"history\":[{\"name\":\"Legacy\",\"url\":\"https://example.org/legacy\"}]}";
            File.WriteAllText(legacyFile, legacyText); recovered.ImportLegacy(legacyFile); recovered.ImportLegacy(legacyFile);
            Check("Original library migration without duplicates or source changes", recovered.Data.Favorites.Count == 1 && recovered.Data.Playlist.Count == 1 && recovered.Data.History.Count == 1 && File.ReadAllText(legacyFile) == legacyText);
            Check("Polish normalization", TextTools.Normalize("GŁOŚNIEJ ŻÓŁĆ") == "glosniej zolc");
            Check("Search query extraction", IntentNet.Query("Znajdź radio techno") == "techno");
            Check("Model architecture", model.Labels.Length == 19 && model.TrainingExamples >= 200);
            string casesPath = Path.Combine(AppContext.BaseDirectory, "model", "evaluation.json");
            var cases = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(casesPath))!;
            int correct = 0, acceptedCorrect = 0, total = 0;
            var predictions = new List<object>();
            foreach (var (intent, phrases) in cases) foreach (var phrase in phrases)
            {
                var p = model.Predict(phrase); total++; if (p.Intent == intent) correct++; if (p.Intent == intent && p.Accepted) acceptedCorrect++;
                predictions.Add(new { phrase, expected = intent, prediction = p });
            }
            Check("Held-out IntentNet classification >= 90%", correct / (double)total >= .9, new { correct, acceptedCorrect, total, predictions });
            Check("Out-of-domain rejection", new[] { "", "xyzqwerty", "jaka pogoda jutro w krakowie", "napisz wiersz o kosmosie", "ile kosztuje chleb" }.All(t => !model.Predict(t).Accepted));
            using (var vlc = new SwirPlayer(IntPtr.Zero, true))
            {
                vlc.Play(Path.Combine(AppContext.BaseDirectory, "assets", "local-test.mp4"));
                for (int i = 0; i < 40 && (vlc.Stats.VideoProgressSamples < 5 || vlc.Stats.AudioProgressSamples < 5); i++) Thread.Sleep(250);
                var stats = vlc.Stats; Check("Swir Player local video decoding", stats.VideoProgressSamples >= 5, new { vlc.Version, stats.VideoProgressSamples });
                Check("Swir Player local audio decoding", stats.AudioProgressSamples >= 5, stats.AudioProgressSamples);
                vlc.Pause(); Thread.Sleep(200); Check("Swir Player pause", vlc.State == 4);
                vlc.Resume(); Thread.Sleep(200); Check("Swir Player resume", vlc.State == 3);
                Check("Swir Player file seeking", vlc.Seekable); vlc.Position = .4f;
                int stableSeekSamples=0;var seekWatch=System.Diagnostics.Stopwatch.StartNew();
                while(seekWatch.ElapsedMilliseconds<3000&&stableSeekSamples<4){Thread.Sleep(100);stableSeekSamples=vlc.Position is >=.35f and <.6f?stableSeekSamples+1:0;}
                Check("Swir Player precise seek position",stableSeekSamples>=4,new{position=vlc.Position,timeMs=vlc.Time,elapsedMs=seekWatch.ElapsedMilliseconds});
                vlc.Stop(); Check("Swir Player stop", vlc.State == 5);
                bool rejected = false; try { vlc.Play("vlc://quit"); } catch (ArgumentException) { rejected = true; } Check("Swir Player invalid protocol rejected", rejected);
                using var localHls = new TestMediaServer(Path.Combine(AppContext.BaseDirectory, "assets", "test-hls"));
                vlc.Play(localHls.Url);
                for (int i = 0; i < 60 && vlc.Stats.VideoProgressSamples < 5 && vlc.State != 7; i++) Thread.Sleep(250);
                Check("Local HTTP HLS segments decoded", vlc.Stats.VideoProgressSamples >= 5, new { decodedVideo = vlc.Stats.VideoProgressSamples, state = vlc.State });
                vlc.Stop();
            }
            if (network)
            {
                using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var catalog = new Catalog();
                var radio = catalog.SearchRadio("Radio Paradise", ct.Token).GetAwaiter().GetResult();
                Check("Live Radio Browser API", radio.Stations.Count > 0, new { count = radio.Stations.Count, radio.Detail });
                var tv = catalog.SearchTv("polskie", true, ct.Token).GetAwaiter().GetResult();
                Check("Live iptv-org catalog", tv.Stations.Count > 0, new { count = tv.Stations.Count, tv.Detail });
                bool played = false; var attempts = new List<object>();
                using var vlc = new SwirPlayer(IntPtr.Zero, true);
                foreach (var station in radio.Stations.Take(3))
                {
                    vlc.Play(station.Url);
                    for (int i = 0; i < 60 && vlc.Stats.AudioProgressSamples == 0 && vlc.State != 7; i++) Thread.Sleep(250);
                    attempts.Add(new { station.Name, station.Url, state = vlc.State, decodedAudio = vlc.Stats.AudioProgressSamples });
                    played |= vlc.Stats.AudioProgressSamples > 0; vlc.Stop(); if (played) break;
                }
                Check("Public internet audio decoded by Swir Player", played, attempts);
                var news = catalog.SearchTv("DW English", false, CancellationToken.None).GetAwaiter().GetResult();
                bool videoPlayed = false; var videoAttempts = new List<object>();
                foreach (var station in news.Stations.Take(3))
                {
                    vlc.Play(station.Url);
                    for (int i = 0; i < 80 && vlc.Stats.VideoProgressSamples < 5 && vlc.State != 7; i++) Thread.Sleep(250);
                    videoAttempts.Add(new { station.Name, station.Url, state = vlc.State, decodedVideo = vlc.Stats.VideoProgressSamples });
                    videoPlayed |= vlc.Stats.VideoProgressSamples >= 5; vlc.Stop(); if (videoPlayed) break;
                }
                Check("Public internet TV video decoded by Swir Player", videoPlayed, videoAttempts);
            }
        }
        catch (Exception ex) { Check("Unexpected failure", false, ex.ToString()); }
        File.WriteAllText(output, JsonSerializer.Serialize(new { app = "SwirStream AI 0.9.8", date = DateTimeOffset.Now, os = Environment.OSVersion.ToString(), architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), success, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return success ? 0 : 1;
    }
}
