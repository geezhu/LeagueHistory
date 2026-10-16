using System.Security.Cryptography;
using System.Text;

namespace League.Services;

/// <summary>Bounded background logging. No account names, raw player IDs, responses or credentials.</summary>
public sealed class RoomLoadDiagnostics
{
    public static RoomLoadDiagnostics Shared { get; } =
        new(Path.Combine(LogRetention.LogsDirectory, "room-players.log"));
    private readonly string _salt = Guid.NewGuid().ToString("N");
    private readonly LogFileWriter _log;
    public string LogPath { get; }

    public RoomLoadDiagnostics(string path)
    {
        LogPath = path;
        _log = new LogFileWriter(path, 512 * 1024, 500);
    }

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
        _log.Write(
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} pid={Environment.ProcessId} generation={generation} stage={stage} team={team} player={player} {detail}"
        );
    }

    public Task FlushAsync() => _log.FlushAsync();
}
