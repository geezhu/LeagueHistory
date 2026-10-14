using League.Models;

namespace League.Services;

/// <summary>
/// Estimates waves from the game clock and structure events. 2999 reports no minion positions, so arrivals
/// assume nothing stops the wave; the Game Bar labels them as such.
/// </summary>
public static class MinionWaveForecaster
{
    private const int UpcomingWaves = 4;
    private const double NoteWindow = 120;

    public static MinionWaveForecast? Calculate(LiveMatchSnapshot snapshot, long clock)
    {
        if (MinionWaveRules.For(snapshot.Mode, snapshot.Map) is not { } rules)
        {
            return null;
        }

        var events = snapshot.Events ?? [];
        double first = FirstWave(rules, events);
        // Two waves can be on a lane at once: a lane takes longer to walk than the time between spawns.
        var recent = Spawns(rules, first).TakeWhile(s => s.Time <= snapshot.Time).TakeLast(2).ToArray();
        var current = recent.LastOrDefault();
        var spawns = Spawns(rules, first)
            .SkipWhile(s => s.Time <= snapshot.Time)
            .Take(UpcomingWaves)
            .Select(s => s with { Gold = Gold(rules, s), Experience = Experience(rules, s) })
            .ToArray();
        var structures = LaneStructureState.From(events, rules.Lanes.Lanes.Length == 1);
        int own = LiveMatchSnapshot.TeamId(snapshot.Players.FirstOrDefault(p => p.Self)?.Team ?? "") is 200 ? 200 : 100;
        var lanes = rules
            .Lanes.Lanes.Select(lane =>
                ForecastLane(rules, lane, structures, spawns[0], own, snapshot.Time, first) with
                {
                    Progress = TurretInsight.Annotate(
                        LaneProgressEstimator.Build(rules, lane, structures, recent.Concat(spawns), own),
                        rules,
                        lane,
                        structures,
                        own,
                        snapshot
                    ),
                }
            )
            .ToArray();
        if (rules.Turrets is { } turrets)
        {
            bool mayhem = snapshot.Mode.Equals("KIWI", StringComparison.OrdinalIgnoreCase);
            var stats = snapshot.Active?.Stats;
            lanes = lanes
                .Select(l =>
                    l with
                    {
                        Siege = TurretSiegeCalculator.Evaluate(turrets, l.Progress!, stats, snapshot.Time, mayhem),
                    }
                )
                .ToArray();
        }
        return new(rules.Name, snapshot.Time, clock, spawns, Notes(rules, snapshot.Time), Value(rules, spawns), lanes)
        {
            UnknownStructures = structures.Unknown.Distinct().ToArray(),
            Current = current,
            Deaths = DeathCosts(snapshot, own),
            DeathFactor = (DeathTimeRules.For(snapshot.Mode, snapshot.Map)?.Factor(snapshot.Time) ?? 0) * 100,
        };
    }

    /// <summary>Own team first, then by level; empty for modes whose death timer table is unknown.</summary>
    public static DeathCost[] DeathCosts(LiveMatchSnapshot snapshot, int own)
    {
        if (DeathTimeRules.For(snapshot.Mode, snapshot.Map) is not { } rules)
        {
            return [];
        }

        return snapshot
            .Players.Where(p => p.Level > 0)
            .Select(p => new DeathCost(
                LiveMatchSnapshot.TeamId(p.Team) == own,
                p.ChampionName,
                p.Level,
                rules.Seconds(p.Level, snapshot.Time),
                p.Self,
                p.Key
            ))
            .OrderByDescending(d => d.Ally)
            .ThenByDescending(d => d.Level)
            .ToArray();
    }

    // The client's first-wave event corrects small offsets; a value far from the rule is ignored.
    private static double FirstWave(MinionWaveRules rules, IEnumerable<LiveMatchEvent> events) =>
        events.FirstOrDefault(e => e.Name == "MinionsSpawning")?.Time is double time
        && Math.Abs(time - rules.FirstWave) <= 15
            ? time
            : rules.FirstWave;

    public static IEnumerable<WaveSpawn> Spawns(MinionWaveRules rules, double first)
    {
        double time = first;
        for (int wave = 1; ; wave++)
        {
            int siege = MinionWaveRules.Rotate(MinionWaveRules.Stage(rules.Siege, time), wave);
            yield return new(
                wave,
                time,
                MinionWaveRules.Rotate(MinionWaveRules.Stage(rules.Melee, time), wave),
                MinionWaveRules.Rotate(MinionWaveRules.Stage(rules.Caster, time), wave),
                siege
            );
            time = NextSpawn(rules, time);
        }
    }

    /// <summary>
    /// The next spawn after one at <paramref name="time"/>. A shorter interval starting before the next spawn
    /// pulls it in, but never before that interval starts: 13:30 → 14:00 → 14:25 and 29:25 → 29:50 → 30:10.
    /// </summary>
    public static double NextSpawn(MinionWaveRules rules, double time)
    {
        double next = time + MinionWaveRules.Stage(rules.Intervals, time);
        foreach (var stage in rules.Intervals.Where(s => s.Start > time && next > s.Start))
        {
            next = Math.Max(stage.Start, time + stage.Value);
        }
        return next;
    }

