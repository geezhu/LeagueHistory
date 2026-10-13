using League.Models;

namespace League.Services;

/// <summary>
/// Lane bars for the wave page. Positions are theoretical (time × speed); the hint reads which half the lane
/// usually sits in from the structures each side has lost, which is still an inference, not a position.
/// </summary>
public static class LaneProgressEstimator
{
    private const double SideLaneBonusEnd = 840;

    // The side-lane bonus fades in four stages over 25 seconds; this is the distance it adds per bonus point.
    private const double SideLaneBonusDistance = 6.25 * (1 + 0.75 + 0.5 + 0.25);

    /// <summary>Extra distance early Summoner's Rift side-lane waves cover from their speed bonus.</summary>
    public static double SideLaneBonus(MinionWaveRules rules, MinionLane lane, WaveSpawn spawn) =>
        rules.SideLaneBonus && lane.Key != "mid" && spawn.Number >= 2 && spawn.Time < SideLaneBonusEnd
            ? Math.Max(0, 111 - 4.5 * (spawn.Number - 2)) * SideLaneBonusDistance
            : 0;

    public static LaneProgress Build(
        MinionWaveRules rules,
        MinionLane lane,
        LaneStructureState structures,
        WaveSpawn? current,
        int own
    )
    {
        int enemy = own == 100 ? 200 : 100;
        var markers = Markers(lane, structures, own, own)
            .Concat(Markers(lane, structures, enemy, own))
            .OrderBy(m => m.At);
        return new(
            lane.Length,
            current == null ? rules.Speed.Base : rules.Speed.At(current.Time),
            current == null ? 0 : SideLaneBonus(rules, lane, current),
            markers.ToArray(),
            Hint(lane, structures, own, enemy)
        );
    }

    private static IEnumerable<LaneMarker> Markers(MinionLane lane, LaneStructureState structures, int team, int own)
    {
        bool ally = team == own;
        int down = structures.TurretsDown(team, lane.Key);
        string side = ally ? "我方" : "敌方";
        var turrets = lane.Turrets(team);
        for (int i = 0; i < turrets.Length; i++)
        {
            yield return new(At(lane, turrets[i], own), side + turrets[i].Label, ally, i >= down);
        }

        var inhibitor = lane.Inhibitor(team);
        yield return new(
            At(lane, inhibitor, own),
            side + inhibitor.Label,
            ally,
            structures.InhibitorDownSince(team, lane.Key) == null
        );
    }

    // The bar always runs from the own inhibitor (0) to the enemy inhibitor (1).
    private static double At(MinionLane lane, LaneStructure structure, int own) =>
        own == 100 ? structure.FromBlue / lane.Length : 1 - structure.FromBlue / lane.Length;

    private static string Hint(MinionLane lane, LaneStructureState structures, int own, int enemy)
    {
        bool ownInhibitor = structures.InhibitorDownSince(own, lane.Key) != null;
        bool enemyInhibitor = structures.InhibitorDownSince(enemy, lane.Key) != null;
        int ownLost = structures.TurretsDown(own, lane.Key) + (ownInhibitor ? 1 : 0);
        int enemyLost = structures.TurretsDown(enemy, lane.Key) + (enemyInhibitor ? 1 : 0);
        return (ownInhibitor, enemyInhibitor) switch
        {
            (false, true) => "推测：敌方水晶被破，我方超级兵持续推进，兵线多在敌方高地附近",
            (true, false) => "推测：我方水晶被破，敌方超级兵持续推进，兵线多在我方高地附近",
            _ when enemyLost > ownLost => $"推测：敌方这一路少 {enemyLost - ownLost} 座建筑，兵线多在敌方半场",
            _ when ownLost > enemyLost => $"推测：我方这一路少 {ownLost - enemyLost} 座建筑，兵线多在我方半场",
            _ when ownLost > 0 => "推测：双方损失相同，看不出兵线偏向哪边",
            _ => "推测：双方塔都在，无法按塔判断兵线位置",
        };
    }
}
