using System.Text.Json;
using System.Diagnostics;

namespace SwirStreamAI;

public static class StartupResourceTests
{
    public static int Run(string report)
    {
        var checks = new List<object>(); bool success = true;
        void Check(string name, bool ok) { checks.Add(new { name, ok }); success &= ok; }
        try
        {
            using var release = new ManualResetEventSlim();
            SwirPlayer? created = null;
            using var pending = new PlayerStartup(() => { release.Wait(); return created = new SwirPlayer(IntPtr.Zero, true); });
            pending.Dispose(); release.Set();
            bool cancelled = false;
            try { pending.Ready.GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
            Check("Closing during native startup cancels result and releases native player", cancelled && created?.State == 0);
            using var ready = new PlayerStartup(() => new SwirPlayer(IntPtr.Zero, true));
            var instance = ready.Ready.GetAwaiter().GetResult();
            instance.Play(Path.Combine(AppContext.BaseDirectory, "assets", "local-test.mp4"));
            for (int i = 0; i < 60 && instance.Stats.VideoProgressSamples == 0; i++) Thread.Sleep(100);
            Check("Background-created native player decodes video", instance.Stats.VideoProgressSamples > 0);
            Check("Native graphics runtime remains mapped during headless verification", Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase)));
            ready.Dispose(); ready.Dispose(); Check("Closing after initialization releases player exactly once", instance.State == 0);
            using var failed = new PlayerStartup(() => throw new IOException("Missing test engine"));
            bool propagated = false; try { failed.Ready.GetAwaiter().GetResult(); } catch (IOException) { propagated = true; }
            Check("Native initialization error propagates without hanging", propagated);
            using (var display = new SwirPlayer(IntPtr.Zero)) { }
            Check("Display runtime remains mapped after native player disposal", Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) { checks.Add(new { name = "Unexpected error", ok = false, detail = ex.ToString() }); success = false; }
        File.WriteAllText(report, JsonSerializer.Serialize(new { success, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return success ? 0 : 1;
    }
}
