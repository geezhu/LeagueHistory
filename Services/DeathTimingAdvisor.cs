using League.Models;

namespace League.Services;

/// <summary>
/// Advice on dying now to shop on the Howling Abyss, where the shop opens only on death. It weighs the time away
/// against the theoretical enemy waves, the enemies already dead, own gold and health, and the team pressures.
/// Waves are timed as if nothing stops them, so a wave "at the turret" is the earliest it could arrive.
/// </summary>
public static class DeathTimingAdvisor
{
    // From the fountain through the Frost-gate back to the outer turret; estimated, not measured.
    private const double WalkBack = 8;
    private const double ItemGold = 1100;
    private const double FullItemGold = 2500;
    private const double PoorGold = 600;
    private const double LowHealth = 0.4;
    private const double LongAbsence = 40;
    private const int ShutdownStreak = 3;
    private const int Blocking = -10;

    // ARAM: Mayhem offers an augment at these levels, chosen only while the shop is open: dead or on the platform.
    private static readonly int[] AugmentLevels = [7, 11, 15];

    /// <param name="augmentsOpenedAt">
    /// The level at which the shop was last open in ARAM: Mayhem, so augments up to it were offered; null when
    /// unknown, such as after the program started mid-game.
    /// </param>
    public static DeathTimingAdvice? Evaluate(
        LiveMatchSnapshot snapshot,
        MinionWaveForecast waves,
        TeamPressure? pressure,
        int? augmentsOpenedAt = null
    )
    {
        // Elsewhere recall opens the shop, so dying to shop is never the better trade.
        if (snapshot.Map != 12 || DeathTimeRules.For(snapshot.Mode, snapshot.Map) is not { } rules)
        {
            return null;
        }

        var self = snapshot.Players.FirstOrDefault(p => p.Self);
        if (self == null || self.IsDead || self.Level <= 0)
        {
            return null;
        }

        double away = rules.Seconds(self.Level, snapshot.Time) + WalkBack;
        var factors = new List<DeathTimingFactor>();
        AddStructures(factors, waves);
        AddStreak(factors, snapshot, self);
        AddWaves(factors, waves, snapshot.Time, away, pressure);
        AddPlayers(factors, snapshot, self, away);
        AddResources(factors, snapshot.Active, pressure);
        AddAugments(factors, snapshot, self, augmentsOpenedAt);
        AddPressure(factors, pressure, snapshot, self);
        return Decide(factors);
    }

    private static DeathTimingAdvice Decide(List<DeathTimingFactor> factors)
    {
        int points = factors.Sum(f => f.Weight);
        var verdict =
            factors.Any(f => f.Weight <= Blocking) ? DeathTimingVerdict.Avoid
            : points >= 2 ? DeathTimingVerdict.Good
            : points >= 0 ? DeathTimingVerdict.Okay
            : DeathTimingVerdict.Avoid;
        // The verdict leads with the factors that point its way; "可以" shows the best case and the main risk.
        var ordered = factors.Where(f => f.Weight != 0).OrderByDescending(f => Math.Abs(f.Weight)).ToArray();
        var reasons = verdict switch
        {
            DeathTimingVerdict.Avoid => ordered.Where(f => f.Weight < 0).Take(2),
            DeathTimingVerdict.Good => ordered.Where(f => f.Weight > 0).Take(2),
            _ => ordered.Where(f => f.Weight > 0).Take(1).Concat(ordered.Where(f => f.Weight < 0).Take(1)),
        };
        return new(verdict, reasons.Select(f => f.Text).ToArray(), factors.ToArray());
    }

    private static void AddStructures(List<DeathTimingFactor> factors, MinionWaveForecast waves)
    {
        bool inhibitorDown = waves
            .Lanes.SelectMany(l => l.Progress?.Markers ?? [])
            .Any(m => m.Ally && m.Kind == LaneStructureKind.Inhibitor && !m.Alive);
        if (inhibitorDown)
        {
            factors.Add(new(Blocking, "我方小水晶已破，超级兵持续进攻"));
        }
    }

    // Bounties follow gold now, not streaks, so a streak only hints at a shutdown; the text says it is a guess.
    private static void AddStreak(List<DeathTimingFactor> factors, LiveMatchSnapshot snapshot, LiveMatchPlayer self)
    {
        var aliases = snapshot.EventAliasMap();
        int streak = 0;
        foreach (var kill in (snapshot.Events ?? []).Where(e => e.Name == "ChampionKill").OrderBy(e => e.Time))
        {
            if (aliases.GetValueOrDefault(kill.Victim) == self.Key)
            {
                streak = 0;
            }
            else if (aliases.GetValueOrDefault(kill.Killer) == self.Key)
            {
                streak++;
            }
        }

        if (streak >= ShutdownStreak)
        {
            factors.Add(new(Blocking, $"你已连杀 {streak} 次，可能有终结赏金（估）"));
        }
    }

