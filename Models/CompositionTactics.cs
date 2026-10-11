using System.Text.RegularExpressions;

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
    private static bool Has(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);

    public static CompositionFinding[] Analyze(
        int[] team,
        IReadOnlyDictionary<int, ChampionAnalysisReport> reports,
        IReadOnlyDictionary<int, CompositionSkillText[]> descriptions,
        Func<int, string> name
    )
    {
        var slots = team.Select(id => new
            {
                Id = id,
                Report = reports.GetValueOrDefault(id),
                Skills = descriptions.GetValueOrDefault(id) ?? [],
            })
            .ToArray();
        bool complete =
            slots.Length > 0 && slots.All(s => s.Id > 0 && s.Report != null && s.Skills.Any(t => t.Key == "R"));
        string Scope(string text) => complete ? text : "已读取部分：" + text + "（资料未齐，不能代表全队）";
        var control = new List<(int Id, string Key, string Text, bool Conditional, bool Targeted)>();
        var protection = new List<string>();
        var protectIds = new List<int>();
        var damage = new List<(int Id, bool Physical, bool Magic)>();
        var ranges = new List<(int Id, double Range)>();
        var spellRanges = new List<(int Id, string Key, double Range)>();
        foreach (var slot in slots)
        {
            foreach (
                var key in (slot.Report?.Spells.Select(s => s.Key) ?? [])
                    .Concat(slot.Skills.Select(s => s.Key))
                    .Distinct()
            )
            {
                var spell = slot.Report?.Spells.FirstOrDefault(s => s.Key == key);
                var skill = slot.Skills.FirstOrDefault(s => s.Key == key);
                string text = skill?.Description ?? "";
                // Only explicit display ranges of damaging abilities; shields, dashes and raw internal ranges are excluded.
                if (Has(text, "物理伤害|魔法伤害|真实伤害"))
                {
                    var values = (spell?.Parameters ?? [])
                        .Where(p => p.Name == "显示施法范围")
                        .SelectMany(p => p.Values)
                        .Where(v => v is > 0 and < 25000)
                        .Select(v => v!.Value)
                        .ToArray();
                    if (values.Length > 0)
                        spellRanges.Add((slot.Id, key, values.Max()));
                }
                if (
                    spell?.Tags.Contains("Trait_ImmobilizingCCSpell") == true
                    || Regex
                        .Split(text, "[。！!；;\\n]")
                        .Any(sentence =>
                            Has(sentence, "眩晕|禁锢|击飞|压制|恐惧|魅惑|嘲讽")
                            && !Has(sentence, "免疫|解除|抵抗|无法被|不会被")
                        )
                )
                    control.Add(
                        (
                            slot.Id,
                            key,
                            text,
                            Has(text, "如果|若|再次|叠|层|蓄力|延迟|命中|碰撞|第一|首个"),
                            spell?.Targeting.Equals("Target", StringComparison.OrdinalIgnoreCase) == true
                                && text.Length > 0
                        )
                    );
            }
            var protective = slot
                .Skills.Where(s =>
                    Regex
                        .Split(s.Description, "[。！!；;\\n]")
                        .Any(sentence =>
                            Has(sentence, "友方|友军|队友|我方|友善")
                            && Has(sentence, "护盾|治疗|回复.{0,8}生命|恢复.{0,8}生命")
                            && !Has(sentence, "不能|无法|不会")
                        )
                )
                .ToArray();
            if (protective.Length > 0)
            {
                protectIds.Add(slot.Id);
                protection.AddRange(protective.Select(s => $"{name(slot.Id)} · {s.Key} {s.Name}：{s.Description}"));
            }
            bool physical = slot.Skills.Any(s => s.Description.Contains("物理伤害"));
            bool magic = slot.Skills.Any(s => s.Description.Contains("魔法伤害"));
            if (physical || magic)
                damage.Add((slot.Id, physical, magic));
            var attack = slot.Report?.Attributes.FirstOrDefault(a => a.Name == "普攻距离")?.Values.FirstOrDefault();
            if (attack is > 0)
                ranges.Add((slot.Id, attack.Value));
        }
        var findings = new List<CompositionFinding>();
        // Count positions, not unique champion IDs: duplicate champions still provide independent abilities.
        int controlSlots = slots.Count(s => control.Any(c => c.Id == s.Id));
        var ccEvidence = control
            .DistinctBy(c => (c.Id, c.Key))
            .Select(c => $"{name(c.Id)} · {c.Key}：" + (c.Text.Length > 0 ? c.Text : "硬控制标签；命中方式待确认"))
            .ToArray();
        findings.Add(
            new(
                "控制分布",
                controlSlots == 1 ? "来源集中"
                    : controlSlots > 1 ? "多个来源"
                    : "待确认",
                Scope($"{controlSlots}/{team.Length} 个位置有硬控制记录。"),
                controlSlots == 1
                    ? "关键控制落空或进入冷却后，避免立即强行接战；换人可补充第二个控制来源。"
                    : "留意控制衔接，避免全部控制同时交出。",
                ccEvidence
            )
        );
        findings.Add(
            new(
                "团队保护",
                !complete ? "待补全资料"
                    : protectIds.Count <= 1 ? "可能有限"
                    : "多个来源",
                Scope($"{protectIds.Count}/{team.Length} 个位置的说明同时提及友方目标与护盾或治疗。"),
                "这是说明文本线索，需核对作用对象、触发条件与冷却；自身护盾不计入。保护来源少时更需留意走位与消耗。",
                protection.ToArray()
            )
        );
        var direct = control.Where(c => c.Targeted && !c.Conditional).ToArray();
        findings.Add(
            new(
                "可靠先手",
                direct.Length > 0 ? "有指向控制线索"
                    : control.Count > 0 ? "可能依赖命中或条件"
                    : "待确认",
                Scope(
                    direct.Length > 0
                        ? "发现目标型硬控制，仍需核对距离、前置条件和能否主动接近目标。"
                        : "未确认无前置条件的指向控制；非指向控制不代表不能开团。"
                ),
                "优先等待命中机会，结合突进、侧翼与队友跟进距离决定是否开团；不能将目标类型理解为必中。",
                ccEvidence
            )
        );
        int shortRange = ranges.Count(r => r.Range < 400);
        findings.Add(
            new(
                "射程分布",
                ranges.Count > 0 && shortRange == ranges.Count ? "普攻距离偏近" : "普攻距离参考",
                Scope(
                    (
                        ranges.Count == 0
                            ? "普攻距离尚未读取。"
                            : $"已读取 {ranges.Count} 个位置，其中 {shortRange} 个基础普攻距离低于 400。"
                    )
                        + (
                            spellRanges.Count > 0
                                ? $"伤害技能显示施法距离最大记录为 {spellRanges.Max(s => s.Range):0.#}。"
                                : "伤害技能的显示施法距离尚未确认。"
                        )
                ),
                "近距离输出需要考虑接近目标的成本，可能更依赖绕侧或反打。显示施法距离不是最终命中距离，不包含弹道延伸、范围半径或条件加成；消耗能力还取决于命中和冷却。",
                ranges
                    .Select(r => $"{name(r.Id)}：基础普攻距离 {r.Range:0.#}")
                    .Concat(spellRanges.Select(r => $"{name(r.Id)} · {r.Key}：伤害技能显示施法距离 {r.Range:0.#}"))
                    .ToArray()
            )
        );
        int p = damage.Count(d => d.Physical && !d.Magic),
            m = damage.Count(d => d.Magic && !d.Physical),
            mixed = damage.Count - p - m;
        bool concentrated = damage.Count > 0 && Math.Max(p, m) >= Math.Ceiling(team.Length * 0.8);
        findings.Add(
            new(
                "伤害结构",
                concentrated ? "技能类型可能集中" : "技能类型线索",
                Scope(
                    $"技能说明：仅物理 {p} 位、仅魔法 {m} 位、两者都有 {mixed} 位；未识别 {team.Length - damage.Count} 位。"
                ),
                concentrated
                    ? "至少八成位置的已识别技能类型相同，对手可能集中堆对应抗性；可比较另一类伤害来源。普攻、真实伤害、出装及实际伤害占比未纳入。"
                    : "这是技能说明中的类型分布，不是实际伤害占比；需同时考虑普攻、真实伤害和出装。",
                damage
                    .Select(d =>
                        $"{name(d.Id)}："
                        + (
                            d.Physical && d.Magic ? "物理 / 魔法"
                            : d.Physical ? "物理"
                            : "魔法"
                        )
                    )
                    .ToArray()
            )
        );
        double? Coverage(int count) => complete ? 100.0 * count / team.Length : null;
        var axes = new[]
        {
            new CompositionRadarAxis(
                "控制覆盖",
                Coverage(controlSlots),
                "有硬控制记录的位置数 ÷ 队伍位置数 × 100；不衡量控制时长。"
            ),
            new CompositionRadarAxis(
                "保护覆盖",
                Coverage(protectIds.Count),
                "有友方护盾或治疗说明的位置数 ÷ 队伍位置数 × 100；不衡量保护强度。"
            ),
            new CompositionRadarAxis(
                "指向控制",
                Coverage(slots.Count(s => direct.Any(c => c.Id == s.Id))),
                "有无已识别前置条件的目标型硬控制的位置占比；不是必中率或开团成功率。"
            ),
            new CompositionRadarAxis(
                "普攻距离",
                team.Length > 0 && ranges.Count == team.Length
                    ? Math.Clamp(ranges.Average(r => r.Range) / 1000 * 100, 0, 100)
                    : null,
                "全队基础普攻距离均值 ÷ 1000 × 100，上限 100；不包含技能射程。"
            ),
            new CompositionRadarAxis(
                "伤害多样",
                complete && damage.Count == team.Length ? 100.0 * (1 - Math.Abs(p - m) / (double)damage.Count) : null,
                "技能类型平衡度：100 × (1 − |仅物理位置数 − 仅魔法位置数| ÷ 已识别位置数)。双类型等分，不含普攻、真实伤害及实际伤害占比。"
            ),
        };
        return findings.Select((f, i) => f with { Radar = axes[i] }).ToArray();
    }
}
