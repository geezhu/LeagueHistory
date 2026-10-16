namespace League.Services;

/// <summary>
/// 界面卡顿检测：后台每 250 毫秒向界面线程投递一个空操作，超过 2 秒没执行就记一条「界面无响应」，
/// 恢复时再记一共卡了多久。只能记录时间和当时的游戏阶段：.NET 无法在不附加调试器时取得界面线程的调用栈。
/// </summary>
public sealed class UiHangMonitor : IDisposable
{
    public const int ThresholdMilliseconds = 2000;

    private readonly object _gate = new();
    private readonly Action<Action> _post;
    private readonly Func<string> _context;
    private readonly AppLog _log;
    private readonly Func<long> _clock;
    private System.Threading.Timer? _timer;
    private bool _pending;
    private bool _reported;
    private long _sentAt;

    /// <param name="post">把一个操作排到界面线程执行，例如 Control.BeginInvoke。</param>
    /// <param name="context">卡顿时附带的说明，例如当前游戏阶段。</param>
    public UiHangMonitor(Action<Action> post, Func<string> context, AppLog? log = null, Func<long>? clock = null)
    {
        _post = post;
        _context = context;
        _log = log ?? AppLog.Shared;
        _clock = clock ?? (() => Environment.TickCount64);
    }

    public void Start() => _timer = new System.Threading.Timer(_ => Tick(), null, 250, 250);

    /// <summary>计时器每次触发时调用；测试里直接调用以控制时间。</summary>
    public void Tick()
    {
        long now = _clock();
        bool report = false;
        long waited = 0;
        lock (_gate)
        {
            if (!_pending)
            {
                _pending = true;
                _sentAt = now;
            }
            else
            {
                waited = now - _sentAt;
                if (!_reported && waited >= ThresholdMilliseconds)
                {
                    _reported = report = true;
                }

                // Still waiting for the previous probe; do not queue another behind it.
                if (!report)
                {
                    return;
                }
            }
        }

        if (report)
        {
            _log.Warn(LogCategory.Ui, $"界面已 {waited / 1000.0:0.#} 秒无响应（{SafeContext()}）");
            return;
        }

        try
        {
            _post(Responded);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The window is closing; there is no UI thread left to watch.
            Dispose();
        }
    }

    private void Responded()
    {
        long now = _clock();
        bool recovered;
        long waited;
        lock (_gate)
        {
            recovered = _reported;
            waited = now - _sentAt;
            _pending = _reported = false;
        }

        if (recovered)
        {
            _log.Warn(LogCategory.Ui, $"界面恢复响应，共卡住 {waited / 1000.0:0.#} 秒（{SafeContext()}）");
        }
    }

    private string SafeContext()
    {
        try
        {
            return _context();
        }
        catch (Exception ex)
        {
            // The description is optional; a failing lookup must not stop the hang record itself.
            return "状态未知：" + ex.GetType().Name;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
