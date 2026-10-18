using League.Models;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// Advice on dying now to shop on the Howling Abyss. The expected values come from the checks in
/// Tests/StatisticsChecks/DeathTimingChecks.cs, which this class replaces: the mode and dead-player guards, a
/// fallen inhibitor and a kill streak, the enemies dead and the gold, the augments Mayhem offers while the shop is
/// open, and the pressure the team statistics add.
/// </summary>
public class DeathTimingAdvisorTests
{
    [Fact]
    public void Evaluate_SummonersRift_ReturnsNull()
    {
        var rift = LiveMatchFixture.AramGame(300, 1500, 1000) with { Mode = "CLASSIC", Map = 11 };

        var advice = Advise(rift);

        Assert.Null(advice);
    }

    [Fact]
    public void Evaluate_SelfAlreadyDead_ReturnsNull()
    {
        var dead = LiveMatchFixture.AramGame(300, 1500, 1000, (0, 20));

        var advice = Advise(dead);

        Assert.Null(advice);
    }

    [Fact]
    public void Evaluate_OwnInhibitorFallen_AvoidsAndNamesTheSuperMinions()
    {
        var game = LiveMatchFixture.AramGame(300, 3000, 200) with
        {
            Events = [LiveMatchFixture.Structure("InhibKilled", 250, "Barracks_T1_C1")],
        };

        var advice = Advise(game);

        Assert.Equal(DeathTimingVerdict.Avoid, advice!.Verdict);
        Assert.Equal("我方小水晶已破，超级兵持续进攻", advice.Reasons[0]);
    }

    [Fact]
    public void Evaluate_ThreeKillsInARow_AvoidsAndWarnsOfAShutdown()
    {
        var game = LiveMatchFixture.AramGame(300, 3000, 200) with
        {
            Events =
            [
                LiveMatchFixture.Kill(1, 100, "我", "敌方1"),
                LiveMatchFixture.Kill(2, 150, "我", "敌方1"),
                LiveMatchFixture.Kill(3, 200, "我", "敌方1"),
            ],
        };

        var advice = Advise(game);

        Assert.Equal(DeathTimingVerdict.Avoid, advice!.Verdict);
        Assert.Equal("你已连杀 3 次，可能有终结赏金（估）", advice.Reasons[0]);
    }

    [Fact]
    public void Evaluate_OwnDeathResetsTheStreak_DoesNotWarnOfAShutdown()
    {
        var game = LiveMatchFixture.AramGame(300, 3000, 200) with
        {
            Events =
            [
                LiveMatchFixture.Kill(1, 100, "我", "敌方1"),
                LiveMatchFixture.Kill(2, 150, "我", "敌方1"),
                LiveMatchFixture.Kill(3, 180, "敌方1", "我"),
                LiveMatchFixture.Kill(4, 200, "我", "敌方1"),
            ],
        };

        var advice = Advise(game);

        Assert.DoesNotContain(advice!.Factors, factor => factor.Text.Contains("终结赏金"));
    }

    [Fact]
    public void Evaluate_TwoEnemiesDeadWithAFullItemAndLowHealth_IsAGoodTrade()
    {
        var game = LiveMatchFixture.AramGame(300, 3000, 200, (5, 25), (6, 25));

        var advice = Advise(game);

        Assert.Equal(DeathTimingVerdict.Good, advice!.Verdict);
        Assert.Equal("对面 2 人阵亡（最长还剩 25 秒）", advice.Reasons[0]);
        Assert.Contains(advice.Factors, factor => factor.Text == "身上 3000 金，够买大件");
        Assert.Contains(advice.Factors, factor => factor.Text == "血量只剩 20%，出生平台不回血");
    }

    [Fact]
    public void Evaluate_TwoAlliesDeadAndNoGold_Avoids()
    {
        var game = LiveMatchFixture.AramGame(300, 300, 1000, (1, 20), (2, 20));

        var advice = Advise(game);

        Assert.Equal(DeathTimingVerdict.Avoid, advice!.Verdict);
        Assert.Contains(advice.Factors, factor => factor.Text == "我方已有 2 人阵亡，再死只剩 2 人守");
        Assert.Contains(advice.Factors, factor => factor.Text == "身上只有 300 金");
    }

