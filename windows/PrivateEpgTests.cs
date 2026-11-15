using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwirStreamAI;

public static class PrivateEpgTests
{
    const string Secret = "FIXTURE-ONLY-EPG-TOKEN-090";
    const string Address = "https://epg.example/guide.xml?username=fixture&password=" + Secret;
    record Result(string Name, bool Passed, string Detail);
    static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static string Text(string path) => Encoding.UTF8.GetString(File.ReadAllBytes(path));
    static string CachePath(string folder, string url, string suffix) => Path.Combine(folder, "epg-cache", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("PL" + url))) + suffix);
    static void NoPlainSecret(string folder)
    {
        Assert(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).All(path => !Text(path).Contains(Secret, StringComparison.Ordinal)), "Fixture credential found in a persisted file.");
    }
    public static async Task<int> Run(string report)
    {
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!, "private-epg-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var results = new List<Result>(); int index = 0;
        string Folder() { string path = Path.Combine(root, (++index).ToString("D3")); Directory.CreateDirectory(path); return path; }
        async Task Check(string name, Func<Task> test)
        {
            try { await test(); results.Add(new(name, true, "Passed")); }
            catch (Exception ex) { results.Add(new(name, false, ex.GetType().Name + ": " + (ex.Message.Contains(Secret) ? "Fixture failure included a protected value." : ex.Message))); }
        }
        var now = DateTimeOffset.UtcNow;
        byte[] xml = Encoding.UTF8.GetBytes(EpgTests.Fixture(now));
        var station = new Station("TVN", "https://media.example/tvn.m3u8", "TV", Country: "PL", ChannelId: "TVN.pl");
        Task<(byte[], DateTimeOffset?)> Fetch(string url, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult<(byte[], DateTimeOffset?)>((xml, now)); }
        Task<(byte[], DateTimeOffset?)> Offline(string url, CancellationToken ct) => throw new IOException("Failure for " + url + " / " + Secret);
        void Legacy(string folder) => File.WriteAllText(Path.Combine(folder, "epg-settings.json"), JsonSerializer.Serialize(new EpgSettings { Sources = new() { ["PL"] = Address }, Matches = new() { [EpgService.MappingKey(station)] = "TVN.pl" } }));

        await Check("legacy_credentials_migrate_on_load_and_public_settings_keep_mapping", () =>
        {
            string folder = Folder(); Legacy(folder); var service = new EpgService(folder, Fetch);
            Assert(service.Settings.Sources["PL"] == Address, "Migrated source is unavailable in memory.");
            using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "epg-settings.json")));
            Assert(!settings.RootElement.TryGetProperty("Sources", out _) && settings.RootElement.GetProperty("Templates").GetArrayLength() == 2 && service.Settings.Matches[EpgService.MappingKey(station)] == "TVN.pl", "Public settings or mapping changed.");
            Assert(File.Exists(Path.Combine(folder, "epg-private-sources.dpapi")), "Protected sources were not created."); NoPlainSecret(folder); return Task.CompletedTask;
        });
        await Check("settings_restart_preserves_full_query_credentials_and_local_file_path", () =>
        {
            string folder = Folder(); var service = new EpgService(folder, Fetch);
            service.Settings.Sources["PL"] = Address; service.Settings.Sources["DE"] = Path.Combine(folder, Secret + ".xml"); service.SaveSettings();
            var restarted = new EpgService(folder, Fetch);
            Assert(restarted.Settings.Sources["PL"] == Address && restarted.Settings.Sources["DE"].EndsWith(Secret + ".xml"), "Private source was truncated or omitted."); NoPlainSecret(folder); return Task.CompletedTask;
        });
        await Check("legacy_migration_write_failure_keeps_original_and_retry_recovers", () =>
        {
            string folder = Folder(); Legacy(folder); string settings = Path.Combine(folder, "epg-settings.json"), vault = Path.Combine(folder, "epg-private-sources.dpapi"); byte[] original = File.ReadAllBytes(settings);
            Directory.CreateDirectory(vault); var service = new EpgService(folder, Fetch);
            Assert(original.SequenceEqual(File.ReadAllBytes(settings)) && service.Settings.Sources["PL"] == Address, "Failed migration changed the original source.");
            Assert(service.Notice.Contains("Oryginalny zapis") && !service.Notice.Contains(Secret), "Failed migration has no safe diagnostic.");
            Directory.Delete(vault); service.SaveSettings();
            Assert(new EpgService(folder, Fetch).Settings.Sources["PL"] == Address, "Retry did not recover the source."); NoPlainSecret(folder); return Task.CompletedTask;
        });
        await Check("protected_settings_recover_backup_without_overwriting_it_with_corruption", () =>
        {
            string folder = Folder(); var service = new EpgService(folder, Fetch); service.Settings.Sources["PL"] = Address; service.SaveSettings(); service.SaveSettings();
            string vault = Path.Combine(folder, "epg-private-sources.dpapi"); byte[] backup = File.ReadAllBytes(vault + ".bak"); File.WriteAllBytes(vault, [1, 2, 3]);
            var recovered = new EpgService(folder, Fetch); Assert(recovered.Settings.Sources["PL"] == Address && recovered.Notice.Contains("kopię"), "Protected backup did not recover.");
            recovered.SaveSettings(); Assert(backup.SequenceEqual(File.ReadAllBytes(vault + ".bak")), "Good backup was replaced by corrupted bytes."); NoPlainSecret(folder); return Task.CompletedTask;
        });
        await Check("unreadable_private_settings_are_not_silently_overwritten", () =>
        {
            string folder = Folder(); var service = new EpgService(folder, Fetch); service.Settings.Sources["PL"] = Address; service.SaveSettings(); string vault = Path.Combine(folder, "epg-private-sources.dpapi"); File.WriteAllBytes(vault, [1, 2, 3]);
            var locked = new EpgService(folder, Fetch); bool rejected = false; try { locked.SaveSettings(); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && File.ReadAllBytes(vault).SequenceEqual(new byte[] { 1, 2, 3 }) && !locked.Notice.Contains(Secret), "Unreadable protected data was overwritten."); return Task.CompletedTask;
        });
        await Check("removed_custom_source_stays_removed_after_restart", () =>
        {
            string folder = Folder(); var service = new EpgService(folder, Fetch); service.Settings.Sources["PL"] = Address; service.SaveSettings(); service.Settings.Sources.Clear(); service.SaveSettings();
            Assert(new EpgService(folder, Fetch).Settings.Sources.Count == 0, "Deleted source reappeared from the backup."); NoPlainSecret(folder); return Task.CompletedTask;
        });
        await Check("interrupted_public_commit_does_not_resurrect_removed_legacy_source", () =>
        {
            string folder = Folder(); var service = new EpgService(folder, Fetch); service.Settings.Sources["PL"] = Address; service.SaveSettings(); service.Settings.Sources.Clear(); service.SaveSettings(); Legacy(folder);
            Assert(new EpgService(folder, Fetch).Settings.Sources.Count == 0, "Old plaintext source overrode the newer protected deletion."); NoPlainSecret(folder); return Task.CompletedTask;
        });
        await Check("private_fetch_keeps_full_url_but_feed_and_notice_use_safe_label", async () =>
        {
            string folder = Folder(); string requested = ""; var service = new EpgService(folder, (url, ct) => { requested = url; return Fetch(url, ct); }); service.Settings.Sources["PL"] = Address; service.SaveSettings();
            await service.Refresh(["PL"], true, CancellationToken.None);
            Assert(requested == Address && service.Get(station).Now != null, "Private source did not fetch or match.");
            Assert(!service.Feeds.Single().Source.Contains("https:") && !service.Notice.Contains(Address) && !service.Notice.Contains(Secret), "Private source appeared in display metadata.");
            Assert(File.Exists(CachePath(folder, Address, ".dpapi")) && !File.Exists(CachePath(folder, Address, ".json")), "Custom cache was not protected."); NoPlainSecret(folder);
        });
        await Check("private_cache_restart_avoids_fetch_and_preserves_original_freshness", async () =>
        {
            string folder = Folder(); var service = new EpgService(folder, Fetch); service.Settings.Sources["PL"] = Address; service.SaveSettings(); await service.Refresh(["PL"], true, CancellationToken.None); var first = service.Feeds.Single(); int requests = 0;
            var restarted = new EpgService(folder, (url, ct) => { requests++; return Offline(url, ct); }); await restarted.Refresh(["PL"], false, CancellationToken.None);
            Assert(requests == 0 && restarted.Get(station).Now != null && restarted.Feeds.Single().Downloaded == first.Downloaded && restarted.Feeds.Single().Modified == first.Modified, "Private cache expiry or loading changed."); NoPlainSecret(folder);
        });
        await Check("private_errors_never_echo_url_or_exception_credentials", async () =>
        {
            string folder = Folder(); var service = new EpgService(folder, Offline); service.Settings.Sources["PL"] = Address; service.SaveSettings(); await service.Refresh(["PL"], true, CancellationToken.None);
            Assert(service.Get(station).Now == null && service.Notice.Contains("Brak aktualnego EPG") && !service.Notice.Contains(Secret) && !service.Notice.Contains(Address), "Private error leaked its request."); NoPlainSecret(folder);
        });
        await Check("private_offline_fallback_preserves_schedule_without_leaking_failure", async () =>
        {
            string folder = Folder(); var service = new EpgService(folder, Fetch); service.Settings.Sources["PL"] = Address; service.SaveSettings(); await service.Refresh(["PL"], true, CancellationToken.None);
            var offline = new EpgService(folder, Offline); await offline.Refresh(["PL"], true, CancellationToken.None);
            Assert(offline.Get(station).Now != null && offline.Notice.Contains("OFFLINE") && !offline.Notice.Contains(Secret), "Protected offline fallback failed."); NoPlainSecret(folder);
        });
        await Check("legacy_plain_cache_is_encrypted_on_load_without_refreshing_dates", () =>
        {
            string folder = Folder(); Legacy(folder); Directory.CreateDirectory(Path.Combine(folder, "epg-cache")); var feed = EpgService.Parse(xml, "PL", Address, now.AddMinutes(-10), now.AddMinutes(-5)); File.WriteAllText(CachePath(folder, Address, ".json"), JsonSerializer.Serialize(feed));
            _ = new EpgService(folder, Fetch); var stored = PrivateData.Read<EpgFeed>(CachePath(folder, Address, ".dpapi"), "epg-custom-cache-v1");
            Assert(!File.Exists(CachePath(folder, Address, ".json")) && stored.Downloaded == feed.Downloaded && stored.Modified == feed.Modified && stored.Channels.Count == feed.Channels.Count && !stored.Source.Contains(Secret), "Legacy cache migration lost dates or left the URL visible."); NoPlainSecret(folder); return Task.CompletedTask;
        });
        await Check("legacy_cache_failed_protected_write_keeps_original", () =>
        {
            string folder = Folder(); Legacy(folder); Directory.CreateDirectory(Path.Combine(folder, "epg-cache")); var feed = EpgService.Parse(xml, "PL", Address, now, now); string old = CachePath(folder, Address, ".json"); File.WriteAllText(old, JsonSerializer.Serialize(feed)); byte[] original = File.ReadAllBytes(old); Directory.CreateDirectory(CachePath(folder, Address, ".dpapi"));
            var service = new EpgService(folder, Fetch);
            Assert(original.SequenceEqual(File.ReadAllBytes(old)) && service.Notice.Contains("Ochrona cache") && !service.Notice.Contains(Secret), "Failed cache migration lost the original or leaked its URL."); return Task.CompletedTask;
        });
        await Check("expired_private_cache_cannot_become_fresh_by_reopening", async () =>
        {
            string folder = Folder(); var service = new EpgService(folder, Fetch); service.Settings.Sources["PL"] = Address; service.SaveSettings(); await service.Refresh(["PL"], true, CancellationToken.None);
            string path = CachePath(folder, Address, ".dpapi"); var cached = PrivateData.Read<EpgFeed>(path, "epg-custom-cache-v1"); PrivateData.Write(path, cached with { Downloaded = now.AddHours(-49) }, "epg-custom-cache-v1", backup: false);
            var restarted = new EpgService(folder, Offline); await restarted.Refresh(["PL"], false, CancellationToken.None); Assert(restarted.Get(station).Now == null && !restarted.Feeds.Any(), "Expired private cache was reused."); NoPlainSecret(folder);
        });
        await Check("private_settings_and_cache_use_distinct_dpapi_purposes", async () =>
        {
            string folder = Folder(); var service = new EpgService(folder, Fetch); service.Settings.Sources["PL"] = Address; service.SaveSettings(); await service.Refresh(["PL"], true, CancellationToken.None); bool rejected = false;
            try { _ = PrivateData.Unprotect(File.ReadAllBytes(CachePath(folder, Address, ".dpapi")), "epg-sources-v1"); } catch (CryptographicException) { rejected = true; }
            Assert(rejected, "Cache and settings protections are interchangeable.");
        });
        await Check("public_epg_retains_public_url_plain_cache_and_provider_fallback", async () =>
        {
            string folder = Folder(); var service = new EpgService(folder, (url, ct) => url.Contains("iptv-epg") ? throw new IOException("Public fixture unavailable") : Fetch(url, ct)); await service.Refresh(["PL"], true, CancellationToken.None);
            Assert(service.Get(station).Now != null && service.Feeds.Single().Source == "https://free-epg.de/api/epg/pl.xml.gz" && service.Notice.Contains("zapasowego") && Directory.EnumerateFiles(Path.Combine(folder, "epg-cache"), "*.json").Count() == 1, "Public EPG behavior changed.");
        });
        await Check("shared_epg_settings_follow_data_folder_not_application_profile", () =>
        {
            string shared = Folder(); Directory.CreateDirectory(Path.Combine(shared, "profiles", "first")); Directory.CreateDirectory(Path.Combine(shared, "profiles", "second"));
            var first = new EpgService(shared, Fetch); first.Settings.Sources["PL"] = Address; first.SaveSettings();
            Assert(new EpgService(shared, Fetch).Settings.Sources["PL"] == Address && new EpgService(Folder(), Fetch).Settings.Sources.Count == 0 && !Directory.EnumerateFiles(Path.Combine(shared, "profiles"), "*", SearchOption.AllDirectories).Any(), "Shared EPG scope was confused with a profile vault."); NoPlainSecret(shared); return Task.CompletedTask;
        });
        bool success = results.All(x => x.Passed);
        File.WriteAllText(report, JsonSerializer.Serialize(new { success, fixtureOnly = true, scope = "Custom Sources EPG is shared by profiles of one app data folder and protected for its Windows account. Provider EPG remains in its own profile vault.", checks = results }, new JsonSerializerOptions { WriteIndented = true }));
        return success ? 0 : 1;
    }
}
