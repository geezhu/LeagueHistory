using League.Models;

namespace League.Tests.Models;

/// <summary>
/// The lane paths: which lane a structure name belongs to, the order a team's turrets fall in, and the distance a
/// wave walks from its own nexus. The expected values come from the checks in
/// Tests/StatisticsChecks/MinionWaveChecks.cs.
/// </summary>
public class MinionLaneMapTests
{
    private static MinionLaneMap Rift => MinionLaneMap.SummonersRift;

    [Theory]
    [InlineData(LaneStructureKind.Outer, "一塔")]
    [InlineData(LaneStructureKind.Inner, "二塔")]
    [InlineData(LaneStructureKind.InhibitorTurret, "高地塔")]
    [InlineData(LaneStructureKind.Inhibitor, "小水晶")]
    [InlineData(LaneStructureKind.Nexus, "门牙塔")]
    [InlineData(LaneStructureKind.Base, "大水晶")]
    public void Label_StructureKind_NamesTheStructure(LaneStructureKind kind, string expected)
    {
        var structure = new LaneStructure(100, kind, 0);

        Assert.Equal(expected, structure.Label);
    }

    [Fact]
    public void SummonersRift_HasThreeLanesAndTheHowlingAbyssHasOne()
    {
        Assert.Equal(["top", "mid", "bot"], Rift.Lanes.Select(lane => lane.Key));
        Assert.Equal(["mid"], MinionLaneMap.HowlingAbyss.Lanes.Select(lane => lane.Key));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public void Turrets_Team_RunsOutermostFirst(int team)
    {
        var mid = Rift.Lanes.Single(lane => lane.Key == "mid");

        var labels = mid.Turrets(team).Select(turret => turret.Label);

        Assert.Equal(["一塔", "二塔", "高地塔"], labels);
    }

    [Fact]
    public void Structures_OneLane_PlacesBothBasesAroundTheInhibitors()
    {
        var lane = MinionLaneMap.HowlingAbyss.Lanes[0];

        Assert.Equal(9149, lane.Length);
        Assert.Equal(0, lane.Inhibitor(100).FromBlue);
        Assert.Equal(9149, lane.Inhibitor(200).FromBlue);
        Assert.Equal(-1334, lane.Base(100).FromBlue);
        Assert.Equal(10466, lane.Base(200).FromBlue);
    }

    [Fact]
    public void Distance_TeamAndStructure_MeasuresFromThatTeamsOwnBase()
    {
        var lane = MinionLaneMap.HowlingAbyss.Lanes[0];
        var ownOuter = lane.Turrets(100).Single(turret => turret.Kind == LaneStructureKind.Outer);
        var enemyOuter = lane.Turrets(200).Single(turret => turret.Kind == LaneStructureKind.Outer);

        // The bases sit 1334 units behind the blue inhibitor and 1317 behind the red one.
        Assert.Equal(1334 + 2527, lane.Distance(100, ownOuter));
        Assert.Equal(1317 + (9149 - 6614), lane.Distance(200, enemyOuter));
    }

    [Fact]
    public void Distance_OwnBase_IsZeroForBothTeams()
    {
        var lane = MinionLaneMap.HowlingAbyss.Lanes[0];

        Assert.Equal(0, lane.Distance(100, lane.Base(100)));
        Assert.Equal(0, lane.Distance(200, lane.Base(200)));
    }
}
