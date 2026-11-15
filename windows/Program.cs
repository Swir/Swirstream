using System.Text.Json;

namespace SwirStreamAI;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        string Arg(string name, string fallback) { int idx = Array.IndexOf(args, name); return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : fallback; }
        string report = Arg("--report", Path.Combine(AppContext.BaseDirectory, "test-report.json"));
        StartupDiagnostics.Begin(args.Contains("--startup-test"), report);
        try
        {
            if (args.Contains("--probe")) return StreamProbe.Worker(Arg("--probe", ""), report);
            if (args.Contains("--provider-test")) return PrivateProviderTests.Run(report, args.Contains("--network"));
            if (args.Contains("--portal-protocol-test")) return PortalProtocolTests.Run(report);
            if (args.Contains("--portal-catalog-test")) return PortalCatalogTests.Run(report);
            if (args.Contains("--source-explorer-test")) return SourceExplorerTests.Run(report,args.Contains("--network"));
            if (args.Contains("--xtream-link-test")) return XtreamLinkTests.Run(report);
            if (args.Contains("--public-playlist-test")) return PublicPlaylistDiscoveryTests.Run(report,args.Contains("--network"));
            if (args.Contains("--public-provider-test")) return PublicProviderHandoffTests.Run(report);
            if (args.Contains("--provider-player-test")) return PrivateProviderPlayerTests.Run(report);
            if (args.Contains("--private-epg-test")) return PrivateEpgTests.Run(report).GetAwaiter().GetResult();
            if (args.Contains("--web-test")) return WebDiscoveryTests.Run(report, args.Contains("--network"));
            if (args.Contains("--deep-web-test")) return DeepWebTests.Run(report);
            if (args.Contains("--deep-identity-test")) return DeepIdentityTests.Run(report);
            if (args.Contains("--local-search-test")) return LocalSearchTests.Run(report, args.Contains("--network"));
            if (args.Contains("--playlist-test")) return PlaylistTests.Run(report, args.Contains("--network"));
            if (args.Contains("--search-learning-test")) return SearchLearningTests.Run(report);
            if (args.Contains("--player-test")) return PlayerEngineTests.Run(report);
            if (args.Contains("--playback-progress-test")) return PlaybackProgressTests.Run(report);
            if (args.Contains("--encrypted-guard-test")) return EncryptedPlaybackGuardTests.Run(report);
            if (args.Contains("--provider-failure-test")) return ProviderFailureTests.Run(report);
            if (args.Contains("--discovery-test")) return DiscoveryTests.Run(report, args.Contains("--network"));
            if (args.Contains("--train"))
            {
                string input = Arg("--input", Path.Combine(AppContext.BaseDirectory, "model", "training.json"));
                string output = Arg("--output", Path.Combine(AppContext.BaseDirectory, "model", "intentnet.json"));
                var data = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(input))!;
                var trained = IntentNet.Train(data); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                File.WriteAllText(output, JsonSerializer.Serialize(trained)); return 0;
            }
            if (args.Contains("--self-test")) return SelfTests.Run(IntentNet.Load(Path.Combine(AppContext.BaseDirectory, "model", "intentnet.json")), report, args.Contains("--network"));
            if (args.Contains("--personal-test")) return PersonalizationTests.Run(report, args.Contains("--network"));
            if (args.Contains("--startup-resource-test")) return StartupResourceTests.Run(report);
            if (args.Contains("--deep-test")) return DeepSearchTests.Run(report, args.Contains("--network"));
            if (args.Contains("--epg-test")) return EpgTests.Run(report, args.Contains("--network"));
            var model = IntentNet.Load(Path.Combine(AppContext.BaseDirectory, "model", "tastenet.json"), 10);
            StartupDiagnostics.Mark("modelLoadedMs");
            ApplicationConfiguration.Initialize();
            if (args.Contains("--gate-test")) return StartupGateTests.Run(report,model);
            string dataPath = Arg("--data-dir", Path.Combine(AppContext.BaseDirectory, "data"));
            if (!args.Contains("--gui-test") && !args.Contains("--no-auto-search") && !args.Contains("--startup-test"))
            {
                using var gate = new StartupGate(() => new MainForm(dataPath, model, autoDiscover:false));
                Application.Run(gate); return Environment.ExitCode;
            }
            int startupDelay = args.Contains("--startup-test") ? Math.Clamp(int.Parse(Arg("--startup-delay-ms", "0")), 0, 10000) : 0;
            var form = new MainForm(dataPath, model, !args.Contains("--gui-test") && !args.Contains("--no-auto-search") && !args.Contains("--startup-test"), startupDelay);
            form.SetAutomaticEpg(!args.Contains("--gui-test") && !args.Contains("--startup-test"));
            StartupDiagnostics.Mark("formConstructedMs");
            StartupDiagnostics.Attach(form, report, Arg("--startup-action", ""));
            if (args.Contains("--gui-test"))
            {
                form.Shown += async (_, _) =>
                {
                    var checks = new List<object>(); bool success = true;
                    void Check(string name, bool ok, object? detail = null) { checks.Add(new { name, ok, detail }); success &= ok; }
                    try
                    {
                        if(args.Contains("--provider-ui-only"))
                        {
                            await form.PlayerReady.WaitAsync(TimeSpan.FromSeconds(30));
                            await form.PortalStatusUiTest(Check,Path.ChangeExtension(report,".portal-status.png"));
                            await form.ProviderModulesUiTest(Check,Path.ChangeExtension(report,".modules.png"));
                            await form.PrivateSourcesUiTest(Check,Path.ChangeExtension(report,".sources.png"));
                            await form.LocalSearchUiTest(Check);
                        }
                        else
                        {
                        await form.PlayerReady.WaitAsync(TimeSpan.FromSeconds(30)); await Task.Delay(100); form.PlayDemo();
                        var firstOutput = System.Diagnostics.Stopwatch.StartNew();
                        while (firstOutput.Elapsed < TimeSpan.FromSeconds(15) && (form.Diagnostics.Stats.VideoProgressSamples == 0 || form.Diagnostics.Stats.AudioProgressSamples == 0)) await Task.Delay(200);
                        var d = form.Diagnostics; Check("GUI Swir Player video timeline advances after decoder output", d.Stats.VideoProgressSamples > 0, d.Stats.VideoProgressSamples);
                        Check("GUI Swir Player audio output timeline advances", d.Stats.AudioProgressSamples > 0, d.Stats.AudioProgressSamples);
                        Check("GUI Swir Player has embedded video output", d.Stats.VideoOutputPresent && d.Stats.OutputWidth > 0, new { d.Stats.OutputWidth, d.Stats.OutputHeight });
                        await form.TestControl("pause"); await Task.Delay(500); Check("Player pause", form.Diagnostics.State == 4);
                        await form.TestControl("resume"); await Task.Delay(500); Check("Player resume", form.Diagnostics.State == 3);
                        int vol = form.Diagnostics.Volume; await form.TestControl("quieter"); Check("Player volume", form.Diagnostics.Volume == vol - 10);
                        await form.TestControl("mute"); await Task.Delay(200); Check("Player mute", form.Diagnostics.Muted);
                        await form.TestControl("unmute"); await Task.Delay(200); Check("Player unmute", !form.Diagnostics.Muted);
                        await form.TestControl("volume40"); Check("Volume setting", form.Diagnostics.Volume == 40);
                        await form.TestControl("favorite"); Check("Player favorite", form.Diagnostics.Favorites >= 1);
                        Check("History after decoding", form.Diagnostics.History >= 1);
                        await form.TestControl("favorites");
                        await form.TestControl("stop"); await Task.Delay(500); Check("Player stop", form.Diagnostics.State == 5);
                        await form.DiscoveryUiTest(Path.Combine(AppContext.BaseDirectory, "assets", "local-test.mp4")); Check("Discovery probe + TOP + variants + automatic failover playback", true);
                        await form.DeepSearchUiTest(); Check("UI alias search reaches extra index and selected-channel search preserves identity", true);
                        await form.CatalogUiTest(Check, Path.ChangeExtension(report, ".first-scan.png"));
                        await form.LocalSearchUiTest(Check);
                        await form.EpgUiTest(Check);
                        form.PlaylistUiTest(Check);
                        await form.ProfileUiTest(); Check("Separate profiles and no assistant panel", true);
                        await form.PrivateSourcesUiTest(Check,Path.ChangeExtension(report,".sources.png"));
                        await form.ProviderModulesUiTest(Check,Path.ChangeExtension(report,".modules.png"));
                        await form.SmartImportUiTest(Check,Path.ChangeExtension(report,".smart-import.png"));
                        await form.SourceExplorerUiTest(Check,Path.ChangeExtension(report,".explorer.png"));
                        await form.PlaybackProgressUiTest(Check);
                        await form.PortalStatusUiTest(Check,Path.ChangeExtension(report,".portal-status.png"));
                        form.SavePreview(Arg("--screenshot", Path.ChangeExtension(report, ".png")));
                        }
                    }
                    catch (Exception ex) { Check("Unexpected error", false, ex.ToString()); }
                    File.WriteAllText(report, JsonSerializer.Serialize(new { success, checks }, new JsonSerializerOptions { WriteIndented = true }));
                    Environment.ExitCode = success ? 0 : 1; form.Close();
                };
            }
            Application.Run(form); return Environment.ExitCode;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(report, ex.ToString()); } catch { }
            if (!args.Any(a => a.StartsWith("--"))) MessageBox.Show("Nie udało się uruchomić SwirStream AI.\n" + ex.Message + "\n\nRozpakuj całą paczkę ZIP przed uruchomieniem.", "SwirStream AI", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
