using League.Models;

namespace League.Tests.Models;

/// <summary>
/// Which queue a recommendation should be drawn from, given the queue the game is in and the mode it reports.
///
/// Two ARAM queues are kept as they are, because they are different sources with different statistics. Everything else
/// is decided by the mode the game reports: "KIWI" is ARAM: Mayhem and "ARAM" is the ordinary one. A game in some other
/// mode has no recommendation queue at all, which is what zero means here.
/// </summary>
public class LiveMatchModeTests
{
    private static LiveMatchSnapshot Snapshot(string mode) => new(mode, 11, 0, []);

    [Theory]
    [InlineData(450)]
    [InlineData(2400)]
    public void RecommendationQueue_AnAramQueue_IsItself(int queue)
    {
        // The two ARAM queues are their own sources, whatever the live game says.
        Assert.Equal(queue, LiveMatchMode.RecommendationQueue(queue, Snapshot("ARAM")));
        Assert.Equal(queue, LiveMatchMode.RecommendationQueue(queue, Snapshot("KIWI")));
    }

    [Theory]
    [InlineData(450)]
    [InlineData(2400)]
    public void RecommendationQueue_AnAramQueue_StaysItselfEvenWhenTheModeDisagrees(int queue)
    {
        // A queue number is the stronger statement: it says which game this is.
        Assert.Equal(queue, LiveMatchMode.RecommendationQueue(queue, Snapshot("CLASSIC")));
    }

    [Fact]
    public void RecommendationQueue_MayhemsMode_IsTheMayhemQueue()
    {
        Assert.Equal(2400, LiveMatchMode.RecommendationQueue(420, Snapshot("KIWI")));
    }

    [Fact]
    public void RecommendationQueue_AransMode_IsTheAramQueue()
    {
        Assert.Equal(450, LiveMatchMode.RecommendationQueue(420, Snapshot("ARAM")));
    }

    [Theory]
    [InlineData("ARAM")]
    [InlineData("aram")]
    [InlineData("Aram")]
    public void RecommendationQueue_TheModeIsMatchedWhateverItsCase(string mode)
    {
        Assert.Equal(450, LiveMatchMode.RecommendationQueue(420, Snapshot(mode)));
    }

    [Theory]
    [InlineData("CLASSIC")]
    [InlineData("URF")]
    [InlineData("")]
    public void RecommendationQueue_AnotherMode_HasNoRecommendationQueue(string mode)
    {
        // Zero is "none"; a guess here would draw the wrong statistics.
        Assert.Equal(0, LiveMatchMode.RecommendationQueue(420, Snapshot(mode)));
    }

    [Fact]
    public void RecommendationQueue_AnotherMode_DoesNotBecomeAramJustBecauseTheQueueIsOdd()
    {
        // The queue is only consulted for the two ARAM queues; for anything else the mode decides alone.
        Assert.Equal(0, LiveMatchMode.RecommendationQueue(440, Snapshot("CLASSIC")));
    }
}
