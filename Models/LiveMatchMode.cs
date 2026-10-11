namespace League.Models;

public static class LiveMatchMode
{
    // Normalize recommendation sources, without rewriting the original queue identity.
    public static int RecommendationQueue(int queue, LiveMatchSnapshot match)
    {
        if (queue is 450 or 2400)
            return queue;
        if (string.Equals(match.Mode, "KIWI", StringComparison.OrdinalIgnoreCase))
            return 2400;
        if (string.Equals(match.Mode, "ARAM", StringComparison.OrdinalIgnoreCase))
            return 450;
        return 0;
    }
}
