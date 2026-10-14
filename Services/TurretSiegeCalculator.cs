using League.Models;

namespace League.Services;

/// <summary>
/// Basic-attack arithmetic between the active player and the next enemy turret. Structures take 100% attack
/// damage plus 60% ability power, physical or magic by the larger share; 2999 does not split base and bonus
/// attack damage, so the type compares total attack damage, an estimate for low-level mages. Abilities, runes and
/// augments are not counted.
/// </summary>
public static class TurretSiegeCalculator
{
    private const double AbilityPowerRatio = 0.6;

    // Melee champions attack from 125 to 250 units, ranged from 425; the gap separates them safely.
    private const double MeleeRange = 300;

    private const int MaxShots = 30;

    public static TurretSiege? Evaluate(
        TurretRules rules,
        LaneProgress progress,
        LiveChampionStats? stats,
        double now,
        bool mayhem
    )
    {
        if (
            stats is not { AttackDamage: > 0, AttackSpeed: > 0 }
            || Target(rules, progress, now, mayhem) is not { } target
        )
        {
            return null;
        }

        var turret = rules.At(target.Kind, now, mayhem)!;
        bool magic = AbilityPowerRatio * stats.AbilityPower > stats.AttackDamage;
        double resist = magic
            ? turret.Resist * (1 - stats.MagicPenetrationPercent) - stats.MagicPenetrationFlat
            : turret.Resist * (1 - stats.ArmorPenetrationPercent) - stats.PhysicalLethality;
        double fortification = target.FortifiedUntil > now ? target.Fortification : 0;
        double hit =
            (stats.AttackDamage + AbilityPowerRatio * stats.AbilityPower)
            * Mitigation(resist)
            * rules.StructureDamage(now)
            * (stats.AttackRange < MeleeRange ? 1 + TurretRules.MeleeBonus : 1)
            * (1 - fortification);
        int hits = (int)Math.Ceiling(turret.Health / hit);
        int alone = (int)Math.Ceiling(turret.Health / (hit * (1 - TurretRules.Reinforced)));
        // The first attack lands at once, the rest one attack interval apart.
        double Seconds(int count) => (count - 1) / stats.AttackSpeed;

        double armor = stats.Armor * (1 - TurretRules.ArmorPenetration);
        double[] shots = Enumerable
            .Range(0, TurretRules.WarmUpStacks + 1)
            .Select(i => turret.Damage * (1 + TurretRules.WarmUpStep * i) * Mitigation(armor))
            .ToArray();
        return new(
            target.Label,
            hit,
            magic,
            hits,
            Seconds(hits),
            alone,
            Seconds(alone),
            shots,
            ShotsToDown(shots, stats.CurrentHealth)
        )
        {
            Fortification = fortification,
        };
    }

    // The outermost standing enemy turret, unless a standing inhibitor comes first: inhibitors have no
    // reinforced armor, so the numbers would not apply.
    private static LaneMarker? Target(TurretRules rules, LaneProgress progress, double now, bool mayhem)
    {
        var next = progress
            .Markers.Where(m => !m.Ally && m.Alive && m.Kind != LaneStructureKind.Base)
            .OrderBy(m => m.At)
            .FirstOrDefault();
        return next != null && rules.At(next.Kind, now, mayhem) != null ? next : null;
    }

    private static double Mitigation(double resist) => resist >= 0 ? 100 / (100 + resist) : 2 - 100 / (100 - resist);

    private static int ShotsToDown(double[] shots, double health)
    {
        double taken = 0;
        for (int shot = 0; shot < MaxShots; shot++)
        {
            taken += shots[Math.Min(shot, shots.Length - 1)];
            if (taken >= health)
            {
                return shot + 1;
            }
        }
        return MaxShots;
    }
}
