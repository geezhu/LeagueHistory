namespace League.Models;

/// <summary>A turret's stats at one game time; health is its maximum, since 2999 does not report current health.</summary>
public sealed record TurretStats(double Health, double Damage, double Resist)
{
    /// <summary>Health as damage before armor or magic resistance, both of which are <see cref="Resist"/>.</summary>
    public double EffectiveHealth => Health * (1 + Resist / 100);
}

/// <summary>One turret tier: base health, damage growing every minute until a time, and base armor and resistance.</summary>
public sealed record TurretTier(
    double Health,
    double Damage,
    double DamagePerMinute,
    double DamageUntil,
    double Resist
);

/// <summary>
/// Turret rules from the 16.20 game data: the HA_Outer, HA_Inhibitor and HA_Nexus character records and the turret
/// items 1500 (Warming Up), 1502 (Reinforced Armor), 1521 (Fortification) and 1522 (Tower Power-Up).
/// </summary>
public sealed record TurretRules(TurretTier Outer, TurretTier Inhibitor, TurretTier Nexus)
{
    /// <summary>Damage taken from champions while no enemy lane minion is near; true damage included.</summary>
    public const double Reinforced = 0.8;

    public const double MeleeBonus = 0.2;

    /// <summary>Each hit on a champion adds half the damage, three times at most, until 5 seconds pass.</summary>
    public const double WarmUpStep = 0.5;

    public const int WarmUpStacks = 3;

    public const double ArmorPenetration = 0.3;

    // ARAMGamePacingScriptData: champions' damage to turrets rises from 1.0 at 18:00 to 1.25 at 30:00; the data
    // gives the two ends, so the rise between them is taken as linear.
    private const double StructureDamageStart = 1080;
    private const double StructureDamageEnd = 1800;
    private const double StructureDamageLate = 0.25;

    // Armor and resistance grow by 3 a minute; the wiki's 60–81 range gives seven steps, and the data ends the
    // growth at 8:10, so the minute of each step may be off by one.
    private const double ResistPerMinute = 3;
    private const int ResistSteps = 7;

    /// <summary>The outer turret's armor and resistance drop to this at <see cref="OuterFalloffTime"/>.</summary>
    public double OuterFalloff { get; init; }

    public double OuterFalloffTime { get; init; }

    /// <summary>ARAM: Mayhem overrides the nexus turrets' health.</summary>
    public double? MayhemNexusHealth { get; init; }

    /// <summary>
    /// The inhibitor turret's damage reduction after the outer turret falls. Item 1521's tooltip says 35%; its
    /// data values, which carry a separate Mayhem override, say 30% for 60 seconds and 40% for 70 in Mayhem.
    /// </summary>
    public (double Reduction, double Seconds) Fortification { get; init; }

    public (double Reduction, double Seconds) MayhemFortification { get; init; }

    public static readonly TurretRules HowlingAbyss = new(
        new(1300, 185, 9, 720, 60),
        new(3900, 205, 12, 900, 63),
        new(1800, 195, 12, 900, 60)
    )
    {
        OuterFalloff = 40,
        OuterFalloffTime = 720,
        MayhemNexusHealth = 1200,
        Fortification = (0.3, 60),
        MayhemFortification = (0.4, 70),
    };

    /// <summary>Stats of a turret tier at a game time; null for structures that are not turrets.</summary>
    public TurretStats? At(LaneStructureKind kind, double time, bool mayhem)
    {
        var tier = kind switch
        {
            LaneStructureKind.Outer => Outer,
            LaneStructureKind.Inner or LaneStructureKind.InhibitorTurret => Inhibitor,
            LaneStructureKind.Nexus => Nexus,
            _ => null,
        };
        if (tier == null)
        {
            return null;
        }

        int minutes = (int)Math.Floor(Math.Max(0, time) / 60);
        double damage = tier.Damage + tier.DamagePerMinute * Math.Min(minutes, tier.DamageUntil / 60);
        double resist =
            kind == LaneStructureKind.Outer && time >= OuterFalloffTime
                ? OuterFalloff
                : tier.Resist + ResistPerMinute * Math.Min(minutes, ResistSteps);
        double health = kind == LaneStructureKind.Nexus && mayhem ? MayhemNexusHealth ?? tier.Health : tier.Health;
        return new(health, damage, resist);
    }

    /// <summary>The multiplier on champions' damage to turrets at a game time.</summary>
    public double StructureDamage(double time) =>
        1
        + StructureDamageLate
            * Math.Clamp((time - StructureDamageStart) / (StructureDamageEnd - StructureDamageStart), 0, 1);

    public (double Reduction, double Seconds) FortificationFor(bool mayhem) =>
        mayhem ? MayhemFortification : Fortification;
}
