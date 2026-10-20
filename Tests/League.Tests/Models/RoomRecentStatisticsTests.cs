using League.Models;

namespace League.Tests.Models;

/// <summary>
/// The two numbers shown beside a player in the loading screen: how many recent games were read, the KDA over them,
/// and the share won.
///
/// The two lists come from the same source but are read separately, and the game count is the one the matches give -
/// so a win history shorter than the match list does not change how many games are reported, only the rate.
/// Twenty games are the sample: more would say less about how the player is doing now.
/// </summary>
public class RoomRecentStatisticsTests
{
    private static MatchStat Stat(int kills, int deaths, int assists) =>
        new()
        {
            Kills = kills,
            Deaths = deaths,
            Assists = assists,
        };

    private static PlayerMatchInfo Info(MatchStat[] matches, bool[] wins) =>
        new() { RecentMatches = [.. matches], WinHistory = [.. wins] };

    // ---- nothing to report ----

    [Fact]
    public void From_NoGamesAtAll_ReportsNothing()
    {
        // Zero games is not a KDA of zero and not a win rate of zero: there is nothing to average.
        RoomRecentStatistics statistics = RoomRecentStatistics.From(Info([], []));

        Assert.Equal(0, statistics.Games);
        Assert.Null(statistics.Kda);
        Assert.Null(statistics.WinRate);
    }

    [Fact]
    public void From_MatchesWithoutAWinHistory_StillReportsTheKda()
    {
        // The two lists are read independently, so one being empty does not spoil the other.
        RoomRecentStatistics statistics = RoomRecentStatistics.From(Info([Stat(1, 1, 1)], []));

        Assert.Equal(1, statistics.Games);
        Assert.NotNull(statistics.Kda);
        Assert.Null(statistics.WinRate);
    }

    // ---- the KDA ----

    [Fact]
    public void From_TheKda_IsThreeTimesKillsAndAssistsOverDeaths()
    {
        // Traced: kills and assists 5 + 2 + 1 + 0 = 8, deaths 3 + 4 = 7, so 3 × 8 / 7.
        RoomRecentStatistics statistics = RoomRecentStatistics.From(
            Info([Stat(5, 3, 2), Stat(1, 4, 0)], [true, false])
        );

        Assert.Equal(3 * 8d / 7, statistics.Kda.GetValueOrDefault(), 3);
    }

    [Fact]
    public void From_NoDeathsAtAll_DoesNotDivideByZero()
    {
        // The denominator is floored at one, so a flawless run reads as a large number rather than infinity.
        RoomRecentStatistics statistics = RoomRecentStatistics.From(Info([Stat(1, 0, 0)], []));

        Assert.Equal(3, statistics.Kda.GetValueOrDefault(), 3);
    }

    // ---- the win rate ----

    [Fact]
    public void From_TheWinRate_IsTheShareWonAsAPercentage()
    {
        RoomRecentStatistics statistics = RoomRecentStatistics.From(Info([], [true, false, true, false]));

        Assert.Equal(50, statistics.WinRate.GetValueOrDefault(), 3);
    }

    [Fact]
    public void From_EveryGameWon_IsAHundredPercent()
    {
        Assert.Equal(100, RoomRecentStatistics.From(Info([], [true, true])).WinRate.GetValueOrDefault(), 3);
    }

    [Fact]
    public void From_EveryGameLost_IsZeroPercentRatherThanNothing()
    {
        // Zero is a real answer here: games were played and none were won.
        Assert.Equal(0, RoomRecentStatistics.From(Info([], [false, false])).WinRate);
    }

    // ---- the sample size ----

    [Fact]
    public void From_MoreThanTwentyGames_KeepsTwenty()
    {
        // Twenty is the sample; older games say less about how the player is doing now.
        MatchStat[] matches = [.. Enumerable.Range(0, 25).Select(_ => Stat(1, 1, 1))];

        Assert.Equal(20, RoomRecentStatistics.From(Info(matches, [])).Games);
    }

    [Fact]
    public void From_TheFirstTwentyOfTheList_AreTheOnesCounted()
    {
        // Traced: the first twenty are kills of one and deaths of zero, so 3 × 20 / max(1, 0) = 60.
        MatchStat[] matches = [.. Enumerable.Range(0, 20).Select(_ => Stat(1, 0, 0)), Stat(0, 100, 0)];

        Assert.Equal(60, RoomRecentStatistics.From(Info(matches, [])).Kda.GetValueOrDefault(), 3);
    }

    [Fact]
    public void From_AWinHistoryLongerThanTheMatches_IsAlsoCutToTwenty()
    {
        // Traced: twenty-five entries, the first ten wins. Taking twenty keeps ten wins and ten losses, so half.
        RoomRecentStatistics statistics = RoomRecentStatistics.From(
            Info(
                [.. Enumerable.Range(0, 20).Select(_ => Stat(1, 1, 0))],
                [.. Enumerable.Range(0, 25).Select(i => i < 10)]
            )
        );

        Assert.Equal(20, statistics.Games);
        Assert.Equal(50, statistics.WinRate.GetValueOrDefault(), 3);
    }
}
