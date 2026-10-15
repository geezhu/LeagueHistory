using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>
/// A summoner spell from Data Dragon, keyed by the game's internal name ("SummonerFlash") that 2999 reports. Modes
/// use their own spells (Mark in ARAM, Flee in Arena), each with its own cooldown; some modes also change a shared
/// spell's cooldown or give everyone summoner haste (<see cref="SummonerSpells.Cooldown"/>).
/// </summary>
public sealed record SummonerSpellInfo(string Id, string Name, double Cooldown, string Image);

/// <summary>A summoner spell timer the user started on an ally: whose, which slot (0 or 1) and the game time.</summary>
public sealed record SummonerTimer(string Player, int Slot, double Time);

/// <summary>One of a player's two summoner spells as the Game Bar shows it.</summary>
/// <param name="Cooldown">Seconds after summoner haste; 0 for enemies, whose spells are never timed.</param>
/// <param name="Started">The game time the user started the timer; null when it is not running.</param>
public sealed record PlayerSummoner(int Slot, SummonerSpellInfo Spell, double Cooldown, double? Started)
{
    public double? ReadyAt => Started + Cooldown;
}

public static class SummonerSpells
{
    private const int MaxTimers = 10;

    /// <summary>The internal name in 2999's "GeneratedTip_SummonerSpell_SummonerFlash_DisplayName".</summary>
    public static string InternalName(string? raw)
    {
        const string prefix = "GeneratedTip_SummonerSpell_",
            suffix = "_DisplayName";
        return
            raw != null
            && raw.StartsWith(prefix, StringComparison.Ordinal)
            && raw.EndsWith(suffix, StringComparison.Ordinal)
            ? raw[prefix.Length..^suffix.Length]
            : "";
    }

    /// <summary>
    /// Summoner haste visible for a player: Ionian Boots of Lucidity 10, Crimson Lucidity 20 and, for the active
    /// player whose runes 2999 reports, Cosmic Insight 18. Allies' minor runes are not reported.
    /// </summary>
    public static double Haste(IEnumerable<LiveMatchItem> items, IEnumerable<int> runes)
    {
        double haste = 0;
        foreach (var item in items)
        {
            int id = item.Id >= 100000 ? item.Id % 10000 : item.Id;
            haste += id switch
            {
                3158 => 10,
                3171 => 20,
                _ => 0,
            };
        }

        return haste + (runes.Contains(8347) ? 18 : 0);
    }

    /// <summary>
    /// Summoner haste every champion gets from the mode: the Howling Abyss aura gives 70 in ARAM and ARAM: Mayhem,
    /// URF gives 300. The game applies both in script, so the numbers come from the Wiki, not the game data.
    /// </summary>
    public static double ModeHaste(string mode) =>
        mode.ToLowerInvariant() switch
        {
            "aram" or "kiwi" => 70,
            "urf" => 300,
            _ => 0,
        };

    /// <summary>The spell's base cooldown in the mode: the game data's override (Exhaust 290 in ARAM), else its own.</summary>
    public static double BaseCooldown(
        SummonerSpellInfo spell,
        string mode,
        IReadOnlyDictionary<string, Dictionary<string, double>>? modes
    ) =>
        modes?.GetValueOrDefault(spell.Id)?.GetValueOrDefault(mode.ToLowerInvariant()) is double cooldown and > 0
            ? cooldown
            : spell.Cooldown;

    /// <summary>The cooldown after the player's summoner haste and the mode's.</summary>
    public static double Cooldown(
        SummonerSpellInfo spell,
        double haste,
        string mode = "",
        IReadOnlyDictionary<string, Dictionary<string, double>>? modes = null
    ) => BaseCooldown(spell, mode, modes) * 100 / (100 + Math.Max(0, haste) + ModeHaste(mode));

    /// <summary>The widget's running timers for this session from <c>summoner-timers.json</c>; null when malformed.</summary>
    public static SummonerTimer[]? ParseTimers(JObject value, string session)
    {
        if (
            value["protocol"]?.Type != JTokenType.Integer
            || value["protocol"]!.ToString() != "1"
            || (string?)value["session"] != session
            || session.Length == 0
            || value["timers"] is not JArray timers
            || timers.Count > MaxTimers * 2
        )
        {
            return null;
        }

        var result = new List<SummonerTimer>();
        foreach (var entry in timers)
        {
            if (
                entry is not JObject row
                || row["player"]?.Type != JTokenType.String
                || row["slot"]?.Type != JTokenType.Integer
                || row["time"]?.Type is not (JTokenType.Integer or JTokenType.Float)
            )
            {
                return null;
            }

            var timer = new SummonerTimer((string)row["player"]!, (int)row["slot"]!, (double)row["time"]!);
            if (
                timer.Player.Length is 0 or > 256
                || timer.Slot is not (0 or 1)
                || !double.IsFinite(timer.Time)
                || timer.Time < 0
                || result.Any(t => t.Player == timer.Player && t.Slot == timer.Slot)
            )
            {
                return null;
            }

            result.Add(timer);
        }

        return result.ToArray();
    }
}
