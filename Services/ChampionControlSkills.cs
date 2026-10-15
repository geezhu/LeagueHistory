using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>A champion ability that applies crowd control, with the effects its description names.</summary>
public sealed record ControlSkill(string Key, string Name, string[] Effects, string Description)
{
    /// <summary>A ranged strong slow at the top rank, in percent, from the game's spell data.</summary>
    public int? Slow { get; init; }

    public int? Range { get; init; }

    /// <summary>Seconds one slow lasts, and a longer-lasting zone that may slow again; 0 when unknown or none.</summary>
    public double SlowDuration { get; init; }

    public double SlowZone { get; init; }

    /// <summary>The slow at each rank, so the page can show what the champion's level allows.</summary>
    public SlowRank[] SlowRanks { get; init; } = [];
}

/// <summary>
/// Recognizes hard crowd control in the client's champion data (/lol-game-data/assets/v1/champions/{id}.json), which
/// has no structured control field. Both the summary and the detailed description are read. Slows and blinds are
/// left out; the result can miss an effect a description does not mention, so the widget labels it as recognized.
/// </summary>
public static class ChampionControlSkills
{
    private static readonly (string Effect, string[] Words)[] Rules =
    [
        ("击飞", ["击飞", "浮空", "击起", "抛向空中", "抛到空中", "撞飞"]),
        ("击退", ["击退", "推挤", "推开"]),
        ("压制", ["压制"]),
        ("晕眩", ["眩晕", "晕眩", "击晕"]),
        ("魅惑", ["魅惑"]),
        ("嘲讽", ["嘲讽"]),
        ("恐惧", ["恐惧", "恐吓"]),
        ("沉睡", ["沉睡", "昏睡", "入睡"]),
        ("禁锢", ["禁锢", "束缚", "定身", "无法移动", "缠绕", "困住", "绑住"]),
        ("变形", ["变形", "变成"]),
        ("拉拽", ["拉拽", "拽向", "拉向", "拽到", "拉到"]),
        ("沉默", ["沉默"]),
    ];

    // Clauses about removing, resisting or shortening control, cooldowns, or allies' effects apply none themselves.
    private static readonly string[] Negative =
    [
        "免疫",
        "移除",
        "解除",
        "不受",
        "无视",
        "减少",
        "缩短",
        "抵抗",
        "韧性",
        "冷却",
        "友军的",
    ];

    private static readonly Regex Clauses = new("[，。；！,;：:]");
    private static readonly Regex Tags = new("<[^>]+>");
    private static readonly Regex Placeholders = new("@[^@]*@");
    private static readonly Regex SelfPull = new("(自身|自己)[^，。；]{0,3}(拉|拽)");

    // "将其变形" polymorphs an enemy; "变形成魔龙" or "将他的锤子变形" transforms the champion.
    private static readonly Regex Polymorph = new("(使|将)(其|一名敌|敌方|敌人)[^，。；]{0,8}(变形|变成)");

    public static ControlSkill[] Parse(JObject champion)
    {
        var spells = (champion["spells"] as JArray ?? new()).OfType<JObject>().ToArray();
        var passive = champion["passive"] as JObject;
        // Ability names inside descriptions ("缠绕之根") are not effects.
        string[] names = spells
            .Append(passive)
            .Select(s => (string?)s?["name"] ?? "")
            .Where(n => n.Length > 0)
            .ToArray();
        var entries = new List<(string Key, JObject Spell, bool Passive)>();
        if (passive != null)
        {
            entries.Add(("被动", passive, true));
        }

        entries.AddRange(spells.Select(s => (((string?)s["spellKey"] ?? "").ToUpperInvariant(), s, false)));
        var result = new List<ControlSkill>();
        foreach (var (key, spell, isPassive) in entries)
        {
            string summary = Clean((string?)spell["description"]);
            string text = summary + "。" + Clean((string?)spell["dynamicDescription"]);
            foreach (string name in names)
            {
                text = text.Replace(name, "");
            }

            string[] effects = Effects(text, isPassive);
            if (effects.Length > 0)
            {
                result.Add(new(key, (string?)spell["name"] ?? key, effects, summary));
            }
        }

        return result.ToArray();
    }

    public static string[] Effects(string text, bool passive)
    {
        var found = new List<string>();
        foreach (string clause in Clauses.Split(text))
        {
            if (Negative.Any(clause.Contains))
            {
                continue;
            }

            // A passive's "……时" or "被……" clause names its trigger, not an effect it applies.
            if (passive && (clause.Contains('时') || clause.Contains('被')))
            {
                continue;
            }

            foreach (var (effect, words) in Rules)
            {
                if (found.Contains(effect) || !words.Any(clause.Contains))
                {
                    continue;
                }

                if ((effect == "变形" && !Polymorph.IsMatch(clause)) || (effect == "拉拽" && SelfPull.IsMatch(clause)))
                {
                    continue;
                }

                found.Add(effect);
            }
        }

        return found.ToArray();
    }

    /// <summary>The ability's summary without markup, for tooltips.</summary>
    public static string Summary(JObject? spell) => Clean((string?)spell?["description"]);

    private static string Clean(string? text) => Placeholders.Replace(Tags.Replace(text ?? "", ""), "");
}
