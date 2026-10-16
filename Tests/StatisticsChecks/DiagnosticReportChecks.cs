using System.IO.Compression;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class DiagnosticReportChecks
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "league-report-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            await ExportAsync(root);
            ConfigIsSanitized();
            await ClientInfoAsync();
        }
        finally
        {
            Directory.Delete(root, true);
        }
        Console.WriteLine(
            "PASS: diagnostic report contents, 7-day file selection, redaction, sanitized config and atomic zip."
        );
    }

    private static async Task ExportAsync(string root)
    {
        var now = new DateTime(2026, 10, 16, 12, 0, 0);
        string logs = Directory.CreateDirectory(Path.Combine(root, "Logs")).FullName;
        string gameBar = Directory.CreateDirectory(Path.Combine(root, "LocalState")).FullName;
        string uuid = "0f8fad5b-d9cb-469f-a165-70867728950e";
        await File.WriteAllTextAsync(
            Path.Combine(logs, "app-2026-10-16.log"),
            $@"connected --remoting-auth-token=secret123 player {uuid} at C:\Users\测试用户\AppData"
        );
        string old = Path.Combine(logs, "app-2026-10-01.log");
        await File.WriteAllTextAsync(old, "too old");
        File.SetLastWriteTime(old, now.AddDays(-15));
        await File.WriteAllTextAsync(Path.Combine(logs, "notes.txt"), "not a log");
        await File.WriteAllTextAsync(Path.Combine(gameBar, "component-error.txt"), "1700000000 read failed");
        await File.WriteAllTextAsync(Path.Combine(gameBar, "snapshot.json"), "{\"players\":[]}");
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(f => f != old))
        {
            File.SetLastWriteTime(file, now.AddHours(-1));
        }

        var exporter = new DiagnosticReportExporter(
            new[]
            {
                new DiagnosticSource(logs, new[] { "*.log", "*.log.previous", "*.json" }, "logs"),
                new DiagnosticSource(gameBar, new[] { "component-error.txt", "widget-status.json" }, "gamebar"),
                new DiagnosticSource(Path.Combine(root, "missing"), new[] { "*.log" }, "shutdown"),
            },
            new DiagnosticRedactor("salt", @"C:\Users\测试用户"),
            () => now
        );
        string zip = Path.Combine(root, "report.zip");
        var config = new LeagueConfig { CustomSendContent = "我的私人留言", EnableAutoSkinSelect = true };
        int count = await exporter.ExportAsync(zip, new DiagnosticAppState(true, false, "组件已连接", config), default);

        Check(count == 2 && !File.Exists(zip + ".tmp"), "Two recent files are exported and the temp file is renamed");
        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Check(
            names.SequenceEqual(new[] { "gamebar/component-error.txt", "logs/app-2026-10-16.log", "report.json" }),
            "Only matching files from the last seven days are included: " + string.Join(", ", names)
        );

        string log = Read(archive, "logs/app-2026-10-16.log");
        Check(
            !log.Contains("secret123") && !log.Contains(uuid) && !log.Contains("测试用户"),
            "Exported logs are redacted"
        );

        string reportText = Read(archive, "report.json");
        Check(!reportText.Contains("我的私人留言"), "Custom chat text never reaches the report");
        var report = JObject.Parse(reportText);
        Check(report["generatedAt"]!.ToString().StartsWith("2026-10-16 12:00:00"), "Report records when it was made");
        Check(report["build"]?["commit"]?.ToString().Length >= 7, "Report records the build commit");
        Check(report["environment"]?["screens"] is JArray { Count: > 0 }, "Report lists screens");
        Check(report["app"]?["lcuConnected"]?.Value<bool>() == true, "Report records the client connection");
        Check(report["app"]?["gameBar"]?.ToString() == "组件已连接", "Report records the Game Bar state");
        Check(report["app"]?["phase"]?.ToString() == "未知", "Report records the game phase");
        Check(report["client"]?["available"]?.Value<bool>() == false, "Without a client connection it says so");
        Check(report["processes"]?["League of Legends"]?.Type == JTokenType.Integer, "Report counts game processes");
        Check(report["config"]?["EnableAutoSkinSelect"]?.Value<bool>() == true, "Report keeps ordinary settings");
        Check(report["files"] is JArray { Count: 2 }, "Report lists the included files");
    }

    private static void ConfigIsSanitized()
    {
        var json = DiagnosticReportExporter.DescribeConfig(
            new LeagueConfig { CustomSendContent = "abc", VoiceMicrophoneId = "{0.0.1.00000000}.{device-guid}" }
        );
        Check(json["CustomSendContent"]?.ToString() == "（已去掉，3 字）", "Custom content is replaced by its length");
        Check(json["VoiceMicrophoneId"]?.ToString() == "已指定设备", "Microphone device IDs are not exported");
        Check(DiagnosticReportExporter.DescribeConfig(null).Type == JTokenType.Null, "Missing config stays null");
    }

    private static async Task ClientInfoAsync()
    {
        using var client = new HttpClient(new FakeClient()) { BaseAddress = new Uri("https://127.0.0.1:1/") };
        var info = await DiagnosticReportExporter.DescribeClientAsync(client, default);
        Check(info["gameVersion"]?.ToString() == "14.20.623.5122", "Game version comes from the client");
        Check(
            info["region"]?.ToString() == "TENCENT" && info["webRegion"]?.ToString() == "hn1",
            "Region comes from the client"
        );

        using var failing = new HttpClient(new FakeClient(fail: true))
        {
            BaseAddress = new Uri("https://127.0.0.1:1/"),
        };
        var missing = await DiagnosticReportExporter.DescribeClientAsync(failing, default);
        Check(
            missing["gameVersion"]?.ToString() == "未取得（503）" && missing["region"]?.ToString() == "未取得（503）",
            "A failing client records why the fields are missing"
        );
    }

    private sealed class FakeClient(bool fail = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (fail)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
            }

            string body = request.RequestUri!.AbsolutePath switch
            {
                "/lol-patch/v1/game-version" => "\"14.20.623.5122\"",
                "/riotclient/region-locale" => "{\"locale\":\"zh_CN\",\"region\":\"TENCENT\",\"webRegion\":\"hn1\"}",
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage { Content = new StringContent(body) });
        }
    }

    private static string Read(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