    [Fact]
    public void Evaluate_NoTeamStatistics_SaysWhatItLeftOut()
    {
        var game = LiveMatchFixture.AramGame(300, 300, 1000);

        var advice = Advise(game);

        Assert.Contains(
            advice!.Factors,
            factor => factor is { Weight: 0, Text: "阵容数据未加载，只按兵线、复活时间和双方阵亡判断" }
        );
    }

    [Fact]
    public void Evaluate_DivingEnemyAndMainFrontLine_EachCostsAPoint()
    {
        var game = LiveMatchFixture.AramGame(300, 1500, 1000);
        var dive = new TeamPressure(PressureLevel.Medium, PressureLevel.High, PressureLevel.Low)
        {
            SelfRoles = ["前排"],
        };

        var plain = Advise(game);
        var risky = Advise(game, dive);

        Assert.Equal(Points(plain!.Factors) - 2, Points(risky!.Factors));
        Assert.Contains(risky.Factors, factor => factor.Text == "你是我方主要前排");
    }

    [Fact]
    public void Evaluate_EnemyPokeAndMissingSustain_AddsBothPenalties()
    {
        var game = LiveMatchFixture.AramGame(300, 1500, 200);

        var advice = Advise(game, new TeamPressure(PressureLevel.High, PressureLevel.Low, PressureLevel.High));

        Assert.Contains(advice!.Factors, factor => factor is { Weight: 0, Text: "对面消耗和清线强，缺人时塔掉得快" });
        Assert.Contains(advice.Factors, factor => factor is { Weight: 1, Text: "我方缺治疗，送死回满更划算" });
    }

    [Fact]
    public void Evaluate_MayhemAugmentReachedSinceTheShopWasOpen_FavoursDying()
    {
        var mayhem = LiveMatchFixture.AramGame(300, 1500, 1000) with { Mode = "KIWI" };

        var advice = Advise(mayhem, null, 3);

        Assert.Contains(new DeathTimingFactor(2, "7 级的强化还没选，死后才能选"), advice!.Factors);
    }

    [Fact]
    public void Evaluate_MayhemAugmentsAlreadyOffered_DoesNotCountThemAgain()
    {
        var mayhem = LiveMatchFixture.AramGame(300, 1500, 1000) with { Mode = "KIWI" };

        var advice = Advise(mayhem, null, 8);

        Assert.DoesNotContain(advice!.Factors, factor => factor.Text.Contains("强化"));
    }

    [Fact]
    public void Evaluate_MidGameStartWithUnknownHistory_MakesNoClaimAboutAugments()
    {
        var mayhem = LiveMatchFixture.AramGame(300, 1500, 1000) with { Mode = "KIWI" };

        var advice = Advise(mayhem, null, null);

        Assert.DoesNotContain(advice!.Factors, factor => factor.Text.Contains("强化"));
    }

    [Fact]
    public void Evaluate_PlainAram_HasNoAugments()
    {
        var game = LiveMatchFixture.AramGame(300, 1500, 1000);

        var advice = Advise(game, null, 3);

        Assert.DoesNotContain(advice!.Factors, factor => factor.Text.Contains("强化"));
    }

    [Fact]
    public void Evaluate_Level15MayhemWithThreePendingAugments_NamesAllOfThem()
    {
        var mayhem = LiveMatchFixture.WithLevel(LiveMatchFixture.AramGame(300, 1500, 1000), 15) with { Mode = "KIWI" };

        var advice = Advise(mayhem, null, 3);

        Assert.Contains(new DeathTimingFactor(2, "7、11、15 级的 3 个强化还没选，死后才能选"), advice!.Factors);
    }

    private static DeathTimingAdvice? Advise(
        LiveMatchSnapshot snapshot,
        TeamPressure? pressure = null,
        int? augmentsOpenedAt = null
    ) =>
        MinionWaveForecaster.Calculate(snapshot, 0) is { } waves
            ? DeathTimingAdvisor.Evaluate(snapshot, waves, pressure, augmentsOpenedAt)
            : null;

    private static int Points(DeathTimingFactor[] factors) => factors.Sum(factor => factor.Weight);
}
