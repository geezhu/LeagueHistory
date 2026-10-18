using League.Models;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The drawn lane bar: where the structures sit, which waves are walking it and what the structures say about where
/// the lane probably is. The expected values come from the progress checks in
/// Tests/StatisticsChecks/MinionWaveChecks.cs.
/// </summary>
public class LaneProgressEstimatorTests
{
    [Fact]
    public void Progress_SummonersRiftMid_RunsNexusToNexus()
    {
        var mid = Mid(LiveMatchFixture.RiftGame());

        Assert.Equal(16451, mid.Length, 1);
        Assert.Equal(0, mid.Spawn);
        Assert.Equal(1, mid.EnemySpawn);
        Assert.Equal(12, mid.Markers.Length);
    }

    [Fact]
    public void Progress_MidMarkers_RunFromTheOwnBaseToTheEnemyBase()
    {
        var mid = Mid(LiveMatchFixture.RiftGame());

        Assert.Equal("我方大水晶", mid.Markers[0].Label);
        Assert.Equal("我方门牙塔（剩 2 座）", mid.Markers[1].Label);
        Assert.Equal("我方小水晶", mid.Markers[2].Label);
        Assert.Equal("敌方大水晶", mid.Markers[^1].Label);
    }

    [Fact]
    public void Progress_FallenOwnOuterTurret_IsMarkedOnTheNexusToNexusBar()
    {
        var mid = Mid(LiveMatchFixture.RiftGame());

        var outer = mid.Markers.Single(marker => marker.Label == "我方一塔");

        Assert.False(outer.Alive);
        Assert.Equal((4113 + 1717 + 560) / 16451.0, outer.At, 4);
    }

    [Fact]
    public void Progress_TwoWalkingAndFourUpcomingWaves_CarrySpeedAndCannonFlags()
    {
        var mid = Mid(LiveMatchFixture.RiftGame());

        Assert.Equal([27, 28, 29, 30, 31, 32], mid.Waves.Select(wave => wave.Number));
        Assert.Equal((375.0, 0.0, false), (mid.Waves[1].Speed, mid.Waves[1].Bonus, mid.Waves[1].Cannon));
        Assert.True(mid.Waves[2].Cannon, "The 14:00 cannon wave is the third one on the bar.");
    }

    [Fact]
    public void Progress_OwnTeamLostMore_ReadsTheLaneAsSittingOnTheOwnHalf()
    {
        var mid = Mid(LiveMatchFixture.RiftGame());

        Assert.Contains("我方半场", mid.Hint);
    }

    [Fact]
    public void Progress_FallenEnemyInhibitor_ReadsTheLaneAsPushedIntoTheirBase()
    {
        var bot = Progress(LiveMatchFixture.RiftGame(), "下路");

        Assert.Contains("敌方高地", bot.Hint);
    }

    [Fact]
    public void Progress_UntouchedLane_SaysItCannotTell()
    {
        var top = Progress(LiveMatchFixture.RiftGame(), "上路");

        Assert.Contains("无法", top.Hint);
    }

    [Fact]
    public void Progress_EnemyTeamChaos_PutsItsOwnBaseOnTheLeft()
    {
        var mid = Progress(LiveMatchFixture.WaveGame("CLASSIC", 11, 75, "CHAOS"), "中路");

        Assert.Equal("我方小水晶", mid.Markers[2].Label);
        Assert.Equal((1710 + 560) / 16451.0, mid.Markers[2].At, 4);
        Assert.Equal((1710 + 560 + 651) / 16451.0, mid.Markers[3].At, 4);
    }

    [Fact]
    public void Progress_EarlySideLaneWaves_CarryTheirSpeedBonusAndMidWavesDoNot()
    {
        var forecast = MinionWaveForecaster.Calculate(LiveMatchFixture.WaveGame("CLASSIC", 11, 75, "CHAOS"), 0)!;

        var top = forecast.Lanes.Single(lane => lane.Name == "上路").Progress!;
        var mid = forecast.Lanes.Single(lane => lane.Name == "中路").Progress!;

        Assert.DoesNotContain(top.Waves, wave => wave.Number >= 2 && wave.Bonus == 0);
        Assert.DoesNotContain(mid.Waves, wave => wave.Bonus != 0);
    }

    [Fact]
    public void Progress_OwnTeamFirstTurretFallen_StillMarksTheNexusTurrets()
    {
        var mid = Mid(LiveMatchFixture.RiftGame());

        var nexus = mid.Markers.Single(marker => marker.Label == "我方门牙塔（剩 2 座）");

        Assert.True(nexus.Alive, "Nothing has hit the nexus turrets yet.");
    }

    private static LaneProgress Mid(LiveMatchSnapshot snapshot) => Progress(snapshot, "中路");

    private static LaneProgress Progress(LiveMatchSnapshot snapshot, string lane) =>
        MinionWaveForecaster.Calculate(snapshot, 0)!.Lanes.Single(entry => entry.Name == lane).Progress!;
}
