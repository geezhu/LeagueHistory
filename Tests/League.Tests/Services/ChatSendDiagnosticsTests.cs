using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The tier-chat log and the status line beside it: what was attempted, when, and what it came to.
///
/// Two rules are worth pinning. The status and the logged line change together under one lock, so the panel and the
/// file keep the same order - a reader comparing them never sees one ahead of the other. And notifying the panel is
/// best-effort: a subscriber that throws is swallowed, and the remaining ones are still told, because a closed window
/// must not cost the log or the other readers.
/// </summary>
public class ChatSendDiagnosticsTests : IAsyncLifetime
{
    private readonly TempFolder _folder = new("chat-diag");
    private ChatSendDiagnostics? _diagnostics;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_diagnostics is not null)
        {
            await _diagnostics.FlushAsync();
        }

        _folder.Dispose();
    }

    private ChatSendDiagnostics Diagnostics()
    {
        _diagnostics = new ChatSendDiagnostics(_folder.File("tier-chat.log"));
        return _diagnostics;
    }

    private static async Task<string[]> LinesAsync(ChatSendDiagnostics diagnostics)
    {
        await diagnostics.FlushAsync();
        return File.Exists(diagnostics.LogPath) ? await File.ReadAllLinesAsync(diagnostics.LogPath) : [];
    }

    // ---- where it starts ----

    [Fact]
    public void LogPath_IsWhatTheCallerGave()
    {
        Assert.Equal(_folder.File("tier-chat.log"), Diagnostics().LogPath);
    }

    [Fact]
    public void Current_BeforeAnythingHappened_IsReady()
    {
        // The panel shows this before the first send, so it has to say what the user can do.
        ChatSendStatus current = Diagnostics().Current;

        Assert.Equal("ready", current.Stage);
        Assert.Equal("等待发送：游戏内 F9 / F11，或 Game Bar 的「发送」。", current.Message);
    }

    // ---- reporting ----

    [Fact]
    public void Report_UpdatesTheStatus()
    {
        ChatSendDiagnostics diagnostics = Diagnostics();

        diagnostics.Report("sending", "正在发送");

        Assert.Equal("sending", diagnostics.Current.Stage);
        Assert.Equal("正在发送", diagnostics.Current.Message);
    }

    [Fact]
    public async Task Report_WritesAStageAndAMessage()
    {
        ChatSendDiagnostics diagnostics = Diagnostics();

        diagnostics.Report("sending", "正在发送");
        string line = Assert.Single(await LinesAsync(diagnostics));

        Assert.Contains("[sending] 正在发送", line, StringComparison.Ordinal);
        Assert.Contains($" pid={Environment.ProcessId} ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_AMessageWithABreakInside_IsFlattened()
    {
        // Every entry is one line; a break would forge a second one.
        ChatSendDiagnostics diagnostics = Diagnostics();

        diagnostics.Report("stage", "第一行\r\n第二行");

        Assert.Equal("第一行  第二行", diagnostics.Current.Message);
    }

    [Fact]
    public async Task Report_SeveralReports_KeepTheirOrder()
    {
        ChatSendDiagnostics diagnostics = Diagnostics();

        diagnostics.Report("first", "一");
        diagnostics.Report("second", "二");
        string[] lines = await LinesAsync(diagnostics);

        Assert.Equal(2, lines.Length);
        Assert.Contains("[first] 一", lines[0], StringComparison.Ordinal);
        Assert.Contains("[second] 二", lines[1], StringComparison.Ordinal);
    }

    // ---- telling the panel ----

    [Fact]
    public void Report_NotifiesTheSubscribers()
    {
        ChatSendDiagnostics diagnostics = Diagnostics();
        int told = 0;
        diagnostics.Changed += () => told++;

        diagnostics.Report("stage", "消息");

        Assert.Equal(1, told);
    }

    [Fact]
    public void Report_NotifiesEverySubscriber()
    {
        // A status line with several readers still has to reach all of them.
        ChatSendDiagnostics diagnostics = Diagnostics();
        int first = 0,
            second = 0;
        diagnostics.Changed += () => first++;
        diagnostics.Changed += () => second++;

        diagnostics.Report("stage", "消息");

        Assert.Equal(1, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public void Report_ASubscriberThatThrows_DoesNotStopTheOthers()
    {
        // A closed window is the ordinary case: it must not cost the other readers their update.
        ChatSendDiagnostics diagnostics = Diagnostics();
        int told = 0;
        diagnostics.Changed += () => throw new InvalidOperationException("窗口已关闭");
        diagnostics.Changed += () => told++;

        diagnostics.Report("stage", "消息");

        Assert.Equal(1, told);
    }

    [Fact]
    public void Report_ASubscriberThatThrows_DoesNotStopTheStatusUpdate()
    {
        ChatSendDiagnostics diagnostics = Diagnostics();
        diagnostics.Changed += () => throw new InvalidOperationException("窗口已关闭");

        diagnostics.Report("stage", "消息");

        Assert.Equal("stage", diagnostics.Current.Stage);
    }

    [Fact]
    public async Task Report_ASubscriberThatThrows_DoesNotStopTheLog()
    {
        ChatSendDiagnostics diagnostics = Diagnostics();
        diagnostics.Changed += () => throw new InvalidOperationException("窗口已关闭");

        diagnostics.Report("stage", "消息");

        Assert.Single(await LinesAsync(diagnostics));
    }

    [Fact]
    public void Report_WithoutAnySubscriber_IsJustFine()
    {
        ChatSendDiagnostics diagnostics = Diagnostics();

        diagnostics.Report("stage", "消息");

        Assert.Equal("消息", diagnostics.Current.Message);
    }

    // ---- the log's own trouble ----

    [Fact]
    public async Task LogError_AFileThatCannotBeWritten_IsReported()
    {
        // The panel shows this rather than the log failing silently.
        string blocker = _folder.File("blocker");
        await File.WriteAllTextAsync(blocker, "");
        ChatSendDiagnostics diagnostics = new(Path.Combine(blocker, "tier-chat.log"));
        _diagnostics = diagnostics;

        diagnostics.Report("stage", "消息");
        await diagnostics.FlushAsync();

        Assert.NotNull(diagnostics.LogError);
    }

    [Fact]
    public void LogError_AHealthyFile_IsNull()
    {
        Assert.Null(Diagnostics().LogError);
    }
}
