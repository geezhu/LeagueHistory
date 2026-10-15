using System.Globalization;
using System.Text.RegularExpressions;

namespace League.Models;

/// <summary>
/// Flat health, armor, magic resist and ability haste an item lists in its stats block, and its attack speed in
/// percent (bonus attack speed, which Purist - Caster turns into ability haste).
/// </summary>
public sealed record ItemStats(
    double Health,
    double Armor,
    double MagicResist,
    double AbilityHaste,
    double AttackSpeed = 0
)
{
    public static readonly ItemStats None = new(0, 0, 0, 0);

    // The client's item text lists stats as "<attention>150</attention>生命值<br>"; percent stats are other kinds.
    private static readonly Regex Stat = new(
        @"<attention>\s*(?<value>\d+(?:\.\d+)?)\s*</attention>\s*(?<name>生命值|护甲|魔法抗性|技能急速)\s*(?:<|$)",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex AttackSpeedStat = new(
        @"<attention>\s*(?<value>\d+(?:\.\d+)?)%\s*</attention>\s*攻击速度",
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

        foreach (Match match in AttackSpeedStat.Matches(block.Groups[1].Value))
        {
            total = total with
            {
                AttackSpeed =
                    total.AttackSpeed + double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture),
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
                    total.AbilityHaste + item.AbilityHaste,
                    total.AttackSpeed + item.AttackSpeed
                )
        );
}
