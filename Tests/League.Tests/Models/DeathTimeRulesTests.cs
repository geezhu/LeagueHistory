using League.Models;

namespace League.Tests.Models;

/// <summary>
/// Death timers by level and game time, one table per mode. The expected values come from the checks in
/// Tests/StatisticsChecks/MinionWaveChecks.cs.
/// </summary>
public class DeathTimeRulesTests
{
    [Theory]
    [InlineData(599, 0)]
    [InlineData(900, 0.05)]
    [InlineData(1800, 0.1775)]
    public void Factor_SummonersRift_AddsTheRateForEachStepThatPassed(double time, double expected)
    {
        double factor = DeathTimeRules.SummonersRift.Factor(time);

        Assert.Equal(expected, factor, 9);
    }

    [Theory]
    [InlineData(18, 1800, 52.5 * 1.1775)]
    [InlineData(18, 3600, 52.5 * 1.5)]
    [InlineData(30, 0, 52.5)]
    [InlineData(1, 0, 10)]
    public void Seconds_SummonersRift_ClampsTheLevelAndCapsTheFactor(int level, double time, double expected)
    {
        double seconds = DeathTimeRules.SummonersRift.Seconds(level, time);

        Assert.Equal(expected, seconds, 9);
    }

    [Theory]
    [InlineData(18, 1200, 40)]
    [InlineData(1, 0, 9)]
    [InlineData(18, 1740, 45)]
    [InlineData(18, 2400, 50)]
    public void Seconds_HowlingAbyss_GrowsTwoAndAHalfPercentAMinute(int level, double time, double expected)
    {
        double seconds = DeathTimeRules.HowlingAbyss.Seconds(level, time);

        Assert.Equal(expected, seconds, 9);
    }

    [Theory]
    [InlineData(1500, 0)]
    [InlineData(1530, 0.1)]
    public void Factor_Swiftplay_StartsAtTwentyFiveMinutes(double time, double expected)
    {
        double factor = DeathTimeRules.Swiftplay.Factor(time);

        Assert.Equal(expected, factor, 9);
    }

    [Theory]
    [InlineData("CLASSIC", 11)]
    [InlineData("PRACTICETOOL", 11)]
    [InlineData("URF", 11)]
    [InlineData("SNOWURF", 11)]
    [InlineData("ONEFORALL", 11)]
    [InlineData("ULTBOOK", 11)]
    [InlineData("SWIFTPLAY", 11)]
    [InlineData("ARAM", 12)]
    [InlineData("KIWI", 12)]
    [InlineData("KINGPORO", 12)]
    public void For_KnownModeAndMap_ReturnsATable(string mode, int map)
    {
        var rules = DeathTimeRules.For(mode, map);

        Assert.NotNull(rules);
    }

    [Theory]
    [InlineData("CHERRY", 30)]
    [InlineData("CLASSIC", 12)]
    [InlineData("ARAM", 11)]
    public void For_UnknownCombination_ReturnsNull(string mode, int map)
    {
        var rules = DeathTimeRules.For(mode, map);

        Assert.Null(rules);
    }

    [Fact]
    public void For_MayhemAndAram_ShareTheHowlingAbyssTable()
    {
        Assert.Same(DeathTimeRules.HowlingAbyss, DeathTimeRules.For("KIWI", 12));
    }
}
