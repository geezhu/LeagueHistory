namespace League.Models;

/// <summary>A stat's base value (the champion's own at its level) and its total; the difference is the bonus.</summary>
public readonly record struct StatValue(double Base, double Total)
{
    public double Bonus => Total - Base;
}

/// <summary>
/// One champion's stats at a level: 2999's stats panel for the active player, an estimate from the level, items and
/// recorded augments for everyone else (2999 exposes no other player's stats). Spell calculations read it by the game
/// data's stat numbers through <see cref="Stat"/>.
/// </summary>
public sealed record ChampionStatSheet(int Level, bool Estimated)
{
    /// <summary>Crit damage without items that change it.</summary>
    public const double DefaultCritDamage = 1.75;

    public StatValue Health { get; init; }

    public StatValue Mana { get; init; }

    public StatValue AttackDamage { get; init; }

    public StatValue Armor { get; init; }

    public StatValue MagicResist { get; init; }

    public StatValue MoveSpeed { get; init; }

    public StatValue AttackRange { get; init; }

    /// <summary>Base is the level 1 attack speed (growth counts as bonus); the total is attacks per second.</summary>
    public StatValue AttackSpeed { get; init; }

    /// <summary>Bonus attack speed as a fraction (0.4 for 40%), the unit the game data scales with.</summary>
    public double BonusAttackSpeed { get; init; }

    public double AbilityPower { get; init; }

    /// <summary>0 to 1.</summary>
    public double CritChance { get; init; }

    /// <summary>The multiplier a critical strike deals (1.75 for 175%).</summary>
    public double CritDamage { get; init; } = DefaultCritDamage;

    public double AbilityHaste { get; init; }

    public double Lethality { get; init; }

    /// <summary>0 to 1.</summary>
    public double ArmorPenetrationPercent { get; init; }

    public double MagicPenetrationFlat { get; init; }

    /// <summary>0 to 1.</summary>
    public double MagicPenetrationPercent { get; init; }

    /// <summary>Per 5 seconds.</summary>
    public double HealthRegen { get; init; }

    /// <summary>Per 5 seconds.</summary>
    public double ManaRegen { get; init; }

    /// <summary>Life steal, omnivamp, tenacity and heal and shield power, 0 to 1; only 2999 reports them.</summary>
    public double LifeSteal { get; init; }

    public double Omnivamp { get; init; }

    public double Tenacity { get; init; }

    public double HealShieldPower { get; init; }

    /// <summary>
    /// A stat by the game data's numbering (mStat) and formula (mStatFormula: 0 total, 1 base, 2 bonus); null for
    /// stats the sheet does not know, so a calculation using one is shown as unknown rather than as 0.
    /// </summary>
    public double? Stat(int stat, int formula)
    {
        double? Pick(StatValue value) =>
            formula switch
            {
                1 => value.Base,
                2 => value.Bonus,
                _ => value.Total,
            };
        // Numbers checked against the game data's own names: AP ratios use 0, "TotalADRatio" 2, Kai'Sa E's bonus
        // attack speed 4, Galio's magic resist 6, dash speeds 7, crit chance 8 and crit damage ("CritDamageMod") 9,
        // Garen W's health 12, Pyke's lethality 29 and Urgot's range checks 31.
        return stat switch
        {
            0 => formula == 1 ? 0 : AbilityPower,
            1 => Pick(Armor),
            2 => Pick(AttackDamage),
            4 => formula switch
            {
                1 => AttackSpeed.Base,
                2 => BonusAttackSpeed,
                _ => AttackSpeed.Total,
            },
            6 => Pick(MagicResist),
            7 => Pick(MoveSpeed),
            8 => CritChance,
            9 => CritDamage,
            12 => Pick(Health),
            29 => Lethality,
            31 => Pick(AttackRange),
            _ => null,
        };
    }

