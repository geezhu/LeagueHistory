using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record WaveSpawn(int Number, double Time, int Melee, int Caster, int Siege)
{
    public int Count => Melee + Caster + Siege;
}

/// <summary>When the first and last minion of a wave reach a structure if nothing stops them.</summary>
public sealed record WaveArrival(string Target, double From, double To);

public sealed record LaneWaveForecast(string Name, WaveArrival Ally, WaveArrival Enemy, string Structures)
{
    public LaneProgress? Progress { get; init; }
}

/// <summary>A structure on the lane bar: 0 is the own inhibitor, 1 the enemy inhibitor.</summary>
public sealed record LaneMarker(double At, string Label, bool Ally, bool Alive);

/// <summary>
/// What the widget needs to move the latest waves along the lane: both teams spawn together at opposite
/// inhibitors, so without interference they meet halfway. <see cref="Hint"/> reads the lane from structures.
/// </summary>
public sealed record LaneProgress(double Length, double Speed, double Bonus, LaneMarker[] Markers, string Hint);

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
                ["speed"] = progress.Speed,
                ["bonus"] = Math.Round(progress.Bonus),
                ["hint"] = progress.Hint,
                ["markers"] = new JArray(
                    progress.Markers.Select(m => new JObject
                    {
                        ["at"] = Math.Round(m.At, 4),
                        ["label"] = m.Label,
                        ["ally"] = m.Ally,
                        ["alive"] = m.Alive,
                    })
                ),
            };

    private static JObject Arrival(WaveArrival arrival) =>
        new()
        {
            ["target"] = arrival.Target,
            ["from"] = Math.Round(arrival.From, 1),
            ["to"] = Math.Round(arrival.To, 1),
        };
}
