namespace League.Models;

public sealed record LiveBuildOption(int[] Ids, double? WinRate, double? PickRate, long? Games);

/// <summary>The recommended build closest to what the player already owns, and its first missing item.</summary>
public sealed record LiveBuildAdvice(LiveBuildOption Build, int Owned, int? Next)
{
    public static LiveBuildAdvice? Choose(IEnumerable<LiveBuildOption> options, IEnumerable<int> owned)
    {
        var items = owned.ToHashSet();
        // Most items in common first, so the advice follows the path already taken; then the source order.
        var match = options
            .Where(option => option.Ids.Length > 0)
            .Select((option, order) => (Option: option, Order: order, Owned: option.Ids.Count(items.Contains)))
            .OrderByDescending(row => row.Owned)
            .ThenBy(row => row.Order)
            .FirstOrDefault();
        if (match.Option == null)
        {
            return null;
        }

        int? next = match.Option.Ids.Where(id => !items.Contains(id)).Select(id => (int?)id).FirstOrDefault();
        return new(match.Option, match.Owned, next);
    }
}
