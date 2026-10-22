using League.Models;

namespace League.Tests.Models;

/// <summary>
/// The short rank text on the player card. The query layer writes "璀璨钻石 II(56)": the epithet goes, the division
/// stays, and the league points follow a separator so they do not read as part of the division. Master and above have
/// no division. Unranked queues come back empty so the card can leave them out; placeholders pass through unchanged.
/// </summary>
public class RankTextTests
{
    [Theory]
    [InlineData("璀璨钻石 II(56)", "钻石 II·56")]
    [InlineData("坚韧黑铁 IV(0)", "黑铁 IV·0")]
    [InlineData("流光翡翠 III(40)", "翡翠 III·40")]
    [InlineData("超凡大师(128)", "大师·128")]
    [InlineData("最强王者(1503)", "王者·1503")]
    public void Compact_FormattedRank_DropsTheEpithetAndKeepsTheDivisionAndPoints(string rank, string expected)
    {
        Assert.Equal(expected, RankText.Compact(rank));
    }

    [Fact]
    public void Compact_RankWithoutPoints_KeepsTheTierAndDivision()
    {
        Assert.Equal("铂金 II", RankText.Compact("华贵铂金 II"));
    }

    [Theory]
    [InlineData("未定级(0)")]
    [InlineData("未定级")]
    [InlineData("未评级")]
    [InlineData("未知")]
    [InlineData("")]
    [InlineData(null)]
    public void Compact_UnrankedQueue_IsEmpty(string? rank)
    {
        Assert.Equal("", RankText.Compact(rank));
    }

    [Theory]
    [InlineData("隐藏")]
    [InlineData("加载中...")]
    public void Compact_Placeholder_PassesThroughUnchanged(string rank)
    {
        Assert.Equal(rank, RankText.Compact(rank));
    }
}
