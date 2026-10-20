using League.Models;

namespace League.Tests.Models;

/// <summary>
/// The "how it has been going" panel: the last fifty games, four mode slots, and one KDA figure.
///
/// Three rules shape it. Four of the queues are the same mode to a player - all of them are "经典对战" - so they are
/// merged before anything is counted. Only four mode slots are shown, so a fifth mode has to take a slot: an empty one
/// if there is one, and Hextech ARAM additionally evicts the emptiest when nothing is empty, because it is the one
/// that must not disappear. And the KDA is the usual 3×(K+A)/D, taken from the rift games when there are any and from
/// the ARAM games only when there are none.
/// </summary>
public class RecentOverviewTests
{
    private static long _nextId = 1;

    private static CareerGame Game(
        int queue = 420,
        bool win = true,
        long? created = null,
        int? kills = 5,
        int? deaths = 5,
        int? assists = 5,
        long? id = null
    ) =>
        new(
            Id: id ?? _nextId++,
            Created: created ?? 1_000,
            Queue: queue,
            Champion: 1,
            Win: win,
            Kills: kills,
            Deaths: deaths,
            Assists: assists,
            Damage: 0,
            Gold: 0,
            Penta: null
        );

    private static RecentOverview Build(params CareerGame[] games) => RecentOverview.Build(games);

    // ---- what counts as a matchmade rift game ----

    [Theory]
    [InlineData(400)]
    [InlineData(430)]
    [InlineData(480)]
    [InlineData(490)]
    public void IsMatchmadeRift_TheFourRiftQueues_AreThat(int queue)
    {
        Assert.True(RecentOverview.IsMatchmadeRift(queue));
    }

    [Theory]
    [InlineData(420)]
    [InlineData(450)]
    [InlineData(2400)]
    [InlineData(1700)]
    public void IsMatchmadeRift_AnythingElse_IsNot(int queue)
    {
        Assert.False(RecentOverview.IsMatchmadeRift(queue));
    }

    // ---- the sample ----

    [Fact]
    public void Build_NoGamesAtAll_IsAnEmptyOverview()
    {
        // The panel is drawn anyway, so the four slots exist with nothing in them.
        RecentOverview overview = Build();

        Assert.Equal(0, overview.Sample);
        Assert.Equal([430, 450, 420, 440], overview.Modes.Select(m => m.Queue));
        Assert.All(overview.Modes, mode => Assert.Equal(0, mode.Games));
        Assert.Null(overview.Kda);
    }

    [Fact]
    public void Build_TheSample_IsAtMostFiftyGames()
    {
        CareerGame[] games = [.. Enumerable.Range(0, 51).Select(i => Game(created: i))];

        Assert.Equal(50, Build(games).Sample);
    }

    [Fact]
    public void Build_TheNewestGamesAreKept()
    {
        // The oldest game is put in a mode of its own, so dropping it is visible: it takes the only slot it had.
        CareerGame oldest = Game(queue: 1700, created: 0);
        CareerGame[] rest = [.. Enumerable.Range(1, 50).Select(i => Game(created: i))];

        RecentOverview overview = Build([oldest, .. rest]);

        Assert.Equal(50, overview.Sample);
        Assert.DoesNotContain(overview.Modes, mode => mode.Queue == 1700);
        Assert.Equal(50, overview.Modes.Single(m => m.Queue == 420).Games);
    }

    [Fact]
    public void Build_TheSameGameTwice_CountsOnce()
    {
        // The history is polled; a repeated id is the same game however it arrived.
        Assert.Equal(1, Build(Game(id: 7), Game(id: 7)).Sample);
    }

    // ---- the modes ----

    [Theory]
    [InlineData(400)]
    [InlineData(480)]
    [InlineData(490)]
    public void Build_TheRiftQueues_AreMergedIntoOneSlot(int queue)
    {
        RecentOverview overview = Build(Game(queue));

        Assert.Equal(1, overview.Modes.Single(m => m.Queue == 430).Games);
        Assert.Equal("经典对战", overview.Modes.Single(m => m.Queue == 430).Name);
    }

    [Fact]
    public void Build_EachDefaultMode_KeepsItsOwnSlot()
    {
        RecentOverview overview = Build(Game(430), Game(450), Game(420), Game(440));

        Assert.Equal([430, 450, 420, 440], overview.Modes.Select(m => m.Queue));
        Assert.All(overview.Modes, mode => Assert.Equal(1, mode.Games));
    }

    [Fact]
    public void Build_AWinIsCountedPerMode()
    {
        RecentOverview overview = Build(Game(420, win: true), Game(420, win: false), Game(420, win: true));

        ModeWinRate solo = overview.Modes.Single(m => m.Queue == 420);
        Assert.Equal(3, solo.Games);
        Assert.Equal(2, solo.Wins);
    }

    [Fact]
    public void Build_AFifthMode_TakesAnEmptySlot()
    {
        // Only four are shown; a mode the player has played has to appear somewhere.
        RecentOverview overview = Build(Game(420), Game(1700));

        Assert.Contains(overview.Modes, mode => mode.Queue == 1700);
    }

