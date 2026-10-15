namespace League.Models;

/// <summary>A player's health line on the control page and the explanation behind it.</summary>
public sealed record PlayerHealth(string Text, string Details);

/// <summary>Health and resistances, and the damage of each kind it takes to kill through them.</summary>
public sealed record HealthEstimate(double Health, double Armor, double MagicResist)
{
    /// <summary>Tank Engine raises maximum health by this much per stack.</summary>
    public const double TankEngineHealth = 0.05;

    public double PhysicalEffective => Health * Multiplier(Armor);

    public double MagicEffective => Health * Multiplier(MagicResist);

    /// <summary>
    /// Base stats grown to the level plus item stats and the recorded augments: flat health, then the percentage
    /// increases (each multiplies maximum health), then Glass Cannon's 70% cap. Rune shards, unrecorded augments,
    /// stacking items and passives and mode items are not visible to 2999 for other players and are not included.
    /// </summary>
    public static HealthEstimate Estimate(ChampionBaseStats stats, int level, ItemStats items, AugmentEffects augments)
    {
        double health =
            (ChampionBaseStats.Grown(stats.Health, stats.HealthPerLevel, level) + items.Health + augments.Health)
            * augments.HealthMultiplier
            * (augments.GlassCannon ? AugmentEffects.GlassCannonShare : 1);
        return new(
            health,
            ChampionBaseStats.Grown(stats.Armor, stats.ArmorPerLevel, level) + items.Armor + augments.Armor,
            ChampionBaseStats.Grown(stats.MagicResist, stats.MagicResistPerLevel, level)
                + items.MagicResist
                + augments.MagicResist
        );
    }

    // Negative resistances raise damage taken: 100 / (100 - resistance).
    private static double Multiplier(double resistance) =>
        resistance >= 0 ? 1 + resistance / 100 : 100 / (100 - resistance);
}
