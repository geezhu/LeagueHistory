using League.Managers;
using League.Models;
using Newtonsoft.Json;

internal static class LeagueConfigChecks
{
    private sealed record SettingsPaths(string Root, string Shared, string OldExe, string NewExe, string Cwd);

    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new Exception(message);
        }
    }

    public static void Run()
    {
        DefaultSettings();
        string root = Path.Combine(Path.GetTempPath(), "League-Settings-" + Guid.NewGuid().ToString("N"));
        var paths = new SettingsPaths(
            root,
            Path.Combine(root, "user"),
            Path.Combine(root, "old"),
            Path.Combine(root, "new"),
            Path.Combine(root, "launch")
        );
        try
        {
            Directory.CreateDirectory(paths.OldExe);
            Directory.CreateDirectory(paths.Cwd);
            var migrated = MigrateLegacySettings(paths);
            PreserveUpgradeSettings(paths, migrated);
            WorkingDirectoryFallbackAndInvalidSettings(paths);
            Console.WriteLine("Settings migration and upgrade persistence checks passed.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static void DefaultSettings()
    {
        Check(
            new LeagueConfig().ChampionMasteryCacheMinutes == 30,
            "Old configurations default to a 30-minute mastery cache"
        );
        Check(
            JsonConvert.DeserializeObject<LeagueConfig>("{}")!.VoiceMicrophoneId == "",
            "Old configurations follow the Windows default input"
        );
        Check(
            JsonConvert
                .DeserializeObject<LeagueConfig>("{\"ChampionMasteryCacheMinutes\":120}")!
                .ChampionMasteryCacheMinutes == 120,
            "Mastery expiry persists with user settings"
        );
    }

    private static LeagueConfig MigrateLegacySettings(SettingsPaths paths)
    {
        var store = new LeagueConfigStore(paths.Shared, paths.OldExe, paths.Cwd);
        Check(
            store.Load().LoadingOverlayBackend == "Desktop" && !File.Exists(store.ConfigPath),
            "Missing settings preserve defaults without overwriting future migration"
        );
        string legacy =
            "{\"LoadingOverlayBackend\":\"GameBar\",\"LoadingOverlayHotkey\":\"F8\",\"LoadingOverlayHotkeyVersion\":1,\"AramMetaCacheMinutes\":720,\"CustomSendContent\":\"保留我的设置\",\"futureSetting\":true}";
        File.WriteAllText(Path.Combine(paths.OldExe, "LeagueConfig.json"), legacy);
        File.WriteAllText(Path.Combine(paths.Cwd, "LeagueConfig.json"), "{\"LoadingOverlayBackend\":\"Desktop\"}");
        var migrated = store.Load();
        Check(
            migrated.LoadingOverlayBackend == "GameBar" && migrated.AramMetaCacheMinutes == 720,
            "Legacy shortcut fields do not break migration of backend and cache duration"
        );
        Check(
            !JsonConvert.SerializeObject(migrated).Contains("LoadingOverlayHotkey"),
            "Removed shortcut settings are ignored and no longer saved"
        );
        Check(
            File.ReadAllText(store.ConfigPath) == legacy
                && File.ReadAllText(Path.Combine(paths.OldExe, "LeagueConfig.json")) == legacy,
            "First migration preserves all JSON fields and original file"
        );
        return migrated;
    }

    private static void PreserveUpgradeSettings(SettingsPaths paths, LeagueConfig migrated)
    {
        var store = new LeagueConfigStore(paths.Shared, paths.OldExe, paths.Cwd);
        var upgraded = new LeagueConfigStore(paths.Shared, paths.NewExe, paths.Cwd);
        Check(
            upgraded.Load().LoadingOverlayBackend == "GameBar" && upgraded.Load().CustomSendContent == "保留我的设置",
            "Changing executable and launch directories keeps the same preferences"
        );
        migrated.LoadingOverlayBackend = "Desktop";
        migrated.VoiceMicrophoneId = "test-capture-endpoint";
        upgraded.Save(migrated);
        Check(
            store.Load().LoadingOverlayBackend == "Desktop",
            "Old per-folder settings cannot overwrite a later saved selection"
        );
        Check(
            upgraded.Load().VoiceMicrophoneId == "test-capture-endpoint",
            "Microphone selection persists across executable upgrades"
        );
        Check(!Directory.EnumerateFiles(paths.Shared, "*.tmp").Any(), "Completed saves leave no partial files");
    }

    private static void WorkingDirectoryFallbackAndInvalidSettings(SettingsPaths paths)
    {
        var store = new LeagueConfigStore(paths.Shared, paths.OldExe, paths.Cwd);
        var fallback = new LeagueConfigStore(Path.Combine(paths.Root, "fallback"), paths.NewExe, paths.Cwd);
        Check(
            fallback.Load().LoadingOverlayBackend == "Desktop" && File.Exists(fallback.ConfigPath),
            "Legacy working-directory settings still migrate when no executable settings exist"
        );
        File.WriteAllText(store.ConfigPath, "invalid json");
        bool rejected = false;
        try
        {
            store.Load();
        }
        catch (JsonException)
        {
            rejected = true;
        }
        Check(
            rejected && File.ReadAllText(store.ConfigPath) == "invalid json",
            "Invalid shared settings are not silently replaced by stale defaults"
        );
    }
}
