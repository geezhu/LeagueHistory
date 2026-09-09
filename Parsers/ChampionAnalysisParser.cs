using League.Models;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace League.Parsers;

/// <summary>
/// Conservative CommunityDragon extraction. Unknown enum defaults and mechanics remain explicit;
/// internal range, tooltip range and projectile range are never merged into a combat range.
/// </summary>
public static class ChampionAnalysisParser
{
    public const int SchemaVersion = 1;
    private static readonly (string Field, string Name, string Unit)[] Attributes =
    [
        ("baseHPModifiable", "基础生命", "生命"), ("hpPerLevelModifiable", "生命成长", "生命/成长系数"),
        ("baseDamageModifiable", "基础攻击力", "攻击力"), ("damagePerLevelModifiable", "攻击力成长", "攻击力/成长系数"),
        ("baseArmorModifiable", "基础护甲", "护甲"), ("armorPerLevelModifiable", "护甲成长", "护甲/成长系数"),
        ("baseMR", "基础魔抗", "魔抗"), ("mrPerLevel", "魔抗成长", "魔抗/成长系数"),
        ("baseMoveSpeedModifiable", "基础移速", "单位/秒"), ("attackRangeModifiable", "普攻距离", "游戏单位"),
        ("attackSpeedModifiable", "基础攻速", "次/秒"), ("attackSpeedRatioModifiable", "攻速收益系数", "次/秒"),
        ("attackSpeedPerLevelModifiable", "攻速成长", "%/成长系数"),
        ("baseStaticHPRegenModifiable", "基础生命回复", "生命/秒"),
        ("hpRegenPerLevelModifiable", "生命回复成长", "生命/秒/成长系数")
    ];

    public static ChampionAnalysisReport Parse(JObject document, string alias, string source, string version)
    {
        if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("英雄、来源和版本标识不能为空。");
        var rootProperty = document.Properties().FirstOrDefault(p =>
            p.Name.Equals($"Characters/{alias}/CharacterRecords/Root", StringComparison.OrdinalIgnoreCase));
        if (rootProperty?.Value is not JObject root) throw new InvalidDataException("缺少英雄根记录。");
        var warnings = new List<string>
        {
            "仅提取基础模式数据，尚未应用大乱斗平衡、装备或强化。",
            "数组保留原始索引；技能 0 级、最大等级及成长曲线需验证，不能直接当作逐级结果。"
        };
        if (version.Equals("latest", StringComparison.OrdinalIgnoreCase))
            warnings.Add("来源使用 latest，未锁定游戏版本；该报告不能与其他版本直接合并。");
        var attributes = new List<AnalysisValue>();
        foreach (var (field, name, unit) in Attributes)
        {
            var token = root[field];
            string path = Pointer(rootProperty.Name, field);
            if (token is JObject obj) { token = obj["baseValue"]; path += "/baseValue"; }
            attributes.Add(new(name, unit, [Number(token)], path));
        }
        double? speed = attributes.First(a => a.Name == "基础攻速").Values[0];
        attributes.Add(new("基础普攻周期（不含前摇、重置）", "秒", [speed > 0 ? 1 / speed : null],
            "derived: 1 / " + Pointer(rootProperty.Name, "attackSpeedModifiable", "baseValue")));
        var spells = new List<SpellAnalysis>();
        var references = root["spells"] as JArray;
        if (references == null) warnings.Add("缺少 Q/W/E/R 技能引用。");
        for (int i = 0; i < 4; i++)
        {
            string key = "QWER"[i].ToString();
            string? reference = references?.ElementAtOrDefault(i)?.Value<string>();
            if (reference == null || document[reference]?["mSpell"] is not JObject spell)
            {
                warnings.Add($"{key} 技能记录缺失，未将其视为无控制或无护盾。");
                continue;
            }
            spells.Add(ReadSpell(key, reference, spell));
        }
        return new(alias, source, version, attributes, spells, warnings);
    }

