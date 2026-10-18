using League.Models;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The turret stats and rules the wave page shows on each structure of the lane bar: full health at the current
/// game time, the fortification window after the outer turret falls, and which structures get no numbers. The
/// expected values come from the checks in Tests/StatisticsChecks/TurretChecks.cs.
/// </summary>
public class TurretInsightTests
{
    private static readonly LiveMatchEvent EnemyOuterFell = LiveMatchFixture.Structure(
        "TurretKilled",
        500,
        "Turret_TChaos_L1_P3_671196429_0"
    );

    [Fact]
    public void Annotate_AramInnerTurretAfterTheOuterFell_FortifiesForSixtySeconds()
    {
        var inner = Inner(LiveMatchFixture.SinglePlayer("ARAM", 530, null, EnemyOuterFell));

        Assert.Equal(560, inner.FortifiedUntil);
        Assert.Equal(0.3, inner.Fortification);
    }

    [Fact]
    public void Annotate_MayhemInnerTurret_FortifiesLongerAndHarder()
    {
        var inner = Inner(LiveMatchFixture.SinglePlayer("KIWI", 530, null, EnemyOuterFell));

        Assert.Equal(570, inner.FortifiedUntil);
        Assert.Equal(0.4, inner.Fortification);
    }

    [Theory]
    [InlineData("ARAM", "一塔被破后 60 秒内受到伤害 −30%")]
    [InlineData("KIWI", "一塔被破后 70 秒内受到伤害 −40%")]
    public void Annotate_FortifiedInnerTurret_DescribesTheReduction(string mode, string expected)
    {
        var inner = Inner(LiveMatchFixture.SinglePlayer(mode, 530, null, EnemyOuterFell));

        Assert.Contains(expected, inner.Details);
    }

    [Fact]
    public void Annotate_FortificationWindowPassed_LeavesNoWindow()
    {
        var inner = Inner(LiveMatchFixture.SinglePlayer("ARAM", 561, null, EnemyOuterFell));

        Assert.Null(inner.FortifiedUntil);
    }

    [Fact]
    public void Annotate_OuterTurretStillStanding_LeavesTheInnerWithoutAFortificationWindow()
    {
        var inner = Inner(LiveMatchFixture.SinglePlayer("ARAM", 530, null));

        Assert.Null(inner.FortifiedUntil);
    }

    [Fact]
    public void Annotate_FallenTurret_HasNoFortificationWindow()
    {
        var outer = Markers(LiveMatchFixture.SinglePlayer("ARAM", 530, null, EnemyOuterFell))
            .Single(marker => !marker.Ally && marker.Kind == LaneStructureKind.Outer);

        Assert.False(outer.Alive);
        Assert.Null(outer.FortifiedUntil);
    }

    [Fact]
    public void Annotate_InnerTurret_NamesItsFullHealthAtTheCurrentTime()
    {
        var inner = Inner(LiveMatchFixture.SinglePlayer("ARAM", 530, null, EnemyOuterFell));

        Assert.Contains("满血 3900 · 攻击 301 · 护甲与魔抗 84", inner.Details);
    }

    [Fact]
    public void Annotate_TurretsCarryTheirStats_ButInhibitorsAndNexusDoNot()
    {
        var markers = Markers(LiveMatchFixture.SinglePlayer("ARAM", 530, null, EnemyOuterFell));

        Assert.DoesNotContain(markers.Where(IsTurret), marker => marker.Details.Length == 0);
        Assert.DoesNotContain(markers.Where(marker => !IsTurret(marker)), marker => marker.Details.Length > 0);
    }

    [Fact]
    public void Annotate_OuterTurretBeforeTwelveMinutes_NamesTheResistanceFalloff()
    {
        var outer = Markers(LiveMatchFixture.SinglePlayer("ARAM", 300, null))
            .First(marker => marker.Ally && marker.Kind == LaneStructureKind.Outer);

        Assert.Contains("12:00 起护甲与魔抗降到 40", outer.Details);
    }

    [Fact]
    public void Annotate_OuterTurretAfterTwelveMinutes_DropsTheFalloffLine()
    {
        var outer = Markers(LiveMatchFixture.SinglePlayer("ARAM", 720, null))
            .First(marker => marker.Ally && marker.Kind == LaneStructureKind.Outer);

        Assert.DoesNotContain(outer.Details, line => line.Contains("护甲与魔抗降到"));
    }

    [Fact]
    public void Annotate_NexusTurret_SaysWhatOpensIt()
    {
        var nexus = Markers(LiveMatchFixture.SinglePlayer("ARAM", 300, null))
            .First(marker => marker.Kind == LaneStructureKind.Nexus);

        Assert.Contains("这一方任意一座小水晶被破后才能被攻击", nexus.Details);
    }

    [Fact]
    public void Annotate_OwnAndEnemyTurrets_NameTheOtherSidesMinions()
    {
        var markers = Markers(LiveMatchFixture.SinglePlayer("ARAM", 300, null));
        var own = markers.Single(marker => marker.Ally && marker.Kind == LaneStructureKind.Outer);
        var enemy = markers.Single(marker => !marker.Ally && marker.Kind == LaneStructureKind.Outer);

        Assert.Contains(own.Details, line => line.Contains("附近没有敌方小兵时"));
        Assert.Contains(enemy.Details, line => line.Contains("附近没有我方小兵时"));
    }

    [Fact]
    public void Annotate_SummonersRiftWithoutTurretRules_LeavesEveryMarkerWithoutDetails()
    {
        var markers = Markers(LiveMatchFixture.RiftGame());

        Assert.DoesNotContain(markers, marker => marker.Details.Length > 0);
    }

    private static LaneMarker[] Markers(LiveMatchSnapshot game) =>
        MinionWaveForecaster.Calculate(game, 0)!.Lanes[0].Progress!.Markers;

    private static LaneMarker Inner(LiveMatchSnapshot game) =>
        Markers(game).Single(marker => !marker.Ally && marker.Kind == LaneStructureKind.Inner);

    private static bool IsTurret(LaneMarker marker) =>
        marker.Kind
            is LaneStructureKind.Outer
                or LaneStructureKind.Inner
                or LaneStructureKind.InhibitorTurret
                or LaneStructureKind.Nexus;
}
