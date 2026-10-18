using League.Models;

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
}
