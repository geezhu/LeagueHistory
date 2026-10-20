using League.Models;

namespace League.Tests.Models;

/// <summary>
/// The marker put on a suspected premade: its badge, the label and the explanation.
///
/// The wording is deliberately careful - "suspected", "cannot confirm", and a line saying why an unmarked player may
/// be unmarked - because the mark is an inference from shared games, not a fact about who queued together. The tests
/// pin it word for word so a future rewording is a deliberate act rather than a drift.
/// </summary>
public class OverlayPartyMarkerTests
{
    [Theory]
    [InlineData(2, "2人")]
    [InlineData(3, "3人")]
    [InlineData(5, "5人")]
    public void Badge_CountsTheGroup(int size, string expected)
    {
        Assert.Equal(expected, new OverlayPartyMarker(size, 0).Badge);
    }

    [Theory]
    [InlineData(2, "疑似2人组")]
    [InlineData(5, "疑似5人组")]
    public void Label_SaysItIsOnlyASuspicion(int size, string expected)
    {
        Assert.Equal(expected, new OverlayPartyMarker(size, 0).Label);
    }

    [Fact]
    public void Description_StartsWithTheLabelAndExplainsTheInference()
    {
        // The three sentences are the whole point: what the mark is, what it is based on, and why someone may lack it.
        Assert.Equal(
            "疑似3人组。\n根据近期至少两场同队战绩的玩家关联推测，不能确认当前实际组队。\n未标记可能是战绩不足或身份未公开。",
            new OverlayPartyMarker(3, 0).Description
        );
    }

    [Fact]
    public void Description_TheNumberOfPeopleFollowsTheSize()
    {
        Assert.StartsWith("疑似4人组。", new OverlayPartyMarker(4, 0).Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Color_IsCarriedAsGiven()
    {
        // The colour only decides which of the palette's entries the badge is drawn with.
        Assert.Equal(0x82baff, new OverlayPartyMarker(3, 0x82baff).Color);
    }
}