    private static void AddWaves(
        List<DeathTimingFactor> factors,
        MinionWaveForecast waves,
        double now,
        double away,
        TeamPressure? pressure
    )
    {
        var arrivals = waves
            .Lanes.Select(l => l.Progress)
            .OfType<LaneProgress>()
            .SelectMany(progress => EnemyArrivals(progress, now))
            .Where(a => a.Time <= now + away)
            .ToArray();
        string absence = $"缺席约 {away:0} 秒";
        if (arrivals.Length == 0)
        {
            factors.Add(new(1, absence + "，期间敌方兵线到不了我方外塔"));
        }
        else
        {
            int cannons = arrivals.Count(a => a.Cannon);
            int weight = arrivals.Length + cannons + (pressure?.Siege == PressureLevel.High ? arrivals.Length : 0);
            factors.Add(
                new(
                    -weight,
                    $"{absence}，敌方兵线最快 {arrivals.Min(a => a.Time) - now:0} 秒到我方外塔"
                        + (cannons > 0 ? "（含炮车）" : "")
                )
            );
        }

        if (away > LongAbsence)
        {
            factors.Add(new(-1, $"缺席超过 {LongAbsence:0} 秒，约错过两波兵"));
        }
    }

    // Enemy waves walk from their nexus toward the own side; the outermost standing structure is the one they hit.
    private static IEnumerable<(double Time, bool Cannon)> EnemyArrivals(LaneProgress progress, double now)
    {
        var standing = progress
            .Markers.Where(m => m.Ally && m.Alive && m.Kind != LaneStructureKind.Base)
            .Select(m => m.At)
            .DefaultIfEmpty(progress.Spawn)
            .Max();
        double distance = (progress.EnemySpawn - standing) * progress.Length;
        return progress
            .Waves.Where(w => w.Speed > 0)
            .Select(w => (Time: w.Time + Math.Max(0, distance - w.Bonus) / w.Speed, w.Cannon))
            .Where(a => a.Time > now);
    }

    private static void AddPlayers(
        List<DeathTimingFactor> factors,
        LiveMatchSnapshot snapshot,
        LiveMatchPlayer self,
        double away
    )
    {
        // An enemy who is still dead for most of the absence cannot use the numbers advantage.
        var enemies = snapshot
            .Players.Where(p => p.Team != self.Team && p.IsDead && p.RespawnTimer >= away / 2)
            .ToArray();
        if (enemies.Length > 0)
        {
            factors.Add(
                new(
                    enemies.Length >= 2 ? 3 : 1,
                    $"对面 {enemies.Length} 人阵亡（最长还剩 {enemies.Max(p => p.RespawnTimer):0} 秒）"
                )
            );
        }

        int allies = snapshot.Players.Count(p =>
            p.Team == self.Team && p.Key != self.Key && p.IsDead && p.RespawnTimer > 0
        );
        if (allies >= 2)
        {
            factors.Add(new(-2, $"我方已有 {allies} 人阵亡，再死只剩 {4 - allies} 人守"));
        }
    }

    private static void AddResources(List<DeathTimingFactor> factors, LiveActivePlayer? active, TeamPressure? pressure)
    {
        if (active == null)
        {
            return;
        }

        double gold = active.CurrentGold;
        if (gold >= FullItemGold)
        {
            factors.Add(new(2, $"身上 {gold:0} 金，够买大件"));
        }
        else if (gold >= ItemGold)
        {
            factors.Add(new(1, $"身上 {gold:0} 金，够买关键小件"));
        }
        else if (gold < PoorGold)
        {
            factors.Add(new(-1, $"身上只有 {gold:0} 金"));
        }

        if (active.Stats is { MaxHealth: > 0 } stats && stats.CurrentHealth / stats.MaxHealth < LowHealth)
        {
            factors.Add(new(1, $"血量只剩 {stats.CurrentHealth / stats.MaxHealth:0%}，出生平台不回血"));
            if (pressure?.SustainGap == PressureLevel.High)
            {
                factors.Add(new(1, "我方缺治疗，送死回满更划算"));
            }
        }
    }

    private static void AddAugments(
        List<DeathTimingFactor> factors,
        LiveMatchSnapshot snapshot,
        LiveMatchPlayer self,
        int? openedAt
    )
    {
        if (!snapshot.Mode.Equals("KIWI", StringComparison.OrdinalIgnoreCase) || openedAt is not int opened)
        {
            return;
        }

        int[] pending = AugmentLevels.Where(level => level > opened && level <= self.Level).ToArray();
        if (pending.Length > 0)
        {
            factors.Add(
                new(
                    2,
                    pending.Length == 1
                        ? $"{pending[0]} 级的强化还没选，死后才能选"
                        : $"{string.Join("、", pending)} 级的 {pending.Length} 个强化还没选，死后才能选"
                )
            );
        }
    }

    private static void AddPressure(
        List<DeathTimingFactor> factors,
        TeamPressure? pressure,
        LiveMatchSnapshot snapshot,
        LiveMatchPlayer self
    )
    {
        if (pressure == null)
        {
            factors.Add(new(0, "阵容数据未加载，只按兵线、复活时间和双方阵亡判断"));
            return;
        }

        if (pressure.Siege == PressureLevel.High)
        {
            factors.Add(new(0, "对面消耗和清线强，缺人时塔掉得快"));
        }

        bool enemiesDown = snapshot.Players.Any(p => p.Team != self.Team && p.IsDead);
        if (pressure.Dive == PressureLevel.High && !enemiesDown)
        {
            factors.Add(new(-1, "对面开团强，少一人容易被强开"));
        }

        if (pressure.SelfRoles.Length > 0 && pressure.Dive != PressureLevel.Low)
        {
            factors.Add(new(-1, $"你是我方主要{string.Join("、", pressure.SelfRoles)}"));
        }
    }
}
