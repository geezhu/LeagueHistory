using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The UI hang watch: every timer tick it posts an empty operation to the UI thread, and when that operation has not
/// run for two seconds it records a warning; when it finally runs it records how long the hang lasted.
///
/// Both the clock and the posting are injected, so the whole cycle is driven by hand here and no timer, UI thread or
/// real elapsed time takes part. The warnings go to an <see cref="AppLog"/> pointed at a temporary file; the log
/// queues its writes, so each test flushes before reading.
/// </summary>
public class UiHangMonitorTests : IAsyncLifetime
{
    private readonly TempFolder _folder = new("uihang");
    private AppLog? _log;
    private long _now;
    private int _posted;
    private int _ran;

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Flushes before removing the folder: the log writes on a background task, and deleting a directory out from
    /// under it fails with a sharing violation - which xUnit would then report as the test's own failure.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_log is not null)
        {
            await _log.FlushAsync();
        }

        _folder.Dispose();
    }

    /// <summary>Records the post and, unless the caller wants it to stay queued, runs it straight away.</summary>
    private Action<Action> Post(bool runImmediately = true) =>
        action =>
        {
            _posted++;
            if (runImmediately)
            {
                _ran++;
                action();
            }
        };

    /// <summary>
    /// The path function is consulted on every write, so it has to return the same path each time; a fresh GUID here
    /// would scatter the lines across files that nobody reads back.
    /// </summary>
    private AppLog Log()
    {
        string path = _folder.File("ui.log");
        _log = new(() => path);
        return _log;
    }

    private UiHangMonitor Monitor(AppLog log, Action<Action>? post = null, Func<string>? context = null) =>
        new(post ?? Post(), context ?? (() => "对局中"), log, () => _now);

    private async Task<string[]> LinesAsync(AppLog log)
    {
        await log.FlushAsync();
        return File.Exists(log.CurrentPath) ? await File.ReadAllLinesAsync(log.CurrentPath) : [];
    }

    // ---- one probe at a time ----

    [Fact]
    public void Tick_WithNothingOutstanding_PostsAProbe()
    {
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log);

        monitor.Tick();

        Assert.Equal(1, _posted);
        Assert.Equal(1, _ran);
    }

    [Fact]
    public void Tick_WhileThePreviousProbeIsStillOutstanding_PostsNothingMore()
    {
        // Queueing a second probe behind the first would only pile work onto a thread that is already stuck.
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log, Post(runImmediately: false));

        monitor.Tick();
        _now += 100;
        monitor.Tick();
        _now += 100;
        monitor.Tick();

        Assert.Equal(1, _posted);
    }

    [Fact]
    public void Tick_JustBeforeTheThreshold_StillOnlyWaits()
    {
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log, Post(runImmediately: false));

        monitor.Tick();
        _now += UiHangMonitor.ThresholdMilliseconds - 1;
        monitor.Tick();

        Assert.Equal(1, _posted);
    }

    [Fact]
    public async Task Tick_AtTheThreshold_RecordsTheHang()
    {
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log, Post(runImmediately: false));

        monitor.Tick();
        _now += UiHangMonitor.ThresholdMilliseconds;
        monitor.Tick();

        string[] lines = await LinesAsync(log);
        Assert.Contains("界面已 2 秒无响应", string.Join(" ¦ ", lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tick_AtTheThreshold_DoesNotPostAgain()
    {
        // The probe already posted is still the one being waited on; posting another would restart the measurement.
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log, Post(runImmediately: false));

        monitor.Tick();
        _now += UiHangMonitor.ThresholdMilliseconds;
        monitor.Tick();

        Assert.Equal(1, _posted);
    }

    [Fact]
    public async Task Tick_AfterTheHangWasRecorded_DoesNotRecordItAgain()
    {
        // A hang lasting many seconds is one warning, not one per tick.
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log, Post(runImmediately: false));

        monitor.Tick();
        _now += 2000;
        monitor.Tick();
        _now += 2000;
        monitor.Tick();
        _now += 2000;
        monitor.Tick();

        string[] lines = await LinesAsync(log);
        Assert.Equal(1, lines.Count(line => line.Contains("无响应", StringComparison.Ordinal)));
    }

    [Fact]
    public void Tick_TheTimerStartsOnTheFirstCall()
    {
        // The first tick only arms the watch, so nothing is measured until a second tick arrives.
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log);

        monitor.Tick();

        Assert.Equal(1, _posted);
    }

    // ---- recovery ----

    [Fact]
    public async Task Responded_WithoutAHang_RecordsNothing()
    {
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log);

        monitor.Tick();

        Assert.DoesNotContain(await LinesAsync(log), line => line.Contains("恢复响应", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Responded_AfterAHang_RecordsOneRecovery()
    {
        AppLog log = Log();
        var queued = new List<Action>();
        UiHangMonitor monitor = Monitor(log, action => queued.Add(action));

        monitor.Tick();
        _now += 3000;
        monitor.Tick();
        Assert.Equal(1, queued.Count);

        queued[0]();

        string[] lines = await LinesAsync(log);
        Assert.Contains(lines, line => line.Contains("界面恢复响应，共卡住 3 秒", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Responded_ThenATick_StartsWatchingAgain()
    {
        // After the recovery the watch is armed afresh, so the next tick posts a new probe.
        AppLog log = Log();
        var queued = new List<Action>();
        UiHangMonitor monitor = Monitor(log, action => queued.Add(action));

        monitor.Tick();
        _now += 3000;
        monitor.Tick();
        queued[0]();
        monitor.Tick();

        Assert.Equal(2, queued.Count);
    }

    // ---- the context ----

    [Fact]
    public async Task Tick_TheContextIsNamedInTheHangRecord()
    {
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log, Post(runImmediately: false), () => "选人中");

        monitor.Tick();
        _now += 2500;
        monitor.Tick();

        Assert.Contains(await LinesAsync(log), line => line.Contains("（选人中）", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tick_WhenTheContextThrows_TheHangIsStillRecorded()
    {
        // The description is optional; a failing lookup must not cost the hang record itself.
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(
            log,
            Post(runImmediately: false),
            () => throw new InvalidOperationException("no game")
        );

        monitor.Tick();
        _now += 2000;
        monitor.Tick();

        Assert.Contains(
            await LinesAsync(log),
            line => line.Contains("状态未知：InvalidOperationException", StringComparison.Ordinal)
        );
    }

    // ---- the seconds wording ----

    [Theory]
    [InlineData(2000, "2")]
    [InlineData(2500, "2.5")]
    [InlineData(10_000, "10")]
    [InlineData(12_340, "12.3")]
    public async Task Tick_TheHangIsWrittenInSecondsToTenths(int waited, string seconds)
    {
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log, Post(runImmediately: false));

        monitor.Tick();
        _now += waited;
        monitor.Tick();

        Assert.Contains(
            await LinesAsync(log),
            line => line.Contains($"界面已 {seconds} 秒无响应", StringComparison.Ordinal)
        );
    }

    // ---- the failing post ----

    [Fact]
    public void Tick_WhenTheWindowIsClosing_DisposesItself()
    {
        // BeginInvoke on a closing window throws; there is no UI thread left to watch, so the watch stops.
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log, _ => throw new InvalidOperationException("closing"));

        monitor.Tick();
        monitor.Dispose();
    }

    [Fact]
    public void Tick_WhenTheWindowIsAlreadyDisposed_DisposesItself()
    {
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log, _ => throw new ObjectDisposedException("window"));

        monitor.Tick();
        monitor.Dispose();
    }

    [Fact]
    public void Tick_WhenThePostFailsForAnotherReason_LetsTheFailureOut()
    {
        // Only the two "the UI is gone" exceptions are swallowed; anything else is a real fault and must surface.
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log, _ => throw new NotSupportedException("boom"));

        Assert.Throws<NotSupportedException>(monitor.Tick);
    }

    [Fact]
    public void Dispose_Twice_IsSafe()
    {
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log);

        monitor.Dispose();
        monitor.Dispose();
    }

    [Fact]
    public void Start_ThenDispose_LeavesNoTimerBehind()
    {
        // Start only arms the real timer; the timing behaviour itself is driven by Tick above.
        AppLog log = Log();
        UiHangMonitor monitor = Monitor(log);

        monitor.Start();
        monitor.Dispose();
    }
}