    /// <summary>The active player's stats from 2999; base values come from the champion's growth when known.</summary>
    public static ChampionStatSheet FromLive(
        ChampionBaseStats? stats,
        ChampionRecord record,
        int level,
        LiveChampionStats live
    )
    {
        var grown = stats != null ? Grow(stats, level) : null;
        double baseSpeed = stats?.AttackSpeed ?? 0;
        double ratio = record.AttackSpeedRatio > 0 ? record.AttackSpeedRatio : baseSpeed;
        return new(level, false)
        {
            Health = new(grown?.Health ?? live.MaxHealth, live.MaxHealth),
            Mana = new(grown?.Mana ?? live.ResourceMax, live.ResourceMax),
            AttackDamage = new(grown?.AttackDamage ?? live.AttackDamage, live.AttackDamage),
            Armor = new(grown?.Armor ?? live.Armor, live.Armor),
            MagicResist = new(grown?.MagicResist ?? live.MagicResist, live.MagicResist),
            MoveSpeed = new(stats?.MoveSpeed ?? live.MoveSpeed, live.MoveSpeed),
            AttackRange = new(stats?.AttackRange ?? live.AttackRange, live.AttackRange),
            AttackSpeed = new(baseSpeed > 0 ? baseSpeed : live.AttackSpeed, live.AttackSpeed),
            BonusAttackSpeed = ratio > 0 ? Math.Max(0, (live.AttackSpeed - baseSpeed) / ratio) : 0,
            AbilityPower = live.AbilityPower,
            CritChance = Fraction(live.CritChance),
            CritDamage = live.CritDamage > 0 ? live.CritDamage / 100 : DefaultCritDamage,
            AbilityHaste = live.AbilityHaste,
            Lethality = live.PhysicalLethality,
            ArmorPenetrationPercent = live.ArmorPenetrationPercent,
            MagicPenetrationFlat = live.MagicPenetrationFlat,
            MagicPenetrationPercent = live.MagicPenetrationPercent,
            HealthRegen = live.HealthRegen,
            ManaRegen = live.ResourceRegen,
            LifeSteal = Fraction(live.LifeSteal),
            Omnivamp = Fraction(live.Omnivamp),
            Tenacity = Fraction(live.Tenacity),
            HealShieldPower = Fraction(live.HealShieldPower),
        };
    }

    /// <summary>
    /// Another player's stats: the champion's growth to the level, the items' listed stats and the recorded augments'
    /// health and resistances. Runes, item passives and unrecorded augments are not visible, so damage stats can only
    /// be lower than the real ones.
    /// </summary>
    public static ChampionStatSheet Estimate(
        ChampionBaseStats stats,
        ChampionRecord record,
        int level,
        ItemStats items,
        AugmentEffects augments,
        double abilityHaste
    )
    {
        var grown = Grow(stats, level);
        var health = HealthEstimate.Estimate(stats, level, items, augments);
        double ratio = record.AttackSpeedRatio > 0 ? record.AttackSpeedRatio : stats.AttackSpeed;
        double bonusSpeed = (ChampionBaseStats.Grown(0, stats.AttackSpeedPerLevel, level) + items.AttackSpeed) / 100;
        double moveSpeed = (stats.MoveSpeed + items.MoveSpeed) * (1 + items.MoveSpeedPercent / 100);
        return new(level, true)
        {
            Health = new(grown.Health, health.Health),
            Mana = new(grown.Mana, grown.Mana + (grown.Mana > 0 ? items.Mana : 0)),
            AttackDamage = new(grown.AttackDamage, grown.AttackDamage + items.AttackDamage),
            Armor = new(grown.Armor, health.Armor),
            MagicResist = new(grown.MagicResist, health.MagicResist),
            MoveSpeed = new(stats.MoveSpeed, moveSpeed),
            AttackRange = new(stats.AttackRange, stats.AttackRange),
            AttackSpeed = new(stats.AttackSpeed, stats.AttackSpeed + ratio * bonusSpeed),
            BonusAttackSpeed = bonusSpeed,
            AbilityPower = items.AbilityPower,
            CritChance = Math.Clamp(items.CritChance / 100, 0, 1),
            AbilityHaste = abilityHaste,
            Lethality = items.Lethality,
            MagicPenetrationFlat = items.MagicPenetration,
            HealthRegen = grown.HealthRegen,
            ManaRegen = grown.ManaRegen,
        };
    }

    private sealed record Grown(
        double Health,
        double Mana,
        double AttackDamage,
        double Armor,
        double MagicResist,
        double HealthRegen,
        double ManaRegen
    );

    private static Grown Grow(ChampionBaseStats stats, int level) =>
        new(
            ChampionBaseStats.Grown(stats.Health, stats.HealthPerLevel, level),
            ChampionBaseStats.Grown(stats.Mana, stats.ManaPerLevel, level),
            ChampionBaseStats.Grown(stats.AttackDamage, stats.AttackDamagePerLevel, level),
            ChampionBaseStats.Grown(stats.Armor, stats.ArmorPerLevel, level),
            ChampionBaseStats.Grown(stats.MagicResist, stats.MagicResistPerLevel, level),
            ChampionBaseStats.Grown(stats.HealthRegen, stats.HealthRegenPerLevel, level),
            ChampionBaseStats.Grown(stats.ManaRegen, stats.ManaRegenPerLevel, level)
        );

    // 2999 reports these as fractions; a value above 1 is read as already in percent.
    private static double Fraction(double value) => Math.Clamp(value > 1 ? value / 100 : value, 0, 1);
}
