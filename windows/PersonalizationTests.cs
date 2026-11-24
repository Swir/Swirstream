using System.Text.Json;

namespace SwirStreamAI;
static class PersonalizationTests
{
    public static int Run(string report, bool network)
    {
        var checks = new List<object>(); bool success = true;
        void Check(string name, bool ok, object? detail = null) { checks.Add(new { name, ok, detail }); success &= ok; }
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "personal-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var model = IntentNet.Load(Path.Combine(AppContext.BaseDirectory, "model", "tastenet.json"), 10);
            var held = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "model", "taste-evaluation.json")))!;
            var eval = held.SelectMany(pair => pair.Value.Select(text => new { expected = pair.Key, text, prediction = model.Predict(text) })).ToList();
            int correct = eval.Count(e => e.prediction.Intent == e.expected), accepted = eval.Count(e => e.prediction.Accepted), acceptedCorrect = eval.Count(e => e.prediction.Accepted && e.prediction.Intent == e.expected);
            Check("Trained ten-topic neural classifier: held-out evaluation", correct >= eval.Count * .85 && accepted >= 25 && acceptedCorrect >= accepted * .95, new { correct, accepted, acceptedCorrect, total = eval.Count, eval });
            var store = new LibraryStore(root); var taste = new TasteEngine(store.Data, model);
            var jazz = new Station("Evening Jazz", "https://example.org/jazz", "Radio", "music", Country: "PL", ChannelId: "Jazz.pl");
            var news = new Station("Morning News", "https://example.org/news", "TV", "news", Country: "DE", ChannelId: "News.de");
            Check("Unknown station is not assigned an invented category", taste.Categorize(new("QXJ827 ZXQ", "https://example.org/unknown")).Code == "unknown");
            Check("Explicit source category precedes model guess", taste.Categorize(jazz with { Name = "Breaking News" }).Code == "music");
            double initial = taste.Preference(jazz); taste.Learn(jazz, true, "favorite");
            Check("Positive feedback changes real model weights and affinity", taste.Preference(jazz) > initial && store.Data.TasteWeights.Any(w => w != 0));
            double learned = taste.Preference(jazz); int examples = store.Data.TasteExamples;
            Check("Repeated click cannot flood learning", !taste.Learn(jazz, true, "favorite") && store.Data.TasteExamples == examples);
            taste.Learn(jazz, false, "hide"); Check("Negative feedback lowers affinity", taste.Preference(jazz) < learned);
            store.Save(); var reopened = new LibraryStore(root); var reopenedTaste = new TasteEngine(reopened.Data, model);
            Check("Learned preferences survive restart", Math.Abs(taste.Preference(jazz) - reopenedTaste.Preference(jazz)) < .000001);
            store.Data.LearnPreferences = false; examples = store.Data.TasteExamples; taste.Learn(news, true, "favorite"); Check("Learning can be switched off", store.Data.TasteExamples == examples);
            store.Data.HiddenChannels.Add(SourceRanker.ChannelKey(jazz));
            Check("Hiding a channel also hides its source variants", !taste.Filter([jazz, jazz with { Url = "https://example.org/jazz2" }, news]).Any(s => s.ChannelId == jazz.ChannelId));
            var english = news with { Name = "World English", ChannelId = "World.de", Feed = "EnglishHD" };
            var german = english with { Name = "World German", Feed = "GermanHD", Url = "https://example.org/german" };
            string migratedFolder = Path.Combine(root, "legacy-hidden"); Directory.CreateDirectory(migratedFolder);
            File.WriteAllText(Path.Combine(migratedFolder, "library.json"), "{\"HiddenChannels\":[\"TV:World.de\",\"TV:PL:my news\"]}");
            var migrated = new LibraryStore(migratedFolder); var migratedTaste = new TasteEngine(migrated.Data, model);
            Check("Migrated 0.7.0 base-channel hide still covers every language feed", !migratedTaste.Filter([english, german]).Any());
            var unnamed = news with { Name = "My News (English) (1080p)", Country = "PL", ChannelId = "", Feed = "EnglishHD" };
            Check("Migrated no-ID hide retains the exact old name normalization", !migratedTaste.Filter([unnamed]).Any());
            Check("Reading legacy hides does not rewrite stored preferences", migrated.Data.HiddenChannels.SetEquals(["TV:World.de", "TV:PL:my news"]));
            var preciseData = new LibraryData { HiddenChannels = [SourceRanker.ChannelKey(english)] }; var preciseTaste = new TasteEngine(preciseData, model);
            Check("New English-only hide does not hide the German program", preciseTaste.Filter([english, german]).SequenceEqual([german]));
            Check("Variant hide still covers another quality of the same language", !preciseTaste.Filter([english with { Feed = "EnglishSD", Url = "https://example.org/english-sd" }]).Any());
            store.Data.HiddenChannels.Clear(); store.Data.TopicFilter = "music";
            Check("Topic filter keeps intended genre", taste.Filter([jazz, news]).SequenceEqual([jazz]));
            store.Data.PreferredTopics = ["music"]; var ranker = new SourceRanker(root) { Personalize = s => taste.Score(s) };
            store.Data.TopicFilter = "all"; Check("Verification follows preferred topics instead of stopping at unrelated working channels", taste.CandidatesToVerify([jazz, news]).SequenceEqual([jazz]));
            Check("Declared interests influence ranking", ranker.Rank([jazz, news], "Stabilność")[0].Station == jazz);
            ranker.Record(jazz, new(DateTimeOffset.UtcNow, false, 8000)); ranker.Record(news, new(DateTimeOffset.UtcNow, true, 800));
            Check("Personal taste cannot promote a failed stream over verified playback", ranker.Rank([jazz, news], "Stabilność")[0].Station == news);
            string exported = taste.Export([jazz, jazz with { Url = "https://example.org/alternate" }, news]); var imported = M3u.Parse(exported, "https://example.org/list.m3u");
            Check("Organized M3U deduplicates channels and preserves identity/country", imported.Count == 2 && imported.Any(s => s.ChannelId == "Jazz.pl" && s.Country == "PL" && s.Group.Contains("Muzyka")));
            var profiles = new ProfileManager(Path.Combine(root, "profiles-test")); var first = new LibraryStore(profiles.Folder("default")); first.Favorite(jazz, true); first.Played(jazz);
            var secondProfile = profiles.Add("Sport"); profiles.Activate(secondProfile.Id); var second = new LibraryStore(profiles.Folder(secondProfile.Id));
            Check("New profile has its own favorites and history", second.Data.Favorites.Count == 0 && second.Data.History.Count == 0);
            second.Favorite(news, true); Check("Profile library writes do not alter another profile", new LibraryStore(profiles.Folder("default")).Data.Favorites.Single() == jazz);
            Check("Active profile persists", new ProfileManager(Path.Combine(root, "profiles-test")).Index.ActiveId == secondProfile.Id);
            bool traversal = false; try { profiles.Folder("../../outside"); } catch (InvalidDataException) { traversal = true; } Check("Profile paths cannot escape storage", traversal);
            File.WriteAllText(Path.Combine(root, "profiles-test", "profiles.json"), "{invalid"); var recovered = new ProfileManager(Path.Combine(root, "profiles-test"));
            Check("Corrupt profile index recovers without losing legacy library", recovered.Notice.Length > 0 && new LibraryStore(recovered.Folder("default")).Data.Favorites.Count == 1 && Directory.GetFiles(Path.Combine(root, "profiles-test"), "profiles.json.damaged-*").Length == 1);
            Check("GitHub file links resolve to raw public M3U", SharedPlaylists.PlaylistUrl("https://github.com/public/repo/blob/main/list.m3u") == "https://raw.githubusercontent.com/public/repo/main/list.m3u");
            Check("Search link cannot become a local playlist fetch", SharedPlaylists.PlaylistUrl("http://127.0.0.1/list.m3u") == null);
            string m3u = "#EXTM3U\n#EXTINF:-1 tvg-id=\"News.de\",Morning News\nhttps://example.org/news\n#EXTINF:-1,Unknown\nhttps://example.org/unknown";
            var shared = SharedPlaylists.Parse(m3u, "https://raw.githubusercontent.com/public/repo/main/list.m3u", [news], ["DE"], "");
            Check("International playlist filters real channel country, not query country", shared.Count == 1 && shared[0].Country == "DE");
            var duplicateBrand = SharedPlaylists.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"MorningNews.pl\",Morning News\nhttps://example.org/polish-news", "https://example.org/list.m3u", [news], ["PL"], "");
            Check("Matching names from different countries do not merge channels", duplicateBrand.Count == 1 && duplicateBrand[0].ChannelId == "MorningNews.pl" && duplicateBrand[0].Country == "PL");
            var conflict = M3u.Parse("#EXTM3U\n#EXTINF:-1 tvg-id=\"Station.cn\" tvg-country=\"FR\",Station\nhttps://example.org/station", "https://example.org/list");
            Check("Country of origin is not overwritten by distribution country", conflict.Single().Country == "CN");
            Check("Known browser-only URLs are not advertised as direct player sources", SharedPlaylists.RequiresBrowser("https://www.youtube.com/channel/test/live") && !SharedPlaylists.RequiresBrowser("https://cdn.example/live.m3u8"));
            bool refused = false; try { SharedPlaylists.Parse("#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:1", "https://example.org/live.m3u8", [], [], ""); } catch (InvalidDataException) { refused = true; } Check("HLS media playlist is not mistaken for channel collection", refused);
            if (network)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3)); var catalog = new SmartCatalog(Path.Combine(root, "catalog"));
                var directory = catalog.Broadcasters(true, deadline.Token).GetAwaiter().GetResult();
                Check("Production categories and countries present in fresh directory", directory.Stations.Any(s => s.Group.Length > 0) && directory.Stations.Count > 1000, directory.Detail);
                var publicLists = new SharedPlaylists().Search("", ["PL", "DE", "FR"], directory.Stations, catalog.WebSearchTemplate, catalog.SharedPlaylistUrls, null, deadline.Token).GetAwaiter().GetResult();
                Check("Production public IPTV lists add international candidates", publicLists.Stations.Count > 10 && publicLists.Stations.Select(s => s.Country).Distinct().Count() >= 2 && publicLists.Stations.All(s => s.Country is "PL" or "DE" or "FR"), new { count = publicLists.Stations.Count, countries = publicLists.Stations.Select(s => s.Country).Distinct(), publicLists.Evidence, samples = publicLists.Stations.Take(8) });
                var sharedRanker = new SourceRanker(Path.Combine(root, "shared-health")); var playback = new List<object>();
                var analyzer = new DiscoveryAnalyzer(sharedRanker, (s,c) => StreamProbe.Run(s, root, c));
                int tried = analyzer.Run(publicLists.Stations, "Stabilność", (n,s,h) => playback.Add(new { s.Name, s.Country, s.Url, health = h }), deadline.Token, target: 2, budget: 16).GetAwaiter().GetResult();
                Check("Sources from shared IPTV list actually decode in Swir Player", sharedRanker.Results.Values.Count(h => h.Decoded) >= 2, new { tried, playback });
            }
        }
        catch (Exception ex) { Check("Unexpected failure", false, ex.ToString()); }
        File.WriteAllText(report, JsonSerializer.Serialize(new { version = "0.11.0", success, checks }, new JsonSerializerOptions { WriteIndented = true })); return success ? 0 : 1;
    }
}
