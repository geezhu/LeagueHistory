using System.Diagnostics;
using League.Services;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace League.UIHelpers;

public sealed record LolInstallation(string Root, string? OfficialPath, string? WeGamePath);

public class LOLHelper
{
    private readonly string _configPath;
    private readonly object _detectionLock = new();
    private Task<LolInstallation?>? _detection;
    private DateTime _detectedAt;
    public static LOLHelper Shared { get; } = new();

    public LOLHelper(string? configPath = null) =>
        _configPath = configPath ?? Path.Combine(AppContext.BaseDirectory, "LOLPathConfig.json");

    public static LolInstallation? ResolveInstallation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var directory = new DirectoryInfo(File.Exists(path) ? Path.GetDirectoryName(path)! : path);
            for (int i = 0; directory != null && i < 4; i++, directory = directory.Parent)
            {
                var root = directory.FullName;
                if (
                    !File.Exists(Path.Combine(root, "LeagueClient", "LeagueClient.exe"))
                    || !File.Exists(Path.Combine(root, "LeagueClient", "LeagueClientUx.exe"))
                )
                {
                    continue;
                }

                string? Find(params string[] relatives) =>
                    relatives.Select(p => Path.Combine(root, p)).FirstOrDefault(File.Exists);
                var official = Find(@"Launcher\startup_runner.exe", @"Launcher\Client.exe", @"TCLS\Client.exe");
                var wegame = Find(@"WeGameLauncher\launcher.exe", @"Launcher\WeGameLauncher\launcher.exe");
                if (official != null || wegame != null)
                {
                    return new(root, official, wegame);
                }
            }
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "LOL directory validation", ex);
        }
        return null;
    }

    public LolInstallation? ReadSavedInstallation()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                return null;
            }

            var config = JObject.Parse(File.ReadAllText(_configPath));
            foreach (var field in new[] { "InstallRoot", "OfficialPath", "WeGamePath", "LastLOLPath" })
            {
                string? path = config[field]?.ToString();
                var installation = ResolveInstallation(path);
                if (installation == null)
                {
                    continue;
                }

                if (field != "InstallRoot" && !IsLauncher(installation, path))
                {
                    continue;
                }

                return installation;
            }
        }
        catch (Exception ex)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "LOL path config ignored", ex);
        }
        return null;
    }

    private static bool IsLauncher(LolInstallation installation, string? path) =>
        path != null
        && (
            string.Equals(path, installation.OfficialPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, installation.WeGamePath, StringComparison.OrdinalIgnoreCase)
        );

    public LolInstallation SaveInstallation(string path)
    {
        var installation =
            ResolveInstallation(path)
            ?? throw new InvalidOperationException("所选目录不是有效的英雄联盟安装目录，或缺少启动器。");
        var json = new JObject
        {
            ["InstallRoot"] = installation.Root,
            ["OfficialPath"] = installation.OfficialPath,
            ["WeGamePath"] = installation.WeGamePath,
        };
        var temporary = _configPath + ".tmp";
        File.WriteAllText(temporary, json.ToString());
        File.Move(temporary, _configPath, true);
        lock (_detectionLock)
        {
            _detection = Task.FromResult<LolInstallation?>(installation);
            _detectedAt = DateTime.UtcNow;
        }
        return installation;
    }

    public Task<LolInstallation?> DetectAsync()
    {
        lock (_detectionLock)
        {
            if (
                _detection == null
                || (_detection.IsCompleted && DateTime.UtcNow - _detectedAt > TimeSpan.FromSeconds(30))
            )
            {
                _detectedAt = DateTime.UtcNow;
                _detection = Task.Run(Detect);
            }
            return _detection;
        }
    }

    private LolInstallation? Detect()
    {
        var processes = Process.GetProcessesByName("LeagueClientUx");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (ResolveInstallation(process.MainModule?.FileName) is { } running)
                    {
                        return running;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Shared.Warn(LogCategory.Ui, "LOL process path", ex);
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
        if (ReadSavedInstallation() is { } saved)
        {
            return saved;
        }

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                foreach (var key in new[] { @"SOFTWARE\Tencent\LOL", @"SOFTWARE\Tencent\英雄联盟" })
                {
                    try
                    {
                        using var registry = RegistryKey.OpenBaseKey(hive, view);
                        using var entry = registry.OpenSubKey(key);
                        foreach (var field in new[] { "Path", "InstallPath", "InstallLocation" })
                        {
                            if (ResolveInstallation(entry?.GetValue(field)?.ToString()) is { } installation)
                            {
                                return installation;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Shared.Warn(LogCategory.Ui, "LOL registry", ex);
                    }
                }
            }
        }

        return null;
    }

    public void StartValidatedLauncher(string path)
    {
        var installation = ResolveInstallation(path);
        if (installation == null || !IsLauncher(installation, path) || !File.Exists(path))
        {
            throw new InvalidOperationException("英雄联盟启动路径已失效，请重新选择安装目录。");
        }

        Process.Start(
            new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) }
        );
    }
}
