using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>
/// Fills an augment tooltip template from the game's data the way the game does, as far as that can be done outside
/// a match. "@Stack_Duration@" is a fixed value and is written as the number. "@Calc_Damage_Hit@" is a formula: in a
/// match the game evaluates it with the champion's stats, which this tool does not have, so it is written out the way
/// the wiki does, "5–30（随等级）+ 35% 额外攻击力 + 15% 法术强度". The rules block at the end of many tooltips counts
/// what the augment did this match ("@f1@") and is dropped.
///
/// Spell data names a formula either by its name or by the FNV-1a hash of it; both are looked up. Stat numbers are
/// named only where they were confirmed against the game (an in-match tooltip and the tags around the formula);
/// any other stat is written as "属性" rather than guessed.
/// </summary>
public static partial class AugmentTooltipFormatter
{
    private static readonly Dictionary<int, string> StatNames = new()
    {
        [0] = "法术强度",
        [1] = "护甲",
        [2] = "攻击力",
        [4] = "穿甲",
        [11] = "技能急速",
        [12] = "生命值",
    };

    /// <param name="template">The tooltip as the string table holds it.</param>
    /// <param name="spell">The augment's spell object from the mode data.</param>
    /// <param name="lookup">
    /// Resolves another string table text a template quotes as "{{ Item_Keyword_OnHit }}", or null when it is not
    /// known; such a quote is then dropped.
    /// </param>
    public static string Render(string template, JObject spell, Func<string, string?>? lookup = null)
    {
        var context = new Context(spell);
        string text = Quotes(template, lookup, depth: 2);
        text = WithoutMatchCounters(text);
        text = Icon().Replace(text, "");
        text = Placeholder()
            .Replace(
                text,
                match =>
                {
                    string name = match.Groups["name"].Value;
                    double scale = match.Groups["scale"].Success ? Number(match.Groups["scale"].Value) : 1;
                    return context.Value(name, scale) ?? context.Formula(name, scale) ?? "—";
                }
            );
        // A one-stat formula followed by the stat's own name reads "15% 法术强度法术强度"; once is enough.
        text = RepeatedStat().Replace(text, "");
        // The dropped counters leave the line breaks that led up to them.
        return TrailingBreaks().Replace(LeadingBreaks().Replace(text, ""), "");
    }

    // The bound spell is chosen in the match; outside it the template says which kind it is.
    private static string Quotes(string text, Func<string, string?>? lookup, int depth) =>
        Quote()
            .Replace(
                text,
                match =>
                {
                    string name = match.Groups["name"].Value;
                    if (name is "SpellName" or "SpellNameOnly")
                    {
                        return "指定";
                    }

                    string? quoted = depth > 0 ? lookup?.Invoke(name) : null;
                    return quoted == null ? "" : Quotes(quoted, lookup, depth - 1);
                }
            );

