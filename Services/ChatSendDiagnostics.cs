using System.Text;

namespace League.Services;

public sealed record ChatSendStatus(DateTime Time, string Stage, string Message);

/// <summary>Contains diagnostic stages only, never message bodies or authentication data.</summary>
public sealed class ChatSendDiagnostics
{
    public static ChatSendDiagnostics Shared { get; } =
        new(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "League",
                "Logs",
                $"tier-chat-{Environment.ProcessId}.log"
            )
        );
    private readonly object _gate = new();
    private readonly Queue<string> _pending = new();
    private Task _writer = Task.CompletedTask;
    private bool _writing;
    public string LogPath { get; }
    public string? LogError { get; private set; }
    public ChatSendStatus Current { get; private set; } =
        new(DateTime.Now, "ready", "等待快捷键：Ctrl + F9 我方，Ctrl + F11 对方。");
    public event Action? Changed;

    public ChatSendDiagnostics(string path) => LogPath = path;

    public void Report(string stage, string message)
    {
        lock (_gate)
        {
            Current = new(DateTime.Now, stage, message.Replace('\r', ' ').Replace('\n', ' '));
            _pending.Enqueue($"{Current.Time:yyyy-MM-dd HH:mm:ss.fff} [{stage}] {Current.Message}");
            if (!_writing)
            {
                _writing = true;
                _writer = Task.Run(WritePending);
            }
        }
        foreach (var subscriber in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action)subscriber)();
            }
            catch { }
        }
    }

    public async Task FlushAsync()
    {
        while (true)
        {
            Task writer;
            lock (_gate)
            {
                writer = _writer;
            }

            await writer;
            lock (_gate)
            {
                if (!_writing)
                {
                    return;
                }
            }
        }
    }

    private void WritePending()
    {
        while (true)
        {
            string line;
            lock (_gate)
            {
                if (!_pending.TryDequeue(out line!))
                {
                    _writing = false;
                    return;
                }
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 256 * 1024)
                {
                    File.Move(LogPath, LogPath + ".previous", true);
                }

                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
                LogError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogError = "日志保存失败：" + ex.GetType().Name;
            }
        }
    }
}
