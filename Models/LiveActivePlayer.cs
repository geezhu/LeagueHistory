using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>The active player's own panel from 2999; other players' attributes are not exposed.</summary>
public sealed record LiveChampionStats(
    double MaxHealth,
    double Armor,
    double MagicResist,
    double AbilityHaste,
    double PhysicalLethality,
    double ArmorPenetrationPercent,
    double MagicPenetrationFlat,
    double MagicPenetrationPercent
)
{
    public static LiveChampionStats? Parse(JObject? stats)
    {
        if (stats?["maxHealth"] is not JToken health || (double?)health is not (double maxHealth and > 0))
        {
            return null;
        }

        double Value(string name) => (double?)stats[name] is double value && double.IsFinite(value) ? value : 0;

        // 2999 reports percent penetration as the multiplier left on the target: 1.0 means none.
        double Penetration(string name) =>
            (double?)stats[name] is double value && double.IsFinite(value) ? Math.Clamp(1 - value, 0, 1) : 0;
        return new(
            maxHealth,
            Value("armor"),
            Value("magicResist"),
            Value("abilityHaste"),
            Value("physicalLethality"),
            Penetration("armorPenetrationPercent"),
            Value("magicPenetrationFlat"),
            Penetration("magicPenetrationPercent")
        );
    }
}

public sealed record LiveActivePlayer(LiveChampionStats? Stats, int[] Runes)
{
    public static LiveActivePlayer? Parse(JObject? active)
    {
        if (active == null)
        {
            return null;
        }

        // Mayhem (KIWI) returns an empty fullRunes object; the client rune page is the fallback there.
        var runes = active["fullRunes"] as JObject;
        int[] ids = (runes?["generalRunes"] as JArray ?? new())
            .OfType<JObject>()
            .Concat((runes?["statRunes"] as JArray ?? new()).OfType<JObject>())
            .Select(rune => (int?)rune["id"] ?? 0)
            .Where(id => id > 0)
            .ToArray();
        return new(LiveChampionStats.Parse(active["championStats"] as JObject), ids);
    }
}
