using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>
/// A number a tooltip shows. <paramref name="Percent"/> marks a calculation the game displays ×100 with a percent
/// sign; <paramref name="Partial"/> one that left out stacks the game counts while playing (buff counters count 0).
/// </summary>
public readonly record struct SpellNumber(
    double Value,
    bool Percent = false,
    int? Precision = null,
    bool Partial = false
);

/// <summary>What a calculation is evaluated with: the spell, its rank, the champion's level and stats and the mode.</summary>
public sealed record SpellContext(
    ChampionSpellScalings Champion,
    SpellScaling Spell,
    int Rank,
    int Level,
    ChampionStatSheet Stats,
    string Mode
);

/// <summary>
/// Evaluates the game data's GameCalculation objects (the formulas tooltips cite as @TotalDamage@): data values by
/// rank, stat ratios, level curves, sums and products, other calculations of the spell and their multipliers. Parts
/// the game only resolves while playing (conditions on buffs, elapsed buff time, unnamed part types) make the result
/// unknown instead of a guess.
/// </summary>
public static class SpellCalculator
{
    // The game's stat growth reaches 17 times the per-level value at level 18.
    private const double GrowthAtMaxLevel = 17;
    private const int MaxDepth = 12;

    /// <summary>The calculation of that name in the context's spell; null when missing or not computable.</summary>
    public static SpellNumber? Calculate(SpellContext context, string name) =>
        new Evaluation(context).Calculation(context.Spell, name.ToLowerInvariant(), context.Rank, 0);

    private sealed class Evaluation(SpellContext context)
    {
        private bool _partial;

        public SpellNumber? Calculation(SpellScaling spell, string name, int rank, int depth)
        {
            if (depth > MaxDepth || spell.Calculations[name] is not JObject calculation)
            {
                return null;
            }

            switch ((string?)calculation["__type"])
            {
                case "GameCalculation":
                {
                    double? total = 0;
                    foreach (var part in calculation["mFormulaParts"] as JArray ?? new())
                    {
                        total += Part(spell, part, rank, depth + 1);
                    }

                    if (calculation["mMultiplier"] is JObject multiplier)
                    {
                        total *= Part(spell, multiplier, rank, depth + 1);
                    }

                    return Number(total, calculation);
                }
                case "GameCalculationModified":
                {
                    string modified = ((string?)calculation["mModifiedGameCalculation"] ?? "").ToLowerInvariant();
                    int at =
                        calculation["mOverrideSpellLevel"]?.Type == JTokenType.Integer
                            ? (int)calculation["mOverrideSpellLevel"]!
                            : rank;
                    var inner = Calculation(spell, modified, at, depth + 1);
                    double? value = inner?.Value * Part(spell, calculation["mMultiplier"], rank, depth + 1);
                    return Number(value, calculation) is { } number
                        ? number with
                        {
                            Percent = inner!.Value.Percent || number.Percent,
                        }
                        : null;
                }
                case "GameCalculationConditional":
                {
                    // The condition is a buff the game checks while casting; the tooltip's default case is shown.
                    string fallback = ((string?)calculation["mDefaultGameCalculation"] ?? "").ToLowerInvariant();
                    return fallback.Length > 0 ? Calculation(spell, fallback, rank, depth + 1) : null;
                }
                default:
                    return null;
            }
        }

        private SpellNumber? Number(double? value, JObject calculation)
        {
            if (value is not double result || !double.IsFinite(result))
            {
                return null;
            }

            int? precision =
                calculation["mPrecision"]?.Type == JTokenType.Integer ? (int)calculation["mPrecision"]! : null;
            return new(result, (bool?)calculation["mDisplayAsPercent"] == true, precision, _partial);
        }

        // One part being evaluated: the spell whose values it names, at a rank, this deep in the formula.
        private readonly record struct Scope(SpellScaling Spell, JObject Part, int Rank, int Depth);

