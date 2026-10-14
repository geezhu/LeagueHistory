using League.Models;

namespace League.Services;

/// <summary>
/// Adds each turret's stats and rules to its marker on the wave page. 2999 reports no structure health, so the
/// numbers are the turret at full health at the current game time.
/// </summary>
public static class TurretInsight
{
    public static LaneProgress Annotate(
        LaneProgress progress,
        MinionWaveRules rules,
        MinionLane lane,
        LaneStructureState structures,
        int own,
        LiveMatchSnapshot snapshot
    )
    {
        if (rules.Turrets is not { } turrets)
        {
            return progress;
        }

        bool mayhem = snapshot.Mode.Equals("KIWI", StringComparison.OrdinalIgnoreCase);
        int enemy = own == 100 ? 200 : 100;
        var markers = progress
            .Markers.Select(marker =>
                Describe(marker, turrets, structures, lane, marker.Ally ? own : enemy, snapshot.Time, mayhem)
            )
            .ToArray();
        return progress with { Markers = markers };
    }

    private static LaneMarker Describe(
        LaneMarker marker,
        TurretRules turrets,
        LaneStructureState structures,
        MinionLane lane,
        int team,
        double now,
        bool mayhem
    )
    {
        if (turrets.At(marker.Kind, now, mayhem) is not { } stats)
        {
            return marker;
        }

        var (reduction, seconds) = turrets.FortificationFor(mayhem);
        string minions = marker.Ally ? "敌方小兵" : "我方小兵";
        var lines = new List<string>
        {
            $"满血 {stats.Health:0} · 攻击 {stats.Damage:0} · 护甲与魔抗 {stats.Resist:0}",
            $"等效生命约 {stats.EffectiveHealth:0}（满血，未计穿透；接口不提供当前血量）",
            $"附近没有{minions}时，受到英雄伤害 −{TurretRules.Reinforced:0%}（含真实伤害）；近战英雄 +{TurretRules.MeleeBonus:0%}",
            $"打英雄每下伤害 +{TurretRules.WarmUpStep:0%}，最多 +{TurretRules.WarmUpStep * TurretRules.WarmUpStacks:0%}，5 秒不打英雄后清零；无视 {TurretRules.ArmorPenetration:0%} 护甲",
        };
        double? fortifiedUntil = null;
        switch (marker.Kind)
        {
            case LaneStructureKind.Outer when now < turrets.OuterFalloffTime:
                lines.Add(
                    $"{MinionWaveForecaster.Clock(turrets.OuterFalloffTime)} 起护甲与魔抗降到 {turrets.OuterFalloff:0}"
                );
                break;
            case LaneStructureKind.Inner:
                lines.Add($"一塔被破后 {seconds:0} 秒内受到伤害 −{reduction:0%}");
                if (structures.TurretFellAt(team, lane.Key, 0) is double fell && fell + seconds > now)
                {
                    fortifiedUntil = fell + seconds;
                }
                break;
            case LaneStructureKind.Nexus:
                lines.Add("这一方任意一座小水晶被破后才能被攻击");
                break;
        }

        return marker with
        {
            Details = lines.ToArray(),
            FortifiedUntil = marker.Alive ? fortifiedUntil : null,
            Fortification = reduction,
        };
    }
}
