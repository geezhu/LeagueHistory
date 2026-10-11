namespace League.Models;

public sealed record RoomRecentStatistics(int Games, double? Kda, double? WinRate)
{
    public static RoomRecentStatistics From(PlayerMatchInfo info)
    {
        var games = info.RecentMatches.Take(20).ToArray();
        var wins = info.WinHistory.Take(20).ToArray();
        return new RoomRecentStatistics(
            games.Length,
            games.Length == 0
                ? null
                : 3d * games.Sum(g => (long)g.Kills + g.Assists) / Math.Max(1L, games.Sum(g => (long)g.Deaths)),
            wins.Length == 0 ? null : 100d * wins.Count(w => w) / wins.Length
        );
    }
}