    private static SpellAnalysis ReadSpell(string key, string reference, JObject spell)
    {
        string path = Pointer(reference, "mSpell");
        var values = new List<AnalysisValue>();
        var warnings = new List<string>();
        foreach (var (field, name, unit) in new[]
        {
            ("cooldownTime", "冷却时间", "秒"), ("castRange", "内部施法范围", "游戏单位"),
            ("castRangeDisplayOverride", "显示施法范围", "游戏单位"),
            ("castRadius", "内部作用半径", "游戏单位")
        })
        {
            if (spell[field] is JArray array) values.Add(new(name, unit, array.Select(Number).ToArray(), path + "/" + field));
        }
        var data = new Dictionary<string, JArray>(StringComparer.Ordinal);
        if (spell["DataValues"] is JArray dataValues)
        {
            for (int i = 0; i < dataValues.Count; i++)
            {
                var row = dataValues[i];
                if (row["name"]?.Type != JTokenType.String || row["values"] is not JArray array) continue;
                string name = (string)row["name"]!;
                if (!data.TryAdd(name, array)) warnings.Add($"参数 {name} 重复，公式引用需要人工核对。");
                // Names are evidence, not sufficient to classify control type or shield behavior.
                values.Add(new(name, "原始单位待验证", array.Select(Number).ToArray(), path + $"/DataValues/{i}/values"));
            }
        }
        var formulas = new List<AnalysisFormula>();
        if (spell["mSpellCalculations"] is JObject calculations)
        {
            foreach (var calculation in calculations.Properties())
            {
                var result = Formula(calculation.Value, data, calculations, new HashSet<string> { calculation.Name }, 0);
                formulas.Add(new(calculation.Name, result.Text, result.Known,
                    path + "/mSpellCalculations/" + Escape(calculation.Name)));
            }
        }
        string targeting = (string?)spell["mTargetingTypeData"]?["__type"] ?? "未知（未解析默认目标类型）";
        if (spell["DataValuesModeOverride"] != null) warnings.Add("存在模式覆盖参数，当前未应用，不能作为大乱斗最终数值。");
        if (spell["castRange"] != null) warnings.Add("范围仅为独立候选字段，未推导最大有效射程；弹道、碰撞和目标条件尚未归一化。");
        if (data.ContainsKey("ChainDuration")) warnings.Add("ChainDuration 为连接阶段参数，不计入已确认控制时长。");
        if (formulas.Any(f => !f.Resolved)) warnings.Add("部分公式含未知属性枚举、默认值或节点，不进行数值求值。");
        var tags = spell["mSpellTags"] as JArray;
        return new(key, path, targeting, values, formulas,
            tags?.Select(t => t.ToString()).ToArray() ?? [], warnings);
    }

    private static (string Text, bool Known) Formula(JToken node, Dictionary<string, JArray> data,
        JObject calculations, HashSet<string> seen, int depth)
    {
        if (depth > 32 || node is not JObject obj) return ("未知：节点缺失或递归过深", false);
        string type = (string?)obj["__type"] ?? "缺少类型";
        (string Text, bool Known) Read(JToken child) => Formula(child, data, calculations, seen, depth + 1);
        (string Text, bool Known) Combine(string field, string op)
        {
            if (obj[field] is not JArray { Count: > 0 } parts) return ($"未知：{type}.{field}", false);
            var results = parts.Select(Read).ToArray();
            return ("(" + string.Join(op, results.Select(r => r.Text)) + ")", results.All(r => r.Known));
        }
        string name = (string?)obj["mDataValue"] ?? "?";
        switch (type)
        {
            case "GameCalculation":
                // Additional multipliers/overrides must not be silently discarded.
                var sum = Combine("mFormulaParts", " + ");
                return obj["mMultiplier"] == null ? sum : ($"{sum.Text} × [乘数待验证]", false);
            case "NamedDataValueCalculationPart": return (name + "[技能等级索引]", data.ContainsKey(name));
            case "NumberCalculationPart":
                double? number = Number(obj["mNumber"]);
                return (number?.ToString("0.######", CultureInfo.InvariantCulture) ?? "未知常数", number != null);
            case "SumOfSubPartsCalculationPart": return Combine("mSubparts", " + ");
            case "StatByNamedDataValueCalculationPart":
                return ($"{name}[技能等级索引] × 属性枚举({obj["mStat"]?.ToString() ?? "缺省，待验证"})", false);
            case "StatByCoefficientCalculationPart":
                return ($"{Number(obj["mCoefficient"])?.ToString(CultureInfo.InvariantCulture) ?? "未知系数"} × 属性枚举({obj["mStat"]?.ToString() ?? "缺省，待验证"})", false);
            case "GameCalculationModified":
                string? target = (string?)obj["mModifiedGameCalculation"];
                if (target == null || !seen.Add(target) || calculations[target] == null) return ("未知：公式引用缺失或循环", false);
                var nested = Read(calculations[target]!);
                seen.Remove(target);
                var multiplier = obj["mMultiplier"] == null ? (Text: "未知乘数", Known: false) : Read(obj["mMultiplier"]!);
                return ($"{nested.Text} × {multiplier.Text}", nested.Known && multiplier.Known);
            default: return ($"未知节点：{type}", false);
        }
    }

    private static double? Number(JToken? token) => token?.Type is JTokenType.Float or JTokenType.Integer
        && double.IsFinite((double)token) ? (double)token : null;
    private static string Escape(string text) => text.Replace("~", "~0").Replace("/", "~1");
    private static string Pointer(params string[] components) => "/" + string.Join("/", components.Select(Escape));
}
