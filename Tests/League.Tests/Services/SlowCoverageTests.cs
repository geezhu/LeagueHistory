using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// How much of the time a slow can be up: its duration over the cooldown at the same rank, after ability haste.
///
/// The estimate is a ceiling and says so, because hits, other cooldown reductions and zones that slow again are not
/// counted. A charge ability uses one charge's return time rather than the gap between casts, and the share is capped
/// at one - a slow that outlasts its own cooldown is simply always up.
/// </summary>
public class SlowCoverageTests
{
    private static SlowAtLevel Slow(double duration, int rank = 1) =>
        new(new SlowRank(30, duration, 0), rank, "按等级");

    private static SpellCooldown Spell(params double[] cooldowns) => new("Q", "秘术射击", cooldowns);

    private static SpellCooldown Charged(double[] cooldowns, double[] recharge) =>
        new("Q", "秘术射击", cooldowns) { Recharge = recharge };

    // ---- when there is no estimate to give ----

    [Fact]
    public void Estimate_NoCooldownToUse_IsNull()
    {
        // Without the ability's numbers there is nothing to compare the duration against.
        Assert.Null(SlowCoverage.Estimate(Slow(2), null, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Estimate_ASlowThatLastsNoTime_IsNull(double duration)
    {
        Assert.Null(SlowCoverage.Estimate(Slow(duration), Spell(8), null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Estimate_ARankBelowTheFirst_IsNull(int rank)
    {
        // Rank 0 is "not learned", so there is no cooldown to read.
        Assert.Null(SlowCoverage.Estimate(Slow(2, rank), Spell(8), null));
    }

    [Fact]
    public void Estimate_ACooldownOfZero_IsNull()
    {
        Assert.Null(SlowCoverage.Estimate(Slow(2), Spell(0), null));
    }

    // ---- which number is used ----

    [Fact]
    public void Estimate_TheCooldownAtTheSlowsRank_IsTheOneUsed()
    {
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2, rank: 2), Spell(8, 6, 4), null);

        Assert.Equal(6, estimate?.Cooldown);
    }

    [Fact]
    public void Estimate_ARankPastTheLastOne_UsesTheLastCooldown()
    {
        // The slow is shown at the top rank the data has, so a higher rank reads the same numbers rather than failing.
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2, rank: 9), Spell(8, 6), null);

        Assert.Equal(6, estimate?.Cooldown);
    }

    [Fact]
    public void Estimate_AChargeAbility_UsesOneChargesReturnTime()
    {
        // The gap between casts is short; what limits the slow is how long a charge takes to come back.
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2), Charged([3, 3], [9, 9]), null);

        Assert.Equal(9, estimate?.Cooldown);
    }

    [Fact]
    public void Estimate_ARechargeOfZero_IsNotAChargeAbility()
    {
        // A zero recharge means the ability holds no charges, so the cooldown is what matters.
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2), Charged([8, 8], [0, 0]), null);

        Assert.Equal(8, estimate?.Cooldown);
    }

    // ---- ability haste ----

    [Theory]
    [InlineData(0, 8)]
    [InlineData(100, 4)]
    [InlineData(300, 2)]
    public void Estimate_AbilityHaste_ShortensTheCooldownToAHundredOverAHundredPlusHaste(double haste, double expected)
    {
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2), Spell(8), new PlayerHaste(haste, false, ""));

        Assert.NotNull(estimate);
        Assert.Equal(expected, estimate.Cooldown, 3);
    }

    [Fact]
    public void Estimate_NoHasteRecorded_AssumesNoneAndSaysSo()
    {
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2), Spell(8), null);

        Assert.Equal(8, estimate?.Cooldown);
        Assert.Contains("技能急速未知，按 0", estimate?.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_AnEstimatedHaste_SaysItIsAnEstimate()
    {
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2), Spell(8), new PlayerHaste(50, true, ""));

        Assert.Contains("估算技能急速 50", estimate?.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void EstimateAnExactHaste_DoesNotCallItAnEstimate()
    {
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2), Spell(8), new PlayerHaste(50, false, ""));

        Assert.Contains("技能急速 50", estimate?.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("估算", estimate?.Note, StringComparison.Ordinal);
    }

    // ---- the share ----

    [Fact]
    public void Estimate_TheShareIsTheDurationOverTheHastedCooldown()
    {
        // Two seconds of slow on an eight second cooldown is a quarter of the time.
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2), Spell(8), null);

        Assert.NotNull(estimate);
        Assert.Equal(0.25, estimate.Share, 3);
        Assert.Contains("减速约占 25% 的时间", estimate?.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_ASlowThatOutlastsItsCooldown_IsCappedAtAlwaysUp()
    {
        // A ceiling of one, not a number above it: the slow cannot cover more than all of the time.
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(10), Spell(4), null);

        Assert.Equal(1, estimate?.Share);
        Assert.Contains("减速约占 100% 的时间", estimate?.Note, StringComparison.Ordinal);
    }

    // ---- the wording ----

    [Fact]
    public void Estimate_AChargeAbility_CallsItselfACharge()
    {
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2), Charged([3, 3], [9, 9]), null);

        Assert.Contains("每层充能约", estimate?.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_AnOrdinaryAbility_CallsItselfACooldown()
    {
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2), Spell(8), null);

        Assert.Contains("冷却约", estimate?.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("充能", estimate?.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(8, "8 秒")]
    [InlineData(2.5, "2.5 秒")]
    [InlineData(9.94, "9.9 秒")]
    [InlineData(10, "10 秒")]
    [InlineData(12.34, "12 秒")]
    public void Estimate_AWholeSecondIsWrittenWithoutADecimalAndAShortOneToATenth(double seconds, string expected)
    {
        // Below ten seconds the tenth is what a player cares about; above it a whole second is enough.
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(1), Spell(seconds), null);

        Assert.Contains(expected, estimate?.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_TheNoteEndsByNamingWhatItDoesNotCount()
    {
        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2), Spell(8), null);

        Assert.EndsWith("未计命中返还和区域内的再次减速。", estimate?.Note, StringComparison.Ordinal);
    }

    // ---- the options ----

    [Fact]
    public void Estimate_AnOptionThatShortensCooldowns_MultipliesTheHastedOne()
    {
        // Purist - Caster takes a tenth off, so an eight second cooldown becomes 7.2.
        CooldownOptions purist = CooldownOptions.None with
        {
            Purist = true,
        };

        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2), Spell(8), null, purist);

        Assert.NotNull(estimate);
        Assert.Equal(7.2, estimate.Cooldown, 3);
    }

    [Fact]
    public void Estimate_AnOptionWithASummary_AppendsItToTheSource()
    {
        CooldownOptions purist = CooldownOptions.None with { Purist = true };

        SlowCoverageEstimate? estimate = SlowCoverage.Estimate(Slow(2), Spell(8), null, purist);

        Assert.Contains("技能急速未知，按 0，" + purist.Summary(), estimate?.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_NoOptions_BehavesLikeTheNeutralOnes()
    {
        // CooldownOptions.None is what an omitted argument falls back to.
        SlowCoverageEstimate? omitted = SlowCoverage.Estimate(Slow(2), Spell(8), null);
        SlowCoverageEstimate? neutral = SlowCoverage.Estimate(Slow(2), Spell(8), null, CooldownOptions.None);

        Assert.Equal(neutral?.Cooldown, omitted?.Cooldown);
        Assert.Equal(neutral?.Note, omitted?.Note);
    }
}