        private double? Part(SpellScaling spell, JToken? token, int rank, int depth)
        {
            if (depth > MaxDepth || token is not JObject part)
            {
                return null;
            }

            var scope = new Scope(spell, part, rank, depth);
            return (string?)part["__type"] switch
            {
                "SumOfSubPartsCalculationPart"
                or "ProductOfSubPartsCalculationPart"
                or "ClampSubPartsCalculationPart"
                or "{f3cbe7b2}" => Composite(scope),
                "ByCharLevelInterpolationCalculationPart"
                or "ByCharLevelBreakpointsCalculationPart"
                or "ByCharLevelFormulaCalculationPart"
                or "{ee18a47b}"
                or "{4ce08984}" => ByLevel(scope),
                _ => Leaf(scope),
            };
        }

        // Parts that read one number: a data value, a constant, a stat, an effect amount or the haste share.
        private double? Leaf(Scope scope)
        {
            var part = scope.Part;
            switch ((string?)part["__type"])
            {
                case "NamedDataValueCalculationPart":
                    return Value(scope, "mDataValue") ?? 0;
                case "NumberCalculationPart":
                    return Constant(part, "mNumber") ?? 0;
                case "StatByCoefficientCalculationPart":
                    return Stat(part) * (Constant(part, "mCoefficient") ?? 0);
                case "StatByNamedDataValueCalculationPart":
                    return Stat(part) * (Value(scope, "mDataValue") ?? 0);
                case "StatBySubPartCalculationPart":
                    return Stat(part) * Child(scope, "mSubpart");
                case "EffectValueCalculationPart":
                    return part["mEffectIndex"]?.Type == JTokenType.Integer
                        ? scope.Spell.Effect((int)part["mEffectIndex"]!, scope.Rank) ?? 0
                        : null;
                case "AbilityResourceByCoefficientCalculationPart":
                    // The secondary resource (Mordekaiser's shield, Rengar's ferocity) is not on the sheet.
                    return (int?)part["mAbilityResource"] is null or 0
                        ? Pick(context.Stats.Mana, part) * (Constant(part, "mCoefficient") ?? 0)
                        : null;
                case "CooldownMultiplierCalculationPart":
                    return 100 / (100 + Math.Max(0, context.Stats.AbilityHaste));
                case "BuffCounterByCoefficientCalculationPart":
                case "BuffCounterByNamedDataValueCalculationPart":
                    // Stacks only exist in a running game; the value without them is shown and marked.
                    _partial = true;
                    return 0;
                // The extraction has not named this type: a data value of another spell at the same rank.
                case "{9e9e2e5c}":
                {
                    var other = context.Champion.Spell(Script((string?)part["SourceObject"] ?? ""));
                    return other != null && (string?)part["DataValue"] is { } name
                        ? other.Value(name, scope.Rank, context.Mode) ?? 0
                        : null;
                }
                default:
                    return null;
            }
        }

        // Parts made of other parts, or of another calculation of the spell ("{f3cbe7b2}" names it).
        private double? Composite(Scope scope)
        {
            var part = scope.Part;
            switch ((string?)part["__type"])
            {
                case "ProductOfSubPartsCalculationPart":
                    return Child(scope, "mPart1") * Child(scope, "mPart2");
                case "{f3cbe7b2}":
                    return Calculation(
                        scope.Spell,
                        ((string?)part["mSpellCalculationKey"] ?? "").ToLowerInvariant(),
                        scope.Rank,
                        scope.Depth + 1
                    )?.Value;
            }

            double? sum = 0;
            foreach (var child in part["mSubparts"] as JArray ?? new())
            {
                sum += Part(scope.Spell, child, scope.Rank, scope.Depth + 1);
            }

            return (string?)part["__type"] == "ClampSubPartsCalculationPart" && sum is double value
                ? Math.Min(
                    Math.Max(value, Constant(part, "mFloor") ?? double.MinValue),
                    Constant(part, "mCeiling") ?? double.MaxValue
                )
                : sum;
        }

