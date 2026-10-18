using System.Globalization;
using System.Text.RegularExpressions;

namespace League.Models;

/// <summary>
/// Flat health, armor, magic resist and ability haste an item lists in its stats block, and its attack speed in
/// percent (bonus attack speed, which Purist - Caster turns into ability haste). The offensive stats beside them feed
/// the Game Bar's estimated stat sheet for other players.
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

    public double AttackDamage { get; init; }

    public double AbilityPower { get; init; }

    /// <summary>Critical strike chance in percent.</summary>
    public double CritChance { get; init; }

    public double MoveSpeed { get; init; }

    /// <summary>Move speed in percent, applied after the flat bonus.</summary>
    public double MoveSpeedPercent { get; init; }

    public double Mana { get; init; }

    public double Lethality { get; init; }

    public double MagicPenetration { get; init; }

    // The client's item text lists stats as "<attention>150</attention>生命值<br>"; percent stats are other kinds.
    private static readonly Regex Stat = new(
        @"<attention>\s*(?<value>\d+(?:\.\d+)?)\s*</attention>\s*(?<name>生命值|护甲|魔法抗性|技能急速)\s*(?:<|$)",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex AttackSpeedStat = new(
        @"<attention>\s*(?<value>\d+(?:\.\d+)?)%\s*</attention>\s*攻击速度",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex OffenseStat = new(
        @"<attention>\s*(?<value>\d+(?:\.\d+)?)\s*</attention>\s*(?<name>攻击力|法术强度|移动速度|法力|穿甲|法术穿透)\s*(?:<|$)",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex PercentStat = new(
        @"<attention>\s*(?<value>\d+(?:\.\d+)?)%\s*</attention>\s*(?<name>暴击几率|移动速度)\s*(?:<|$)",
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

        foreach (Match match in OffenseStat.Matches(block.Groups[1].Value))
        {
            double value = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            total = match.Groups["name"].Value switch
            {
                "攻击力" => total with { AttackDamage = total.AttackDamage + value },
                "法术强度" => total with { AbilityPower = total.AbilityPower + value },
                "移动速度" => total with { MoveSpeed = total.MoveSpeed + value },
                "法力" => total with { Mana = total.Mana + value },
                "穿甲" => total with { Lethality = total.Lethality + value },
                _ => total with { MagicPenetration = total.MagicPenetration + value },
            };
        }

        foreach (Match match in PercentStat.Matches(block.Groups[1].Value))
        {
            double value = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            total =
                match.Groups["name"].Value == "暴击几率"
                    ? total with
                    {
                        CritChance = total.CritChance + value,
                    }
                    : total with
                    {
                        MoveSpeedPercent = total.MoveSpeedPercent + value,
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
                {
                    AttackDamage = total.AttackDamage + item.AttackDamage,
                    AbilityPower = total.AbilityPower + item.AbilityPower,
                    CritChance = total.CritChance + item.CritChance,
                    MoveSpeed = total.MoveSpeed + item.MoveSpeed,
                    MoveSpeedPercent = total.MoveSpeedPercent + item.MoveSpeedPercent,
                    Mana = total.Mana + item.Mana,
                    Lethality = total.Lethality + item.Lethality,
                    MagicPenetration = total.MagicPenetration + item.MagicPenetration,
                }
        );
}
