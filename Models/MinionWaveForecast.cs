using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record WaveSpawn(int Number, double Time, int Melee, int Caster, int Siege)
{
    public int Count => Melee + Caster + Siege;
}

/// <summary>When the first and last minion of a wave reach a structure if nothing stops them.</summary>
public sealed record WaveArrival(string Target, double From, double To);

public sealed record LaneWaveForecast(string Name, WaveArrival Ally, WaveArrival Enemy, string Structures);

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
