using League.Models;
using League.Tests.Support;

namespace League.Tests.Models;

/// <summary>
/// What playing four against five costs, read from arammeta's capability sums. The expected values come from the
/// pressure checks in Tests/StatisticsChecks/DeathTimingChecks.cs.
/// </summary>
public class TeamPressureTests
{
    private static readonly int[] Own = [4, 35, 37, 63, 102];

    private static readonly int[] Enemy = [112, 134, 157, 202, 876];

    private static readonly string[] Roles = ["前排", "开团", "清线", "续航"];

    [Fact]
    public void From_FullTeams_ReportsOnlyRolesTheTeamCarries()
    {
        var pressure = TeamPressure.From(AramTeamScoreFixture.Index, Own, Enemy, 4);

        Assert.NotNull(pressure);
        Assert.DoesNotContain(pressure!.SelfRoles, role => !Roles.Contains(role));
    }

    [Fact]
    public void From_FullTeams_PinsTheLevelsAndTheRoles()
    {
        var pressure = TeamPressure.From(AramTeamScoreFixture.Index, Own, Enemy, 4);

        Assert.Equal(PressureLevel.High, pressure!.Siege);
        Assert.Equal(PressureLevel.Medium, pressure.Dive);
        Assert.Equal(PressureLevel.Low, pressure.SustainGap);
        Assert.Empty(pressure.SelfRoles);
    }

    [Fact]
    public void From_TooFewKnownChampions_ReturnsNull()
    {
        var pressure = TeamPressure.From(AramTeamScoreFixture.Index, [102], Enemy, 102);

        Assert.Null(pressure);
    }

    [Fact]
    public void From_EnemyTeamWithoutStatistics_ReturnsNull()
    {
        var pressure = TeamPressure.From(AramTeamScoreFixture.Index, Own, [99999], 4);

        Assert.Null(pressure);
    }
}