    private static LaneWaveForecast ForecastLane(
        MinionWaveRules rules,
        MinionLane lane,
        LaneStructureState structures,
        WaveSpawn spawn,
        int own,
        double now,
        double first
    )
    {
        int enemy = own == 100 ? 200 : 100;
        return new(
            lane.Name,
            Arrive(rules, lane, structures, spawn, own, enemy, "敌方"),
            Arrive(rules, lane, structures, spawn, enemy, own, "我方"),
            DescribeStructures(rules, lane, structures, own, now, first)
        );
    }

    private static WaveArrival Arrive(
        MinionWaveRules rules,
        MinionLane lane,
        LaneStructureState structures,
        WaveSpawn spawn,
        int team,
        int target,
        string side
    )
    {
        var next = NextStructure(lane, structures, target);
        double distance = lane.Distance(team, next) - LaneProgressEstimator.SideLaneBonus(rules, lane, spawn);

        double travel = Math.Max(0, distance) / rules.Speed.At(spawn.Time);
        string label =
            next.Kind == LaneStructureKind.Nexus && structures.NexusTurretsDown(target) >= 2 ? "主水晶" : next.Label;
        return new(
            side + label,
            spawn.Time + travel,
            spawn.Time + travel + (spawn.Count - 1) * MinionWaveRules.SpawnSpacing
        );
    }

    /// <summary>Lane turrets fall outermost first; then the inhibitor, then the nexus turrets.</summary>
    public static LaneStructure NextStructure(MinionLane lane, LaneStructureState structures, int team)
    {
        var turrets = lane.Turrets(team);
        int down = structures.TurretsDown(team, lane.Key);
        return down < turrets.Length ? turrets[down]
            : structures.InhibitorDownSince(team, lane.Key) == null ? lane.Inhibitor(team)
            : lane.Nexus(team);
    }

    private static string DescribeStructures(
        MinionWaveRules rules,
        MinionLane lane,
        LaneStructureState structures,
        int own,
        double now,
        double first
    )
    {
        var parts = new List<string>();
        foreach (int team in new[] { own == 100 ? 200 : 100, own })
        {
            if (structures.InhibitorDownSince(team, lane.Key) is not double since)
            {
                continue;
            }

            int inhibitors = Math.Max(1, structures.InhibitorsDown(team));
            int extra = rules.SuperPerInhibitorsDown[Math.Min(inhibitors, rules.SuperPerInhibitorsDown.Length) - 1];
            string text =
                team == own
                    ? $"我方小水晶被破 · 敌方每波 +{extra} 超级兵"
                    : $"敌方小水晶被破 · 我方每波 +{extra} 超级兵";
            double? back = since + rules.InhibitorRespawn;
            if (back > now)
            {
                text += $" · 约 {Clock(back.Value)} 重生";
            }
            else if (rules.SuperWaves is int waves)
            {
                int spawned = Spawns(rules, first)
                    .SkipWhile(s => s.Time <= since)
                    .TakeWhile(s => s.Time <= now)
                    .Count();
                text += waves > spawned ? $" · 超级兵约还剩 {waves - spawned} 波" : " · 超级兵约已出完";
            }
            parts.Add(text);
        }
        return string.Join("；", parts);
    }

    private static string[] Notes(MinionWaveRules rules, double now)
    {
        double interval = MinionWaveRules.Stage(rules.Intervals, now);
        int[] siege = MinionWaveRules.Stage(rules.Siege, now);
        var notes = new List<string> { $"每 {interval:0} 秒一波 · {SiegeText(siege)}" };
        notes.AddRange(
            rules
                .Intervals.Where(s => s.Start > now && s.Start - now <= NoteWindow)
                .Select(s => $"{Clock(s.Start)} 起每 {s.Value:0} 秒一波")
        );
        notes.AddRange(
            rules
                .Siege.Where(s => s.Start > now && s.Start - now <= NoteWindow)
                .Select(s => $"{Clock(s.Start)} 起{SiegeText(s.Value)}")
        );
        return notes.ToArray();
    }

    private static string SiegeText(int[] pattern) =>
        pattern.Sum() == 0 ? "暂无炮车"
        : pattern.Length == 1 ? "每波都有炮车"
        : $"每 {pattern.Length} 波 {pattern.Sum()} 个炮车";

    private static string Value(MinionWaveRules rules, WaveSpawn[] spawns)
    {
        var normal = spawns.FirstOrDefault(s => s.Siege == 0);
        var cannon = spawns.FirstOrDefault(s => s.Siege > 0);
        var parts = new[] { ("普通波", normal), ("炮车波", cannon) }
            .Where(p => p.Item2 != null)
            .Select(p => $"{p.Item1} {Gold(rules, p.Item2!):0} 金 / {Experience(rules, p.Item2!):0} 经验");
        return string.Join(" · ", parts);
    }

    private static double Gold(MinionWaveRules rules, WaveSpawn spawn)
    {
        int upgrades = rules.Upgrades(spawn.Time);
        return spawn.Melee * rules.MeleeGold.At(upgrades)
            + spawn.Caster * rules.CasterGold.At(upgrades)
            + spawn.Siege * rules.SiegeGold.At(upgrades);
    }

    private static double Experience(MinionWaveRules rules, WaveSpawn spawn) =>
        spawn.Melee * rules.Experience[0] + spawn.Caster * rules.Experience[1] + spawn.Siege * rules.Experience[2];

    public static string Clock(double seconds)
    {
        int total = (int)Math.Round(seconds);
        return $"{total / 60}:{total % 60:00}";
    }
}
