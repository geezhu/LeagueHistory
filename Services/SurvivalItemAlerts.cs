namespace League.Services;

/// <summary>A player's survival items on one 2999 poll, by base item id, and whether their purchases sound.</summary>
public sealed record SurvivalInventory(string Key, int[] Items, bool Audible);

/// <summary>The purchase to sound: whose and which base item.</summary>
public sealed record SurvivalAlert(string Key, int Item);

/// <summary>
/// Announces players' new survival items with the item's own sound, for the teams the user chose. 2999 shows
/// inventories but not item cooldowns or activations, so the alert is for getting the item, not for using it. A
/// player first seen with items is silent, so starting the program mid-game does not replay every purchase; several
/// new items in one poll play only the first, since a sound replaces the one still playing.
/// </summary>
public sealed class SurvivalItemAlerts
{
    private readonly Dictionary<string, HashSet<int>> _seen = new(StringComparer.Ordinal);

    /// <summary>The purchase to sound for this poll, or null; every inventory is remembered, audible or not.</summary>
    public SurvivalAlert? Update(IEnumerable<SurvivalInventory> players)
    {
        SurvivalAlert? play = null;
        foreach (var player in players)
        {
            if (!_seen.TryGetValue(player.Key, out var before))
            {
                _seen[player.Key] = player.Items.ToHashSet();
                continue;
            }

            int? fresh = player.Items.Where(id => !before.Contains(id)).Cast<int?>().FirstOrDefault();
            // A sold or used-up item leaves; buying it again is a new purchase.
            before.IntersectWith(player.Items);
            before.UnionWith(player.Items);
            if (player.Audible && fresh is int item)
            {
                play ??= new(player.Key, item);
            }
        }

        return play;
    }

    public void Reset() => _seen.Clear();
}
