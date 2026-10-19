using System.IO.Compression;
using System.Net;
using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// 诊断报告导出器：报告里有哪些段落、段落里的值怎么从程序状态和收录的文件推出来、导出的 zip 收了哪些文件、
/// 日志与配置在哪里被去敏。每个测试自己构造 <see cref="DiagnosticSource"/>，文件全部落在 <see cref="TempFolder"/>
/// 里，时间来自注入的时钟，客户端一律是桩，所以不调用 <c>CreateDefault</c>、不连真实客户端、不碰用户目录。
/// 本类对应 Tests/StatisticsChecks/DiagnosticReportChecks.cs。
/// </summary>
public class DiagnosticReportExporterTests : IDisposable
{
    private const string ChatText = "我的私人留言";
    private const string Uuid = "0f8fad5b-d9cb-469f-a165-70867728950e";
    private const string ReportFile = "report.json";
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0);
    private readonly TempFolder _folder = new("league-diag-report");

    public void Dispose()
    {
        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    // ------------------------------------------------------------------ 报告有哪些段落

    [Fact]
    public void BuildReport_AnyState_ReportsEverySection()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        Assert.Equal(
            new[] { "generatedAt", "build", "environment", "client", "processes", "app", "config", "files", "privacy" },
            report.Properties().Select(property => property.Name)
        );
    }

    [Fact]
    public void BuildReport_State_ReportsTheClientAndGameFlags()
    {
        var state = new DiagnosticAppState(true, false, "组件已连接", null);

        var report = Exporter().BuildReport(state, Array.Empty<JObject>());

        Assert.Equal(true, (bool?)report["app"]!["lcuConnected"]);
        Assert.Equal(false, (bool?)report["app"]!["inGame"]);
    }

    [Fact]
    public void BuildReport_State_ReportsTheGameBarTextAndPhase()
    {
        var state = new DiagnosticAppState(true, false, "组件已连接", null, null, "InProgress");

        var report = Exporter().BuildReport(state, Array.Empty<JObject>());

        Assert.Equal("组件已连接", report["app"]!["gameBar"]!.ToString());
        Assert.Equal("InProgress", report["app"]!["phase"]!.ToString());
    }

    [Fact]
    public void BuildReport_StateWithoutAPhase_ReportsUnknown()
    {
        var state = new DiagnosticAppState(false, false, "组件未连接", null);

        var report = Exporter().BuildReport(state, Array.Empty<JObject>());

        Assert.Equal("未知", report["app"]!["phase"]!.ToString());
    }

    [Fact]
    public void BuildReport_NoConfig_ReportsNull()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        Assert.Equal(JTokenType.Null, report["config"]!.Type);
    }

    [Fact]
    public void BuildReport_Config_ReplacesTheCustomChatText()
    {
        var config = new LeagueConfig { CustomSendContent = ChatText, EnableAutoSkinSelect = true };

        var report = Exporter().BuildReport(State(config), Array.Empty<JObject>());

        Assert.Equal("（已去掉，6 字）", report["config"]!["CustomSendContent"]!.ToString());
        Assert.Equal(true, (bool?)report["config"]!["EnableAutoSkinSelect"]);
    }

    [Fact]
    public void BuildReport_InjectedClock_StampsTheReportWithThatMoment()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        Assert.StartsWith("2026-10-16 12:00:00 ", report["generatedAt"]!.ToString());
    }

    [Fact]
    public void BuildReport_AnyState_WritesTheLocalOffsetAfterTheTimestamp()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [+-]\d{2}:\d{2}$", report["generatedAt"]!.ToString());
    }

    [Fact]
    public void BuildReport_NoClient_ReportsItAsUnavailable()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        Assert.Equal(false, (bool?)report["client"]!["available"]);
    }

    [Fact]
    public void BuildReport_ClientProvided_IsCarriedIntoTheReport()
    {
        var client = new JObject { ["available"] = true, ["region"] = "EUW" };

        var report = Exporter().BuildReport(State(), Array.Empty<JObject>(), client);

        Assert.Equal(true, (bool?)report["client"]!["available"]);
        Assert.Equal("EUW", report["client"]!["region"]!.ToString());
    }

    [Fact]
    public void BuildReport_Files_AreListedInTheGivenOrder()
    {
        var files = new[] { Included("logs/a.log", 12), Included("gamebar/b.txt", 34) };

        var report = Exporter().BuildReport(State(), files);

        var listed = Assert.IsType<JArray>(report["files"]);
        Assert.Equal(2, listed.Count);
        Assert.Equal("logs/a.log", listed[0]!["path"]!.ToString());
        Assert.Equal("gamebar/b.txt", listed[1]!["path"]!.ToString());
    }

    [Fact]
    public void BuildReport_Processes_ListsTheSameNamesAsDescribeProcesses()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        Assert.Equal(
            DiagnosticReportExporter.DescribeProcesses().Properties().Select(property => property.Name),
            Assert.IsType<JObject>(report["processes"]).Properties().Select(property => property.Name)
        );
    }

    [Fact]
    public void BuildReport_Build_DescribesTheBuildItWasMadeFrom()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        var build = Assert.IsType<JObject>(report["build"]);
        Assert.NotEmpty(build["version"]!.ToString());
        Assert.Matches("^[0-9a-f]{7,40}$", build["commit"]!.ToString());
        Assert.Matches("^(True|False|unknown)$", build["uncommittedChanges"]!.ToString());
        Assert.Matches(@"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z|unknown)$", build["builtAtUtc"]!.ToString());
    }

    [Fact]
    public void BuildReport_Environment_DescribesTheOperatingSystem()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        var environment = Assert.IsType<JObject>(report["environment"]);
        Assert.NotEmpty(environment["os"]!.ToString());
        Assert.NotEmpty(environment["dotnet"]!.ToString());
        Assert.Equal(JTokenType.Boolean, environment["is64BitOs"]!.Type);
        Assert.Equal(JTokenType.Boolean, environment["administrator"]!.Type);
        Assert.Equal(JTokenType.String, environment["culture"]!.Type);
        Assert.Equal(JTokenType.String, environment["uiCulture"]!.Type);
    }

    [Fact]
    public void BuildReport_Environment_ListsTheScreens()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        var screens = Assert.IsType<JArray>(report["environment"]!["screens"]);
        Assert.NotEmpty(screens);
        Assert.Equal(JTokenType.Boolean, screens[0]!["primary"]!.Type);
        Assert.Equal(JTokenType.Integer, screens[0]!["bitsPerPixel"]!.Type);
        Assert.Matches(@"^\d+x\d+@-?\d+,-?\d+$", screens[0]!["bounds"]!.ToString());
    }

    [Fact]
    public void BuildReport_Environment_ReportsTheLocalUtcOffset()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        Assert.Matches(@"^[+-]\d{2}:\d{2}(:\d{2})?$", report["environment"]!["utcOffset"]!.ToString());
    }

    [Fact]
    public void BuildReport_Environment_ReportsTheProcessUptime()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        Assert.InRange(report["environment"]!["processUptimeMinutes"]!.Value<int>(), 0, int.MaxValue);
    }

    [Fact]
    public void BuildReport_App_ReportsTheVoiceModelState()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        Assert.Equal(JTokenType.Boolean, report["app"]!["voiceModelReady"]!.Type);
    }

    [Fact]
    public void BuildReport_App_DescribesTheInstallation()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        var installation = report["app"]!["installation"]!;
        Assert.Equal(JTokenType.Boolean, installation["found"]!.Type);
        Assert.Equal(JTokenType.String, installation["root"]!.Type);
        Assert.Equal(JTokenType.Boolean, installation["official"]!.Type);
        Assert.Equal(JTokenType.Boolean, installation["wegame"]!.Type);
    }

    [Fact]
    public void BuildReport_Privacy_NamesWhatTheReportLeavesOut()
    {
        var report = Exporter().BuildReport(State(), Array.Empty<JObject>());

        string privacy = report["privacy"]!.ToString();
        Assert.Contains("已替换用户目录", privacy);
        Assert.Contains("不含聊天正文", privacy);
    }

    // ------------------------------------------------------------------ 被监视的进程

    [Theory]
    [InlineData("LeagueClient")]
    [InlineData("LeagueClientUx")]
    [InlineData("LeagueClientUxRender")]
    [InlineData("League of Legends")]
    [InlineData("RiotClientServices")]
    [InlineData("Riot Client")]
    [InlineData("wegame")]
    [InlineData("tgp_daemon")]
    [InlineData("LeagueHistory.GameBar")]
    public void DescribeProcesses_WatchedProcess_IsCounted(string name)
    {
        var processes = DiagnosticReportExporter.DescribeProcesses();

        Assert.Equal(JTokenType.Integer, processes[name]!.Type);
    }

    // ------------------------------------------------------------------ 纯函数式的配置

    [Fact]
    public void DescribeConfig_NoConfig_StaysNull()
    {
        var json = DiagnosticReportExporter.DescribeConfig(null);

        Assert.Equal(JTokenType.Null, json.Type);
    }

    [Fact]
    public void DescribeConfig_CustomChatText_IsReplacedByItsLength()
    {
        var json = DiagnosticReportExporter.DescribeConfig(new LeagueConfig { CustomSendContent = "abc" });

        Assert.Equal("（已去掉，3 字）", json["CustomSendContent"]!.ToString());
    }

    [Fact]
    public void DescribeConfig_EmptyChatText_ReportsNoCharacters()
    {
        var json = DiagnosticReportExporter.DescribeConfig(new LeagueConfig());

        Assert.Equal("（已去掉，0 字）", json["CustomSendContent"]!.ToString());
    }

    [Fact]
    public void DescribeConfig_MicrophoneChosen_HidesTheDeviceId()
    {
        var json = DiagnosticReportExporter.DescribeConfig(
            new LeagueConfig { VoiceMicrophoneId = "{0.0.1.00000000}.{device-guid}" }
        );

        Assert.Equal("已指定设备", json["VoiceMicrophoneId"]!.ToString());
    }

    [Fact]
    public void DescribeConfig_NoMicrophone_FollowsTheSystem()
    {
        var json = DiagnosticReportExporter.DescribeConfig(new LeagueConfig { VoiceMicrophoneId = "" });

        Assert.Equal("跟随系统", json["VoiceMicrophoneId"]!.ToString());
    }

    [Fact]
    public void DescribeConfig_OrdinarySettings_AreKept()
    {
        var json = DiagnosticReportExporter.DescribeConfig(
            new LeagueConfig { EnableAutoSkinSelect = true, GameBarWaveOpacity = 70 }
        );

        Assert.Equal(true, (bool?)json["EnableAutoSkinSelect"]);
        Assert.Equal(70, (int?)json["GameBarWaveOpacity"]);
    }

    // ------------------------------------------------------------------ 客户端版本和大区

    [Fact]
    public async Task DescribeClientAsync_NoClient_ReportsItAsNotConnected()
    {
        var info = await DiagnosticReportExporter.DescribeClientAsync(null, CancellationToken.None);

        Assert.Equal(false, (bool?)info["available"]);
        Assert.Equal("客户端未连接", info["reason"]!.ToString());
    }

    [Fact]
    public async Task DescribeClientAsync_ConnectedClient_ReadsTheGameVersion()
    {
        using var client = Stub(ClientReply);

        var info = await DiagnosticReportExporter.DescribeClientAsync(client, CancellationToken.None);

        Assert.Equal(true, (bool?)info["available"]);
        Assert.Equal("14.20.623.5122", info["gameVersion"]!.ToString());
    }

    [Fact]
    public async Task DescribeClientAsync_ConnectedClient_ReadsTheRegionAndLocale()
    {
        using var client = Stub(ClientReply);

        var info = await DiagnosticReportExporter.DescribeClientAsync(client, CancellationToken.None);

        Assert.Equal("TENCENT", info["region"]!.ToString());
        Assert.Equal("hn1", info["webRegion"]!.ToString());
        Assert.Equal("zh_CN", info["locale"]!.ToString());
    }

    [Fact]
    public async Task DescribeClientAsync_RefusedRequest_RecordsTheStatusCode()
    {
        using var client = Stub(_ => (HttpStatusCode.ServiceUnavailable, ""));

        var info = await DiagnosticReportExporter.DescribeClientAsync(client, CancellationToken.None);

        Assert.Equal("未取得（503）", info["gameVersion"]!.ToString());
        Assert.Equal("未取得（503）", info["region"]!.ToString());
    }

    [Fact]
    public async Task DescribeClientAsync_RegionWithoutALocaleObject_IsRecordedAsItIs()
    {
        using var client = Stub(path =>
            path == "/lol-patch/v1/game-version"
                ? (HttpStatusCode.OK, "\"14.20.623.5122\"")
                : (HttpStatusCode.OK, "\"EUW\"")
        );

        var info = await DiagnosticReportExporter.DescribeClientAsync(client, CancellationToken.None);

        Assert.Equal("EUW", info["region"]!.ToString());
        Assert.Null(info["webRegion"]);
        Assert.Null(info["locale"]);
    }

    [Fact]
    public async Task DescribeClientAsync_UnparsableBody_RecordsTheParseFailure()
    {
        using var client = Stub(_ => (HttpStatusCode.OK, "not json"));

        var info = await DiagnosticReportExporter.DescribeClientAsync(client, CancellationToken.None);

        Assert.Equal("未取得（JsonReaderException）", info["gameVersion"]!.ToString());
    }

    [Fact]
    public async Task DescribeClientAsync_TransportFailure_RecordsTheExceptionType()
    {
        using var client = Stub(_ => throw new HttpRequestException("offline"));

        var info = await DiagnosticReportExporter.DescribeClientAsync(client, CancellationToken.None);

        Assert.Equal("未取得（HttpRequestException）", info["gameVersion"]!.ToString());
    }

    [Fact]
    public async Task DescribeClientAsync_Cancelled_Throws()
    {
        using var client = Stub(ClientReply);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DiagnosticReportExporter.DescribeClientAsync(client, cancellation.Token)
        );
    }

    // ------------------------------------------------------------------ 导出 zip

    [Fact]
    public async Task ExportAsync_RecentMatchingFiles_AreExportedInOrder()
    {
        WriteStandardFixture();
        string zip = _folder.File("report.zip");

        int count = await Exporter(StandardSources()).ExportAsync(zip, State(), CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(new[] { "logs/app-2026-10-16.log", "gamebar/component-error.txt", ReportFile }, EntryNames(zip));
    }

    [Fact]
    public async Task ExportAsync_CompletedExport_RenamesTheTemporaryArchive()
    {
        WriteStandardFixture();
        string zip = _folder.File("report.zip");

        await Exporter(StandardSources()).ExportAsync(zip, State(), CancellationToken.None);

        Assert.True(File.Exists(zip), "导出后应留下 zip");
        Assert.False(File.Exists(zip + ".tmp"), "改名后不应留下临时文件");
    }

    [Fact]
    public async Task ExportAsync_FileOlderThanSevenDays_IsLeftOut()
    {
        WriteStandardFixture();
        string zip = _folder.File("report.zip");

        await Exporter(StandardSources()).ExportAsync(zip, State(), CancellationToken.None);

        Assert.DoesNotContain("logs/app-2026-10-01.log", EntryNames(zip));
    }

    [Fact]
    public async Task ExportAsync_FileNotMatchingThePatterns_IsLeftOut()
    {
        WriteStandardFixture();
        string zip = _folder.File("report.zip");

        await Exporter(StandardSources()).ExportAsync(zip, State(), CancellationToken.None);

        Assert.DoesNotContain("logs/notes.txt", EntryNames(zip));
    }

    [Fact]
    public async Task ExportAsync_FileMatchingTwoPatterns_IsListedOnce()
    {
        WriteFile("Logs/app.log", "hello", Now.AddHours(-1));
        var sources = new[] { new DiagnosticSource(Logs, new[] { "*.log", "*" }, "logs") };

        int count = await Exporter(sources).ExportAsync(_folder.File("report.zip"), State(), CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ExportAsync_NoSources_ExportsOnlyTheReport()
    {
        string zip = _folder.File("report.zip");

        int count = await Exporter().ExportAsync(zip, State(), CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Equal(new[] { ReportFile }, EntryNames(zip));
    }

    [Fact]
    public async Task ExportAsync_SourceWithoutADirectory_ContributesNothing()
    {
        var sources = new[] { new DiagnosticSource("", new[] { "*.log" }, "logs") };

        int count = await Exporter(sources).ExportAsync(_folder.File("report.zip"), State(), CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ExportAsync_MissingSourceDirectory_ContributesNothing()
    {
        var sources = new[]
        {
            new DiagnosticSource(Path.Combine(_folder.Root, "missing"), new[] { "*.log" }, "shutdown"),
        };

        int count = await Exporter(sources).ExportAsync(_folder.File("report.zip"), State(), CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ExportAsync_Report_ListsTheFilesThatWereArchived()
    {
        WriteStandardFixture();
        string zip = _folder.File("report.zip");

        await Exporter(StandardSources()).ExportAsync(zip, State(), CancellationToken.None);

        var files = Assert.IsType<JArray>(Report(zip)["files"]);
        Assert.Equal(2, files.Count);
        Assert.Equal("logs/app-2026-10-16.log", files[0]!["path"]!.ToString());
        Assert.Equal(ByteCount(Path.Combine(Logs, "app-2026-10-16.log")), files[0]!["bytes"]!.Value<long>());
        Assert.Equal("2026-10-16 11:00:00", files[0]!["modified"]!.ToString());
    }

    [Fact]
    public async Task ExportAsync_Report_IsStampedWithTheInjectedClock()
    {
        WriteStandardFixture();
        string zip = _folder.File("report.zip");

        await Exporter(StandardSources()).ExportAsync(zip, State(), CancellationToken.None);

        Assert.StartsWith("2026-10-16 12:00:00 ", Report(zip)["generatedAt"]!.ToString());
    }

    [Fact]
    public async Task ExportAsync_Report_RecordsTheProgramState()
    {
        WriteStandardFixture();
        string zip = _folder.File("report.zip");

        await Exporter(StandardSources())
            .ExportAsync(zip, new DiagnosticAppState(true, false, "组件已连接", null), CancellationToken.None);

        var report = Report(zip);
        Assert.Equal(true, (bool?)report["app"]!["lcuConnected"]);
        Assert.Equal("组件已连接", report["app"]!["gameBar"]!.ToString());
    }

    [Fact]
    public async Task ExportAsync_LogContents_AreRedacted()
    {
        WriteStandardFixture();
        string zip = _folder.File("report.zip");

        await Exporter(StandardSources()).ExportAsync(zip, State(), CancellationToken.None);

        string exported = EntryText(zip, "logs/app-2026-10-16.log");
        Assert.DoesNotContain("secret123", exported);
        Assert.DoesNotContain(Uuid, exported);
        Assert.Contains("%USERPROFILE%", exported);
    }

    [Fact]
    public async Task ExportAsync_Report_DoesNotCarryTheCustomChatText()
    {
        WriteStandardFixture();
        string zip = _folder.File("report.zip");

        await Exporter(StandardSources())
            .ExportAsync(zip, State(new LeagueConfig { CustomSendContent = ChatText }), CancellationToken.None);

        Assert.DoesNotContain(ChatText, EntryText(zip, ReportFile));
    }

    [Fact]
    public async Task ExportAsync_Cancelled_ThrowsAndLeavesNothingBehind()
    {
        WriteStandardFixture();
        string zip = _folder.File("report.zip");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Exporter(StandardSources()).ExportAsync(zip, State(), cancellation.Token)
        );

        Assert.False(File.Exists(zip));
        Assert.False(File.Exists(zip + ".tmp"));
    }

    // ------------------------------------------------------------------ 夹具

    private string Logs => Path.Combine(_folder.Root, "Logs");

    private DiagnosticReportExporter Exporter(params DiagnosticSource[] sources) =>
        new(sources, new DiagnosticRedactor("salt", _folder.Root), () => Now);

    private static DiagnosticAppState State(LeagueConfig? config = null) => new(false, false, "组件未连接", config);

    private static JObject Included(string path, long bytes) =>
        new()
        {
            ["path"] = path,
            ["bytes"] = bytes,
            ["modified"] = "2026-10-16 11:00:00",
        };

    private static HttpClient Stub(Func<string, (HttpStatusCode Status, string Body)> reply) =>
        new(new StubLcuClient(reply)) { BaseAddress = new Uri("https://127.0.0.1:1/") };

    private static (HttpStatusCode Status, string Body) ClientReply(string path) =>
        path switch
        {
            "/lol-patch/v1/game-version" => (HttpStatusCode.OK, "\"14.20.623.5122\""),
            "/riotclient/region-locale" => (
                HttpStatusCode.OK,
                """{"locale":"zh_CN","region":"TENCENT","webRegion":"hn1"}"""
            ),
            _ => (HttpStatusCode.NotFound, "{}"),
        };

    /// <summary>日志目录、GameBar 目录和一个不存在的目录；只有前两个里面有文件。</summary>
    private DiagnosticSource[] StandardSources() =>
        new[]
        {
            new DiagnosticSource(Logs, new[] { "*.log" }, "logs"),
            new DiagnosticSource(Path.Combine(_folder.Root, "LocalState"), new[] { "component-error.txt" }, "gamebar"),
            new DiagnosticSource(Path.Combine(_folder.Root, "missing"), new[] { "*.log" }, "shutdown"),
        };

    /// <summary>两个最近的文件，外加一个过期文件和一个不匹配模式的文件。</summary>
    private void WriteStandardFixture()
    {
        WriteFile("Logs/app-2026-10-16.log", LogText(), Now.AddHours(-1));
        WriteFile("Logs/app-2026-10-01.log", "too old", Now.AddDays(-15));
        WriteFile("Logs/notes.txt", "not a log", Now.AddHours(-1));
        WriteFile("LocalState/component-error.txt", "1700000000 read failed", Now.AddHours(-1));
    }

    /// <summary>含令牌、玩家 ID 和用户目录的日志正文，三样都该在导出时被替换掉。</summary>
    private string LogText() => $"--remoting-auth-token=secret123 player {Uuid} at {_folder.Root}\\Logs";

    private string WriteFile(string relativePath, string text, DateTime lastWrite)
    {
        string path = Path.Combine(_folder.Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTime(path, lastWrite);
        return path;
    }

    private static string[] EntryNames(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return archive.Entries.Select(entry => entry.FullName).ToArray();
    }

    private static string EntryText(string zipPath, string name)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static JObject Report(string zipPath) => JObject.Parse(EntryText(zipPath, ReportFile));

    private static long ByteCount(string path) => new FileInfo(path).Length;

    /// <summary>按路径回答的假客户端；没有请求会离开进程。</summary>
    private sealed class StubLcuClient(Func<string, (HttpStatusCode Status, string Body)> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var (status, body) = reply(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
