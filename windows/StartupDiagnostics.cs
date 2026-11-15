using System.Diagnostics;
using System.Text.Json;

namespace SwirStreamAI;

// Opt-in startup measurements. No files or timers in normal use.
public static class StartupDiagnostics
{
    static readonly Stopwatch clock = new();
    static readonly Dictionary<string, double> marks = [];
    static double processOffset;
    static string phaseFile = "";
    public static bool Enabled { get; private set; }
    public static void Begin(bool enabled, string report)
    {
        Enabled = enabled;
        if (!enabled) return;
        phaseFile = report + ".phases.json";
        processOffset = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalMilliseconds;
        clock.Start(); Mark("mainMs");
    }
    public static void Mark(string name)
    {
        if (Enabled) lock (marks) { marks.TryAdd(name, Math.Round(processOffset + clock.Elapsed.TotalMilliseconds, 1)); File.WriteAllText(phaseFile, JsonSerializer.Serialize(marks)); }
    }
    public static void Attach(MainForm form, string report, string action = "")
    {
        if (!Enabled) return;
        form.Paint += (_, _) => Mark("firstPaintMs");
        var heartbeat = new System.Windows.Forms.Timer { Interval = 50 };
        int ticks = 0, ticksWhileLoading = 0; long last = clock.ElapsedMilliseconds, maxGap = 0, readyAt = -1;
        heartbeat.Tick += (_, _) =>
        {
            long now = clock.ElapsedMilliseconds; maxGap = Math.Max(maxGap, now - last); last = now; ticks++;
            bool ready; lock (marks) ready = marks.ContainsKey("engineReadyMs") || marks.ContainsKey("engineFailedMs");
            if (!ready) ticksWhileLoading++;
            if (ticks == 1) { Mark("firstMessageMs"); if (action is "play" or "stop") form.PlayDemo(); }
            if (ticks == 3 && action == "stop") _ = form.TestControl("stop");
            if (ready && readyAt < 0) readyAt = now;
            bool played = form.Diagnostics.Stats.VideoProgressSamples > 0;
            bool actionOk = action == "play" ? played : action == "stop" ? !played && form.Diagnostics.History == 0 : true;
            bool finished = ready && ticks >= 12 && (action != "play" || played || now - readyAt > 7000) && (action != "stop" || now - readyAt > 1000);
            if (!finished && now < 20000) return;
            heartbeat.Stop(); heartbeat.Dispose(); Mark("testFinishedMs");
            lock (marks) File.WriteAllText(report, JsonSerializer.Serialize(new { success = ready && marks.ContainsKey("firstMessageMs") && marks.ContainsKey("engineReadyMs") && actionOk, marks, heartbeatTicks = ticks, ticksWhileLoading, maxHeartbeatGapMs = maxGap, action, actionOk, videoProgressSamples = form.Diagnostics.Stats.VideoProgressSamples, note = "Times measured from process creation. Hidden test windows may not receive Paint; firstMessageMs measures a responsive UI message loop." }, new JsonSerializerOptions { WriteIndented = true }));
            form.Close();
        };
        form.Shown += (_, _) => { last = clock.ElapsedMilliseconds; heartbeat.Start(); };
    }
}
