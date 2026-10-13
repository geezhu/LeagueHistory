namespace League.Models;

/// <summary>A value that changes at a game time; the last stage whose start has passed applies.</summary>
public sealed record TimedValue<T>(double Start, T Value);

/// <summary>Bounty of one minion: base plus growth per upgrade, with the growth capped.</summary>
public sealed record MinionGold(double Base, double Growth, double MaxBonus)
{
    public double At(int upgrades) => Base + Math.Min(Growth * upgrades, MaxBonus);
}

/// <summary>Movement speed: base, then a fixed step at a regular interval after a delay, a limited number of times.</summary>
public sealed record MinionSpeed(double Base, double Delay, double Interval, double Step, int Steps)
{
    public double At(double time) =>
        time < Delay ? Base : Base + Step * Math.Min(Steps, (int)Math.Floor((time - Delay) / Interval) + 1);
}

/// <summary>
/// Wave rules for one map family. Values come from the 16.20 BarracksConfig and minion CharacterRecord data;
/// Summoner's Rift late intervals come from the 26.1 patch notes (see docs/minion-wave-requirements-draft.md).
/// Rotating patterns are indexed by wave number, so [0, 0, 1] puts a siege minion in waves 3, 6, 9.
/// </summary>
public sealed record MinionWaveRules(
    string Name,
    double FirstWave,
    TimedValue<double>[] Intervals,
    TimedValue<int[]>[] Melee,
    TimedValue<int[]>[] Caster,
    TimedValue<int[]>[] Siege,
    int[] SuperPerInhibitorsDown,
    double UpgradeInterval,
    MinionGold MeleeGold,
    MinionGold CasterGold,
    MinionGold SiegeGold,
    MinionGold SuperGold,
    MinionSpeed Speed,
    MinionLaneMap Lanes
)
{
    public const double SpawnSpacing = 0.8;

    /// <summary>Experience per melee, caster, siege and super minion.</summary>
    public required double[] Experience { get; init; }

    /// <summary>Seconds until a destroyed inhibitor returns; null where the value is not confirmed.</summary>
    public double? InhibitorRespawn { get; init; }

    /// <summary>Summoner's Rift side lanes: extra speed on early waves so they meet mid-lane timing.</summary>
    public bool SideLaneBonus { get; init; }

    public static T Stage<T>(TimedValue<T>[] stages, double time)
    {
        var current = stages[0];
        foreach (var stage in stages.Where(s => s.Start <= time))
        {
            current = stage;
        }
        return current.Value;
    }

    public static int Rotate(int[] pattern, int wave) => pattern[(wave - 1) % pattern.Length];

    public int Upgrades(double time) =>
        time < FirstWave ? 0 : 1 + (int)Math.Floor((time - FirstWave) / UpgradeInterval);

    public static readonly MinionWaveRules SummonersRift = new(
        "召唤师峡谷",
        30,
        [new(0, 30.0), new(840, 25.0), new(1800, 20.0)],
        [new(0, [3]), new(840, [2, 3]), new(1500, [2])],
        [new(0, [3]), new(1800, [2])],
        [new(0, [0, 0, 1]), new(840, [1, 0]), new(1500, [1])],
        [1, 1, 2],
        90,
        new(20, 0, 0),
        new(14, 0, 0),
        new(49, 1, 41),
        new(49, 1, 41),
        new(350, 600, 300, 25, 4),
        MinionLaneMap.SummonersRift
    )
    {
        Experience = [62, 31, 75, 75],
        InhibitorRespawn = 300,
        SideLaneBonus = true,
    };

    // The URF barracks: siege minions every wave from 1:30 and faster upgrades; interval changes are unconfirmed.
    public static readonly MinionWaveRules UltraRapidFire = SummonersRift with
    {
        Name = "无限火力",
        Intervals = [new(0, 30.0)],
        Siege = [new(0, [0]), new(90, [1])],
        UpgradeInterval = 40,
        MeleeGold = new(35, 0, 0),
        CasterGold = new(8, 0, 0),
        SiegeGold = new(80, 3, 10),
        SuperGold = new(49, 3, 41),
        InhibitorRespawn = null,
        SideLaneBonus = false,
    };

    // The 0:50 Howling Abyss barracks, matching the first wave observed in game; ARAM: Mayhem has no override.
    public static readonly MinionWaveRules HowlingAbyss = new(
        "极地大乱斗",
        50,
        [new(0, 25.0)],
        [new(0, [3])],
        [new(0, [3])],
        [new(0, [0, 0, 1]), new(645, [0, 1]), new(1500, [1])],
        [1],
        40,
        new(20, 0.125, 12),
        new(17, 0, 0),
        new(44, 0.38, 46),
        new(45, 0.35, 45),
        new(325, 360, 60, 10, 12),
        MinionLaneMap.HowlingAbyss
    )
    {
        Experience = [64, 32, 94, 100],
    };

    /// <summary>Rules for a 2999 game mode and map; null for modes without lane minions or with unknown rules.</summary>
    public static MinionWaveRules? For(string mode, int map) =>
        (mode.ToUpperInvariant(), map) switch
        {
            ("URF" or "ARURF", 11) => UltraRapidFire,
            ("CLASSIC" or "SWIFTPLAY", 11) => SummonersRift,
            (_, 12) => HowlingAbyss,
            _ => null,
        };
}
