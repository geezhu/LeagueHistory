using League.Models;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The whole analysis for a roster: the evaluation of the picked champions, the best swap from the bench, and the two
/// lines built from them.
///
/// What is pinned here is the plumbing rather than the numbers - those are arammeta's and are checked through the
/// fixture's own cases. The plumbing has three decisions of its own: empty seats do not enter the evaluation, a swap is
/// only looked for when the user's own seat holds a champion and there is a bench to look at, and both lines are
/// produced from the same evaluation so they cannot disagree about it.
/// </summary>
public class TeamAnalysisAnalyzeTests
{
    private static string Name(int id) => "英雄" + id;

    private static ChampionSelectRosterEntry Seat(int championId, int cellId = 0, bool self = false) =>
        new(championId, cellId, self, Preview: false);

    private static ChampionSelectRoster Roster(int[] champions, int[]? bench = null, bool selfFirst = true) =>
        new(
            [.. champions.Select((id, index) => Seat(id, cellId: index, self: selfFirst && index == 0))],
            bench ?? [],
            BenchEnabled: bench is { Length: > 0 }
        );

    [Fact]
    public void Analyze_TheEvaluationCoversThePickedChampions()
    {
        // Empty seats are left out: they are not champions and would otherwise be scored as zeroes.
        TeamAnalysis analysis = TeamAnalysisChatMessage.Analyze(
            AramTeamScoreFixture.Index,
            Roster([1, 2, 0, 3]),
            mayhem: false,
            Name
        );

        Assert.Equal([1, 2, 3], analysis.Evaluation.Team);
    }

    [Fact]
    public void Analyze_BothLinesComeFromTheSameEvaluation()
    {
        // The brief and the message are two renderings of one analysis; a caller reading both must not see two
        // different teams.
        TeamAnalysis analysis = TeamAnalysisChatMessage.Analyze(
            AramTeamScoreFixture.Index,
            Roster([1, 2]),
            mayhem: false,
            Name
        );

        Assert.Equal(
            TeamAnalysisChatMessage.Brief(analysis.Evaluation, analysis.Best, mayhem: false, Name),
            analysis.Brief
        );
        Assert.Equal(
            TeamAnalysisChatMessage.Build(
                analysis.Evaluation,
                analysis.Best,
                mayhem: false,
                AramTeamScoreFixture.Index.Patch,
                Name
            ),
            analysis.Message
        );
    }

    [Fact]
    public void Analyze_NoBench_LooksForNoSwap()
    {
        // There is nothing to swap in, so the analysis does not pretend to have an opinion about it.
        TeamAnalysis analysis = TeamAnalysisChatMessage.Analyze(
            AramTeamScoreFixture.Index,
            Roster([1, 2], bench: []),
            mayhem: false,
            Name
        );

        Assert.Null(analysis.Best);
    }

    [Fact]
    public void Analyze_TheUsersSeatHasNoChampion_LooksForNoSwap()
    {
        // The swap replaces the user's own pick; without one there is nothing to replace.
        ChampionSelectRoster roster = new(
            [Seat(championId: 0, cellId: 0, self: true), Seat(2, cellId: 1)],
            [9],
            BenchEnabled: true
        );

        TeamAnalysis analysis = TeamAnalysisChatMessage.Analyze(
            AramTeamScoreFixture.Index,
            roster,
            mayhem: false,
            Name
        );

        Assert.Null(analysis.Best);
    }

    [Fact]
    public void Analyze_NoSelfSeatAtAll_LooksForNoSwap()
    {
        // A roster with no seat marked as the user has nobody to advise.
        TeamAnalysis analysis = TeamAnalysisChatMessage.Analyze(
            AramTeamScoreFixture.Index,
            Roster([1, 2], bench: [9], selfFirst: false),
            mayhem: false,
            Name
        );

        Assert.Null(analysis.Best);
    }

    [Fact]
    public void Analyze_ABenchAndAUserSeat_LooksForASwap()
    {
        // Both halves are present, so the swap search runs; whether it finds something worth announcing is a separate
        // rule, already covered by the Build and Brief tests.
        ChampionSelectRoster roster = new(
            [Seat(1, cellId: 0, self: true), Seat(2, cellId: 1)],
            [9, 10, 11],
            BenchEnabled: true
        );

        TeamAnalysis analysis = TeamAnalysisChatMessage.Analyze(
            AramTeamScoreFixture.Index,
            roster,
            mayhem: false,
            Name
        );

        Assert.NotNull(analysis.Evaluation);
        Assert.NotEmpty(analysis.Brief);
        Assert.NotEmpty(analysis.Message);
    }
}
