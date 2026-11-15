using System.Text.Json;

namespace SwirStreamAI;

public sealed partial class MainForm
{
    public async Task CatalogUiTest(Action<string, bool, object?> check, string screenshot)
    {
        var oldCatalog = smartCatalog;
        var oldCandidates = candidates; var oldDirectory = knownChannels; var oldCountries = store.Data.SelectedCountries;
        string oldQuery = search.Text, oldScope = catalogScope, oldTopic = store.Data.TopicFilter;
        bool oldVerify = verify.Checked, oldWeb = webSearch.Checked, oldLists = store.Data.SearchPlaylists, oldIndex = store.Data.SearchIndex;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        try
        {
            var cfg = new SourceSettings { TvTemplates = ["https://fixture.example/{path}"], ChannelDirectoryUrls = ["https://fixture.example/channels.json"], StreamDirectoryUrls = ["https://fixture.example/streams.json"] };
            string directory = JsonSerializer.Serialize(Enumerable.Range(0,150).Select(i => new { id = "Stable" + i + ".pl", name = "Kanał " + i, alt_names = new[] { "Alias " + i }, country = "PL", website = "", categories = new[] { "news" } }));
            string streams = JsonSerializer.Serialize(Enumerable.Range(0,150).Select(i => new { channel = "Stable" + i + ".pl", title = "Kanał " + i, url = "https://cdn.example/" + i + ".m3u8", feed = "HD" }));
            async Task<(string, DateTimeOffset?)> Fetch(string url, CancellationToken ct)
            {
                requests++;
                if (url.EndsWith("streams.json")) { reached.TrySetResult(); await release.Task.WaitAsync(ct); return (streams, DateTimeOffset.UtcNow); }
                return (url.EndsWith("channels.json") ? directory : "#EXTM3U\n#EXTINF:-1 tvg-id=\"Stable0.pl@HD\",Kanał 0\nhttps://cdn.example/0.m3u8", DateTimeOffset.UtcNow);
            }
            candidates = []; knownChannels = []; catalogScope = "";
            smartCatalog = new(Path.Combine(dataFolder, "stable-fixture"), cfg, Fetch);
            verify.Checked = false; webSearch.Checked = false; store.Data.SearchPlaylists = false; store.Data.SearchIndex = true;
            store.Data.SelectedCountries = ["PL"]; store.Data.TopicFilter = "all"; kind.SelectedItem = "TV"; search.Text = ""; ShowRecommendations();
            var first = SmartSearch(true, initial: true, wholeCatalog: true);
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check("Initial scan shows wait message and disables search while providers run", scanBusy && scanOverlay.Visible && !search.Enabled && scanTitle.Text.Contains("pierwsze"), scanTitle.Text);
            check("Partial provider results never replace the visible catalog", candidates.Count == 0 && shown.Count == 0, null);
            SavePreview(screenshot);
            release.SetResult(); await first;
            check("Completed initial scan enables search and publishes all channels", !scanBusy && !scanOverlay.Visible && search.Enabled && candidates.Count == 150, candidates.Count);
            var pageOne = shown.Select(s => s.ChannelId).ToHashSet(); await ChangePage(1);
            check("Pagination exposes channels beyond the former TOP 40 limit", pageOne.Count == 100 && shown.Count == 50 && pageOne.Union(shown.Select(s => s.ChannelId)).Count() == 150, pageCaption.Text);
            int before = requests; search.Text = "Alias 149"; await FilterCatalog();
            check("Typing an alias finds a channel without network requests", shown.Count == 1 && shown[0].ChannelId == "Stable149.pl" && requests == before, null);
            check("Partial channel names match without confusing numbered channels", ChannelSearch.Matches("TVP", new("TVP1", "https://example.org/1")) && ChannelSearch.Matches("Pols", new("Polsat News", "https://example.org/news")) && !ChannelSearch.Matches("TVP 1", new("TVP10", "https://example.org/10")), null);
            search.Text = ""; await FilterCatalog();
            check("Clearing the query restores the complete catalog", candidates.Count == 150 && shown.Count == 100, null);
            var order = shown.Select(s => s.Url).ToArray(); var selected = Selected?.Url;
            ranker.Record(shown[0], new(DateTimeOffset.UtcNow, false, 8000)); RefreshBadges();
            check("Failed verification updates badges without removing or reordering rows", order.SequenceEqual(shown.Select(s => s.Url)) && Selected?.Url == selected && results.Items[0].SubItems[1].Text.Contains("nie działa"), null);
            search.Text = "Alias 149"; await SmartSearch(true); search.Text = ""; await FilterCatalog();
            check("Named internet search preserves other catalog channels", candidates.Count == 150 && shown.Count == 100, candidates.Count);

            var cancelReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<(string, DateTimeOffset?)> CancelFetch(string url, CancellationToken ct)
            {
                cancelReached.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); throw new Exception("unreachable");
            }
            smartCatalog = new(Path.Combine(dataFolder, "cancel-fixture"), cfg, CancelFetch);
            var saved = candidates.Select(s => s.Url).ToArray(); var savedShown = shown.Select(s => s.Url).ToArray();
            var cancelled = SmartSearch(true, wholeCatalog: true); await cancelReached.Task; searchCancel!.Cancel(); await cancelled;
            check("Cancelling refresh preserves previous catalog and unlocks UI", !scanBusy && search.Enabled && saved.SequenceEqual(candidates.Select(s => s.Url)) && savedShown.SequenceEqual(shown.Select(s => s.Url)), null);

            smartCatalog = new(Path.Combine(dataFolder, "failure-fixture"), cfg, (u,c) => throw new IOException("Fixture provider unavailable"));
            await SmartSearch(true, wholeCatalog: true);
            check("Provider failures keep the previous database and explain the failure", !scanBusy && saved.SequenceEqual(candidates.Select(s => s.Url)) && status.Text.Contains("zachowana") && feedDetails.Contains("BŁĄD"), status.Text);

            // A slow previous request must never publish after a newer request finishes.
            var late = new TaskCompletionSource<(string, DateTimeOffset?)>(TaskCreationOptions.RunContinuationsAsynchronously);
            store.Data.SearchIndex = false;
            smartCatalog = new(Path.Combine(dataFolder, "late-fixture"), cfg, (u,c) => late.Task);
            var stale = SmartSearch(true, wholeCatalog: true);
            smartCatalog = new(Path.Combine(dataFolder, "newer-fixture"), cfg, Fetch); store.Data.SearchIndex = true;
            await SmartSearch(true, wholeCatalog: true);
            var newest = candidates.Select(s => s.Url).ToArray(); string newestStatus = status.Text;
            late.SetResult(("#EXTM3U\n#EXTINF:-1 tvg-id=\"Wrong.pl\",Wrong\nhttps://cdn.example/wrong.m3u8", DateTimeOffset.UtcNow)); await stale;
            check("Late cancelled requests cannot overwrite a newer catalog or status", !scanBusy && newest.SequenceEqual(candidates.Select(s => s.Url)) && status.Text == newestStatus, null);
        }
        finally
        {
            release.TrySetResult(); searchCancel?.Cancel(); EndScan(searchGeneration);
            smartCatalog = oldCatalog; candidates = oldCandidates; knownChannels = oldDirectory; catalogScope = oldScope;
            verify.Checked = oldVerify; webSearch.Checked = oldWeb; store.Data.SearchPlaylists = oldLists; store.Data.SearchIndex = oldIndex;
            store.Data.SelectedCountries = oldCountries; store.Data.TopicFilter = oldTopic; search.Text = oldQuery; lastQuery = oldQuery; resultPage = 0; countriesButton.Text = CountryCaption();
        }
    }
}
