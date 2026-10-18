using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Support;

/// <summary>
/// A spell's numbers for calculator and tooltip tests, in the shape <see cref="ChampionSpellScalings.Parse"/>
/// produces: values indexed by rank with 0 unlearned, calculations by lower-case name.
/// </summary>
internal sealed class SpellBuilder(string script = "testq")
{
    private readonly Dictionary<string, double[]> _values = new();
    private readonly Dictionary<string, Dictionary<string, double[]>> _modes = new();
    private readonly JObject _calculations = new();
    private readonly List<double[]> _effects = new();
    private string _tooltip = "";
    private int _ranks = 5;

    /// <summary>Values from rank 0 (unlearned) up.</summary>
    public SpellBuilder WithValue(string name, params double[] values)
    {
        _values[name.ToLowerInvariant()] = values;
        return this;
    }

    public SpellBuilder WithModeValue(string mode, string name, params double[] values)
    {
        if (!_modes.TryGetValue(mode, out var mapped))
        {
            _modes[mode] = mapped = new();
        }

        mapped[name.ToLowerInvariant()] = values;
        return this;
    }

    public SpellBuilder WithEffect(params double[] values)
    {
        _effects.Add(values);
        return this;
    }

    public SpellBuilder WithCalculation(string name, JObject calculation)
    {
        _calculations[name.ToLowerInvariant()] = calculation;
        return this;
    }

    public SpellBuilder WithTooltip(string key)
    {
        _tooltip = key;
        return this;
    }

    public SpellBuilder WithRanks(int ranks)
    {
        _ranks = ranks;
        return this;
    }

    public SpellScaling Build() =>
        new(script, _tooltip, _ranks)
        {
            Values = _values,
            ModeValues = _modes,
            Effects = _effects.ToArray(),
            Calculations = _calculations,
        };

    /// <summary>A context for the spell; the other spells are what cross-spell tokens and parts can reach.</summary>
    public static SpellContext Context(
        SpellScaling spell,
        ChampionStatSheet? stats = null,
        int rank = 1,
        int level = 1,
        string mode = "",
        params SpellScaling[] others
    ) =>
        new(
            new ChampionSpellScalings
            {
                Slots = new() { ["Q"] = spell.Script },
                Spells = others.Append(spell).ToDictionary(s => s.Script),
            },
            spell,
            rank,
            level,
            stats ?? new ChampionStatSheet(level, true),
            mode
        );
}

/// <summary>GameCalculation objects and parts, written as the game data writes them.</summary>
internal static class Calc
{
    public static JObject Formula(params JObject[] parts) =>
        new() { ["__type"] = "GameCalculation", ["mFormulaParts"] = new JArray(parts) };

    public static JObject Value(string name) =>
        new() { ["__type"] = "NamedDataValueCalculationPart", ["mDataValue"] = name };

    public static JObject Number(double value) => new() { ["__type"] = "NumberCalculationPart", ["mNumber"] = value };

    /// <summary>The stat by the game data's numbering; formula 0 total, 1 base, 2 bonus.</summary>
    public static JObject Stat(int stat, double coefficient, int formula = 0) =>
        new()
        {
            ["__type"] = "StatByCoefficientCalculationPart",
            ["mStat"] = stat,
            ["mStatFormula"] = formula,
            ["mCoefficient"] = coefficient,
        };

    /// <summary>An AP ratio as the game writes it: no mStat at all.</summary>
    public static JObject AbilityPowerBy(string value) =>
        new() { ["__type"] = "StatByNamedDataValueCalculationPart", ["mDataValue"] = value };

    public static JObject Part(string type, JObject fields)
    {
        fields["__type"] = type;
        return fields;
    }
}
