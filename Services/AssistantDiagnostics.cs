using System.Text;

namespace League.Services;

/// <summary>
/// Bounded background log for champion select assistant failures. The panel shows a readable reason;
/// exception types and messages go here. No account names, player IDs or chat text are written.
/// </summary>
public sealed class AssistantDiagnostics
{
    public static AssistantDiagnostics Shared { get; } =
        new(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "League",
                "Logs",
                "assistant.log"
            )
        );
    private readonly object _gate = new();
    private readonly Queue<string> _pending = new();
    private Task _writer = Task.CompletedTask;
    private bool _writing;
    public string LogPath { get; }

    public AssistantDiagnostics(string path) => LogPath = path;

    public void Report(string stage, Exception error)
    {
        var root = error.GetBaseException();
        string line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} pid={Environment.ProcessId} stage={stage} "
            + $"error={error.GetType().Name} root={root.GetType().Name} message={root.Message}";
        lock (_gate)
        {
            if (_pending.Count >= 200)
            {
                _pending.Dequeue();
            }

            _pending.Enqueue(line.Replace('\r', ' ').Replace('\n', ' '));
            if (!_writing)
            {
                _writing = true;
                _writer = Task.Run(WritePending);
            }
        }
    }

    private void WritePending()
    {
        while (true)
        {
            string[] lines;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    _writing = false;
                    return;
                }
                lines = _pending.ToArray();
                _pending.Clear();
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 256 * 1024)
                {
                    File.Move(LogPath, LogPath + ".previous", true);
                }

                File.AppendAllLines(LogPath, lines, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Diagnostics are optional; a locked log must not interrupt the assistant.
                System.Diagnostics.Debug.WriteLine("assistant log write: " + ex.GetType().Name);
            }
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
}
