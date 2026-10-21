using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The mode name the profile overview and the history list show for a queue. Custom rooms have queues of their own;
/// every other queue defers to <c>MatchParserSgp.QueueModeName</c>, and unknown ones keep their number. The expected
/// values come from the checks in Tests/StatisticsChecks/GameQueueChecks.cs.
/// </summary>
public class CareerStatisticsTests
{
    [Theory]
    [InlineData(420, "单双排")]
    [InlineData(440, "灵活排位")]
    [InlineData(450, "极地大乱斗")]
    [InlineData(2400, "海克斯大乱斗")]
    [InlineData(400, "匹配")]
    [InlineData(430, "匹配")]
    [InlineData(1700, "斗魂竞技场")]
    [InlineData(3270, "自定义海克斯大乱斗")]
    [InlineData(3100, "自定义峡谷")]
    [InlineData(3140, "训练模式")]
    [InlineData(0, "自定义")]
    public void QueueName_QueueWithItsOwnName_ReturnsThatName(int queue, string expected)
    {
        string name = CareerStatistics.QueueName(queue);

        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(1300, "极限闪击")]
    [InlineData(2450, "经典海克斯乱斗")]
    [InlineData(480, "快速模式")]
    public void QueueName_OtherMatchmadeQueue_UsesTheHistoryListsName(int queue, string expected)
    {
        string name = CareerStatistics.QueueName(queue);

        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(-1, "模式 -1")]
    [InlineData(987654, "模式 987654")]
    public void QueueName_QueueOutsideTheTable_KeepsItsNumber(int queue, string expected)
    {
        string name = CareerStatistics.QueueName(queue);

        Assert.Equal(expected, name);
    }

    // ---- where the records are ----

    private static JObject Game(long id = 1, string puuid = "p", bool win = true, bool remake = false)
    {
        var game = new JObject
        {
            ["gameId"] = id,
            ["gameCreation"] = 1_000L,
            ["queueId"] = 420,
            ["participants"] = new JArray(
                new JObject
                {
                    ["puuid"] = puuid,
                    ["championId"] = 1,
                    ["stats"] = new JObject
                    {
                        ["win"] = win,
                        ["kills"] = 5,
                        ["deaths"] = 2,
                        ["assists"] = 7,
                    },
                }
            ),
        };
        if (remake)
        {
            game["gameEndedInEarlySurrender"] = true;
        }

        return game;
    }

    private static CareerStatistics Parse(params JObject[] games) => CareerStatistics.Parse(new JArray(games), "p");

    [Fact]
    public void Records_AnArray_IsItsOwnList()
    {
        // The client returns the list bare on some endpoints.
        JArray records = new JArray(Game());

        Assert.Same(records, CareerStatistics.Records(records));
    }

    [Fact]
    public void Records_AnObjectHoldingGames_IsThatList()
    {
        JArray inner = new JArray(Game());
        var wrapper = new JObject { ["games"] = inner };

        Assert.Same(inner, CareerStatistics.Records(wrapper));
    }

    [Fact]
    public void Records_AnObjectHoldingAnObjectOfGames_IsTheNestedList()
    {
        // The paged endpoint wraps them twice; the outer "games" is an object rather than an array.
        JArray inner = new JArray(Game());
        var wrapper = new JObject { ["games"] = new JObject { ["games"] = inner } };

        Assert.Same(inner, CareerStatistics.Records(wrapper));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData("games")]
    public void Records_AnythingElse_IsNothing(JToken? token)
    {
        Assert.Null(CareerStatistics.Records(token));
    }

    // ---- what counts, what is skipped, what is unreadable ----

    [Fact]
    public void Parse_AWellFormedGame_IsCounted()
    {
        CareerStatistics statistics = Parse(Game(id: 7));

        CareerGame game = Assert.Single(statistics.Games);
        Assert.Equal(7, game.Id);
        Assert.Equal(420, game.Queue);
        Assert.Equal(1, statistics.Wins);
        Assert.Equal(0, statistics.Unrecognized);
        Assert.Equal(0, statistics.ExcludedRemakes);
    }

    [Fact]
    public void Parse_TheSameGameTwice_IsCountedOnce()
    {
        // Overlapping pages both report it; it is one game, and it is not an error.
        CareerStatistics statistics = Parse(Game(id: 7), Game(id: 7));

        Assert.Single(statistics.Games);
        Assert.Equal(0, statistics.Unrecognized);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Parse_AGameWithoutARealId_IsSkippedRatherThanCalledUnreadable(long id)
    {
        // Nothing can be done with an id of zero, and it is not something the reader has to be told about.
        CareerStatistics statistics = Parse(Game(id: id));

        Assert.Empty(statistics.Games);
        Assert.Equal(0, statistics.Unrecognized);
    }

    [Fact]
    public void Parse_AParticipantWithoutAWinFlag_IsUnreadable()
    {
        // The win flag is the one thing every row needs; without it the game cannot be counted either way.
        JObject game = Game();
        ((JObject)((JArray)game["participants"]!)[0]!["stats"]!).Remove("win");

        CareerStatistics statistics = Parse(game);

        Assert.Empty(statistics.Games);
        Assert.Equal(1, statistics.Unrecognized);
    }

    [Fact]
    public void Parse_ARemake_IsExcludedOnItsOwnTally()
    {
        // A remake happened but does not count towards a career, so it is neither counted nor called unreadable.
        CareerStatistics statistics = Parse(Game(id: 7, remake: true));

        Assert.Empty(statistics.Games);
        Assert.Equal(1, statistics.ExcludedRemakes);
        Assert.Equal(0, statistics.Unrecognized);
    }

    [Fact]
    public void Parse_ARemakeWhenAskedFor_IsCounted()
    {
        CareerStatistics statistics = CareerStatistics.Parse(
            new JArray(Game(id: 7, remake: true)),
            "p",
            includeRemakes: true
        );

        Assert.Single(statistics.Games);
        Assert.Equal(0, statistics.ExcludedRemakes);
    }

    [Fact]
    public void Parse_ARemakeAndAnOrdinaryGame_OnlyTheOrdinaryOneCounts()
    {
        CareerStatistics statistics = Parse(Game(id: 7), Game(id: 8, remake: true), Game(id: 9));

        Assert.Equal([7L, 9L], statistics.Games.Select(g => g.Id));
        Assert.Equal(1, statistics.ExcludedRemakes);
    }
}
