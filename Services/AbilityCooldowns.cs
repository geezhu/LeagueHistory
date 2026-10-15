using League.Models;

namespace League.Services;

/// <summary>
/// An ability's cooldown for the active player now: its rank from 2999, the base at that rank and after ability
/// haste. For a charge ability the cooldown shown is the charge's return time.
/// </summary>
public sealed record AbilityCooldown(string Key, string Name, int Rank, int Ranks, double Base, double Current)
{
    /// <summary>Charges the ability holds at this rank; 0 for an ability without charges.</summary>
    public int Charges { get; init; }

    /// <summary>Back to Basics sealed the ultimate: it cannot be cast, so it has no cooldown.</summary>
    public bool Sealed { get; init; }
}

public static class AbilityCooldowns
{
    /// <summary>Ability haste shortens a cooldown to 100 / (100 + haste) of its base.</summary>
    public static double Hasted(double seconds, double haste) => seconds * 100 / (100 + Math.Max(0, haste));

    /// <summary>The active player's cooldowns: 2999's ranks and haste, with the cooldown page's options.</summary>
    public static AbilityCooldown[] Describe(
        IEnumerable<SpellCooldown> spells,
        IReadOnlyDictionary<string, int> ranks,
        double haste,
        CooldownOptions? options = null
    ) =>
        spells
            .Select(spell =>
            {
                int count = spell.Cooldowns.Length;
                if (spell.Key == "R" && options?.UltimateSealed == true)
                {
                    return new AbilityCooldown(spell.Key, spell.Name, 0, count, 0, 0) { Sealed = true };
                }

                int rank = Math.Clamp(ranks.GetValueOrDefault(spell.Key), 0, count);
                if (rank == 0)
                {
                    return new AbilityCooldown(spell.Key, spell.Name, 0, count, 0, 0);
                }

                bool charged = spell.Recharge.Length >= rank && spell.Recharge[rank - 1] > 0;
                double seconds = charged ? spell.Recharge[rank - 1] : spell.Cooldowns[rank - 1];
                var set = options ?? CooldownOptions.None;
                double current = Hasted(seconds, set.Haste(spell.Key, haste, estimated: false)) * set.Multiplier;
                return new AbilityCooldown(spell.Key, spell.Name, rank, count, seconds, current)
                {
                    Charges = charged ? spell.Charges.ElementAtOrDefault(rank - 1) : 0,
                };
            })
            .ToArray();
}
