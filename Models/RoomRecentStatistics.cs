namespace League.Models;

public sealed record RoomRecentStatistics(int Games, double? Kda, double? WinRate)
{
    private const int Window = 20;

    /// <summary>
    /// The mean estimated score of the ARAM: Mayhem games in the window, remakes left out; null without any. The card
    /// shows it in place of the KDA.
    /// </summary>
    public double? AverageScore { get; init; }

    /// <summary>How many games the average covers.</summary>
    public int ScoredGames { get; init; }

    public static RoomRecentStatistics From(PlayerMatchInfo info)
    {
        var games = info.RecentMatches.Take(Window).ToArray();
        var wins = info.WinHistory.Take(Window).ToArray();
        var scores = info
            .MatchItems.Take(Window)
            .Where(item => item.ForeColor != MatchResult.RemakeColor)
            .Select(item => (item.Tag as MatchMetadata)?.Score)
            .OfType<OwnMatchScore>()
            .Select(score => score.Estimate)
            .ToArray();
        return new RoomRecentStatistics(
            games.Length,
            games.Length == 0
                ? null
                : 3d * games.Sum(g => (long)g.Kills + g.Assists) / Math.Max(1L, games.Sum(g => (long)g.Deaths)),
            wins.Length == 0 ? null : 100d * wins.Count(w => w) / wins.Length
        )
        {
            AverageScore = scores.Length == 0 ? null : scores.Average(),
            ScoredGames = scores.Length,
        };
    }
}