    [Fact]
    public void Build_HextechAram_PrefersTheEmptyAramSlot()
    {
        // It is a kind of ARAM, so it takes the ARAM slot when that one is empty - which means plain ARAM is no
        // longer one of the four shown.
        RecentOverview overview = Build(Game(420), Game(2400));

        Assert.DoesNotContain(overview.Modes, mode => mode.Queue == 450);
        Assert.Equal(2400, overview.Modes[1].Queue);
    }

    [Fact]
    public void Build_HextechAram_EvictsTheEmptiestSlotWhenNoneIsEmpty()
    {
        // The comment in the source says it must remain visible; the price is one of the other four.
        RecentOverview overview = Build(
            Game(420),
            Game(420),
            Game(420),
            Game(430),
            Game(430),
            Game(440),
            Game(450),
            Game(2400)
        );

        Assert.Contains(overview.Modes, mode => mode.Queue == 2400);
        Assert.Equal(4, overview.Modes.Length);
    }

    [Fact]
    public void Build_TwoExtraModes_EachTakeASlot()
    {
        RecentOverview overview = Build(Game(420), Game(1700), Game(3100));

        Assert.Contains(overview.Modes, mode => mode.Queue == 1700);
        Assert.Contains(overview.Modes, mode => mode.Queue == 3100);
    }

    [Fact]
    public void Build_TheSlots_AreAlwaysFour()
    {
        RecentOverview overview = Build([.. Enumerable.Range(0, 10).Select(i => Game(1000 + i))]);

        Assert.Equal(4, overview.Modes.Length);
    }

    // ---- the KDA ----

    [Fact]
    public void Build_TheKda_IsThreeTimesKillsAndAssistsOverDeaths()
    {
        // Traced: two games, kills and assists 10 + 5 = 15, deaths 5, so 3 × 15 / 5 = 9.
        RecentOverview overview = Build(
            Game(420, kills: 6, deaths: 3, assists: 2),
            Game(420, kills: 4, deaths: 2, assists: 3)
        );

        Assert.Equal(2, overview.KdaGames);
        Assert.Equal(9, overview.Kda.GetValueOrDefault(), 3);
    }

    [Fact]
    public void Build_NoDeathsAtAll_DoesNotDivideByZero()
    {
        // The denominator is floored at one, so a flawless run reads as a large number rather than infinity.
        RecentOverview overview = Build(Game(420, kills: 1, deaths: 0, assists: 0));

        Assert.Equal(3, overview.Kda.GetValueOrDefault(), 3);
    }

    [Fact]
    public void Build_AGameWithoutACompleteScore_LeavesTheKdaUnknown()
    {
        // A partial sum would understate it; the panel shows a dash instead.
        RecentOverview overview = Build(Game(420, kills: 5), Game(420, deaths: null));

        Assert.Null(overview.Kda);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(430)]
    [InlineData(480)]
    [InlineData(490)]
    public void Build_TheRiftQueues_FeedTheKda(int queue)
    {
        Assert.Equal(1, Build(Game(queue, kills: 1, deaths: 1, assists: 0)).KdaGames);
    }

    [Theory]
    [InlineData(420)]
    [InlineData(440)]
    public void Build_TheRankedQueues_FeedTheKdaToo(int queue)
    {
        Assert.Equal(1, Build(Game(queue, kills: 1, deaths: 1, assists: 0)).KdaGames);
    }

    [Fact]
    public void Build_OnlyAramGames_FallBackToTheAramKda()
    {
        // With no rift game there is nothing else to compute from, and the panel says so.
        RecentOverview overview = Build(Game(450, kills: 1, deaths: 1, assists: 2));

        Assert.True(overview.UsesAramKda);
        Assert.Equal(1, overview.KdaGames);
        Assert.Equal(9, overview.Kda.GetValueOrDefault(), 3);
    }

    [Fact]
    public void Build_AramGamesWithARiftGame_AreLeftOutOfTheKda()
    {
        // The rift games are what the figure is about when there are any.
        RecentOverview overview = Build(Game(420), Game(450));

        Assert.False(overview.UsesAramKda);
        Assert.Equal(1, overview.KdaGames);
    }

    [Fact]
    public void Build_OnlyACustomGame_HasNoKdaAtAll()
    {
        // A custom room is neither rift nor ARAM, so there is nothing to average.
        RecentOverview overview = Build(Game(3100));

        Assert.False(overview.UsesAramKda);
        Assert.Equal(0, overview.KdaGames);
        Assert.Null(overview.Kda);
    }

    // ---- the rate on a mode ----

    [Theory]
    [InlineData(3, 2, 0.667)]
    [InlineData(4, 2, 0.5)]
    [InlineData(1, 1, 1.0)]
    public void Rate_GamesPlayed_IsTheShareWon(int games, int wins, double expected)
    {
        Assert.Equal(expected, new ModeWinRate(420, "单双排", games, wins).Rate!.Value, 3);
    }

    [Fact]
    public void Rate_NoGames_IsNothingRatherThanZero()
    {
        // A mode the player has not played has no rate; zero would read as "never won".
        Assert.Null(new ModeWinRate(420, "单双排", 0, 0).Rate);
    }
}
