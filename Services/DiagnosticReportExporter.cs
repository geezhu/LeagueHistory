using System.IO.Compression;
using System.Text;
using League.Infrastructure;
using League.Models;
using League.UIHelpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>导出时在界面线程上取的程序状态；其余信息在后台收集。</summary>
/// <param name="Lcu">已连接时用来读客户端版本和大区；只读这两项，不读账号数据。</param>
public sealed record DiagnosticAppState(
    bool LcuConnected,
    bool InGame,
    string GameBarStatus,
    LeagueConfig? Config,
    HttpClient? Lcu = null,
    string Phase = "未知"
);

/// <summary>要收进报告的一个目录：只取顶层、匹配文件名、最近 7 天改过的文件。</summary>
public sealed record DiagnosticSource(string Directory, string[] Patterns, string ArchiveFolder);

/// <summary>
/// 诊断报告：一个 zip，内含 report.json（版本、环境、程序状态、去敏后的配置、收录的文件列表）和最近 7 天的日志。
/// 每个文本文件写入前都经过 <see cref="DiagnosticRedactor"/>；只保存到用户选择的位置，不会上传。
/// </summary>
public sealed class DiagnosticReportExporter
{
    private readonly DiagnosticRedactor _redactor;
    private readonly IReadOnlyList<DiagnosticSource> _sources;
    private readonly Func<DateTime> _now;

    public DiagnosticReportExporter(
        IReadOnlyList<DiagnosticSource> sources,
        DiagnosticRedactor? redactor = null,
        Func<DateTime>? now = null
    )
    {
        _sources = sources;
        _redactor = redactor ?? DiagnosticRedactor.Shared;
        _now = now ?? (() => DateTime.Now);
    }

    public static DiagnosticReportExporter CreateDefault() =>
        new(
            new List<DiagnosticSource>
            {
                new(LogRetention.LogsDirectory, new[] { "*.log", "*.log.previous", "*.json" }, "logs"),
                new(
                    FindGameBarDirectory(),
                    new[]
                    {
                        "component-error.txt",
                        "component-error.previous.txt",
                        "component-diagnostics.txt",
                        "component-diagnostics.previous.txt",
                        "widget-status.json",
                    },
                    "gamebar"
                ),
                new(new ScheduledShutdownService().DirectoryPath, new[] { "shutdown.log" }, "shutdown"),
            }
        );

