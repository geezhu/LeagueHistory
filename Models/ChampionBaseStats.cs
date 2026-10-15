using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>A champion's level 1 health and resistances and their growth, from Data Dragon's champion list.</summary>
public sealed record ChampionBaseStats(
    double Health,
    double HealthPerLevel,
    double Armor,
    double ArmorPerLevel,
    double MagicResist,
    double MagicResistPerLevel
)
{
    /// <summary>Attack speed gained per level, in percent; it counts as bonus attack speed.</summary>
    public double AttackSpeedPerLevel { get; init; }

    /// <summary>The champion's default attack range; Draw Your Sword scales with it.</summary>
    public double AttackRange { get; init; }

    /// <summary>The game's growth curve: each level adds a little more than the one before.</summary>
    public static double Grown(double value, double perLevel, int level)
    {
        int steps = Math.Max(0, level - 1);
        return value + perLevel * steps * (0.7025 + 0.0175 * steps);
    }

    /// <summary>Champion stats by numeric champion id; entries with missing or invalid numbers are left out.</summary>
    public static Dictionary<int, ChampionBaseStats> ParseAll(JObject championList)
    {
        var result = new Dictionary<int, ChampionBaseStats>();
        foreach (var entry in (championList["data"] as JObject ?? new()).Properties())
        {
            if (
                entry.Value is not JObject champion
                || !int.TryParse((string?)champion["key"], out int id)
                || champion["stats"] is not JObject stats
            )
            {
                continue;
            }

            double[] values =
            [
                .. new[] { "hp", "hpperlevel", "armor", "armorperlevel", "spellblock", "spellblockperlevel" }.Select(
                    name => stats[name]?.Type is JTokenType.Integer or JTokenType.Float ? (double)stats[name]! : -1
                ),
            ];
            if (values.All(value => value >= 0 && double.IsFinite(value)) && values[0] > 0)
            {
                double growth = stats["attackspeedperlevel"]?.Type is JTokenType.Integer or JTokenType.Float
                    ? (double)stats["attackspeedperlevel"]!
                    : 0;
                result[id] = new(values[0], values[1], values[2], values[3], values[4], values[5])
                {
                    AttackSpeedPerLevel = double.IsFinite(growth) ? Math.Max(0, growth) : 0,
                    AttackRange = stats["attackrange"]?.Type is JTokenType.Integer or JTokenType.Float
                        ? Math.Max(0, (double)stats["attackrange"]!)
                        : 0,
                };
            }
        }

        return result;
    }
}
