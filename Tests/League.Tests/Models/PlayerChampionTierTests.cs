using League.Models;
using League.Services;

namespace League.Tests.Models;

/// <summary>
/// The tier line shown for one champion: which lane the OP.GG statistics describe, whether that lane is the
/// popular-lane reference instead of the player's own pick, and the numbers copied out of the matching row. The
/// expected values come from the ModelSelection checks in Tests/StatisticsChecks/PlayerChampionChecks.cs, which
/// this class replaces.
/// </summary>
public class PlayerChampionTierTests
{
    // Support has the higher pick rate, so the popular-lane fallback has to choose it over the first row.
    private static readonly OpggTier[] Lanes =
    {
        new OpggTier(99, "mid", 0.51, 0.1, null, 2, 2),
        new OpggTier(99, "support", 0.53, 0.2, null, 1, 1),
    };

    [Fact]
    public void FromOpgg_UnknownLane_FallsBackToThePopularReferenceLane()
    {
        var tier = PlayerChampionTier.FromOpgg(99, 430, "", "test", Lanes);

        Assert.Equal("support", tier.Position);
        Assert.True(tier.ReferencePosition);
    }

    [Fact]
    public void FromOpgg_UnknownLane_MarksTheRateAsReference()
    {
        var tier = PlayerChampionTier.FromOpgg(99, 430, "", "test", Lanes);

        Assert.Equal("胜 53.0% · 参考辅助", tier.Rate);
    }

    [Theory]
    [InlineData("middle", "mid")]
    [InlineData("mid", "mid")]
    [InlineData("TOP", "top")]
    [InlineData("bottom", "adc")]
    [InlineData("utility", "support")]
    public void FromOpgg_AssignedPosition_MapsToOneLane(string assigned, string expected)
    {
        var tier = PlayerChampionTier.FromOpgg(99, 420, assigned, "test", Lanes);

        Assert.Equal(expected, tier.Position);
    }

    [Fact]
    public void FromOpgg_AssignedLane_WinsOverThePopularLane()
    {
        var tier = PlayerChampionTier.FromOpgg(99, 420, "middle", "test", Lanes);

        Assert.Equal("mid", tier.Position);
        Assert.False(tier.ReferencePosition);
        Assert.Equal("T2", tier.Tier);
        Assert.Equal("胜 51.0% · 中路", tier.Rate);
    }

    [Fact]
    public void FromOpgg_AssignedLaneMissingFromRows_DoesNotSwitchLanes()
    {
        var tier = PlayerChampionTier.FromOpgg(99, 420, "top", "test", Lanes);

        Assert.Equal("top", tier.Position);
        Assert.False(tier.ReferencePosition);
        Assert.Equal("—", tier.Tier);
    }

    [Theory]
    [InlineData(450)]
    [InlineData(900)]
    [InlineData(1200)]
    [InlineData(1300)]
    [InlineData(1700)]
    [InlineData(1710)]
    [InlineData(1900)]
    public void FromOpgg_SpecialMode_UsesItsOwnNonLaneStatistics(int queue)
    {
        var rows = new[] { new OpggTier(99, "none", 0.51, 0.1, null, 2, 2) };

        var tier = PlayerChampionTier.FromOpgg(99, queue, "middle", "test", rows);

        Assert.Equal("none", tier.Position);
        Assert.False(tier.ReferencePosition);
        Assert.Equal("T2", tier.Tier);
        Assert.Equal("胜 51.0%", tier.Rate);
    }

    [Fact]
    public void FromOpgg_MatchingRankedRow_CopiesItsNumbersAndVersion()
    {
        var tier = PlayerChampionTier.FromOpgg(99, 420, "middle", "15.1", Lanes);

        Assert.Equal(420, tier.Queue);
        Assert.Equal(0.51, tier.WinRateValue);
        Assert.Equal(0.1, tier.PickRateValue);
        Assert.Equal("10.0%", tier.PickRate);
        Assert.Equal("15.1", tier.Version);
    }

    [Fact]
    public void FromOpgg_NoMatchingRow_LeavesTheOptionalNumbersUnset()
    {
        var tier = PlayerChampionTier.FromOpgg(99, 420, "top", "15.1", Lanes);

        Assert.Null(tier.WinRateValue);
        Assert.Null(tier.PickRateValue);
        Assert.Equal("—", tier.PickRate);
    }

    [Fact]
    public void PlayerChampionTier_OptionalProperties_DefaultToEmpty()
    {
        var tier = new PlayerChampionTier("T2", "胜 52.0%", "OP.GG", 420, "mid");

        Assert.False(tier.ReferencePosition);
        Assert.Equal("—", tier.PickRate);
        Assert.Null(tier.WinRateValue);
        Assert.Null(tier.PickRateValue);
        Assert.Equal(string.Empty, tier.Version);
    }

    [Theory]
    [InlineData(0, "URF", 900)]
    [InlineData(0, "ARAM", 450)]
    [InlineData(3270, "ARAM", 2400)]
    [InlineData(480, "", 480)]
    [InlineData(0, "KIWI", 2400)]
    [InlineData(-1, "kiwi", 2400)]
    public void NormalizeQueue_ClientQueueAndMode_ReturnsTheStatisticsQueue(int queue, string mode, int expected)
    {
        int normalized = PlayerChampionTier.NormalizeQueue(queue, mode);

        Assert.Equal(expected, normalized);
    }
}
