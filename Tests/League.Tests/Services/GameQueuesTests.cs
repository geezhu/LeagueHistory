using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// Custom games report queue -1 from the client, so the game mode decides which queue's statistics apply. The
/// expected values come from the checks in Tests/StatisticsChecks/GameQueueChecks.cs, which this class replaces.
/// </summary>
public class GameQueuesTests
{
    [Theory]
    [InlineData(-1, "KIWI", 2400)]
    [InlineData(-1, "ARAM", 450)]
    [InlineData(3270, "KIWI", 2400)]
    [InlineData(420, "CLASSIC", 420)]
    public void Effective_GameData_ReturnsStatisticsQueue(int id, string gameMode, int expected)
    {
        var gameData = Data(id, gameMode);

        int queue = GameQueues.Effective(gameData);

        Assert.Equal(expected, queue);
    }

    [Fact]
    public void Effective_NoGameData_ReturnsZero()
    {
        int queue = GameQueues.Effective(null);

        Assert.Equal(0, queue);
    }

    [Theory]
    [InlineData(-1, "KIWI", "2400")]
    [InlineData(-1, "ARAM", "450")]
    [InlineData(3270, "KIWI", "2400")]
    public void ForHistory_ModeWithStatisticsQueue_ReturnsStatisticsQueue(int id, string gameMode, string expected)
    {
        var gameData = Data(id, gameMode);

        string queue = GameQueues.ForHistory(gameData);

        Assert.Equal(expected, queue);
    }

    [Fact]
    public void ForHistory_CustomRift_KeepsTheClientValue()
    {
        string queue = GameQueues.ForHistory(Data(-1, "CLASSIC"));

        Assert.Equal("-1", queue);
    }

    [Fact]
    public void ForHistory_NoQueue_ReturnsEmpty()
    {
        string queue = GameQueues.ForHistory(null);

        Assert.Equal(string.Empty, queue);
    }

    private static JObject Data(int id, string gameMode) =>
        new()
        {
            ["queue"] = new JObject { ["id"] = id, ["gameMode"] = gameMode },
        };
}
