using League.Models;

namespace League.Services;

/// <summary>
/// Lane timetables for the wave page. Positions are theoretical (time × speed); the hint reads which half the lane
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
        IEnumerable<WaveSpawn> waves,
        int own
    )
    {
        int enemy = own == 100 ? 200 : 100;
        var bar = new LaneBar(lane, own);
        var markers = Markers(lane, structures, own, bar)
            .Concat(Markers(lane, structures, enemy, bar))
            .OrderBy(m => m.At);
        return new(
            bar.Length,
            bar.At(lane.Base(own)),
            bar.At(lane.Base(enemy)),
            waves
                .Select(w => new LaneWave(
                    w.Number,
                    w.Time,
                    rules.Speed.At(w.Time),
                    SideLaneBonus(rules, lane, w),
                    w.Siege > 0
                ))
                .ToArray(),
            markers.ToArray(),
            Hint(lane, structures, own, enemy)
        );
    }

    /// <summary>
    /// The drawn lane: nexus to nexus, where the waves spawn, with nexus turrets and inhibitors inside the bar
    /// like on the minimap. Fractions run from the own side (0) to the enemy side (1).
    /// </summary>
    private readonly record struct LaneBar(MinionLane Lane, int Own)
    {
        private double Start => Lane.Base(Own).FromBlue;

        public double Length => Math.Abs(Lane.Base(Own == 100 ? 200 : 100).FromBlue - Start);

        public double At(LaneStructure structure) => Math.Abs(structure.FromBlue - Start) / Length;
    }

    private static IEnumerable<LaneMarker> Markers(
        MinionLane lane,
        LaneStructureState structures,
        int team,
        LaneBar bar
    )
    {
        bool ally = team == bar.Own;
        int down = structures.TurretsDown(team, lane.Key);
        string side = ally ? "我方" : "敌方";
        var turrets = lane.Turrets(team);
        for (int i = 0; i < turrets.Length; i++)
        {
            yield return new(bar.At(turrets[i]), side + turrets[i].Label, ally, i >= down);
        }

        var inhibitor = lane.Inhibitor(team);
        yield return new(
            bar.At(inhibitor),
            side + inhibitor.Label,
            ally,
            structures.InhibitorDownSince(team, lane.Key) == null
        );
        // Both nexus turrets share one marker; it reads as fallen once both are down.
        int standing = 2 - Math.Min(2, structures.NexusTurretsDown(team));
        yield return new(bar.At(lane.Nexus(team)), $"{side}门牙塔（剩 {standing} 座）", ally, standing > 0);
        yield return new(bar.At(lane.Base(team)), side + "大水晶", ally, true);
    }

    private static string Hint(MinionLane lane, LaneStructureState structures, int own, int enemy)
    {
        bool ownInhibitor = structures.InhibitorDownSince(own, lane.Key) != null;
        bool enemyInhibitor = structures.InhibitorDownSince(enemy, lane.Key) != null;
        int ownLost = structures.TurretsDown(own, lane.Key) + (ownInhibitor ? 1 : 0);
        int enemyLost = structures.TurretsDown(enemy, lane.Key) + (enemyInhibitor ? 1 : 0);
        return (ownInhibitor, enemyInhibitor) switch
        {
            (false, true) => "推测：敌方小水晶被破，我方超级兵持续推进，兵线多在敌方高地附近",
            (true, false) => "推测：我方小水晶被破，敌方超级兵持续推进，兵线多在我方高地附近",
            _ when enemyLost > ownLost => $"推测：敌方这一路少 {enemyLost - ownLost} 座建筑，兵线多在敌方半场",
            _ when ownLost > enemyLost => $"推测：我方这一路少 {ownLost - enemyLost} 座建筑，兵线多在我方半场",
            _ when ownLost > 0 => "推测：双方损失相同，看不出兵线偏向哪边",
            _ => "推测：双方塔都在，无法按塔判断兵线位置",
        };
    }
}
