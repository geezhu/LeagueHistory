namespace League.Models;

public enum LiveDefensePriority
{
    Unknown,
    Armor,
    MagicResist,
    Balanced,
}

/// <summary>
/// Effective health against each damage type, and which resistance adds more against the enemy damage mix.
/// True damage and penetration are ignored, so the advice compares resistances, not whole items.
/// </summary>
public sealed record LiveDefenseAdvice(
    double PhysicalHealth,
    double MagicHealth,
    double? PhysicalShare,
    LiveDefensePriority Priority,
    double ArmorGain,
    double MagicGain
)
{
    public const double Step = 10;

    // One resistance is clearly better only beyond this ratio; otherwise either is fine.
    private const double Margin = 1.15;

    public static LiveDefenseAdvice Calculate(LiveChampionStats stats, double? physicalShare)
    {
        double health = stats.MaxHealth;
        if (physicalShare is not double share || !double.IsFinite(share))
        {
            return new(
                health / Taken(stats.Armor),
                health / Taken(stats.MagicResist),
                null,
                LiveDefensePriority.Unknown,
                0,
                0
            );
        }

        share = Math.Clamp(share, 0, 1);
        double Mixed(double armor, double magic) => health / (share * Taken(armor) + (1 - share) * Taken(magic));
        double current = Mixed(stats.Armor, stats.MagicResist);
        double armorGain = Mixed(stats.Armor + Step, stats.MagicResist) - current;
        double magicGain = Mixed(stats.Armor, stats.MagicResist + Step) - current;
        var priority =
            armorGain > magicGain * Margin ? LiveDefensePriority.Armor
            : magicGain > armorGain * Margin ? LiveDefensePriority.MagicResist
            : LiveDefensePriority.Balanced;
        return new(
            health / Taken(stats.Armor),
            health / Taken(stats.MagicResist),
            share,
            priority,
            armorGain,
            magicGain
        );
    }

    // League's damage multiplier: 100/(100+R) for positive resistance, 2-100/(100-R) below zero.
    private static double Taken(double resistance) =>
        resistance >= 0 ? 100 / (100 + resistance) : 2 - 100 / (100 - resistance);
}
