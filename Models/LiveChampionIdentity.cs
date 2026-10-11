namespace League.Models;

public static class LiveChampionIdentity
{
    public static string ReadAlias(string? rawName)
    {
        string raw = rawName?.Trim() ?? "";
        const string legacy = "game_character_displayname_",
            prefix = "Character_",
            suffix = "_Name";
        string alias =
            raw.StartsWith(legacy, StringComparison.OrdinalIgnoreCase) ? raw[legacy.Length..]
            : raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && raw.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && raw.Length > prefix.Length + suffix.Length
                ? raw[prefix.Length..^suffix.Length]
            : "";
        return alias.Length is > 0 and <= 80 && alias.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? alias : "";
    }

    public static Champion? Resolve(LiveMatchPlayer player, IEnumerable<Champion> champions)
    {
        var catalog = champions.Where(c => c.Id > 0);
        string alias = ReadAlias(player.ChampionAlias);
        if (alias.Length == 0)
            alias = player.ChampionAlias.Trim();
        var matches = catalog
            .Where(c => alias.Length > 0 && string.Equals(c.Alias, alias, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(c => c.Id)
            .Take(2)
            .ToArray();
        if (matches.Length > 0)
            return matches.Length == 1 ? matches[0] : null;
        // Exact resource names also work when the game omits or changes the raw key.
        string name = player.ChampionName.Trim();
        matches = catalog
            .Where(c =>
                name.Length > 0
                && (
                    string.Equals(c.Name, name, StringComparison.Ordinal)
                    || string.Equals(c.Alias, name, StringComparison.OrdinalIgnoreCase)
                )
            )
            .DistinctBy(c => c.Id)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
