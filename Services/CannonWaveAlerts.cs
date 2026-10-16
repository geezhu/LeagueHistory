using League.Models;

namespace League.Services;

/// <summary>When the cannon-wave sound plays, as chosen on the widget's wave page.</summary>
public static class CannonSoundModes
{
    public const string Off = "off";

    /// <summary>As the wave before a cannon wave spawns: about one wave interval of warning.</summary>
    public const string Next = "next";

    /// <summary>As the cannon wave itself spawns.</summary>
    public const string Spawn = "spawn";

    public static bool IsKnown(string? mode) => mode is Off or Next or Spawn;
}

/// <summary>
/// Announces cannon waves from the wave forecast. Each poll remembers the upcoming spawns; a spawn whose time passed
/// since the previous poll is a crossing, announced once. The first poll and a jump of more than a few seconds (a
/// program started mid-game, a reconnect, a stalled poll) only remember, so no stale sound plays.
/// </summary>
public sealed class CannonWaveAlerts
{
    private const double MaxGap = 5;

    private WaveSpawn[] _upcoming = [];
    private double? _lastTime;

    /// <summary>Whether to play the cannon sound for this poll.</summary>
    public bool Update(IReadOnlyList<WaveSpawn>? spawns, double gameTime, string mode)
    {
        var before = _upcoming;
        double? last = _lastTime;
        _upcoming = spawns?.OrderBy(s => s.Time).ToArray() ?? [];
        _lastTime = gameTime;
        if (last is not double previous || gameTime <= previous || gameTime - previous > MaxGap)
        {
            return false;
        }

        for (int i = 0; i < before.Length; i++)
        {
            var spawn = before[i];
            if (spawn.Time <= previous || spawn.Time > gameTime)
            {
                continue;
            }

            bool cannon = mode switch
            {
                CannonSoundModes.Spawn => spawn.Siege > 0,
                CannonSoundModes.Next => i + 1 < before.Length && before[i + 1].Siege > 0,
                _ => false,
            };
            if (cannon)
            {
                return true;
            }
        }

        return false;
    }

    public void Reset()
    {
        _upcoming = [];
        _lastTime = null;
    }
}