    // The Game Bar package keeps its files in its own LocalState; GameBarBridge looks it up the same way.
    private static string FindGameBarDirectory()
    {
        string packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages"
        );
        return Directory.Exists(packages)
            ? Directory
                .EnumerateDirectories(packages, "LeagueHistory.GameBar_*")
                .Select(p => Path.Combine(p, "LocalState"))
                .FirstOrDefault(Directory.Exists)
                ?? ""
            : "";
    }

    /// <summary>写到临时文件再改名，导出中断时不会留下半个 zip。返回收录的文件数。</summary>
    public async Task<int> ExportAsync(string zipPath, DiagnosticAppState state, CancellationToken cancellationToken)
    {
        await AppLog.Shared.FlushAsync();
        string temporary = zipPath + ".tmp";
        try
        {
            int count = await WriteArchiveAsync(temporary, state, cancellationToken);
            File.Move(temporary, zipPath, true);
            AppLog.Shared.Info(LogCategory.App, $"已导出诊断报告，收录 {count} 个文件");
            return count;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 导出已经失败，残留的临时文件不影响用户再次导出（会被覆盖）。
        }
    }

    private async Task<int> WriteArchiveAsync(
        string temporary,
        DiagnosticAppState state,
        CancellationToken cancellationToken
    )
    {
        var included = new List<JObject>();
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var (file, folder) in SelectFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await TryAddFileAsync(archive, file, folder, cancellationToken))
                {
                    included.Add(
                        new JObject
                        {
                            ["path"] = $"{folder}/{file.Name}",
                            ["bytes"] = file.Length,
                            ["modified"] = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        }
                    );
                }
            }

            var report = BuildReport(state, included, await DescribeClientAsync(state.Lcu, cancellationToken));
            var entry = archive.CreateEntry("report.json");
            await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            await writer.WriteAsync(_redactor.Redact(report.ToString(Formatting.Indented)));
        }

        return included.Count;
    }

    public JObject BuildReport(DiagnosticAppState state, IReadOnlyList<JObject> files, JObject? client = null)
    {
        var build = BuildInfo.Current;
        return new JObject
        {
            ["generatedAt"] = _now().ToString("yyyy-MM-dd HH:mm:ss zzz"),
            ["build"] = new JObject
            {
                ["version"] = build.Version,
                ["commit"] = build.Commit,
                ["uncommittedChanges"] = build.Dirty?.ToString() ?? "unknown",
                ["builtAtUtc"] = build.BuiltAtUtc?.ToString("o") ?? "unknown",
            },
            ["environment"] = DescribeEnvironment(),
            ["client"] = client ?? new JObject { ["available"] = false },
            ["processes"] = DescribeProcesses(),
            ["app"] = new JObject
            {
                ["lcuConnected"] = state.LcuConnected,
                ["inGame"] = state.InGame,
                ["phase"] = state.Phase,
                ["gameBar"] = state.GameBarStatus,
                ["voiceModelReady"] = VoiceModelStore.Shared.AvailableDirectory != null,
                ["installation"] = DescribeInstallation(),
            },
            ["config"] = DescribeConfig(state.Config),
            ["files"] = new JArray(files),
            ["privacy"] =
                "已替换用户目录，去掉客户端令牌、授权信息和自定义发送内容；玩家 ID 已换成短哈希；不含聊天正文。",
        };
    }

    private IEnumerable<(FileInfo File, string Folder)> SelectFiles()
    {
        DateTime oldest = _now() - LogRetention.MaxAge;
        foreach (var source in _sources)
        {
            if (source.Directory.Length == 0 || !Directory.Exists(source.Directory))
            {
                continue;
            }

            var directory = new DirectoryInfo(source.Directory);
            var files = source
                .Patterns.SelectMany(p => directory.EnumerateFiles(p, SearchOption.TopDirectoryOnly))
                .DistinctBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
                .Where(f => f.LastWriteTime >= oldest)
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                yield return (file, source.ArchiveFolder);
            }
        }
    }

    // Logs are still being written by this and other processes; open them shared and skip the ones that vanish.
    private async Task<bool> TryAddFileAsync(
        ZipArchive archive,
        FileInfo file,
        string folder,
        CancellationToken cancellationToken
    )
    {
        string text;
        try
        {
            await using var input = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 文件刚被轮转或删除、或没有权限读取时跳过，报告的文件列表里不会出现它。
            return false;
        }

        var entry = archive.CreateEntry($"{folder}/{file.Name}");
        await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        await writer.WriteAsync(_redactor.Redact(text));
        return true;
    }

    private static JObject DescribeEnvironment()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return new JObject
        {
            ["os"] = Environment.OSVersion.VersionString,
            ["is64BitOs"] = Environment.Is64BitOperatingSystem,
            ["dotnet"] = Environment.Version.ToString(),
            ["administrator"] = AppLog.IsAdministrator(),
            ["culture"] = System.Globalization.CultureInfo.CurrentCulture.Name,
            ["uiCulture"] = System.Globalization.CultureInfo.CurrentUICulture.Name,
            ["utcOffset"] = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).ToString(),
            ["processUptimeMinutes"] = (int)(DateTime.Now - process.StartTime).TotalMinutes,
            ["screens"] = new JArray(
                Screen.AllScreens.Select(s => new JObject
                {
                    ["primary"] = s.Primary,
                    ["bounds"] = $"{s.Bounds.Width}x{s.Bounds.Height}@{s.Bounds.X},{s.Bounds.Y}",
                    ["bitsPerPixel"] = s.BitsPerPixel,
                })
            ),
        };
    }

    // Process names only, never paths: enough to tell a closed client, a running game or a WeGame launch apart.
    private static readonly string[] WatchedProcesses =
    {
        "LeagueClient",
        "LeagueClientUx",
        "LeagueClientUxRender",
        "League of Legends",
        "RiotClientServices",
        "Riot Client",
        "wegame",
        "tgp_daemon",
        "LeagueHistory.GameBar",
    };

    public static JObject DescribeProcesses()
    {
        var result = new JObject();
        foreach (string name in WatchedProcesses)
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(name);
            result[name] = processes.Length;
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
        return result;
    }

    /// <summary>客户端版本和大区；未连接或 3 秒内没有响应时只记原因。</summary>
    public static async Task<JObject> DescribeClientAsync(HttpClient? lcu, CancellationToken cancellationToken)
    {
        if (lcu == null)
        {
            return new JObject { ["available"] = false, ["reason"] = "客户端未连接" };
        }

        var result = new JObject { ["available"] = true };
        result["gameVersion"] = await ReadClientAsync(lcu, "/lol-patch/v1/game-version", cancellationToken);
        var region = await ReadClientAsync(lcu, "/riotclient/region-locale", cancellationToken);
        if (region is JObject locale)
        {
            result["region"] = locale["region"]?.ToString() ?? "";
            result["webRegion"] = locale["webRegion"]?.ToString() ?? "";
            result["locale"] = locale["locale"]?.ToString() ?? "";
        }
        else
        {
            result["region"] = region;
        }
        return result;
    }

    private static async Task<JToken> ReadClientAsync(HttpClient lcu, string path, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await lcu.GetAsync(path, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return $"未取得（{(int)response.StatusCode}）";
            }

            return JToken.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            // The report is still useful without these two fields; record why they are missing.
            return $"未取得（{ex.GetType().Name}）";
        }
    }

    private static JObject DescribeInstallation()
    {
        var installation = LOLHelper.Shared.ReadSavedInstallation();
        return new JObject
        {
            ["found"] = installation != null,
            ["root"] = installation?.Root ?? "",
            ["official"] = installation?.OfficialPath != null,
            ["wegame"] = installation?.WeGamePath != null,
        };
    }

    /// <summary>配置原样附上，只去掉自定义发送内容（可能是个人文字）和麦克风设备 ID。</summary>
    public static JToken DescribeConfig(LeagueConfig? config)
    {
        if (config == null)
        {
            return JValue.CreateNull();
        }

        var json = JObject.FromObject(config);
        json["CustomSendContent"] = $"（已去掉，{config.CustomSendContent?.Length ?? 0} 字）";
        json["VoiceMicrophoneId"] = string.IsNullOrEmpty(config.VoiceMicrophoneId) ? "跟随系统" : "已指定设备";
        return json;
    }
}
