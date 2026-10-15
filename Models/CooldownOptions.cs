using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>
/// The cooldown rules a player's recorded augments and hand-entered ultimate haste imply. 2999 reports none of them:
/// the Bread augments add 100 haste to Q, W or E, Pursuit of Haste adds 80 or 160 to one ability, Purist - Caster
/// cuts total cooldowns by 10% and Ult Bot moves all ability haste to the ultimate at 1.5 times its effect.
/// </summary>
public sealed record CooldownOptions(
    bool[] Bread,
    bool Purist,
    string Pursuit,
    double PursuitHaste,
    bool UltBot,
    double UltimateHaste
)
{
    public const double BreadHaste = 100;
    public const double PuristCooldown = 0.9;
    public const double UltBotHaste = 100;
    public const double UltBotEffect = 1.5;
    public const double MaxHaste = 500;

    public static readonly CooldownOptions None = new([false, false, false], false, "", 0, false, 0);

    /// <summary>
    /// The haste one ability's cooldown uses. Ult Bot's 100 is in 2999's value already, so it is added only to an
    /// estimate; abilities' own haste (Bread, Pursuit) is kept under Ult Bot, which the Wiki does not say otherwise.
    /// </summary>
    public double Haste(string key, double haste, bool estimated)
    {
        int slot = "QWER".IndexOf(key, StringComparison.Ordinal);
        double general = haste + (UltBot && estimated ? UltBotHaste : 0);
        double own =
            (slot is >= 0 and < 3 && Bread.ElementAtOrDefault(slot) ? BreadHaste : 0)
            + (key.Length == 1 && Pursuit == key ? PursuitHaste : 0);
        return key == "R"
            ? (general + UltimateHaste) * (UltBot ? UltBotEffect : 1) + own
            : (UltBot ? 0 : general) + own;
    }

    public double Multiplier => Purist ? PuristCooldown : 1;

    /// <summary>The options in effect, in words; empty when none is set.</summary>
    public string Summary()
    {
        string[] bread = ["面包和黄油", "面包和果酱", "面包和奶酪"];
        var parts = bread.Where((_, i) => Bread.ElementAtOrDefault(i)).ToList();
        if (Pursuit.Length > 0)
        {
            parts.Add($"急速之追求 {Pursuit} +{PursuitHaste:0}");
        }

        if (UltBot)
        {
            parts.Add("大招工具人");
        }

        if (UltimateHaste > 0)
        {
            parts.Add($"大招急速 {UltimateHaste:0}");
        }

        if (Purist)
        {
            parts.Add("纯粹主义者 - 术师");
        }

        return string.Join("、", parts);
    }

    /// <summary>
    /// Ultimate haste entered by hand on the augment page, by roster player key, from <c>ultimate-haste.json</c>;
    /// null when malformed. Runes such as Ultimate Hunter grant it and 2999 does not report it.
    /// </summary>
    public static Dictionary<string, double>? ParseUltimateHaste(JObject value, string session)
    {
        if (
            value["protocol"]?.Type != JTokenType.Integer
            || value["protocol"]!.ToString() != "1"
            || (string?)value["session"] != session
            || session.Length == 0
            || value["players"] is not JArray players
            || players.Count > 10
        )
        {
            return null;
        }

        var result = new Dictionary<string, double>();
        foreach (var entry in players)
        {
            string player = entry["player"]?.Type == JTokenType.String ? (string)entry["player"]! : "";
            double? haste = entry["ultimateHaste"]?.Type is JTokenType.Integer or JTokenType.Float
                ? (double)entry["ultimateHaste"]!
                : null;
            if (
                player.Length is 0 or > 256
                || haste is not (>= 0 and <= MaxHaste)
                || !result.TryAdd(player, haste.Value)
            )
            {
                return null;
            }
        }

        return result;
    }
}
