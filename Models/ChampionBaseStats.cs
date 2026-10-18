using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>A champion's level 1 stats and their growth, from Data Dragon's champion list.</summary>
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

    /// <summary>Attacks per second at level 1.</summary>
    public double AttackSpeed { get; init; }

    public double AttackDamage { get; init; }

    public double AttackDamagePerLevel { get; init; }

    /// <summary>The resource bar at level 1; 0 for champions without one.</summary>
    public double Mana { get; init; }

    public double ManaPerLevel { get; init; }

    public double MoveSpeed { get; init; }

    /// <summary>Health regenerated per 5 seconds.</summary>
    public double HealthRegen { get; init; }

    public double HealthRegenPerLevel { get; init; }

    /// <summary>Resource regenerated per 5 seconds.</summary>
    public double ManaRegen { get; init; }

    public double ManaRegenPerLevel { get; init; }

    /// <summary>The resource's name as Data Dragon words it ("法力值", "能量", "无").</summary>
    public string Resource { get; init; } = "";

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
                // Optional stats read as 0 when missing or invalid; negative growth (none today) is kept out.
                double Number(string name) =>
                    stats[name]?.Type is JTokenType.Integer or JTokenType.Float
                    && (double)stats[name]! is var value
                    && double.IsFinite(value)
                        ? Math.Max(0, value)
                        : 0;
                result[id] = new(values[0], values[1], values[2], values[3], values[4], values[5])
                {
                    AttackSpeedPerLevel = Number("attackspeedperlevel"),
                    AttackRange = Number("attackrange"),
                    AttackSpeed = Number("attackspeed"),
                    AttackDamage = Number("attackdamage"),
                    AttackDamagePerLevel = Number("attackdamageperlevel"),
                    Mana = Number("mp"),
                    ManaPerLevel = Number("mpperlevel"),
                    MoveSpeed = Number("movespeed"),
                    HealthRegen = Number("hpregen"),
                    HealthRegenPerLevel = Number("hpregenperlevel"),
                    ManaRegen = Number("mpregen"),
                    ManaRegenPerLevel = Number("mpregenperlevel"),
                    Resource = (string?)champion["partype"] ?? "",
                };
            }
        }

        return result;
    }
}
