namespace League.Services;

public sealed record ChatSendStatus(DateTime Time, string Stage, string Message);

/// <summary>Contains diagnostic stages only, never message bodies or authentication data.</summary>
public sealed class ChatSendDiagnostics
{
    // One file for every process; the process ID is on each line. Per-process files used to pile up without cleanup.
    public static ChatSendDiagnostics Shared { get; } = new(Path.Combine(LogRetention.LogsDirectory, "tier-chat.log"));
    private readonly object _gate = new();
    private readonly LogFileWriter _log;
    public string LogPath { get; }
    public string? LogError => _log.LastError;
    public ChatSendStatus Current { get; private set; } =
        new(DateTime.Now, "ready", "等待发送：游戏内 F9 / F11，或 Game Bar 的「发送」。");
    public event Action? Changed;

    public ChatSendDiagnostics(string path)
    {
        LogPath = path;
        _log = new LogFileWriter(path, 256 * 1024, 1000);
    }

    public void Report(string stage, string message)
    {
        // Current and the queued line change together so the status and the log keep the same order.
        lock (_gate)
        {
            Current = new(DateTime.Now, stage, message.Replace('\r', ' ').Replace('\n', ' '));
            _log.Write(
                $"{Current.Time:yyyy-MM-dd HH:mm:ss.fff} pid={Environment.ProcessId} [{stage}] {Current.Message}"
            );
        }
        foreach (var subscriber in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action)subscriber)();
            }
            catch
            {
                // A closed UI subscriber must not prevent logging or notifying the remaining subscribers.
            }
        }
    }

    public Task FlushAsync() => _log.FlushAsync();
}
