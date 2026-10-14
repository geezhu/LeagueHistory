using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>
/// The active player against the next enemy turret on a lane, at the turret's full health: basic attacks to take it
/// with own minions near and without them, and the turret's warming-up shots against the player.
/// </summary>
public sealed record TurretSiege(
    string Target,
    double HitDamage,
    bool Magic,
    int Hits,
    double Seconds,
    int HitsAlone,
    double SecondsAlone,
    double[] Shots,
    int ShotsToDown
)
{
    /// <summary>The fortification counted in <see cref="HitDamage"/>, as a fraction; zero when none is active.</summary>
    public double Fortification { get; init; }

    public JObject ToJson() =>
        new()
        {
            ["target"] = Target,
            ["hit"] = Math.Round(HitDamage),
            ["magic"] = Magic,
            ["hits"] = Hits,
            ["seconds"] = Math.Round(Seconds, 1),
            ["hitsAlone"] = HitsAlone,
            ["secondsAlone"] = Math.Round(SecondsAlone, 1),
            ["shots"] = new JArray(Shots.Select(s => Math.Round(s))),
            ["shotsToDown"] = ShotsToDown,
            ["fortification"] = Math.Round(Fortification, 2),
        };
}
