using System.Globalization;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>What one champion's stat page is built from: its game data facts, the client's champion data and its base stats.</summary>
public sealed record StatPageSource(ChampionSpellFacts Facts, JObject Champion, ChampionBaseStats? BaseStats);

/// <summary>
/// The Game Bar's stat page for one player: the stat table (current value, base at the level, growth and bonus),
/// character facts, and each ability's tooltip with its numbers computed from those stats for every rank. The widget
/// only picks the rank to show.
/// </summary>
public static class ChampionStatPage
{
    private static readonly string[] SlotOrder = ["P", "Q", "W", "E", "R"];

    /// <summary>
    /// The page as the widget reads it; <paramref name="sheet"/> is null when no stats are known yet, and
    /// <paramref name="texts"/> null while the tooltips are still loading.
    /// </summary>
    public static JObject Build(
        StatPageSource source,
        ChampionStatSheet? sheet,
        IReadOnlyDictionary<string, string>? texts,
        string mode
    )
    {
        var stats = sheet ?? new ChampionStatSheet(1, true);
        return new JObject
        {
            ["level"] = stats.Level,
            ["estimated"] = stats.Estimated,
            ["source"] =
                sheet == null ? "基础属性尚未读取，技能数值未计入属性加成。"
                : !sheet.Estimated ? $"{sheet.Level} 级 · 当前属性取自 2999，与游戏属性面板相同。"
                : $"{sheet.Level} 级 · 估算：基础属性按等级成长，加上装备列出的属性和已记录海克斯的生命与双抗；"
                    + "符文、装备被动和未记录的海克斯不在其中，实际数值可能更高。",
            ["stats"] = sheet != null ? StatRows(sheet, source.BaseStats) : new JArray(),
            ["extras"] = Extras(source.Facts.Record, source.BaseStats),
            ["textSource"] =
                texts == null
                    ? "技能说明加载中…"
                    : $"技能说明和数值取自游戏数据，按{(stats.Estimated ? "估算" : "当前")}属性计算；"
                        + "标 ? 的数值由游戏在对局中决定，标 + 的未计入层数。",
            ["spells"] = Spells(source, stats, texts, mode),
        };
    }

    private static JArray StatRows(ChampionStatSheet sheet, ChampionBaseStats? stats) =>
        new(CombatRows(sheet, stats).Concat(SustainRows(sheet, stats)));

    // Health, resource, damage, defense, speed and penetration, in the order of the game's stats panel.
    private static IEnumerable<JObject> CombatRows(ChampionStatSheet sheet, ChampionBaseStats? stats)
    {
        yield return Pair("生命值", sheet.Health, stats?.Health ?? 0, stats?.HealthPerLevel ?? 0);
        if (sheet.Mana.Total > 0)
        {
            yield return Pair(
                stats?.Resource is { Length: > 0 } resource ? resource : "法力值",
                sheet.Mana,
                stats?.Mana ?? 0,
                stats?.ManaPerLevel ?? 0
            );
        }

        yield return Pair("攻击力", sheet.AttackDamage, stats?.AttackDamage ?? 0, stats?.AttackDamagePerLevel ?? 0);
        yield return Row("法术强度", Number(sheet.AbilityPower), bonus: Bonus(sheet.AbilityPower));
        yield return Pair("护甲", sheet.Armor, stats?.Armor ?? 0, stats?.ArmorPerLevel ?? 0);
        yield return Pair("魔法抗性", sheet.MagicResist, stats?.MagicResist ?? 0, stats?.MagicResistPerLevel ?? 0);
        yield return Row(
            "攻击速度",
            sheet.AttackSpeed.Total.ToString("0.000", CultureInfo.InvariantCulture),
            sheet.AttackSpeed.Base.ToString("0.000", CultureInfo.InvariantCulture),
            stats?.AttackSpeedPerLevel > 0 ? $"+{Number(stats.AttackSpeedPerLevel)}%/级" : "",
            sheet.BonusAttackSpeed > 0 ? $"+{Number(sheet.BonusAttackSpeed * 100)}%" : ""
        );
        yield return Row("暴击几率", Percent(sheet.CritChance));
        yield return Row("暴击伤害", Percent(sheet.CritDamage));
        yield return Pair("移动速度", sheet.MoveSpeed, stats?.MoveSpeed ?? 0, 0);
        yield return Pair("攻击距离", sheet.AttackRange, stats?.AttackRange ?? 0, 0);
        yield return Row(
            "技能急速",
            Number(sheet.AbilityHaste),
            bonus: $"冷却缩减 {Number(100 - 10000 / (100 + sheet.AbilityHaste))}%"
        );
        // Percent penetration is only in 2999's panel; items list it as a passive the estimate cannot read.
        yield return Row(
            "穿甲",
            Number(sheet.Lethality),
            bonus: sheet.Estimated ? "" : $"护甲穿透 {Percent(sheet.ArmorPenetrationPercent)}"
        );
        yield return Row(
            "法术穿透",
            Number(sheet.MagicPenetrationFlat),
            bonus: sheet.Estimated ? "" : $"百分比 {Percent(sheet.MagicPenetrationPercent)}"
        );
    }

