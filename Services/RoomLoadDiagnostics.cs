using System.Security.Cryptography;
using System.Text;

namespace League.Services;

/// <summary>Bounded background logging. No account names, raw player IDs, responses or credentials.</summary>
public sealed class RoomLoadDiagnostics
{
    public static RoomLoadDiagnostics Shared { get; } =
        new(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "League",
                "Logs",
                "room-players.log"
            )
        );
    private readonly object _gate = new();
    private readonly Queue<string> _pending = new();
    private Task _writer = Task.CompletedTask;
    private readonly string _salt = Guid.NewGuid().ToString("N");
    private bool _writing;
    public string LogPath { get; }

    public RoomLoadDiagnostics(string path) => LogPath = path;

    public void Report(long generation, string stage, string? key = null, string detail = "")
    {
        string team =
            key?.StartsWith("ally/") == true ? "ally"
            : key?.StartsWith("enemy/") == true ? "enemy"
            : "-";
        string? identity = team == "-" ? key : key![(key!.IndexOf('/') + 1)..];
        string player =
            identity == null
                ? "-"
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_salt + identity)))[..10];
        string line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} pid={Environment.ProcessId} generation={generation} stage={stage} team={team} player={player} {detail}";
        lock (_gate)
        {
            if (_pending.Count >= 500)
                _pending.Dequeue();
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
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 512 * 1024)
                    File.Move(LogPath, LogPath + ".previous", true);
                File.AppendAllLines(LogPath, lines, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public async Task FlushAsync()
    {
        while (true)
        {
            Task writer;
            lock (_gate)
                writer = _writer;
            await writer;
            lock (_gate)
                if (!_writing)
                    return;
        }
    }
}
