using League.Parsers;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Parsers;

/// <summary>
/// The history filter a queue turns into: matchmade queues have one, custom games resolve to a mode first. The
/// expected values come from the checks in Tests/StatisticsChecks/GameQueueChecks.cs.
/// </summary>
public class PlayerMatchDataFetcherTests
{
    [Theory]
    [InlineData("420", "q_420")]
    [InlineData("440", "q_440")]
    [InlineData("400", "q_400")]
    [InlineData("430", "q_430")]
    [InlineData("450", "q_450")]
    [InlineData("480", "q_480")]
    [InlineData("490", "q_490")]
    [InlineData("2400", "q_2400")]
    public void GetQueueFilter_KnownQueue_ReturnsItsFilter(string queueId, string expected)
    {
        string filter = PlayerMatchDataFetcher.GetQueueFilter(queueId);

        Assert.Equal(expected, filter);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("3100")]
    [InlineData("420 ")]
    public void GetQueueFilter_QueueWithoutAFilter_ReturnsEmpty(string queueId)
    {
        string filter = PlayerMatchDataFetcher.GetQueueFilter(queueId);

        Assert.Equal(string.Empty, filter);
    }

    [Fact]
    public void GetQueueFilter_CustomMayhem_IsTheMayhemFilter()
    {
        string queue = GameQueues.ForHistory(GameData(-1, "KIWI"));

        string filter = PlayerMatchDataFetcher.GetQueueFilter(queue);

        Assert.Equal("q_2400", filter);
    }

    [Fact]
    public void GetQueueFilter_CustomSummonersRift_KeepsAllRecentGames()
    {
        string queue = GameQueues.ForHistory(GameData(-1, "CLASSIC"));

        string filter = PlayerMatchDataFetcher.GetQueueFilter(queue);

        Assert.Empty(filter);
    }

    private static JObject GameData(int id, string gameMode) =>
        new()
        {
            ["queue"] = new JObject { ["id"] = id, ["gameMode"] = gameMode },
        };
}
