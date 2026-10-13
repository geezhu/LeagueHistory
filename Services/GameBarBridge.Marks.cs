using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

public sealed partial class GameBarBridge
{
    /// <summary>The widget's respawn marks for the current session; null when missing, stale or malformed.</summary>
    public async Task<RespawnMarks?> ReadRespawnMarksAsync()
    {
        var scope = _detailScope;
        string directory = _testDirectory ?? _directory;
        if (_disposed || scope.Session.Length == 0 || directory.Length == 0)
        {
            return null;
        }

        string path = Path.Combine(directory, "respawn-marks.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4096)
            {
                return null;
            }

            var marks = RespawnMarks.Parse(
                JObject.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false)),
                scope.Session
            );
            // A mark names a slot and the champion seen there; a changed slot is no longer the marked player.
            return marks == null
                ? null
                : marks with
                {
                    Slots = marks
                        .Slots.Where((slot, i) => scope.Heroes.ElementAtOrDefault(slot) == marks.Heroes[i])
                        .ToArray(),
                };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            // The widget replaces the file atomically; a read during the swap is retried on the next poll.
            return null;
        }
    }
}
