namespace League.Models;

public sealed record CompositionConcern(string Tag, string Title, string Impact, string Suggestion, int[] Providers);

public sealed record CompositionAssessment(int Known, int Slots, CompositionConcern[] Concerns, string[] Unconfirmed)
{
    public bool Complete => Slots > 0 && Known == Slots;
}

public static class CompositionWeakness
{
    public static string[] HeroInsights(ChampionAnalysisReport report)
    {
        var lines = new List<string>();
        string[] Keys(string tag) =>
            report
                .Spells.Where(s => s.Tags.Contains(tag, StringComparer.Ordinal))
                .Select(s => s.Key)
                .Distinct()
                .ToArray();
        var control = Keys("Trait_ImmobilizingCCSpell");
        if (control.Length > 0)
        {
            lines.Add("硬控制来源：" + string.Join(" / ", control) + "。");
            if (control.Length == 1)
            {
                lines.Add(
                    control[0] == "R"
                        ? "控制依赖：目前只在 R 中确认了硬控制。建议围绕大招可用时机交战，大招不可用时先核对其他留人手段。"
                        : "控制依赖：目前只在 "
                            + control[0]
                            + " 中确认了硬控制。建议保留该技能用于关键留人或自保，交出后留意对手突进。"
                );
            }
            else
            {
                lines.Add("控制衔接：已记录多个控制技能，可考虑错开使用；能否连续命中仍需结合目标类型和技能说明判断。");
            }
        }
        var shield = Keys("Trait_Shield");
        if (shield.Length > 0)
        {
            lines.Add("护盾来源：" + string.Join(" / ", shield) + "。先确认能否作用队友，再决定用于自保还是保护队友。");
        }

        var area = Keys("Trait_AoE");
        if (area.Length > 0)
        {
            lines.Add(
                "范围效果："
                    + string.Join(" / ", area)
                    + "。可结合技能说明寻找覆盖多目标的机会；范围标签本身不代表范围伤害。"
            );
        }

        double? Attribute(string name) =>
            report.Attributes.FirstOrDefault(a => a.Name == name)?.Values.FirstOrDefault();
        var range = Attribute("普攻距离");
        var speed = Attribute("基础攻速");
        if (range is > 0)
        {
            lines.Add($"基础普攻距离：{range:0.##}。这是普攻的接近距离参考，不是技能最远攻击距离。");
        }

        if (speed is > 0)
        {
            lines.Add(
                $"基础攻速：{speed:0.###} 次/秒，约 {1 / speed:0.##} 秒一次。未计等级成长、装备和技能效果，也不代表攻击前摇。"
            );
        }

        if (lines.Count == 0)
        {
            lines.Add("该英雄的已解析数据不足以生成能力建议，请查看下方技能说明；不会将缺少标签当作没有对应能力。");
        }

        return lines.ToArray();
    }

    private static readonly (string Tag, string Name, string Impact, string Suggestion)[] Rules =
    [
        (
            "Trait_ImmobilizingCCSpell",
            "硬控制",
            "已记录的硬控制集中在一个位置，相关英雄无法出手或技能不可用时，其他控制来源需要核对。",
            "换人时可优先比较另一名有硬控制记录的英雄，并确认控制的命中条件。"
        ),
        (
            "Trait_Shield",
            "护盾",
            "已记录的护盾机制集中在一个位置；护盾可能仅作用自身，暂不能判断团队保护是否充足。",
            "核对护盾目标；如果希望补充保护，可比较能作用队友的护盾或治疗技能。"
        ),
    ];

    public static int[] Providers(int[] team, IReadOnlyDictionary<int, ChampionAnalysisReport> reports, string tag) =>
        team.Where(id =>
                id > 0
                && reports.TryGetValue(id, out var report)
                && report.Spells.Any(s => s.Tags.Contains(tag, StringComparer.Ordinal))
            )
            .ToArray();

    public static CompositionAssessment Analyze(int[] team, IReadOnlyDictionary<int, ChampionAnalysisReport> reports)
    {
        int known = team.Count(id => id > 0 && reports.ContainsKey(id));
        var concerns = new List<CompositionConcern>();
        var unknown = new List<string>();
        foreach (var rule in Rules)
        {
            var providers = Providers(team, reports, rule.Tag);
            if (providers.Length == 0)
            {
                unknown.Add(rule.Name);
            }
            else if (providers.Length == 1 && team.Length > 1)
            {
                concerns.Add(
                    new(rule.Tag, "已记录的" + rule.Name + "来源集中", rule.Impact, rule.Suggestion, providers)
                );
            }
        }
        return new(known, team.Length, concerns.ToArray(), unknown.ToArray());
    }

    public static string[] Compare(int[] before, int[] after, IReadOnlyDictionary<int, ChampionAnalysisReport> reports)
    {
        if (!Analyze(before, reports).Complete || !Analyze(after, reports).Complete)
        {
            return ["阵容尚未选齐或数据未读全，暂不判断换人是否缓解短板。"];
        }

        var changes = new List<string>();
        foreach (var rule in Rules)
        {
            int oldCount = Providers(before, reports, rule.Tag).Length,
                newCount = Providers(after, reports, rule.Tag).Length;
            if (oldCount == newCount)
            {
                continue;
            }

            string change =
                newCount == 0 ? "已记录来源归零，转为待确认，不能认定能力缺失"
                : oldCount == 0 ? "新增已记录来源，实际覆盖仍需核对"
                : newCount == 1 ? "已记录来源减少到一个位置，集中风险增加"
                : oldCount == 1 ? "已记录来源扩展到多个位置，集中风险降低"
                : "已记录来源数量变化";
            changes.Add($"{rule.Name}：{oldCount} → {newCount} 个位置；{change}。");
        }
        return changes.Count > 0 ? changes.ToArray() : ["已记录的控制与护盾来源分布未变；不代表整体阵容强度不变。"];
    }
}
