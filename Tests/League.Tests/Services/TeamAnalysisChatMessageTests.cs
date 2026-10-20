using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// The champion select chat lines about a team: a one-line brief for the assistant, and the longer message for the
/// widget. Both name the gaps, the damage mix and a swap worth making.
///
/// What counts as worth announcing is the rule with teeth: in Mayhem a swap needs a better estimate and no new gaps,
/// and elsewhere it needs to close more gaps than it opens. A swap that does neither is left out of both lines.
/// </summary>
public class TeamAnalysisChatMessageTests
{
    private static AramTeamCapability Cap(string name, double sum = 1, bool lack = false) =>
        new(name, name, sum, 0.5, lack);

    private static AramTeamEvaluation Evaluation(
        double estimatedWinRate = 0.5234,
        double baseWinRate = 0.5,
        double pairLift = 0.0123,
        double compositionScore = -0.004,
        double adShare = 0.6,
        int frontCount = 2,
        params AramTeamCapability[] capabilities
    ) =>
        new(
            Team: [1, 2, 3],
            Missing: [],
            BaseWinRate: baseWinRate,
            PairLift: pairLift,
            CompositionScore: compositionScore,
            EstimatedWinRate: estimatedWinRate,
            Capabilities: capabilities,
            AdShare: adShare,
            FrontCount: frontCount,
            Pairs: [],
            PairEdges: 0,
            Confidence: ""
        );

    private static AramTeamSwap Swap(
        double delta = 0.02,
        AramTeamCapability[]? added = null,
        AramTeamCapability[]? closed = null
    ) => new(ChampionId: 99, Delta: delta, Fixed: closed ?? [], Added: added ?? []);

    private static string Name(int championId) => "英雄" + championId;

    // ---- the one-line brief ----

    [Fact]
    public void Brief_NothingInParticular_NamesTheGapsTheDamageMixAndNoSwap()
    {
        string brief = TeamAnalysisChatMessage.Brief(
            Evaluation(adShare: 0.6, capabilities: [Cap("抗性"), Cap("控制")]),
            best: null,
            mayhem: false,
            Name
        );

        Assert.Equal("阵容 短板 无 · 物理 60%", brief);
    }

