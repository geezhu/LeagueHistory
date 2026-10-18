using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>
/// One spell's numbers from the game data: its values by rank (index 0 is unlearned), the older effect amounts, the
/// calculations its tooltip names and the tooltip's string table key. Names are lower case, so tooltip tokens match
/// whatever case they are written in.
/// </summary>
public sealed record SpellScaling(string Script, string TooltipKey, int Ranks)
{
    public Dictionary<string, double[]> Values { get; init; } = new();

    /// <summary>Values a game mode replaces, by lower-case mode and then value name.</summary>
    public Dictionary<string, Dictionary<string, double[]>> ModeValues { get; init; } = new();

    /// <summary>"Effect1Amount" and on: the first list is effect 1, each indexed by rank like the values.</summary>
    public double[][] Effects { get; init; } = [];

    /// <summary>The resource each cast costs, from rank 1 (unlike the values, the list has no unlearned entry).</summary>
    public double[] Costs { get; init; } = [];

    /// <summary>The spell's GameCalculation objects by lower-case name, as the extraction writes them.</summary>
    public JObject Calculations { get; init; } = new();

    /// <summary>A value at one rank in a mode; null when the spell has no value of that name.</summary>
    public double? Value(string name, int rank, string mode)
    {
        string key = name.ToLowerInvariant();
        var values =
            ModeValues.GetValueOrDefault(mode.ToLowerInvariant())?.GetValueOrDefault(key)
            ?? Values.GetValueOrDefault(key);
        return values == null ? null : At(values, rank);
    }

    /// <summary>Effect <paramref name="index"/> (from 1) at one rank; null when the spell has no such effect.</summary>
    public double? Effect(int index, int rank) =>
        index >= 1 && index <= Effects.Length ? At(Effects[index - 1], rank) : null;

    // The extraction leaves out trailing values equal to the game's default of 0.
    private static double At(double[] values, int rank) =>
        values.Length == 0 ? 0 : values[Math.Clamp(rank, 0, values.Length - 1)];
}

/// <summary>A champion's spells by lower-case script name, and which of them sit on the passive and Q, W, E, R.</summary>
public sealed record ChampionSpellScalings
{
    public static readonly ChampionSpellScalings None = new();

    /// <summary>"P", "Q", "W", "E", "R" to the lower-case script name of the spell in that slot.</summary>
    public Dictionary<string, string> Slots { get; init; } = new();

    public Dictionary<string, SpellScaling> Spells { get; init; } = new();

    public SpellScaling? Slot(string key) =>
        Slots.TryGetValue(key, out var script) ? Spells.GetValueOrDefault(script) : null;

    public SpellScaling? Spell(string script) => Spells.GetValueOrDefault(script.ToLowerInvariant());

    /// <summary>
    /// The five slots' spells and every other spell of the champion's game data that carries numbers: tooltips also
    /// cite values of helper spells ("@spell.EliseR:PassiveTotalDamage@").
    /// </summary>
    public static ChampionSpellScalings Parse(JObject bin, string alias)
    {
        if (bin[$"Characters/{alias}/CharacterRecords/Root"] is not JObject root)
        {
            return None;
        }

        var slotted = new List<(string Slot, string Script)>();
        if ((string?)root["mCharacterPassiveSpell"] is { } passive)
        {
            slotted.Add(("P", Script(passive)));
        }

        var names = (root["spellNames"] as JArray)?.Values<string>().ToArray() ?? [];
        for (int i = 0; i < Math.Min(4, names.Length); i++)
        {
            if (names[i] is { } name)
            {
                slotted.Add(("QWER"[i].ToString(), Script(name)));
            }
        }

        var spells = new Dictionary<string, SpellScaling>();
        foreach (var property in bin.Properties())
        {
            if ((property.Value as JObject)?["mSpell"] is not JObject spell)
            {
                continue;
            }

            string script = Script(property.Name);
            bool numbers =
                spell["DataValues"] is JArray
                || spell["mSpellCalculations"] is JObject
                || spell["mEffectAmount"] is JArray;
            if (
                script.Length > 0
                && !spells.ContainsKey(script)
                && (numbers || slotted.Any(slot => slot.Script == script))
            )
            {
                spells[script] = ParseSpell(script, spell);
            }
        }

        var slots = slotted
            .Where(slot => spells.ContainsKey(slot.Script))
            .GroupBy(slot => slot.Slot)
            .ToDictionary(group => group.Key, group => group.First().Script);

        // Abilities without a rank count use the usual five ranks, three for the ultimate, as the cooldowns do.
        foreach (var (slot, script) in slots)
        {
            if (spells[script].Ranks == 0)
            {
                spells[script] = spells[script] with
                {
                    Ranks =
                        slot == "P" ? 1
                        : slot == "R" ? 3
                        : 5,
                };
            }
        }

        return new() { Slots = slots, Spells = spells };
    }

