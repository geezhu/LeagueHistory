namespace League.Models;

/// <summary>
/// Game modes as the champion data names its overrides, in lower case: "aram", "kiwi" (ARAM: Mayhem), "urf",
/// "cherry" (Arena) and "swiftplay"; empty for the usual rules. 2999 reports the mode once a game runs; before that
/// the queue tells it.
/// </summary>
public static class GameModes
{
    private static readonly Dictionary<int, string> Queues = new()
    {
        [100] = "aram",
        [450] = "aram",
        [2400] = "kiwi",
        [3270] = "kiwi",
        [900] = "urf",
        [1010] = "urf",
        [1900] = "urf",
        [1700] = "cherry",
        [1710] = "cherry",
        [480] = "swiftplay",
        [490] = "swiftplay",
    };

    public static string FromQueue(int queue) => Queues.GetValueOrDefault(queue, "");

    /// <summary>The running game's mode from 2999 ("KIWI", "ARAM", "CLASSIC"), else the queue's.</summary>
    public static string Of(string? liveMode, int queue) =>
        string.IsNullOrWhiteSpace(liveMode) || liveMode.Equals("CLASSIC", StringComparison.OrdinalIgnoreCase)
            ? FromQueue(queue)
            : liveMode.Trim().ToLowerInvariant();
}