    [Fact]
    public void Brief_AGap_IsNamed()
    {
        string brief = TeamAnalysisChatMessage.Brief(
            Evaluation(capabilities: [Cap("控制", lack: true), Cap("抗性")]),
            best: null,
            mayhem: false,
            Name
        );

        Assert.Contains("短板 控制", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void Brief_SeveralGaps_AreJoinedByTheEnumeratingComma()
    {
        string brief = TeamAnalysisChatMessage.Brief(
            Evaluation(capabilities: [Cap("控制", lack: true), Cap("抗性", lack: true)]),
            best: null,
            mayhem: false,
            Name
        );

        Assert.Contains("短板 控制、抗性", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void Brief_Mayhem_NamesTheEstimatedWinRateFirst()
    {
        string brief = TeamAnalysisChatMessage.Brief(
            Evaluation(estimatedWinRate: 0.5234),
            best: null,
            mayhem: true,
            Name
        );

        Assert.StartsWith("阵容 胜率 52.3% · 短板 无", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void Brief_NotMayhem_LeavesTheWinRateOut()
    {
        // The estimate only applies to the mode whose statistics back it.
        Assert.DoesNotContain(
            "胜率",
            TeamAnalysisChatMessage.Brief(Evaluation(), best: null, mayhem: false, Name),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Brief_ARecommendedSwap_IsNamedWithoutItsDelta()
    {
        // The brief is one line; the delta belongs to the longer message.
        string brief = TeamAnalysisChatMessage.Brief(Evaluation(), Swap(closed: [Cap("控制")]), mayhem: false, Name);

        Assert.Contains("可换 英雄99", brief, StringComparison.Ordinal);
        Assert.DoesNotContain("+", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void Brief_AMayhemSwap_NamesItsDelta()
    {
        string brief = TeamAnalysisChatMessage.Brief(Evaluation(), Swap(delta: 0.0123), mayhem: true, Name);

        Assert.Contains("可换 英雄99 +1.2%", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void Brief_ASwapThatHelpsNowhere_IsLeftOut()
    {
        // Closes no gap and opens none: announcing it would only add noise.
        Assert.DoesNotContain(
            "可换",
            TeamAnalysisChatMessage.Brief(Evaluation(), Swap(), mayhem: false, Name),
            StringComparison.Ordinal
        );
    }

    // ---- the longer message ----

    [Fact]
    public void Build_NotMayhem_OpensWithTheModeAndClosesWithNoSwap()
    {
        string message = TeamAnalysisChatMessage.Build(
            Evaluation(adShare: 0.6),
            best: null,
            mayhem: false,
            "15.1",
            Name
        );

        Assert.Equal("【大乱斗·阵容分析】短板：无；物理伤害 60%", message);
    }

    [Fact]
    public void Build_AGap_IsNamedAfterTheColon()
    {
        string message = TeamAnalysisChatMessage.Build(
            Evaluation(capabilities: [Cap("控制", lack: true)]),
            best: null,
            mayhem: false,
            "15.1",
            Name
        );

        Assert.Contains("短板：控制", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Mayhem_OpensWithTheEstimateAndItsParts()
    {
        // The three parts are there so the estimate can be read as what it is: heroes, their pairing, the mix.
        string message = TeamAnalysisChatMessage.Build(Evaluation(), best: null, mayhem: true, "15.1", Name);

        Assert.StartsWith(
            "【海克斯大乱斗·阵容分析】arammeta 15.1 估计胜率 52.3%（英雄 50.0% 搭配 +1.2% 阵容 -0.4%）",
            message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Build_ThePartsAreSeparatedByTheFullWidthSemicolon()
    {
        string message = TeamAnalysisChatMessage.Build(
            Evaluation(),
            Swap(closed: [Cap("控制")]),
            mayhem: false,
            "15.1",
            Name
        );

        Assert.Contains("；", message, StringComparison.Ordinal);
        Assert.DoesNotContain(" · ", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ARecommendedSwap_NamesTheGapsItCloses()
    {
        string message = TeamAnalysisChatMessage.Build(
            Evaluation(),
            Swap(closed: [Cap("控制"), Cap("抗性")]),
            mayhem: false,
            "15.1",
            Name
        );

        Assert.Contains("可换 英雄99 补控制、抗性", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AMayhemSwapWithNoGapsToClose_NamesOnlyItsDelta()
    {
        // No "补" part when nothing is closed, and no trailing space either.
        string message = TeamAnalysisChatMessage.Build(Evaluation(), Swap(delta: 0.0123), mayhem: true, "15.1", Name);

        Assert.Contains("可换 英雄99 +1.2%", message, StringComparison.Ordinal);
        Assert.DoesNotContain("补", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ThePatchNameIsFlattened()
    {
        // A patch name is data; a newline in it would break the line in two.
        string message = TeamAnalysisChatMessage.Build(Evaluation(), best: null, mayhem: true, " 15.1\nbeta ", Name);

        Assert.Contains("arammeta 15.1 beta 估计胜率", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AChampionNameWithABreakInside_IsFlattened()
    {
        string message = TeamAnalysisChatMessage.Build(
            Evaluation(),
            Swap(closed: [Cap("控制")]),
            mayhem: false,
            "15.1",
            _ => "带\n换行"
        );

        Assert.Contains("可换 带 换行", message, StringComparison.Ordinal);
    }

    // ---- the length limit ----

    [Fact]
    public void Build_ATooLongMessage_IsCutToTheLimitWithAnEllipsis()
    {
        // One chat line has to stay one line, so the tail is replaced rather than the whole thing rejected.
        var many = new List<AramTeamCapability>();
        for (int i = 0; i < 40; i++)
        {
            many.Add(Cap("短板名很长" + i, lack: true));
        }

        string message = TeamAnalysisChatMessage.Build(
            Evaluation(capabilities: [.. many]),
            best: null,
            mayhem: false,
            "15.1",
            Name
        );

        Assert.Equal(180, message.Length);
        Assert.EndsWith("…", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AMessageJustUnderTheLimit_IsLeftWhole()
    {
        string message = TeamAnalysisChatMessage.Build(
            Evaluation(adShare: 0.6),
            best: null,
            mayhem: false,
            "15.1",
            Name
        );

        Assert.True(message.Length < 180, "This case is here for the boundary, so the message must be short.");
        Assert.DoesNotContain("…", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TheEllipsisReplacesTheLastCharacterSoTheLineStaysAtTheLimit()
    {
        // Cutting to 179 and appending the ellipsis keeps the total at the limit rather than one over.
        var many = new List<AramTeamCapability>();
        for (int i = 0; i < 40; i++)
        {
            many.Add(Cap("短板名很长" + i, lack: true));
        }

        string message = TeamAnalysisChatMessage.Build(
            Evaluation(capabilities: [.. many]),
            best: null,
            mayhem: false,
            "15.1",
            Name
        );

        Assert.Equal(180, message.Length);
    }

    // ---- the swap rules ----

    [Fact]
    public void Build_InMayhem_ASwapThatLowersTheEstimate_IsLeftOut()
    {
        Assert.DoesNotContain(
            "可换",
            TeamAnalysisChatMessage.Build(Evaluation(), Swap(delta: -0.01), mayhem: true, "15.1", Name),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Build_InMayhem_ASwapThatOpensAGap_IsLeftOut()
    {
        // Even a better estimate is not worth announcing if it costs a gap.
        Assert.DoesNotContain(
            "可换",
            TeamAnalysisChatMessage.Build(
                Evaluation(),
                Swap(delta: 0.02, added: [Cap("控制")]),
                mayhem: true,
                "15.1",
                Name
            ),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Build_InMayhem_ATinyImprovementCounts()
    {
        // Half a thousandth of a percent is the smallest change the estimate reports.
        Assert.Contains(
            "可换",
            TeamAnalysisChatMessage.Build(Evaluation(), Swap(delta: 0.0005), mayhem: true, "15.1", Name),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Build_Elsewhere_ASwapThatClosesNoGap_IsLeftOut()
    {
        Assert.DoesNotContain(
            "可换",
            TeamAnalysisChatMessage.Build(Evaluation(), Swap(), mayhem: false, "15.1", Name),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Build_Elsewhere_ASwapThatClosesAsManyAsItOpens_IsLeftOut()
    {
        // A swap has to be an improvement, not a trade.
        Assert.DoesNotContain(
            "可换",
            TeamAnalysisChatMessage.Build(
                Evaluation(),
                Swap(closed: [Cap("控制")], added: [Cap("抗性")]),
                mayhem: false,
                "15.1",
                Name
            ),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Build_Elsewhere_ASwapThatClosesMoreThanItOpens_IsAnnounced()
    {
        Assert.Contains(
            "可换",
            TeamAnalysisChatMessage.Build(
                Evaluation(),
                Swap(closed: [Cap("控制"), Cap("抗性")], added: [Cap("射程")]),
                mayhem: false,
                "15.1",
                Name
            ),
            StringComparison.Ordinal
        );
    }

    // ---- the numbers ----

    [Theory]
    [InlineData(0.5234, "52.3%")]
    [InlineData(0.5, "50.0%")]
    [InlineData(0, "0.0%")]
    [InlineData(1, "100.0%")]
    public void Build_TheWinRateIsWrittenToATenthOfAPercent(double value, string expected)
    {
        Assert.Contains(
            expected,
            TeamAnalysisChatMessage.Build(Evaluation(estimatedWinRate: value), best: null, mayhem: true, "15.1", Name),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(0.0001, "搭配 ±0.0%")]
    [InlineData(-0.0001, "搭配 ±0.0%")]
    [InlineData(0.0123, "搭配 +1.2%")]
    [InlineData(-0.0123, "搭配 -1.2%")]
    public void Build_ADifferenceSmallerThanThePrecision_IsWrittenAsPlusOrMinusNothing(double lift, string expected)
    {
        // A change below the reported precision would otherwise read as "+0.0%" or "-0.0%".
        // This goes through the pairing term, because a swap is only announced when it helps - so a negative or
        // vanishing delta never reaches the swap wording in the first place.
        Assert.Contains(
            expected,
            TeamAnalysisChatMessage.Build(Evaluation(pairLift: lift), best: null, mayhem: true, "15.1", Name),
            StringComparison.Ordinal
        );
    }
}
