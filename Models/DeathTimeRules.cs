namespace League.Models;

/// <summary>
/// Death timer = time dead for the level × (1 + time factor). The factor starts at <see cref="ScalingStart"/> and
/// grows by the current rate every <see cref="Increment"/> seconds, up to <see cref="Cap"/> − 1. Values come from
/// each mode's 16.20 DeathTimes data; augments and items that change respawn time are not included.
/// </summary>
public sealed record DeathTimeRules(
    double[] PerLevel,
    double ScalingStart,
    double Increment,
    double Rate,
    TimedValue<double>[] Rates,
    double Cap
)
{
    /// <summary>
    /// The time factor at a game time. Each step covers the preceding increment and uses the rate in force at its
    /// start, so the 15:00 step still adds the 10:00–15:00 rate.
    /// </summary>
    public double Factor(double time)
    {
        double factor = 0;
        for (double step = ScalingStart + Increment; step <= time; step += Increment)
        {
            factor += Rates.LastOrDefault(r => r.Start <= step - Increment)?.Value ?? Rate;
        }
        return Math.Min(factor, Cap - 1);
    }

    public double Seconds(int level, double time) =>
        PerLevel[Math.Clamp(level, 1, PerLevel.Length) - 1] * (1 + Factor(time));

    private static readonly TimedValue<double>[] RiftRates = [new(900, 0.00425), new(1800, 0.003), new(2700, 0.0145)];

    public static readonly DeathTimeRules SummonersRift = new(
        [10, 10, 12, 12, 14, 16, 20, 25, 28, 32.5, 35, 37.5, 40, 42.5, 45, 47.5, 50, 52.5],
        600,
        30,
        0.005,
        RiftRates,
        1.5
    );

    public static readonly DeathTimeRules UltraRapidFire = SummonersRift with
    {
        PerLevel = [6, 8, 10, 12, 14, 16, 19, 22, 25, 28, 31, 34, 37, 40, 43, 46, 49, 52],
    };

    public static readonly DeathTimeRules OneForAll = SummonersRift with
    {
        PerLevel = [6, 6, 8, 8, 10, 12, 16, 21, 26, 32.5, 35, 37.5, 40, 42.5, 45, 47.5, 50, 52.5],
    };

    // Swiftplay data: no growth until 25:00, then 5% every 15 seconds; the meaning of this table is unconfirmed.
    public static readonly DeathTimeRules Swiftplay = new(
        [10, 10, 12, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40],
        1200,
        15,
        0,
        [new(1200, 0), new(1500, 0.05)],
        1.5
    );

    // ARAM, ARAM: Mayhem (KIWI) and Poro King share one table.
    public static readonly DeathTimeRules HowlingAbyss = new(
        [9, 10, 11, 13, 15, 17, 19, 21, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40],
        1440,
        60,
        0.025,
        [],
        1.25
    );

    public static DeathTimeRules? For(string mode, int map) =>
        (mode.ToUpperInvariant(), map) switch
        {
            ("CLASSIC" or "PRACTICETOOL" or "TUTORIAL", 11) => SummonersRift,
            ("URF" or "ARURF" or "SNOWURF", 11) => UltraRapidFire,
            ("ONEFORALL" or "ULTBOOK", 11) => OneForAll,
            ("SWIFTPLAY", 11) => Swiftplay,
            ("ARAM" or "KIWI" or "KINGPORO", 12) => HowlingAbyss,
            _ => null,
        };
}
