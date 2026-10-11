using System.Text.RegularExpressions;

namespace League.Models;

/// <summary>Extracted clues retain uncertainty; missing descriptions never prove a capability is absent.</summary>
internal sealed class CompositionEvidence
{
    internal sealed record ControlSpell(int Id, string Key, string Text, bool Conditional, bool Targeted);

    internal sealed record DamageTypes(int Id, bool Physical, bool Magic);

    public int[] Team { get; }
    public bool Complete { get; }
    private readonly List<ControlSpell> _control = [];
    public IReadOnlyList<ControlSpell> Control => _control;
    private readonly List<string> _protection = [];
    public IReadOnlyList<string> Protection => _protection;
    private readonly List<int> _protectionIds = [];
    public IReadOnlyList<int> ProtectionIds => _protectionIds;
    private readonly List<DamageTypes> _damage = [];
    public IReadOnlyList<DamageTypes> Damage => _damage;
    private readonly List<(int Id, double Range)> _attackRanges = [];
    public IReadOnlyList<(int Id, double Range)> AttackRanges => _attackRanges;
    private readonly List<(int Id, string Key, double Range)> _spellRanges = [];
    public IReadOnlyList<(int Id, string Key, double Range)> SpellRanges => _spellRanges;

    private CompositionEvidence(int[] team, bool complete)
    {
        Team = team;
        Complete = complete;
    }

    public static CompositionEvidence Read(
        int[] team,
        IReadOnlyDictionary<int, ChampionAnalysisReport> reports,
        IReadOnlyDictionary<int, CompositionSkillText[]> descriptions,
        Func<int, string> name
    )
    {
        bool complete =
            team.Length > 0
            && team.All(id =>
                id > 0
                && reports.GetValueOrDefault(id) != null
                && (descriptions.GetValueOrDefault(id) ?? []).Any(skill => skill.Key == "R")
            );
        var evidence = new CompositionEvidence(team, complete);
        foreach (int id in team)
        {
            evidence.ReadHero(id, reports.GetValueOrDefault(id), descriptions.GetValueOrDefault(id) ?? [], name);
        }
        return evidence;
    }

    public string Scope(string text) => Complete ? text : "已读取部分：" + text + "（资料未齐，不能代表全队）";

    public double? Coverage(int count) => Complete ? 100.0 * count / Team.Length : null;

    // Count positions, not unique IDs: duplicate champions provide independent abilities.
    public int ControlSlots => Team.Count(id => Control.Any(control => control.Id == id));

    public ControlSpell[] DirectControl => Control.Where(control => control.Targeted && !control.Conditional).ToArray();

    public string[] ControlDescriptions(Func<int, string> name) =>
        Control
            .DistinctBy(control => (control.Id, control.Key))
            .Select(control =>
                $"{name(control.Id)} · {control.Key}："
                + (control.Text.Length > 0 ? control.Text : "硬控制标签；命中方式待确认")
            )
            .ToArray();

    private void ReadHero(int id, ChampionAnalysisReport? report, CompositionSkillText[] skills, Func<int, string> name)
    {
        foreach (
            string key in (report?.Spells.Select(spell => spell.Key) ?? [])
                .Concat(skills.Select(skill => skill.Key))
                .Distinct()
        )
        {
            ReadAbility(
                id,
                key,
                report?.Spells.FirstOrDefault(spell => spell.Key == key),
                skills.FirstOrDefault(skill => skill.Key == key)?.Description ?? ""
            );
        }
        ReadProtection(id, skills, name);
        bool physical = skills.Any(skill => skill.Description.Contains("物理伤害"));
        bool magic = skills.Any(skill => skill.Description.Contains("魔法伤害"));
        if (physical || magic)
        {
            _damage.Add(new(id, physical, magic));
        }
        var attack = report
            ?.Attributes.FirstOrDefault(attribute => attribute.Name == "普攻距离")
            ?.Values.FirstOrDefault();
        if (attack is > 0)
        {
            _attackRanges.Add((id, attack.Value));
        }
    }

    private void ReadAbility(int id, string key, SpellAnalysis? spell, string text)
    {
        // Only explicit display ranges of damaging abilities, excluding shields, dashes and internal ranges.
        if (Has(text, "物理伤害|魔法伤害|真实伤害"))
        {
            var values = (spell?.Parameters ?? [])
                .Where(parameter => parameter.Name == "显示施法范围")
                .SelectMany(parameter => parameter.Values)
                .Where(value => value is > 0 and < 25000)
                .Select(value => value!.Value)
                .ToArray();
            if (values.Length > 0)
            {
                _spellRanges.Add((id, key, values.Max()));
            }
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
        {
            _control.Add(
                new(
                    id,
                    key,
                    text,
                    Has(text, "如果|若|再次|叠|层|蓄力|延迟|命中|碰撞|第一|首个"),
                    spell?.Targeting.Equals("Target", StringComparison.OrdinalIgnoreCase) == true && text.Length > 0
                )
            );
        }
    }

    private void ReadProtection(int id, CompositionSkillText[] skills, Func<int, string> name)
    {
        var protective = skills
            .Where(skill =>
                Regex
                    .Split(skill.Description, "[。！!；;\\n]")
                    .Any(sentence =>
                        Has(sentence, "友方|友军|队友|我方|友善")
                        && Has(sentence, "护盾|治疗|回复.{0,8}生命|恢复.{0,8}生命")
                        && !Has(sentence, "不能|无法|不会")
                    )
            )
            .ToArray();
        if (protective.Length > 0)
        {
            _protectionIds.Add(id);
            _protection.AddRange(
                protective.Select(skill => $"{name(id)} · {skill.Key} {skill.Name}：{skill.Description}")
            );
        }
    }

    private static bool Has(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
}
