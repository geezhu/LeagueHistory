namespace League.Models;

public enum BehaviorTagSeverity
{
    Info,
    Warn,
    Bad,
}

/// <summary>One behaviour tag: when it applies, how serious it reads and the rule as players see it.</summary>
public sealed record BehaviorTagRule(
    string Name,
    BehaviorTagSeverity Severity,
    string Description,
    Func<PlayerBehavior, bool> Applies
);

/// <summary>
/// The behaviour tags of a player's recent games, as upstream tuned them in 2026-10 (b1d561c): thresholds on the
/// per-game averages, and the severity of upstream's README ratings (very high is bad, high warn, medium info). The
/// chat summaries, the Game Bar record page and README all read them from here.
/// </summary>
public static class BehaviorTags
{
    public const double LovesSurrenderRate = 35;
    public const double EasySurrenderRate = 22;
    public const double MissingPings = 2.6;
    public const double VisionPings = 2.2;
    public const double GetBackPings = 3.0;
    public const double DangerPings = 2.2;
    public const double UnsteadyTotal = 8.0;
    public const double FragileTotal = 10.5;
    public const double FragileGetBack = 3.8;
    public const double ComplainGetBack = 4.2;
    public const double ComplainTotal = 10.0;
    public const double PressureTotal = 12.5;
    public const double ImpatientTotal = 14.5;
    public const double ImpatientMissing = 4.2;
    public const double ImpatientGetBack = 4.8;

    /// <summary>In the order the tags are listed in a summary.</summary>
    public static readonly IReadOnlyList<BehaviorTagRule> Rules =
    [
        new(
            "爱投降",
            BehaviorTagSeverity.Bad,
            $"以投降结束的对局 ≥ {LovesSurrenderRate}%",
            b => b.SurrenderRate >= LovesSurrenderRate
        ),
        new(
            "容易投降",
            BehaviorTagSeverity.Info,
            $"以投降结束的对局 ≥ {EasySurrenderRate}%",
            b => b.SurrenderRate >= EasySurrenderRate && b.SurrenderRate < LovesSurrenderRate
        ),
        new(
            "爱发问号",
            BehaviorTagSeverity.Warn,
            $"每场平均问号（敌人消失）≥ {MissingPings} 次",
            b => b.AvgMissingPings >= MissingPings
        ),
        new(
            "爱发提醒",
            BehaviorTagSeverity.Info,
            $"每场平均视野提醒 ≥ {VisionPings} 次",
            b => b.AvgVisionPings >= VisionPings
        ),
        new(
            "爱发撤退",
            BehaviorTagSeverity.Warn,
            $"每场平均撤退 ≥ {GetBackPings} 次",
            b => b.AvgGetBackPings >= GetBackPings
        ),
        new(
            "爱发危险",
            BehaviorTagSeverity.Info,
            $"每场平均危险 ≥ {DangerPings} 次",
            b => b.AvgDangerPings >= DangerPings
        ),
        new(
            "心态容易崩",
            BehaviorTagSeverity.Warn,
            $"四种信号每场合计 ≥ {UnsteadyTotal} 次",
            b => Total(b) >= UnsteadyTotal
        ),
        new(
            "玻璃心",
            BehaviorTagSeverity.Bad,
            $"四种信号每场合计 ≥ {FragileTotal} 次，或撤退 ≥ {FragileGetBack} 次",
            b => Total(b) >= FragileTotal || b.AvgGetBackPings >= FragileGetBack
        ),
        new(
            "爱抱怨",
            BehaviorTagSeverity.Bad,
            $"撤退 ≥ {ComplainGetBack} 次且四种信号合计 ≥ {ComplainTotal} 次",
            b => b.AvgGetBackPings >= ComplainGetBack && Total(b) >= ComplainTotal
        ),
        new(
            "压力怪",
            BehaviorTagSeverity.Bad,
            $"四种信号每场合计 ≥ {PressureTotal} 次",
            b => Total(b) >= PressureTotal
        ),
        new(
            "急性子",
            BehaviorTagSeverity.Bad,
            $"四种信号每场合计 ≥ {ImpatientTotal} 次，或问号 ≥ {ImpatientMissing} 次，或撤退 ≥ {ImpatientGetBack} 次",
            b =>
                Total(b) >= ImpatientTotal
                || b.AvgMissingPings >= ImpatientMissing
                || b.AvgGetBackPings >= ImpatientGetBack
        ),
    ];

    /// <summary>The four signals' per-game averages added up.</summary>
    public static double Total(PlayerBehavior b) =>
        b.AvgMissingPings + b.AvgVisionPings + b.AvgGetBackPings + b.AvgDangerPings;

    /// <summary>The tags that apply, joined with 「、」 and without brackets; the chat line adds them.</summary>
    public static string Summary(PlayerBehavior b) =>
        string.Join("、", Rules.Where(rule => rule.Applies(b)).Select(rule => rule.Name));

    public static BehaviorTagRule? Find(string name) => Rules.FirstOrDefault(rule => rule.Name == name);
}
