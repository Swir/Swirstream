using System.Text.Json;

namespace SwirStreamAI;

public static class PlaybackProgressTests
{
    static SwirMediaStats Stats(int video, int audio, long time, bool selected = true, bool videoReady = true)
        => new(videoReady, true, videoReady ? 960 : 0, videoReady ? 540 : 0, time, video, audio, 0, false, selected);
    public static int Run(string report)
    {
        var checks = new List<object>(); bool success = true;
        void Check(string name, bool ok) { checks.Add(new { name, ok }); success &= ok; }
        var gate = new PlaybackProgress(); PlaybackProgressState observed = default;
        for (int i = 0; i < 100; i++) observed = gate.Observe(3, Stats(i, 0, i * 400), "TV", true, i * 400);
        Check("Encrypted video timeline without selected audio never confirms or advances learning", !observed.Confirmed && !observed.Advancing);
        gate.Reset();
        for (int i = 0; i < 25; i++) observed = gate.Observe(3, Stats(i, i, i * 400), "TV", true, i * 400);
        Check("DRM clear-lead-like early AV does not yet confirm", !observed.Confirmed && observed.Advancing);
        for (int i = 25; i < 36; i++) observed = gate.Observe(3, Stats(i, i, i * 400), "TV", true, i * 400);
        Check("Sustained encrypted AV eventually confirms", observed.Confirmed && observed.Advancing);
        for (int i = 36; i < 50; i++) observed = gate.Observe(3, Stats(35, i, i * 400), "TV", true, i * 400);
        Check("Continuing audio with frozen TV video cannot accrue watching", !observed.Confirmed && !observed.Advancing);
        gate.Reset();
        for (int i = 0; i < 10; i++) observed = gate.Observe(3, Stats(i, i, i * 400), "TV", false, i * 400);
        Check("Plain black or static video is accepted based on actual track timestamps, not pixels", observed.Confirmed && observed.Advancing);
        for (int i = 10; i < 30; i++) observed = gate.Observe(3, Stats(i, 9, i * 400), "TV", false, i * 400);
        Check("Frozen selected audio cannot keep TV confirmed", !observed.Confirmed && !observed.Advancing);
        gate.Reset();
        for (int i = 0; i < 10; i++) observed = gate.Observe(3, Stats(i, 0, i * 400, false), "TV", false, i * 400);
        Check("Video-only TV has no fabricated audio requirement", observed.Confirmed && observed.Advancing);
        gate.Reset();
        for (int i = 0; i < 10; i++) observed = gate.Observe(3, Stats(0, i, i * 400, true, false), "Radio", false, i * 400);
        Check("Radio confirms actual audio without video", observed.Confirmed && observed.Advancing);
        gate.Reset();
        for (int i = 0; i < 25; i++) observed = gate.Observe(3, Stats(i, i, i * 400), "TV", true, i * 400);
        for (int i = 25; i < 125; i++) observed = gate.Observe(4, Stats(i, i, i * 400), "TV", true, i * 400);
        Check("Pause never confirms even if a supplied timeline changes", !observed.Confirmed && !observed.Advancing);
        observed = gate.Observe(3, Stats(125, 125, 50000), "TV", true, 50000);
        Check("Wall-clock pause cannot satisfy the encrypted playback duration", !observed.Confirmed);
        Check("Manual pause cannot consume the UI startup deadline", gate.StartupElapsedMs < 15000);
        for (int i = 126; i < 140; i++) observed = gate.Observe(3, Stats(i, i, i * 400), "TV", true, i * 400);
        Check("Playback resumes after a normal pause", observed.Confirmed && observed.Advancing);
        observed = gate.Observe(2, Stats(141, 141, 56400), "TV", true, 56400);
        Check("Buffering never confirms or learns", !observed.Confirmed && !observed.Advancing);
        observed = gate.Observe(7, Stats(142, 142, 56800), "TV", true, 56800);
        Check("Runtime error overrides earlier positive counters", !observed.Confirmed && !observed.Advancing);
        gate.Reset();
        observed = gate.Observe(3, Stats(0, 0, 0), "TV", true, 57200);
        Check("New playback resets previous confirmation", !observed.Confirmed && !observed.Advancing);
        gate.Reset();
        for (int i = 0; i < 36; i++) observed = gate.Observe(3, Stats(i, 0, i * 400, false), "TV", true, i * 400);
        Check("Encrypted video-only playback can confirm without audio", observed.Confirmed);
        for (int i = 36; i < 40; i++) observed = gate.Observe(3, Stats(i, i - 35, i * 400, true), "TV", true, i * 400);
        Check("Newly selected audio cannot inherit video-only DRM confirmation", !observed.Confirmed && observed.Advancing);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report, JsonSerializer.Serialize(new { success, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return success ? 0 : 1;
    }
}

public sealed partial class MainForm
{
    // Controlled decoder snapshots exercise the real UI/store/learning path;
    // native positive/negative CENC decoding is tested separately.
    public async Task PlaybackProgressUiTest(Action<string, bool, object?> check)
    {
        string original = profiles.Index.ActiveId;
        var profile = profiles.Add("Test potwierdzania " + DateTime.UtcNow.Ticks);
        try
        {
            await SwitchProfile(profile.Id, false); await Stop(); timer.Stop();
            current = new Station("Kontrolna TV", "https://fixture.example/av.mpd", "TV", "news", Country: "PL");
            currentEncrypted = true; historySaved = false; watchedSeconds = 0; learnedWatch = false;
            playbackProgress.Reset(); playbackRequested = DateTime.UtcNow.AddSeconds(-40);
            for (int i = 0; i < 100; i++)
            {
                watchTick = DateTime.UtcNow.AddMilliseconds(-400);
                ObservePlayback(3, new(true, true, 1920, 1080, i * 400, i, 0, 0, true, true), i * 400);
            }
            check("UI rejects fake encrypted video progress without selected audio: no history, positive source learning or watch learning",
                !historySaved && store.Data.History.Count == 0 && searchLearning.ExampleCount == 0 && store.Data.TasteExamples == 0 && watchedSeconds == 0, null);
            playbackProgress.Reset();
            for (int i = 0; i < 25; i++)
            {
                watchTick = DateTime.UtcNow.AddMilliseconds(-400);
                ObservePlayback(3, new(true, true, 1920, 1080, i * 400, i, i, 0, true, true), i * 400);
            }
            check("UI does not save encrypted clear-lead-like early progress", !historySaved && store.Data.History.Count == 0 && watchedSeconds == 0, null);
            for (int i = 25; i < 120; i++)
            {
                watchTick = DateTime.UtcNow.AddMilliseconds(-400);
                ObservePlayback(3, new(true, true, 1920, 1080, i * 400, i, i, 0, true, true), i * 400);
            }
            check("Confirmed sustained AV reaches history and both learning paths", historySaved && store.Data.History.Count == 1 && searchLearning.ExampleCount == 1 && learnedWatch && store.Data.TasteExamples == 1, null);
            learnedWatch = false; watchedSeconds = 0;
            for (int i = 120; i < 220; i++)
            {
                watchTick = DateTime.UtcNow.AddMilliseconds(-400);
                ObservePlayback(3, new(true, true, 1920, 1080, i * 400, 119, i, 0, true, true), i * 400);
            }
            check("Continuing audio cannot train watching after TV video freezes", watchedSeconds == 0 && !learnedWatch && store.Data.TasteExamples == 1, null);
            expectedPlayback = true; fallback.Checked = false;
            TryFallback(SwirPlayer.EncryptedDecodeFailureMessage);
            ObservePlayback(5, default, 90000);
            check("Decoder failure remains visible after stopped playback without exposing a URL", playback.Text == SwirPlayer.EncryptedDecodeFailureMessage && !playback.Text.Contains("fixture.example"), null);
            await Stop();
            check("Explicit Stop clears the previous error", playbackFailure.Length == 0 && playback.Text == "Zatrzymano", null);
        }
        finally { timer.Start(); await SwitchProfile(original, false); }
    }
}
