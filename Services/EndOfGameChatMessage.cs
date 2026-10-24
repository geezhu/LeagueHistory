namespace League.Services;

/// <summary>The lines the end-of-game panel sends to the post-game chat: a thumbs-up after a win, a jab after a loss.</summary>
public static class EndOfGameChatMessage
{
    // WeGame's own wording, signed with this application's name where WeGame signs with its own.
    public static string Honor(IEnumerable<string> names) =>
        $"和{Join(names)}这样的神级好队友一起战斗真是太酷炫了！ ——LeagueHistory";

    public static string Block(IEnumerable<string> names) => $"不怕神一样的对手，就怕{Join(names)}一样的队友";

    // A name can never break the chat line or leave a gap where it should be.
    private static string Join(IEnumerable<string> names)
    {
        string[] cleaned = names
            .Select(name => name.Replace('\r', ' ').Replace('\n', ' ').Trim())
            .Where(name => name.Length > 0)
            .ToArray();
        if (cleaned.Length == 0)
        {
            throw new ArgumentException("至少需要一个玩家名。", nameof(names));
        }

        return string.Join("、", cleaned);
    }
}