    /// <summary>
    /// Takes out what counts this match: a value in brackets ("20% (@f1@) 攻击力"), the progress before a quest's goal
    /// ("攻击@f1@/25次" reads "攻击25次"), a rules block reporting counters, and any other line holding a counter.
    /// </summary>
    private static string WithoutMatchCounters(string text)
    {
        text = BracketedCounter().Replace(text, "");
        text = CounterProgress().Replace(text, "");
        text = RulesWithMatchCounters().Replace(text, "");
        var lines = LineBreak().Split(text).Where(line => !Counter().IsMatch(line));
        return ExtraBreaks().Replace(string.Join("<br>", lines), "<br><br>");
    }

    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    // Up to two decimals, without trailing zeros: 5, 0.35, 12.5.
    private static string Format(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Amount(double value, bool percent) => percent ? Format(value * 100) + "%" : Format(value);

    private sealed class Context(JObject spell)
    {
        private readonly JArray _values = spell["mSpell"]?["DataValues"] as JArray ?? [];
        private readonly JObject _formulas = spell["mSpell"]?["mSpellCalculations"] as JObject ?? [];

        // A value is named in the template and by formula parts either plainly or by the hash of its name.
        // An augment has one rank; its values sit at index 1 like a spell's first rank.
        public double? DataValue(string name)
        {
            var entry = _values
                .OfType<JObject>()
                .FirstOrDefault(v =>
                    string.Equals((string?)v["name"], name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Hash((string?)v["name"] ?? ""), name, StringComparison.Ordinal)
                );
            var values = entry?["values"] as JArray;
            return values == null || values.Count == 0 ? null : (double?)values[Math.Min(1, values.Count - 1)];
        }

        public string? Value(string name, double scale) =>
            DataValue(name) is double value ? Format(value * scale) : null;

        public string? Formula(string name, double scale) =>
            Find(name) is JObject formula ? Describe(formula, scale, percent: false) : null;

        private JObject? Find(string name) =>
            (
                _formulas[name]
                ?? _formulas[Hash(name)]
                ?? _formulas
                    .Properties()
                    .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?.Value
            ) as JObject;

        private string Describe(JObject formula, double scale, bool percent)
        {
            percent |= (bool?)formula["mDisplayAsPercent"] == true;
            switch ((string?)formula["__type"])
            {
                case "GameCalculationModified":
                    double factor = Scalar(formula["mMultiplier"] as JObject) ?? 1;
                    return Find((string?)formula["mModifiedGameCalculation"] ?? "") is JObject modified
                        ? Describe(modified, scale * factor, percent)
                        : "—";
                case "GameCalculationConditional":
                    return Find((string?)formula["mDefaultGameCalculation"] ?? "") is JObject fallback
                        ? Describe(fallback, scale, percent)
                        : "—";
            }

            // A formula may carry a multiplier of its own besides its parts.
            scale *= Scalar(formula["mMultiplier"] as JObject) ?? 1;
            var parts = (formula["mFormulaParts"] as JArray ?? [])
                .OfType<JObject>()
                .Select(part => Part(part, scale, percent))
                .ToArray();
            string text = parts.Length == 0 ? "—" : string.Join(" + ", parts);
            double? ranged = Scalar(formula["mRangedMultiplier"] as JObject);
            return ranged is double share && share != 1 ? $"{text}（远程英雄 {Format(share * 100)}%）" : text;
        }

        private string Part(JObject part, double scale, bool percent)
        {
            switch ((string?)part["__type"])
            {
                case "NumberCalculationPart":
                    return Amount(((double?)part["mNumber"] ?? 0) * scale, percent);
                case "NamedDataValueCalculationPart":
                    return DataValue((string?)part["mDataValue"] ?? "") is double named
                        ? Amount(named * scale, percent)
                        : "—";
                case "ByCharLevelInterpolationCalculationPart":
                    double start = ((double?)part["mStartValue"] ?? 0) * scale,
                        end = ((double?)part["mEndValue"] ?? 0) * scale;
                    return $"{Amount(start, percent)}–{Amount(end, percent)}（随等级）";
                case "ByCharLevelBreakpointsCalculationPart":
                    return $"{Amount(((double?)part["mLevel1Value"] ?? 0) * scale, percent)}起（随等级）";
                case "StatByCoefficientCalculationPart":
                    return Stat(part, ((double?)part["mCoefficient"] ?? 0) * scale, percent);
                case "StatByNamedDataValueCalculationPart":
                    return DataValue((string?)part["mDataValue"] ?? "") is double coefficient
                        ? Stat(part, coefficient * scale, percent)
                        : "—";
                case "BuffCounterByCoefficientCalculationPart":
                    return $"{Amount(((double?)part["mCoefficient"] ?? 0) * scale, percent)}×层数";
                case "BuffCounterByNamedDataValueCalculationPart":
                    return DataValue((string?)part["mDataValue"] ?? "") is double perStack
                        ? $"{Amount(perStack * scale, percent)}×层数"
                        : "—";
                case "SumOfSubPartsCalculationPart":
                    var sum = (part["mSubparts"] as JArray ?? [])
                        .OfType<JObject>()
                        .Select(p => Part(p, scale, percent));
                    return "(" + string.Join(" + ", sum) + ")";
                case "ProductOfSubPartsCalculationPart":
                    return Part(part["mPart1"] as JObject ?? [], scale, percent)
                        + " × "
                        + Part(part["mPart2"] as JObject ?? [], 1, percent);
                default:
                    return "…";
            }
        }

        // "35% 额外攻击力" for a share of a stat; a percentage per stat point reads "0.15%×技能急速".
        private static string Stat(JObject part, double coefficient, bool percent)
        {
            int stat = (int?)part["mStat"] ?? 0;
            string kind = ((int?)part["mStatFormula"] ?? 0) switch
            {
                1 => "基础",
                2 => "额外",
                _ => "",
            };
            string name = kind + StatNames.GetValueOrDefault(stat, "属性");
            return percent ? $"{Format(coefficient * 100)}%×{name}" : $"{Format(coefficient * 100)}% {name}";
        }

        private double? Scalar(JObject? part) =>
            (string?)part?["__type"] switch
            {
                "NumberCalculationPart" => (double?)part!["mNumber"],
                "NamedDataValueCalculationPart" => DataValue((string?)part!["mDataValue"] ?? ""),
                _ => null,
            };
    }

    /// <summary>The name as spell data stores it when it does not keep the name itself: "{c75ec8fe}".</summary>
    public static string Hash(string name)
    {
        uint hash = 0x811c9dc5;
        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(name.ToLowerInvariant()))
        {
            hash = (hash ^ b) * 0x01000193;
        }
        return "{" + hash.ToString("x8", CultureInfo.InvariantCulture) + "}";
    }

    [GeneratedRegex(@"@(?<name>[A-Za-z0-9_.:]+)(?:\*(?<scale>-?[0-9]+(?:\.[0-9]+)?))?@")]
    private static partial Regex Placeholder();

    // A rules block that reports this match's counters ("@f1@") means nothing outside a match.
    [GeneratedRegex(@"<rules>(?:(?!</rules>).)*@f\d[^@]*@(?:(?!</rules>).)*</rules>", RegexOptions.Singleline)]
    private static partial Regex RulesWithMatchCounters();

    [GeneratedRegex(@"(?:<br\s*/?>|\s)+$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingBreaks();

    [GeneratedRegex(@"^(?:<br\s*/?>|\s)+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingBreaks();

    [GeneratedRegex(@"\{\{\s*(?<name>[A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex Quote();

    [GeneratedRegex(@"%i:[A-Za-z0-9_]+%")]
    private static partial Regex Icon();

    [GeneratedRegex(@"@f\d+(?:\.\d+)?@")]
    private static partial Regex Counter();

    [GeneratedRegex(@"\s*\(\s*@f\d+(?:\.\d+)?@\s*\)")]
    private static partial Regex BracketedCounter();

    [GeneratedRegex(@"@f\d+(?:\.\d+)?@\s*/\s*")]
    private static partial Regex CounterProgress();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"(?:<br>){3,}")]
    private static partial Regex ExtraBreaks();

    // The second of two stat names in a row, so "35% 额外攻击力攻击力" keeps "35% 额外攻击力".
    [GeneratedRegex(@"(?<=(?<stat>法术强度|护甲|攻击力|穿甲|技能急速|生命值)(?:</?\w+>)*)\k<stat>")]
    private static partial Regex RepeatedStat();
}