    private static IEnumerable<JObject> SustainRows(ChampionStatSheet sheet, ChampionBaseStats? stats)
    {
        yield return Row(
            "生命回复",
            Number(sheet.HealthRegen) + "/5秒",
            growth: stats != null ? Growth(stats.HealthRegen, stats.HealthRegenPerLevel) : ""
        );
        if (sheet.Mana.Total > 0)
        {
            yield return Row(
                (stats?.Resource is { Length: > 0 } resource ? resource.Replace("值", "") : "法力") + "回复",
                Number(sheet.ManaRegen) + "/5秒",
                growth: stats != null ? Growth(stats.ManaRegen, stats.ManaRegenPerLevel) : ""
            );
        }

        // 2999 reports these for the active player only; nothing visible estimates them for anyone else.
        if (!sheet.Estimated)
        {
            yield return Row("生命偷取", Percent(sheet.LifeSteal));
            yield return Row("全能吸血", Percent(sheet.Omnivamp));
            yield return Row("韧性", Percent(sheet.Tenacity));
            yield return Row("治疗与护盾强度", Percent(sheet.HealShieldPower));
        }
    }

    private static JObject Row(string name, string value, string level = "", string growth = "", string bonus = "") =>
        new()
        {
            ["name"] = name,
            ["value"] = value,
            ["base"] = level,
            ["growth"] = growth,
            ["bonus"] = bonus,
        };

    // A stat with growth: the total, the base at the level, level 1 with the growth per level, and the bonus.
    private static JObject Pair(string name, StatValue value, double first, double perLevel) =>
        Row(name, Number(value.Total), Number(value.Base), Growth(first, perLevel), Bonus(value.Bonus));

    private static string Growth(double first, double perLevel) =>
        perLevel > 0 ? $"{Number(first)} +{Number(perLevel)}/级" : Number(first);

    private static string Bonus(double value) => Math.Abs(value) >= 0.05 ? (value > 0 ? "+" : "") + Number(value) : "";

    private static JArray Extras(ChampionRecord record, ChampionBaseStats? stats)
    {
        var rows = new JArray();
        void Add(string name, string value, string note)
        {
            rows.Add(
                new JObject
                {
                    ["name"] = name,
                    ["value"] = value,
                    ["note"] = note,
                }
            );
        }

        if (record.AttackSpeedRatio > 0)
        {
            Add(
                "攻速系数",
                record.AttackSpeedRatio.ToString("0.###", CultureInfo.InvariantCulture),
                "附加攻速乘以这个系数加到攻击速度上。"
            );
        }

        if (record.AttackWindup > 0)
        {
            Add("攻击前摇", Percent(record.AttackWindup), "一次普攻里出手前的时间占比。");
        }

        if (record.GameplayRadius > 0)
        {
            Add("碰撞半径", Number(record.GameplayRadius), "技能和普攻按这个半径判定命中。");
        }

        if (record.SelectionRadius > 0)
        {
            Add("选取半径", Number(record.SelectionRadius), "鼠标选中英雄的范围。");
        }

        if (record.AcquisitionRange > 0)
        {
            Add("索敌范围", Number(record.AcquisitionRange), "自动攻击会追向这个范围内的目标。");
        }

        if (stats?.Resource is { Length: > 0 } resource)
        {
            Add("资源", resource, "技能消耗的资源。");
        }

        return rows;
    }

    private static JArray Spells(
        StatPageSource source,
        ChampionStatSheet stats,
        IReadOnlyDictionary<string, string>? texts,
        string mode
    )
    {
        var result = new JArray();
        var scalings = source.Facts.Scalings;
        foreach (string key in SlotOrder)
        {
            if (scalings.Slot(key) is not { } spell)
            {
                continue;
            }

            var context = new SpellContext(scalings, spell, 1, stats.Level, stats, mode);
            string? template = texts?.GetValueOrDefault(spell.TooltipKey);
            int ranks = key == "P" ? 1 : spell.Ranks;
            var value = new JObject
            {
                ["key"] = key,
                ["name"] = SpellName(source.Champion, key),
                ["ranks"] = ranks,
                ["segments"] = new JArray(
                    (template != null ? SpellTooltip.Render(template, context, ranks) : []).Select(Segment)
                ),
            };
            if (key != "P" && spell.Costs.Take(ranks).Any(cost => cost > 0))
            {
                value["costs"] = new JArray(
                    Enumerable.Range(0, ranks).Select(i => Number(spell.Costs.ElementAtOrDefault(i)))
                );
            }

            result.Add(value);
        }

        return result;
    }

    private static JObject Segment(TooltipSegment segment)
    {
        var value = new JObject { ["style"] = segment.Style };
        if (segment.Unknown)
        {
            value["unknown"] = true;
            value["text"] = segment.Text;
        }
        else if (segment.Values.Length > 0)
        {
            value["values"] = new JArray(segment.Values);
            if (segment.Partial)
            {
                value["partial"] = true;
            }
        }
        else
        {
            value["text"] = segment.Text;
        }

        return value;
    }

    // The client's champion data names the abilities; the passive has its own entry.
    private static string SpellName(JObject champion, string key)
    {
        if (key == "P")
        {
            return (string?)champion["passive"]?["name"] ?? "被动";
        }

        return (champion["spells"] as JArray ?? new())
                .OfType<JObject>()
                .FirstOrDefault(spell =>
                    string.Equals((string?)spell["spellKey"], key, StringComparison.OrdinalIgnoreCase)
                )
                ?["name"]?.ToString()
            ?? key;
    }

    private static string Number(double value) =>
        Math.Round(value, Math.Abs(value) >= 100 ? 0 : 1, MidpointRounding.AwayFromZero)
            .ToString("0.#", CultureInfo.InvariantCulture);

    private static string Percent(double fraction) => Number(fraction * 100) + "%";
}

/// <summary>Every roster champion's page source by champion id, the tooltips once loaded and the game mode.</summary>
public sealed record StatPageSources(
    IReadOnlyDictionary<int, StatPageSource> Sources,
    IReadOnlyDictionary<string, string>? Texts,
    string Mode
);
