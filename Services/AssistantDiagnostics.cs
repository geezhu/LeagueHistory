namespace League.Services;

/// <summary>
/// Bounded background log for champion select assistant failures. The panel shows a readable reason;
/// exception types and messages go here. No account names, player IDs or chat text are written.
/// </summary>
public sealed class AssistantDiagnostics
{
    public static AssistantDiagnostics Shared { get; } = new(Path.Combine(LogRetention.LogsDirectory, "assistant.log"));
    private readonly LogFileWriter _log;
    public string LogPath { get; }

    public AssistantDiagnostics(string path)
    {
        LogPath = path;
        _log = new LogFileWriter(path, 256 * 1024, 200);
    }

    public void Report(string stage, Exception error)
    {
        var root = error.GetBaseException();
        _log.Write(
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} pid={Environment.ProcessId} stage={stage} "
                + $"error={error.GetType().Name} root={root.GetType().Name} message={root.Message}"
        );
    }

    public Task FlushAsync() => _log.FlushAsync();
}
