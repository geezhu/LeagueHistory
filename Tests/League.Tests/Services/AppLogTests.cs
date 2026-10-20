using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The application log's own rules: a session header written once, a warning repeated in quick succession counted
/// instead of written, a state written only when it changes, and the crash line written before the process can go away.
///
/// The clock is injected, so the sixty-second window is driven by hand here rather than by waiting.
/// </summary>
public class AppLogTests : IAsyncLifetime
{
    private readonly TempFolder _folder = new("applog");
    private long _now = 1_000_000;
    private AppLog? _log;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_log is not null)
        {
            await _log.FlushAsync();
        }

        _folder.Dispose();
    }

    private AppLog Log()
    {
        string path = _folder.File("app.log");
        _log = new(() => path, clock: () => _now);
        return _log;
    }

    private static async Task<string[]> LinesAsync(AppLog log)
    {
        await log.FlushAsync();
        return File.Exists(log.CurrentPath) ? await File.ReadAllLinesAsync(log.CurrentPath) : [];
    }

    /// <summary>The lines that are not the session header, so a case can talk about its own entries only.</summary>
    private static async Task<string[]> EntriesAsync(AppLog log) =>
        [.. (await LinesAsync(log)).Where(line => !line.Contains("session version=", StringComparison.Ordinal))];

    // ---- the header ----

    [Fact]
    public async Task TheFirstWrite_PutsASessionHeaderInFront()
    {
        AppLog log = Log();

        log.Info(LogCategory.App, "first");
        string[] lines = await LinesAsync(log);

        Assert.Equal(2, lines.Length);
        Assert.Contains("[info] [App] session version=", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHeader_IsWrittenOnce()
    {
        AppLog log = Log();

        log.Info(LogCategory.App, "one");
        log.Info(LogCategory.App, "two");
        string[] lines = await LinesAsync(log);

        Assert.Equal(1, lines.Count(line => line.Contains("session version=", StringComparison.Ordinal)));
    }

    // ---- the shape of an entry ----

    [Fact]
    public async Task AnEntry_CarriesItsLevelCategoryAndMessage()
    {
        AppLog log = Log();

        log.Info(LogCategory.Data, "读取完成");
        string entry = Assert.Single(await EntriesAsync(log));

        Assert.Contains(" pid=", entry, StringComparison.Ordinal);
        Assert.Contains("[info] [Data] 读取完成", entry, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("warn")]
    [InlineData("error")]
    public async Task AWarningOrAnError_IsMarkedAsSuch(string level)
    {
        AppLog log = Log();
        if (level == "warn")
        {
            log.Warn(LogCategory.Data, "出错了");
        }
        else
        {
            log.Error(LogCategory.Data, "出错了");
        }

        Assert.Contains(
            "[" + level + "] [Data] 出错了",
            Assert.Single(await EntriesAsync(log)),
            StringComparison.Ordinal
        );
    }

    // ---- repeating ----

    [Fact]
    public async Task AWarningRepeatedWithinTheWindow_IsCountedNotWritten()
    {
        // One page of failing icons should not produce one line per icon.
        AppLog log = Log();

        log.Warn(LogCategory.Data, "图标加载失败");
        _now += 1_000;
        log.Warn(LogCategory.Data, "图标加载失败");
        _now += 1_000;
        log.Warn(LogCategory.Data, "图标加载失败");

        Assert.Single(await EntriesAsync(log));
    }

    [Fact]
    public async Task AWarningRepeatedAfterTheWindow_IsWrittenAgainWithTheCount()
    {
        // The count is what makes the gap between the two lines readable.
        AppLog log = Log();

        log.Warn(LogCategory.Data, "图标加载失败");
        _now += 2_000;
        log.Warn(LogCategory.Data, "图标加载失败");
        _now += 60_000 + 1;
        log.Warn(LogCategory.Data, "图标加载失败");

        string[] entries = await EntriesAsync(log);
        Assert.Equal(2, entries.Length);
        Assert.Contains("此前 60 秒内又出现 1 次", entries[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADifferentMessage_IsNeverCounted()
    {
        AppLog log = Log();

        log.Warn(LogCategory.Data, "第一个");
        log.Warn(LogCategory.Data, "第二个");

        Assert.Equal(2, (await EntriesAsync(log)).Length);
    }

    [Fact]
    public async Task TheSameMessageInAnotherCategory_IsNotARepeat()
    {
        // The category is part of what makes two warnings "the same", so different subjects are not merged.
        AppLog log = Log();

        log.Warn(LogCategory.Data, "出错了");
        log.Warn(LogCategory.Ui, "出错了");

        Assert.Equal(2, (await EntriesAsync(log)).Length);
    }

    [Fact]
    public async Task AnInfoRepeated_IsWrittenEveryTime()
    {
        // Only warnings and errors are counted; an info line is there because someone wanted it.
        AppLog log = Log();

        log.Info(LogCategory.Data, "轮询");
        log.Info(LogCategory.Data, "轮询");

        Assert.Equal(2, (await EntriesAsync(log)).Length);
    }

    // ---- states ----

    [Fact]
    public async Task AStateWrittenTwiceUnchanged_IsRecordedOnce()
    {
        // Polling reports the same state every few seconds; only a change is worth a line.
        AppLog log = Log();

        log.State(LogCategory.Data, "client", "未启动");
        log.State(LogCategory.Data, "client", "未启动");

        Assert.Single(await EntriesAsync(log));
    }

    [Fact]
    public async Task AStateThatChanged_IsRecordedAgain()
    {
        AppLog log = Log();

        log.State(LogCategory.Data, "client", "未启动");
        log.State(LogCategory.Data, "client", "已启动");

        Assert.Equal(2, (await EntriesAsync(log)).Length);
    }

    [Fact]
    public async Task AStateGoingBackToAnEarlierValue_IsRecordedAgain()
    {
        // The comparison is with the previous value, not with everything ever seen.
        AppLog log = Log();

        log.State(LogCategory.Data, "client", "未启动");
        log.State(LogCategory.Data, "client", "已启动");
        log.State(LogCategory.Data, "client", "未启动");

        Assert.Equal(3, (await EntriesAsync(log)).Length);
    }

    [Fact]
    public async Task ResettingAState_MakesTheNextSameValueRecorded()
    {
        // A new game resets the subjects it cares about, so the first report in it is worth a line.
        AppLog log = Log();
        log.State(LogCategory.Data, "client", "未启动");

        log.ResetStates("client");
        log.State(LogCategory.Data, "client", "未启动");

        Assert.Equal(2, (await EntriesAsync(log)).Length);
    }

    [Fact]
    public async Task TwoStatesWithDifferentKeys_AreIndependent()
    {
        AppLog log = Log();

        log.State(LogCategory.Data, "client", "未启动");
        log.State(LogCategory.Data, "game", "未启动");

        Assert.Equal(2, (await EntriesAsync(log)).Length);
    }

    // ---- errors attached to a line ----

    [Fact]
    public async Task AWarningWithAnError_NamesItsTypeAndResult()
    {
        AppLog log = Log();

        log.Warn(LogCategory.Data, "保存失败", new IOException("磁盘已满"));
        string entry = Assert.Single(await EntriesAsync(log));

        Assert.Contains("保存失败 | IOException", entry, StringComparison.Ordinal);
        Assert.Contains("HRESULT=", entry, StringComparison.Ordinal);
        Assert.Contains("磁盘已满", entry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWarningWithANestedError_NamesTheRootCause()
    {
        // What matters is the cause at the bottom, so it is named alongside the one that was caught.
        AppLog log = Log();

        log.Warn(LogCategory.Data, "保存失败", new InvalidOperationException("外层", new IOException("根因")));
        string entry = Assert.Single(await EntriesAsync(log));

        Assert.Contains(" | InvalidOperationException <- IOException", entry, StringComparison.Ordinal);
    }

    // ---- crashing ----

    [Fact]
    public async Task Crash_IsWrittenWithTheStackOnOneLine()
    {
        // The process may be about to go away, so this line carries the stack and cannot wait for a background write.
        AppLog log = Log();
        // A stack exists only once it has been thrown, and the stack is what this case is about.
        Exception caught;
        try
        {
            throw new InvalidOperationException("坏了");
        }
        catch (Exception error)
        {
            caught = error;
        }

        log.Crash("崩溃了", caught);

        string[] entries = await EntriesAsync(log);
        string crash = entries.First(line => line.Contains("[fatal]", StringComparison.Ordinal));
        Assert.Contains("[Crash] 崩溃了 | InvalidOperationException", crash, StringComparison.Ordinal);
        Assert.Contains(" ⏎ ", crash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Crash_WritesTheHeaderEvenWhenNothingElseDid()
    {
        AppLog log = Log();
        // A stack exists only once it has been thrown, and the stack is what this case is about.
        Exception caught;
        try
        {
            throw new InvalidOperationException("坏了");
        }
        catch (Exception error)
        {
            caught = error;
        }

        log.Crash("崩溃了", caught);

        Assert.Contains(await LinesAsync(log), line => line.Contains("session version=", StringComparison.Ordinal));
    }

    // ---- the environment line ----

    [Fact]
    public void DescribeEnvironment_NamesTheVersionTheSystemAndTheRuntime()
    {
        string described = AppLog.DescribeEnvironment();

        Assert.Contains("version=", described, StringComparison.Ordinal);
        Assert.Contains("os=", described, StringComparison.Ordinal);
        Assert.Contains("dotnet=", described, StringComparison.Ordinal);
        Assert.Contains("admin=", described, StringComparison.Ordinal);
    }
}
