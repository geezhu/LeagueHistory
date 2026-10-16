using System.Security.Principal;
using League.Infrastructure;

namespace League.Services;

public enum LogCategory
{
    App,
    Connection,
    Sgp,
    Update,
    Crash,

    // 游戏阶段变化和对局流程。
    Game,

    // 自动接受、自动预选、换皮肤、结算后返回房间等替用户执行的操作。
    Automation,

    // 战绩、召唤师、英雄与图片等数据读取和解析。
    Data,

    // 界面展示和交互。
    Ui,
}

/// <summary>
/// 应用日志：按天一个文件（app-yyyy-MM-dd.log），每个进程第一次写入时先记版本、commit 和系统信息。
/// 只记阶段、状态码和异常类型；写入前经过 <see cref="DiagnosticRedactor"/>，不写令牌、玩家名或原始玩家 ID。
/// 调试版同时输出到调试器，替代原来 Release 版里会被去掉的 Debug.WriteLine。
/// </summary>
public sealed class AppLog
{
    public static AppLog Shared { get; } =
        new(() => Path.Combine(LogRetention.LogsDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log"));

    // Identical warnings within this window are counted instead of written, e.g. a page of icons failing offline.
    private const long RepeatWindowMilliseconds = 60_000;

    private readonly LogFileWriter _log;
    private readonly DiagnosticRedactor _redactor;
    private readonly Func<long> _clock;
    private readonly Dictionary<string, string> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Since, int Suppressed)> _repeats = new(StringComparer.Ordinal);
    private int _headerWritten;

    public AppLog(Func<string> path, DiagnosticRedactor? redactor = null, Func<long>? clock = null)
    {
        _log = new LogFileWriter(path, 4 * 1024 * 1024, 2000);
        _redactor = redactor ?? DiagnosticRedactor.Shared;
        _clock = clock ?? (() => Environment.TickCount64);
    }

    public string CurrentPath => _log.CurrentPath;

    public void Info(LogCategory category, string message) => Write("info", category, message);

    public void Warn(LogCategory category, string message, Exception? error = null) =>
        WriteUnlessRepeated("warn", category, WithError(message, error, includeStack: false));

    public void Error(LogCategory category, string message, Exception? error = null) =>
        WriteUnlessRepeated("error", category, WithError(message, error, includeStack: false));

    private void WriteUnlessRepeated(string level, LogCategory category, string message)
    {
        string key = $"{level}|{category}|{message}";
        long now = _clock();
        int suppressed;
        lock (_repeats)
        {
            if (_repeats.TryGetValue(key, out var seen) && now - seen.Since < RepeatWindowMilliseconds)
            {
                _repeats[key] = (seen.Since, seen.Suppressed + 1);
                return;
            }

            suppressed = _repeats.TryGetValue(key, out seen) ? seen.Suppressed : 0;
            _repeats[key] = (now, 0);
        }
        Write(level, category, suppressed > 0 ? $"{message}（此前 60 秒内又出现 {suppressed} 次）" : message);
    }

    /// <summary>
    /// 同一件事只在状态变化时记一次，例如每 5 秒的连接轮询只记「客户端未启动 → 已找到客户端」。
    /// </summary>
    public void State(LogCategory category, string key, string message)
    {
        lock (_states)
        {
            if (_states.TryGetValue(key, out var previous) && previous == message)
            {
                return;
            }

            _states[key] = message;
        }
        Write("info", category, message);
    }

    /// <summary>忘记这些状态，下一次即使内容相同也会再记一次，例如进入新的一局选人。</summary>
    public void ResetStates(params string[] keys)
    {
        lock (_states)
        {
            foreach (var key in keys)
            {
                _states.Remove(key);
            }
        }
    }

    /// <summary>崩溃时同步写入完整调用栈；进程可能马上退出，不能等后台写入。</summary>
    public void Crash(string message, Exception error)
    {
        EnsureHeader(synchronous: true);
        _log.WriteNow(Format("fatal", LogCategory.Crash, WithError(message, error, includeStack: true)));
    }

    public Task FlushAsync() => _log.FlushAsync();

    private void Write(string level, LogCategory category, string message)
    {
        EnsureHeader(synchronous: false);
        string line = Format(level, category, message);
        System.Diagnostics.Debug.WriteLine(line);
        _log.Write(line);
    }

    private string Format(string level, LogCategory category, string message) =>
        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} pid={Environment.ProcessId} [{level}] [{category}] {_redactor.Redact(message)}";

    private static string WithError(string message, Exception? error, bool includeStack)
    {
        if (error == null)
        {
            return message;
        }

        var root = error.GetBaseException();
        string summary = $"{message} | {error.GetType().Name}";
        if (root != error)
        {
            summary += $" <- {root.GetType().Name}";
        }

        summary += $" HRESULT={root.HResult:X8}: {root.Message}";
        return includeStack ? summary + " | " + error.ToString().Replace(Environment.NewLine, " ⏎ ") : summary;
    }

    private void EnsureHeader(bool synchronous)
    {
        if (Interlocked.Exchange(ref _headerWritten, 1) != 0)
        {
            return;
        }

        string header = Format("info", LogCategory.App, "session " + DescribeEnvironment());
        if (synchronous)
        {
            _log.WriteNow(header);
        }
        else
        {
            _log.Write(header);
        }
    }

    public static string DescribeEnvironment()
    {
        var build = BuildInfo.Current;
        return $"version={build.Version} commit={build.ShortCommit} dirty={build.Dirty?.ToString() ?? "unknown"} "
            + $"os={Environment.OSVersion.Version} 64bit={Environment.Is64BitOperatingSystem} "
            + $"dotnet={Environment.Version} admin={IsAdministrator()}";
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
