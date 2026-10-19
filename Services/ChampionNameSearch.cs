using League.Models;

namespace League.Services;

/// <summary>
/// Finds champions by what a player types: the name, the title, the English alias or the pinyin initials of the
/// name. An exact match wins outright; otherwise every partial match is returned, so the caller can ask for more.
/// </summary>
public static class ChampionNameSearch
{
    /// <summary>The text shown for a champion in lists; typing it back finds that champion exactly.</summary>
    public static string Label(Champion champion) =>
        string.IsNullOrEmpty(champion.Description) ? champion.Name : $"{champion.Name}（{champion.Description}）";

    public static IReadOnlyList<Champion> Search(string? query, IEnumerable<Champion> champions)
    {
        string text = query?.Trim() ?? "";
        if (text.Length == 0)
        {
            return [];
        }

        // champion-summary.json lists a placeholder with id -1.
        var known = champions.Where(c => c.Id > 0).ToArray();
        var exact = known.FirstOrDefault(c =>
            Label(c) == text || Same(c.Name, text) || Same(c.Description, text) || Same(c.Alias, text)
        );
        if (exact != null)
        {
            return [exact];
        }

        string initials = text.ToLowerInvariant();
        return known
            .Where(c =>
                Contains(c.Name, text)
                || Contains(c.Description, text)
                || Contains(c.Alias, text)
                || ChineseInitials.Of(c.Name).StartsWith(initials, StringComparison.Ordinal)
            )
            .ToArray();
    }

    private static bool Same(string? value, string text) =>
        string.Equals(value, text, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string text) =>
        value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
}
