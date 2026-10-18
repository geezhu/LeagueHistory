using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The active player's own panel, the only place the live client reports champion stats. The values come from the
/// checks in Tests/StatisticsChecks/DeathTimingChecks.cs and Tests/StatisticsChecks/CooldownChecks.cs.
/// </summary>
public class LiveActivePlayerTests
{
    [Fact]
    public void Parse_GoldAndHealth_ReadsBothNumbers()
    {
        var data = new JObject
        {
            ["currentGold"] = 1234.5,
            ["championStats"] = new JObject { ["maxHealth"] = 1000, ["currentHealth"] = 250 },
        };

        var active = LiveActivePlayer.Parse(data);

        Assert.Equal(1234.5, active!.CurrentGold);
        Assert.Equal(250, active.Stats?.CurrentHealth);
    }

    [Fact]
    public void Parse_NoActivePlayer_ReturnsNull()
    {
        var active = LiveActivePlayer.Parse(null);

        Assert.Null(active);
    }

    [Fact]
    public void Parse_AbilityLevels_KeepsTheFourSlotsAndDropsMalformedOnes()
    {
        var data = JObject.Parse(
            """{"abilities":{"Passive":{},"Q":{"abilityLevel":3},"W":{"abilityLevel":"x"},"E":{"abilityLevel":4}}}"""
        );

        var active = LiveActivePlayer.Parse(data);

        Assert.Equal(2, active!.AbilityRanks.Count);
        Assert.Equal(3, active.AbilityRanks["Q"]);
        Assert.Equal(4, active.AbilityRanks["E"]);
    }

    [Theory]
    [InlineData("""{"championStats":{"maxHealth":0}}""")]
    [InlineData("""{"championStats":{"maxHealth":-1}}""")]
    [InlineData("""{"championStats":{}}""")]
    [InlineData("{}")]
    public void Parse_NoUsableMaxHealth_LeavesTheStatsNull(string json)
    {
        var active = LiveActivePlayer.Parse(JObject.Parse(json));

        Assert.Null(active!.Stats);
    }

    [Fact]
    public void Parse_RegenerationPerSecond_ReadsPerFiveSeconds()
    {
        var data = JObject.Parse(
            """{"championStats":{"maxHealth":653,"healthRegenRate":1.7,"resourceRegenRate":1.6}}"""
        );

        var stats = LiveActivePlayer.Parse(data)!.Stats!;

        Assert.Equal(8.5, stats.HealthRegen, 6);
        Assert.Equal(8.0, stats.ResourceRegen, 6);
    }

    [Fact]
    public void Parse_CritStats_KeepsChanceAndDamageAsReported()
    {
        var data = JObject.Parse("""{"championStats":{"maxHealth":653,"critChance":0.25,"critDamage":175}}""");

        var stats = LiveActivePlayer.Parse(data)!.Stats!;

        Assert.Equal((0.25, 175.0), (stats.CritChance, stats.CritDamage));
    }

    [Fact]
    public void Parse_NegativeGold_ClampsToZero()
    {
        var data = new JObject { ["currentGold"] = -5.0 };

        var active = LiveActivePlayer.Parse(data);

        Assert.Equal(0, active!.CurrentGold);
    }
}