        // Parts that follow the champion's level. The unnamed "{ee18a47b}" and "{4ce08984}" are the interpolation
        // and breakpoints with their numbers in data values.
        private double? ByLevel(Scope scope)
        {
            var part = scope.Part;
            switch ((string?)part["__type"])
            {
                case "ByCharLevelInterpolationCalculationPart":
                    return Interpolate(Constant(part, "mStartValue") ?? 0, Constant(part, "mEndValue") ?? 0, part);
                case "{ee18a47b}":
                    return Interpolate(Value(scope, "StartDataValue") ?? 0, Value(scope, "EndDataValue") ?? 0, part);
                case "ByCharLevelBreakpointsCalculationPart":
                    return Breakpoints(
                        Constant(part, "mLevel1Value") ?? 0,
                        Constant(part, "mInitialBonusPerLevel") ?? 0,
                        (part["mBreakpoints"] as JArray ?? new())
                            .OfType<JObject>()
                            .Select(b =>
                                (
                                    Level: b["mLevel"]?.Type == JTokenType.Integer ? (int)b["mLevel"]! : 0,
                                    Bonus: Constant(b, "mAdditionalBonusAtThisLevel") ?? 0,
                                    PerLevel: Constant(b, "mBonusPerLevelAtAndAfter")
                                )
                            )
                    );
                case "{4ce08984}":
                    return Breakpoints(
                        Value(scope, "{91d404a5}") ?? 0,
                        Value(scope, "{bbd778a2}") ?? 0,
                        (part["{9823b29a}"] as JArray ?? new())
                            .OfType<JObject>()
                            .Select(b =>
                                (
                                    Level: b["level"]?.Type == JTokenType.Integer ? (int)b["level"]! : 0,
                                    Bonus: Value(scope with { Part = b }, "{ae9b464d}") ?? 0,
                                    PerLevel: Value(scope with { Part = b }, "{b0d8b2ac}")
                                )
                            )
                    );
                default:
                {
                    var values = part["values"] as JArray ?? new();
                    return
                        values.Count > 0
                        && values[Math.Clamp(context.Level, 0, values.Count - 1)]
                            is { Type: JTokenType.Float or JTokenType.Integer } value
                        ? (double)value
                        : null;
                }
            }
        }

        // The data value a field of the part names, at the scope's rank in the context's mode.
        private double? Value(Scope scope, string field) =>
            (string?)scope.Part[field] is { } name ? scope.Spell.Value(name, scope.Rank, context.Mode) : null;

        private double? Child(Scope scope, string field) =>
            Part(scope.Spell, scope.Part[field], scope.Rank, scope.Depth + 1);

        private static double? Constant(JObject part, string field) =>
            part[field]?.Type is JTokenType.Float or JTokenType.Integer ? (double)part[field]! : null;

        private double? Stat(JObject part) =>
            context.Stats.Stat(
                part["mStat"]?.Type == JTokenType.Integer ? (int)part["mStat"]! : 0,
                part["mStatFormula"]?.Type == JTokenType.Integer ? (int)part["mStatFormula"]! : 0
            );

        private static double? Pick(StatValue value, JObject part) =>
            (part["mStatFormula"]?.Type == JTokenType.Integer ? (int)part["mStatFormula"]! : 0) switch
            {
                1 => value.Base,
                2 => value.Bonus,
                _ => value.Total,
            };

        // From level 1 to 18 in a straight line, or along the stat growth curve when the part asks for it.
        private double Interpolate(double start, double end, JObject part)
        {
            int steps = Math.Clamp(context.Level, 1, 18) - 1;
            double share =
                (bool?)part["mScaleByStatProgressionMultiplier"] == true
                    ? steps * (0.7025 + 0.0175 * steps) / GrowthAtMaxLevel
                    : steps / GrowthAtMaxLevel;
            return start + (end - start) * share;
        }

        // Each level after the first adds the current per-level bonus; a breakpoint adds its own bonus at its level
        // and may change the per-level bonus from that level on.
        private double Breakpoints(
            double first,
            double perLevel,
            IEnumerable<(int Level, double Bonus, double? PerLevel)> breakpoints
        )
        {
            var points = breakpoints.ToArray();
            double value = first;
            for (int level = 2; level <= context.Level; level++)
            {
                foreach (var point in points.Where(p => p.Level == level))
                {
                    value += point.Bonus;
                    perLevel = point.PerLevel ?? perLevel;
                }

                value += perLevel;
            }

            return value;
        }
    }

    private static string Script(string path) => path[(path.LastIndexOf('/') + 1)..].ToLowerInvariant();
}
