using League.Models;
using League.Tests.Support;

namespace League.Tests.Models;

/// <summary>
/// Destroyed structures rebuilt from the live client's events, including the two known names for a turret. The
/// expected values come from the checks in Tests/StatisticsChecks/MinionWaveChecks.cs.
/// </summary>
public class LaneStructureStateTests
{
    [Theory]
    [InlineData("Turret_T2_L_03_A", false, 200, "top", false)]
    [InlineData("Turret_T1_C_07_A", false, 100, "bot", false)]
    [InlineData("Turret_T1_C_01_A", false, 100, "mid", true)]
    [InlineData("Turret_T2_L0_P3_1", false, 200, "bot", false)]
    [InlineData("Turret_T2_L2_P1_1", false, 200, "top", false)]
    [InlineData("Turret_T1_L1_P4_1", false, 100, "mid", true)]
    [InlineData("Turret_T1_R_02_A", true, 100, "mid", false)]
    [InlineData("Turret_TChaos_L1_P3_671196429_0", true, 200, "mid", false)]
    [InlineData("Turret_TOrder_L1_P5_1477396591_0", true, 100, "mid", true)]
    public void ParseTurret_KnownName_ReturnsTeamLaneAndKind(
        string name,
        bool singleLane,
        int team,
        string lane,
        bool nexus
    )
    {
        var turret = LaneStructureState.ParseTurret(name, singleLane);

        Assert.Equal((team, lane, nexus), turret);
    }

    [Fact]
    public void ParseTurret_UnknownName_ReturnsNull()
    {
        var turret = LaneStructureState.ParseTurret("Obelisk_T1", false);

        Assert.Null(turret);
    }

    [Fact]
    public void ParseTurret_LaneDigitOutOfRange_ReturnsNull()
    {
        var turret = LaneStructureState.ParseTurret("Turret_T1_L3_P1_1", false);

        Assert.Null(turret);
    }

    [Theory]
    [InlineData("Odd_Inhib")]
    [InlineData("Barracks_T2_L3")]
    public void From_InhibitorThatCannotBePlaced_IsReportedAsUnknown(string name)
    {
        var state = LaneStructureState.From([LiveMatchFixture.Structure("InhibKilled", 700, name)], false);

        Assert.Equal([name], state.Unknown);
    }

    [Fact]
    public void From_OneHowlingAbyssGame_CountsTurretsInhibitorAndNexusTurrets()
    {
        // The order one Howling Abyss game logged: outer, inner, inhibitor, then one nexus turret.
        var state = LaneStructureState.From(
            [
                LiveMatchFixture.Structure("TurretKilled", 503, "Turret_TChaos_L1_P3_671196429_0"),
                LiveMatchFixture.Structure("TurretKilled", 710, "Turret_TChaos_L1_P2_620863572_0"),
                LiveMatchFixture.Structure("InhibKilled", 746, "Inhib_TChaos_L1_P1_2432143522_0"),
                LiveMatchFixture.Structure("TurretKilled", 776, "Turret_TChaos_L1_P4_587308334_0"),
            ],
            true
        );

        Assert.Equal(2, state.TurretsDown(200, "mid"));
        Assert.Equal(746, state.InhibitorDownSince(200, "mid"));
        Assert.Equal(1, state.NexusTurretsDown(200));
        Assert.Empty(state.Unknown);
    }

    [Fact]
    public void From_SummonersRiftGame_CountsEachLaneSeparately()
    {
        var state = LaneStructureState.From(RiftEvents(), false);

        Assert.Equal(3, state.TurretsDown(200, "bot"));
        Assert.Equal(780, state.InhibitorDownSince(200, "bot"));
        Assert.Equal(1, state.TurretsDown(100, "mid"));
        Assert.Equal(1, state.InhibitorsDown(200));
    }

    [Fact]
    public void From_UnknownStructureName_ReportsItInsteadOfGuessing()
    {
        var events = RiftEvents().Append(LiveMatchFixture.Structure("TurretKilled", 800, "Odd_Name"));

        var state = LaneStructureState.From(events, false);

        Assert.Equal(["Odd_Name"], state.Unknown);
    }

    [Fact]
    public void From_InhibitorRespawned_LeavesTheInhibitorStanding()
    {
        var events = RiftEvents().Append(LiveMatchFixture.Structure("InhibRespawned", 1080, "Barracks_T2_R1"));

        var state = LaneStructureState.From(events, false);

        Assert.Null(state.InhibitorDownSince(200, "bot"));
    }

    [Fact]
    public void TurretFellAt_Index_ReturnsTheTimesFromTheOutermost()
    {
        var state = LaneStructureState.From(RiftEvents(), false);

        Assert.Equal(610, state.TurretFellAt(200, "bot", 0));
        Assert.Equal(680, state.TurretFellAt(200, "bot", 1));
        Assert.Null(state.TurretFellAt(200, "bot", 3));
        Assert.Null(state.TurretFellAt(100, "top", 0));
    }

    [Fact]
    public void TurretsDown_LaneNobodyTouched_IsZero()
    {
        var state = LaneStructureState.From(RiftEvents(), false);

        Assert.Equal(0, state.TurretsDown(100, "top"));
        Assert.Equal(0, state.NexusTurretsDown(100));
    }

    private static IEnumerable<LiveMatchEvent> RiftEvents() =>
        [
            LiveMatchFixture.Structure("TurretKilled", 610, "Turret_T2_R_03_A"),
            LiveMatchFixture.Structure("TurretKilled", 680, "Turret_T2_R_02_A"),
            LiveMatchFixture.Structure("TurretKilled", 742, "Turret_T2_C_07_A"),
            LiveMatchFixture.Structure("InhibKilled", 780, "Barracks_T2_R1"),
            LiveMatchFixture.Structure("TurretKilled", 701, "Turret_T1_C_05_A"),
        ];
}