    // "Characters/Annie/Spells/AnnieQAbility/AnnieQ" and "AnnieQAbility/AnnieQ" both name the spell "annieq".
    private static string Script(string path) => path[(path.LastIndexOf('/') + 1)..].ToLowerInvariant();

    private static SpellScaling ParseSpell(string script, JObject spell)
    {
        string key = (
            (string?)spell.SelectToken("mClientData.mTooltipData.mLocKeys.keyTooltip") ?? ""
        ).ToLowerInvariant();
        var calculations = new JObject();
        foreach (var calculation in (spell["mSpellCalculations"] as JObject ?? new()).Properties())
        {
            if (calculation.Value is JObject && calculations[calculation.Name.ToLowerInvariant()] == null)
            {
                calculations[calculation.Name.ToLowerInvariant()] = calculation.Value.DeepClone();
            }
        }

        var modes = new Dictionary<string, Dictionary<string, double[]>>();
        foreach (var mode in (spell["DataValuesModeOverride"] as JObject ?? new()).Properties())
        {
            if (mode.Name != "__type" && !mode.Name.StartsWith('{') && mode.Value["SpellDataValues"] is JArray values)
            {
                modes[mode.Name.ToLowerInvariant()] = Values(values);
            }
        }

        // The tooltip data counts the ranks (3 for most ultimates, 4 for Karma's, 6 for Udyr's); 0 when it does not.
        int ranks =
            spell.SelectToken("mClientData.mTooltipData.mLists.LevelUp.levelCount")
                is { Type: JTokenType.Integer } count
            && (int)count is >= 1 and <= 6
                ? (int)count
                : 0;
        var named = Values(spell["DataValues"] as JArray);
        // Tooltips also cite the spell's own fields by these names (Teemo R's @MaxAmmo@, Ziggs's @Cooldown@).
        var fields = new Dictionary<string, double[]>
        {
            ["ammorechargetime"] = Numbers(spell["mAmmoRechargeTime"] as JArray),
            ["maxammo"] = Numbers(spell["mMaxAmmo"] as JArray),
            ["cooldown"] = Numbers((spell["Cooldown"]?["values"] ?? spell["cooldownTime"]) as JArray),
            ["cost"] = [0, .. Numbers(spell["mana"] as JArray)],
        };
        foreach (var (name, field) in fields)
        {
            if (field.Length > 1 && !named.ContainsKey(name))
            {
                named[name] = field;
            }
        }

        return new(script, key, ranks)
        {
            Values = named,
            ModeValues = modes,
            Effects = (spell["mEffectAmount"] as JArray ?? new())
                .Select(effect => Numbers(effect["value"] as JArray))
                .ToArray(),
            Costs = Numbers(spell["mana"] as JArray),
            Calculations = calculations,
        };
    }

    private static Dictionary<string, double[]> Values(JArray? values) =>
        (values ?? new())
            .OfType<JObject>()
            .Where(value => value["name"]?.Type == JTokenType.String)
            .GroupBy(value => ((string)value["name"]!).ToLowerInvariant())
            .ToDictionary(group => group.Key, group => Numbers(group.First()["values"] as JArray));

    // Stored as single precision: 0.8 reads back as 0.800000011920929.
    private static double[] Numbers(JArray? values) =>
        (values ?? new())
            .Select(v =>
                v.Type is JTokenType.Float or JTokenType.Integer && double.IsFinite((double)v)
                    ? Math.Round((double)v, 6)
                    : 0
            )
            .ToArray();
}
