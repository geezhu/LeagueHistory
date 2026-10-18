using League.Models;

namespace League.Tests.Models;

/// <summary>
/// The wave tables themselves: staged values, rotating compositions, upgrade counts, minion bounty and speed. The
/// expected values come from the checks in Tests/StatisticsChecks/MinionWaveChecks.cs.
/// </summary>
public class MinionWaveRulesTests
{
    private static MinionWaveRules Rift => MinionWaveRules.SummonersRift;

    [Theory]
    [InlineData(0, 30.0)]
    [InlineData(839, 30.0)]
    [InlineData(840, 25.0)]
    [InlineData(1799, 25.0)]
    [InlineData(1800, 20.0)]
    public void Stage_GameTime_ReturnsTheLastStageThatStarted(double time, double expected)
    {
        double interval = MinionWaveRules.Stage(Rift.Intervals, time);

        Assert.Equal(expected, interval);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    [InlineData(6, 1)]
    public void Rotate_WaveNumber_IndexesThePattern(int wave, int expected)
    {
        int siege = MinionWaveRules.Rotate([0, 0, 1], wave);

        Assert.Equal(expected, siege);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(29, 0)]
    [InlineData(30, 1)]
    [InlineData(119, 1)]
    [InlineData(120, 2)]
    public void Upgrades_GameTime_CountsIntervalsFromTheFirstWave(double time, int expected)
    {
        int upgrades = Rift.Upgrades(time);

        Assert.Equal(expected, upgrades);
    }

    [Theory]
    [InlineData("CLASSIC", 11, "召唤师峡谷")]
    [InlineData("SWIFTPLAY", 11, "召唤师峡谷")]
    [InlineData("URF", 11, "无限火力")]
    [InlineData("ARURF", 11, "无限火力")]
    [InlineData("KIWI", 12, "极地大乱斗")]
    [InlineData("ARAM", 12, "极地大乱斗")]
    [InlineData("aram", 12, "极地大乱斗")]
    public void For_ModeAndMap_ReturnsThatMapsRules(string mode, int map, string expected)
    {
        var rules = MinionWaveRules.For(mode, map);

        Assert.Equal(expected, rules!.Name);
    }

    [Theory]
    [InlineData("CHERRY", 30)]
    [InlineData("TUTORIAL", 11)]
    [InlineData("SWIFTPLAY", 30)]
    public void For_ArenaOrUnknownMode_ReturnsNull(string mode, int map)
    {
        var rules = MinionWaveRules.For(mode, map);

        Assert.Null(rules);
    }

    [Theory]
    [InlineData(30, 50.0)]
    [InlineData(840, 59.0)]
    [InlineData(1500, 66.0)]
    [InlineData(1800, 69.0)]
    public void SiegeGold_GameTime_FollowsTheBountyGrowth(double time, double expected)
    {
        double gold = Rift.SiegeGold.At(Rift.Upgrades(time));

        Assert.Equal(expected, gold);
    }

    [Fact]
    public void MeleeGold_AfterThirtyMinutes_DoesNotGrow()
    {
        double gold = Rift.MeleeGold.At(Rift.Upgrades(1800));

        Assert.Equal(20, gold);
    }

    [Fact]
    public void MeleeGold_OnTheHowlingAbyss_StopsAtItsCap()
    {
        double gold = MinionWaveRules.HowlingAbyss.MeleeGold.At(1000);

        Assert.Equal(32, gold);
    }

    [Theory]
    [InlineData(599, 350)]
    [InlineData(600, 375)]
    [InlineData(5000, 450)]
    public void Speed_GameTime_StepsUpToItsCap(double time, double expected)
    {
        double speed = Rift.Speed.At(time);

        Assert.Equal(expected, speed);
    }

    [Theory]
    [InlineData("召唤师峡谷", 300.0, true)]
    [InlineData("无限火力", null, false)]
    public void InhibitorRespawnAndSideLaneBonus_Map_KeepsItsOwnValues(
        string name,
        double? inhibitorRespawn,
        bool sideLaneBonus
    )
    {
        var rules = MinionWaveRules.SummonersRift.Name == name ? Rift : MinionWaveRules.UltraRapidFire;

        Assert.Equal(inhibitorRespawn, rules.InhibitorRespawn);
        Assert.Equal(sideLaneBonus, rules.SideLaneBonus);
    }

    [Fact]
    public void HowlingAbyss_Values_CountTenSuperWavesAndCarryTurretRules()
    {
        var abyss = MinionWaveRules.HowlingAbyss;

        Assert.Equal(10, abyss.SuperWaves);
        Assert.Same(TurretRules.HowlingAbyss, abyss.Turrets);
    }
}
