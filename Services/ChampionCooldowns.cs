using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>
/// An ability's cooldown at each rank from the first, in seconds. Charge abilities also list the time one charge
/// takes to return and how many charges they hold; their cooldown is only the short gap between casts.
/// </summary>
public sealed record SpellCooldown(string Key, string Name, double[] Cooldowns)
{
    public double[] Recharge { get; init; } = [];

    public int[] Charges { get; init; } = [];
}

/// <summary>
/// Cooldowns from CommunityDragon's extraction of the game's champion data, read from each ability's own spell
/// (the one named in the character record). Values are per rank with index 0 unlearned, as with the slows.
/// </summary>
public static class ChampionCooldowns
{
    // The extraction leaves out values equal to the game's default; a spell without cooldowns has 10 seconds, as
    // Data Dragon also lists (Morgana Q, Annie E).
    private const double DefaultCooldown = 10;

    public static SpellCooldown[] Parse(JObject bin, JObject champion)
    {
        string alias = (string?)champion["alias"] ?? "";
        var scripts =
            (bin[$"Characters/{alias}/CharacterRecords/Root"]?["spellNames"] as JArray)?.Values<string>().ToArray()
            ?? [];
        var names = (champion["spells"] as JArray ?? new())
            .OfType<JObject>()
            .GroupBy(s => ((string?)s["spellKey"] ?? "").ToUpperInvariant())
            .ToDictionary(g => g.Key, g => (string?)g.First()["name"] ?? "");
        var result = new List<SpellCooldown>();
        for (int i = 0; i < Math.Min(4, scripts.Length); i++)
        {
            string key = "QWER"[i].ToString();
            string script = scripts[i] ?? "";
            var spell = bin.Properties()
                .Where(p =>
                    script.Length > 0
                    && p.Name.EndsWith("/" + script, StringComparison.OrdinalIgnoreCase)
                    && p.Value["mSpell"] is JObject
                )
                .Select(p => (JObject)p.Value["mSpell"]!)
                .FirstOrDefault();
            if (spell == null)
            {
                continue;
            }

            int ranks = ChampionSlowSkills.Ranks(spell, key);
            double[] recharge = Ranked(spell["mAmmoRechargeTime"], ranks);
            double[] cooldowns =
                spell["cooldownTime"] is JArray
                    ? Ranked(spell["cooldownTime"], ranks)
                    : Enumerable.Repeat(DefaultCooldown, ranks).ToArray();
            result.Add(
                new(key, names.GetValueOrDefault(key, key), cooldowns)
                {
                    Recharge = recharge.Any(value => value > 0) ? recharge : [],
                    Charges = recharge.Any(value => value > 0)
                        ? Ranked(spell["mMaxAmmo"], ranks).Select(value => (int)value).ToArray()
                        : [],
                }
            );
        }

        return result.ToArray();
    }

    private static double[] Ranked(JToken? values, int ranks) =>
        (values as JArray ?? new())
            .Skip(1)
            .Take(ranks)
            // Stored as single precision: 0.4 reads back as 0.4000000059604645.
            .Select(v => v.Type is JTokenType.Float or JTokenType.Integer ? Math.Round(Math.Max(0, (double)v), 3) : 0)
            .ToArray();
}
