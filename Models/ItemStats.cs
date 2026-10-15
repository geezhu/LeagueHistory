using System.Globalization;
using System.Text.RegularExpressions;

namespace League.Models;

/// <summary>Flat health, armor, magic resist and ability haste an item lists in its stats block.</summary>
public sealed record ItemStats(double Health, double Armor, double MagicResist, double AbilityHaste)
{
    public static readonly ItemStats None = new(0, 0, 0, 0);

    // The client's item text lists stats as "<attention>150</attention>生命值<br>"; percent stats are other kinds.
    private static readonly Regex Stat = new(
        @"<attention>\s*(?<value>\d+(?:\.\d+)?)\s*</attention>\s*(?<name>生命值|护甲|魔法抗性|技能急速)\s*(?:<|$)",
        RegexOptions.CultureInvariant
    );

    public static ItemStats Parse(string? description)
    {
        var block = Regex.Match(description ?? "", "<stats>(.*?)</stats>", RegexOptions.Singleline);
        if (!block.Success)
        {
            return None;
        }

        var total = None;
        foreach (Match match in Stat.Matches(block.Groups[1].Value))
        {
            double value = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            total = match.Groups["name"].Value switch
            {
                "生命值" => total with { Health = total.Health + value },
                "护甲" => total with { Armor = total.Armor + value },
                "魔法抗性" => total with { MagicResist = total.MagicResist + value },
                _ => total with { AbilityHaste = total.AbilityHaste + value },
            };
        }

        return total;
    }

    public static ItemStats Sum(IEnumerable<ItemStats> items) =>
        items.Aggregate(
            None,
            (total, item) =>
                new(
                    total.Health + item.Health,
                    total.Armor + item.Armor,
                    total.MagicResist + item.MagicResist,
                    total.AbilityHaste + item.AbilityHaste
                )
        );
}
