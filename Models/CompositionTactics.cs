namespace League.Models;

public sealed record CompositionSkillText(string Key, string Name, string Description);

public sealed record CompositionRadarAxis(string Label, double? Score, string Basis);

public sealed record CompositionFinding(string Category, string Status, string Detail, string Advice, string[] Evidence)
{
    public CompositionRadarAxis? Radar { get; init; }
}

/// <summary>Conservative clues from local descriptions and extracted spell data, not a combat simulation.</summary>
public static class CompositionTactics
{
    public static CompositionFinding[] Analyze(
        int[] team,
        IReadOnlyDictionary<int, ChampionAnalysisReport> reports,
        IReadOnlyDictionary<int, CompositionSkillText[]> descriptions,
        Func<int, string> name
    )
    {
        var evidence = CompositionEvidence.Read(team, reports, descriptions, name);
        return
        [
            ControlFinding(evidence, name),
            ProtectionFinding(evidence),
            EngageFinding(evidence, name),
            RangeFinding(evidence, name),
            DamageFinding(evidence, name),
        ];
    }

    private static CompositionFinding ControlFinding(CompositionEvidence evidence, Func<int, string> name)
    {
        int count = evidence.ControlSlots;
        return new(
            "控制分布",
            count == 1 ? "来源集中"
                : count > 1 ? "多个来源"
                : "待确认",
            evidence.Scope($"{count}/{evidence.Team.Length} 个位置有硬控制记录。"),
            count == 1
                ? "关键控制落空或进入冷却后，避免立即强行接战；换人可补充第二个控制来源。"
                : "留意控制衔接，避免全部控制同时交出。",
            evidence.ControlDescriptions(name)
        )
        {
            Radar = new(
                "控制覆盖",
                evidence.Coverage(count),
                "有硬控制记录的位置数 ÷ 队伍位置数 × 100；不衡量控制时长。"
            ),
        };
    }

    private static CompositionFinding ProtectionFinding(CompositionEvidence evidence)
    {
        int count = evidence.ProtectionIds.Count;
        return new(
            "团队保护",
            !evidence.Complete ? "待补全资料"
                : count <= 1 ? "可能有限"
                : "多个来源",
            evidence.Scope($"{count}/{evidence.Team.Length} 个位置的说明同时提及友方目标与护盾或治疗。"),
            "这是说明文本线索，需核对作用对象、触发条件与冷却；自身护盾不计入。保护来源少时更需留意走位与消耗。",
            evidence.Protection.ToArray()
        )
        {
            Radar = new(
                "保护覆盖",
                evidence.Coverage(count),
                "有友方护盾或治疗说明的位置数 ÷ 队伍位置数 × 100；不衡量保护强度。"
            ),
        };
    }

    private static CompositionFinding EngageFinding(CompositionEvidence evidence, Func<int, string> name)
    {
        var direct = evidence.DirectControl;
        return new(
            "可靠先手",
            direct.Length > 0 ? "有指向控制线索"
                : evidence.Control.Count > 0 ? "可能依赖命中或条件"
                : "待确认",
            evidence.Scope(
                direct.Length > 0
                    ? "发现目标型硬控制，仍需核对距离、前置条件和能否主动接近目标。"
                    : "未确认无前置条件的指向控制；非指向控制不代表不能开团。"
            ),
            "优先等待命中机会，结合突进、侧翼与队友跟进距离决定是否开团；不能将目标类型理解为必中。",
            evidence.ControlDescriptions(name)
        )
        {
            Radar = new(
                "指向控制",
                evidence.Coverage(evidence.Team.Count(id => direct.Any(control => control.Id == id))),
                "有无已识别前置条件的目标型硬控制的位置占比；不是必中率或开团成功率。"
            ),
        };
    }

    private static CompositionFinding RangeFinding(CompositionEvidence evidence, Func<int, string> name)
    {
        var ranges = evidence.AttackRanges;
        var spellRanges = evidence.SpellRanges;
        int shortRange = ranges.Count(range => range.Range < 400);
        return new(
            "射程分布",
            ranges.Count > 0 && shortRange == ranges.Count ? "普攻距离偏近" : "普攻距离参考",
            evidence.Scope(
                (
                    ranges.Count == 0
                        ? "普攻距离尚未读取。"
                        : $"已读取 {ranges.Count} 个位置，其中 {shortRange} 个基础普攻距离低于 400。"
                )
                    + (
                        spellRanges.Count > 0
                            ? $"伤害技能显示施法距离最大记录为 {spellRanges.Max(range => range.Range):0.#}。"
                            : "伤害技能的显示施法距离尚未确认。"
                    )
            ),
            "近距离输出需要考虑接近目标的成本，可能更依赖绕侧或反打。显示施法距离不是最终命中距离，不包含弹道延伸、范围半径或条件加成；消耗能力还取决于命中和冷却。",
            ranges
                .Select(range => $"{name(range.Id)}：基础普攻距离 {range.Range:0.#}")
                .Concat(
                    spellRanges.Select(range =>
                        $"{name(range.Id)} · {range.Key}：伤害技能显示施法距离 {range.Range:0.#}"
                    )
                )
                .ToArray()
        )
        {
            Radar = new(
                "普攻距离",
                evidence.Team.Length > 0 && ranges.Count == evidence.Team.Length
                    ? Math.Clamp(ranges.Average(range => range.Range) / 1000 * 100, 0, 100)
                    : null,
                "全队基础普攻距离均值 ÷ 1000 × 100，上限 100；不包含技能射程。"
            ),
        };
    }

    private static CompositionFinding DamageFinding(CompositionEvidence evidence, Func<int, string> name)
    {
        var damage = evidence.Damage;
        int physical = damage.Count(hero => hero.Physical && !hero.Magic);
        int magic = damage.Count(hero => hero.Magic && !hero.Physical);
        int mixed = damage.Count - physical - magic;
        bool concentrated = damage.Count > 0 && Math.Max(physical, magic) >= Math.Ceiling(evidence.Team.Length * 0.8);
        return new(
            "伤害结构",
            concentrated ? "技能类型可能集中" : "技能类型线索",
            evidence.Scope(
                $"技能说明：仅物理 {physical} 位、仅魔法 {magic} 位、两者都有 {mixed} 位；未识别 {evidence.Team.Length - damage.Count} 位。"
            ),
            concentrated
                ? "至少八成位置的已识别技能类型相同，对手可能集中堆对应抗性；可比较另一类伤害来源。普攻、真实伤害、出装及实际伤害占比未纳入。"
                : "这是技能说明中的类型分布，不是实际伤害占比；需同时考虑普攻、真实伤害和出装。",
            damage
                .Select(hero =>
                    $"{name(hero.Id)}："
                    + (
                        hero.Physical && hero.Magic ? "物理 / 魔法"
                        : hero.Physical ? "物理"
                        : "魔法"
                    )
                )
                .ToArray()
        )
        {
            Radar = new(
                "伤害多样",
                evidence.Complete && damage.Count == evidence.Team.Length
                    ? 100.0 * (1 - Math.Abs(physical - magic) / (double)damage.Count)
                    : null,
                "技能类型平衡度：100 × (1 − |仅物理位置数 − 仅魔法位置数| ÷ 已识别位置数)。双类型等分，不含普攻、真实伤害及实际伤害占比。"
            ),
        };
    }
}
