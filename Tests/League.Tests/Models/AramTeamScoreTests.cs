using League.Models;
using League.Tests.Support;

namespace League.Tests.Models;

/// <summary>
/// The team estimate ported from arammeta's Draft page. The expected values were computed by arammeta's own site.js
/// (evaluateFullTeam and teamComposition) on the same statistics, so they pin the port against the original; the
/// fixture and the checks come from Tests/StatisticsChecks/AramTeamScoreChecks.cs.
/// </summary>
public class AramTeamScoreTests
{
    public static TheoryData<int[], double, double, double, double, double, int, string[]> Cases =>
        AramTeamScoreFixture.Cases;

    [Theory]
    [MemberData(nameof(Cases))]
    public void Evaluate_ArammetaTeam_MatchesTheReferenceCalculation(
        int[] team,
        double baseWinRate,
        double pairLift,
        double compositionScore,
        double estimatedWinRate,
        double adShare,
        int pairCount,
        string[] lacks
    )
    {
        var result = AramTeamScore.Evaluate(AramTeamScoreFixture.Index, team);

        Assert.Equal(baseWinRate, result.BaseWinRate, 9);
        Assert.Equal(pairLift, result.PairLift, 9);
        Assert.Equal(compositionScore, result.CompositionScore, 9);
        Assert.Equal(estimatedWinRate, result.EstimatedWinRate, 9);
        Assert.Equal(adShare, result.AdShare, 9);
        Assert.Equal(pairCount, result.Pairs.Length);
        Assert.Equal(lacks.Order(), result.Lacks.Select(lack => lack.Key).Order());
    }

    [Fact]
    public void Evaluate_UnknownChampion_ReportsItInsteadOfCountingItAsZero()
    {
        var result = AramTeamScore.Evaluate(AramTeamScoreFixture.Index, [35, 99999]);

        Assert.Equal([99999], result.Missing);
    }

    [Fact]
    public void Evaluate_TeamWithoutChampions_KeepsTheNeutralBaseRate()
    {
        var result = AramTeamScore.Evaluate(AramTeamScoreFixture.Index, []);

        Assert.Empty(result.Team);
        Assert.Equal(0.5, result.BaseWinRate);
        Assert.Equal(0, result.PairEdges);
        Assert.Equal(0, result.CompositionScore);
        Assert.Equal("低", result.Confidence);
    }

    [Fact]
    public void Evaluate_ChampionAlreadyOnTheTeam_CountsThePairAgainstItself()
    {
        var result = AramTeamScore.Evaluate(AramTeamScoreFixture.Index, [876, 876, 202, 157, 134]);

        Assert.Equal(2, result.Team.Count(id => id == 876));
        Assert.Equal("中", result.Confidence);
    }

    [Fact]
    public void CapabilityKeys_List_MatchesTheOrderTheResultReports()
    {
        var result = AramTeamScore.Evaluate(AramTeamScoreFixture.Index, [35, 63]);

        Assert.Equal(AramTeamScore.CapabilityKeys, result.Capabilities.Select(capability => capability.Key));
    }

    [Fact]
    public void Swaps_ByWinRate_OrdersByTheEstimateChange()
    {
        var index = AramTeamScoreFixture.Index;
        var current = AramTeamScore.Evaluate(index, [4, 35, 37, 63, 102]);

        var swaps = AramTeamScore.Swaps(index, current, 4, [876, 157], byWinRate: true);

        Assert.Equal(
            swaps.OrderByDescending(swap => swap.Delta).Select(swap => swap.Delta),
            swaps.Select(swap => swap.Delta)
        );
    }

    [Fact]
    public void Swaps_ByGapsFixed_OrdersByGapsFixedMinusGapsAdded()
    {
        var index = AramTeamScoreFixture.Index;
        var current = AramTeamScore.Evaluate(index, [4, 35, 37, 63, 102]);

        var swaps = AramTeamScore.Swaps(index, current, 4, [876, 157], byWinRate: false);

        Assert.Equal(
            swaps.OrderByDescending(swap => swap.Fixed.Length - swap.Added.Length).Select(swap => swap.ChampionId),
            swaps.Select(swap => swap.ChampionId)
        );
    }

    [Fact]
    public void Swaps_CurrentChampionAndUnknownIds_AreLeftOut()
    {
        var index = AramTeamScoreFixture.Index;
        var current = AramTeamScore.Evaluate(index, [4, 35, 37, 63, 102]);

        var swaps = AramTeamScore.Swaps(index, current, 4, [4, 0, -1, 876], byWinRate: true);

        Assert.Equal([876], swaps.Select(swap => swap.ChampionId));
    }

    [Fact]
    public void Swaps_RepeatedBenchChampion_IsOfferedOnce()
    {
        var index = AramTeamScoreFixture.Index;
        var current = AramTeamScore.Evaluate(index, [4, 35, 37, 63, 102]);

        var swaps = AramTeamScore.Swaps(index, current, 4, [876, 876], byWinRate: true);

        Assert.Equal([876], swaps.Select(swap => swap.ChampionId));
    }
}
