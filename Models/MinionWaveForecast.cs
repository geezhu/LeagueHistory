using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record WaveSpawn(int Number, double Time, int Melee, int Caster, int Siege)
{
    public int Count => Melee + Caster + Siege;

    /// <summary>Bounty of the whole wave at its upgrade level; set for the upcoming waves sent to the widget.</summary>
    public double Gold { get; init; }

    /// <summary>Experience of the whole wave; per-minion experience does not grow, so only the mix changes it.</summary>
    public double Experience { get; init; }
}

/// <summary>When the first and last minion of a wave reach a structure if nothing stops them.</summary>
public sealed record WaveArrival(string Target, double From, double To);

public sealed record LaneWaveForecast(string Name, WaveArrival Ally, WaveArrival Enemy, string Structures)
{
    public LaneProgress? Progress { get; init; }
}

/// <summary>A structure on the lane bar: 0 is the own inhibitor, 1 the enemy inhibitor.</summary>
public sealed record LaneMarker(double At, string Label, bool Ally, bool Alive, LaneStructureKind Kind);

/// <summary>
/// One wave on a lane, for timetables at any point: both teams spawn it together at opposite inhibitors and walk
/// at <see cref="Speed"/>; <see cref="Bonus"/> is the extra distance from the early side-lane speed bonus.
/// </summary>
public sealed record LaneWave(int Number, double Time, double Speed, double Bonus, bool Cannon);

/// <summary>
/// What the widget needs to time waves at a stop the user picks on the lane: recent and upcoming waves, the
/// structures along it, and <see cref="Hint"/>, which reads the lane from the structures each side has lost.
/// The bar runs nexus to nexus over <see cref="Length"/> units; waves spawn at the nexus, at fractions
/// <see cref="Spawn"/> (own) and <see cref="EnemySpawn"/> of the bar.
/// </summary>
public sealed record LaneProgress(
    double Length,
    double Spawn,
    double EnemySpawn,
    LaneWave[] Waves,
    LaneMarker[] Markers,
    string Hint
);

/// <summary>How long a champion would stay dead if it died now, from its level and the game time.</summary>
public sealed record DeathCost(bool Ally, string Champion, int Level, double Seconds, bool Self);

/// <summary>
/// Time-based wave estimate for the Game Bar. Times are game seconds; <see cref="Clock"/> is the wall clock
/// (Unix milliseconds) at <see cref="GameTime"/>, so the widget counts down without recalculating rules.
/// </summary>
public sealed record MinionWaveForecast(
    string Map,
    double GameTime,
    long Clock,
    WaveSpawn[] Spawns,
    string[] Notes,
    string Value,
    LaneWaveForecast[] Lanes
)
{
    public string[] UnknownStructures { get; init; } = [];

    public DeathCost[] Deaths { get; init; } = [];

    /// <summary>The latest wave already spawned; null before the first wave.</summary>
    public WaveSpawn? Current { get; init; }

    /// <summary>The time factor in percent, shown beside the death costs.</summary>
    public double DeathFactor { get; init; }

    public JObject ToJson() =>
        new()
        {
            ["protocol"] = 1,
            ["map"] = Map,
            ["gameTime"] = Math.Round(GameTime, 2),
            ["clock"] = Clock,
            ["spawns"] = new JArray(
                Spawns.Select(s => new JObject
                {
                    ["wave"] = s.Number,
                    ["time"] = Math.Round(s.Time, 2),
                    ["siege"] = s.Siege,
                    ["label"] = s.Siege > 0 ? "炮车波" : "普通波",
                    ["gold"] = Math.Round(s.Gold),
                    ["xp"] = Math.Round(s.Experience),
                })
            ),
            ["notes"] = new JArray(Notes),
            ["value"] = Value,
            ["lanes"] = new JArray(
                Lanes.Select(l => new JObject
                {
                    ["name"] = l.Name,
                    ["ally"] = Arrival(l.Ally),
                    ["enemy"] = Arrival(l.Enemy),
                    ["structures"] = l.Structures,
                    ["progress"] = Progress(l.Progress),
                })
            ),
            ["current"] =
                Current == null
                    ? null
                    : new JObject { ["wave"] = Current.Number, ["time"] = Math.Round(Current.Time, 2) },
            ["deathFactor"] = Math.Round(DeathFactor, 1),
            ["deaths"] = new JArray(
                Deaths.Select(d => new JObject
                {
                    ["ally"] = d.Ally,
                    ["champion"] = d.Champion,
                    ["level"] = d.Level,
                    ["seconds"] = Math.Round(d.Seconds, 1),
                    ["self"] = d.Self,
                })
            ),
        };

    private static JObject? Progress(LaneProgress? progress) =>
        progress == null
            ? null
            : new JObject
            {
                ["length"] = Math.Round(progress.Length),
                ["spawn"] = Math.Round(progress.Spawn, 4),
                ["enemySpawn"] = Math.Round(progress.EnemySpawn, 4),
                ["hint"] = progress.Hint,
                ["waves"] = new JArray(
                    progress.Waves.Select(w => new JObject
                    {
                        ["wave"] = w.Number,
                        ["time"] = Math.Round(w.Time, 2),
                        ["speed"] = w.Speed,
                        ["bonus"] = Math.Round(w.Bonus),
                        ["cannon"] = w.Cannon,
                    })
                ),
                ["markers"] = new JArray(
                    progress.Markers.Select(m => new JObject
                    {
                        ["at"] = Math.Round(m.At, 4),
                        ["label"] = m.Label,
                        ["ally"] = m.Ally,
                        ["alive"] = m.Alive,
                        ["kind"] = MarkerKind(m.Kind),
                    })
                ),
            };

    // The widget draws a turret, inhibitor or nexus icon; the three turret tiers share one icon.
    private static string MarkerKind(LaneStructureKind kind) =>
        kind switch
        {
            LaneStructureKind.Inhibitor => "inhibitor",
            LaneStructureKind.Nexus => "nexus",
            LaneStructureKind.Base => "base",
            _ => "turret",
        };

    private static JObject Arrival(WaveArrival arrival) =>
        new()
        {
            ["target"] = arrival.Target,
            ["from"] = Math.Round(arrival.From, 1),
            ["to"] = Math.Round(arrival.To, 1),
        };
}
