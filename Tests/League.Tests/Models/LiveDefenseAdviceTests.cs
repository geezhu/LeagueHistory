using League.Models;

namespace League.Tests.Models;

/// <summary>
/// Which resistance is worth buying next, from the enemy's damage mix.
///
/// The comparison is not "armour versus magic resistance" in the abstract - it is "one more step of either, against
/// this mix". Ten points of armour and ten of magic resistance are priced the same, so the advice is about which of
/// them buys more effective health right now. And "clearly better" needs a margin: within fifteen percent either is
/// fine, and calling one of them better would send the user shopping for nothing.
/// </summary>
public class LiveDefenseAdviceTests
{
    private static LiveChampionStats Stats(double health = 1000, double armor = 0, double magicResist = 0) =>
        new(health, armor, magicResist, 0, 0, 0, 0, 0);

    // ---- one kind of damage ----

    [Fact]
    public void Calculate_AllPhysical_AdvisesArmour()
    {
        LiveDefenseAdvice advice = LiveDefenseAdvice.Calculate(Stats(), physicalShare: 1);

        Assert.Equal(LiveDefensePriority.Armor, advice.Priority);
        Assert.True(advice.ArmorGain > 0);
        Assert.Equal(0, advice.MagicGain);
    }

    [Fact]
    public void Calculate_AllMagic_AdvisesMagicResistance()
    {
        LiveDefenseAdvice advice = LiveDefenseAdvice.Calculate(Stats(), physicalShare: 0);

        Assert.Equal(LiveDefensePriority.MagicResist, advice.Priority);
        Assert.Equal(0, advice.ArmorGain);
        Assert.True(advice.MagicGain > 0);
    }

    [Fact]
    public void Calculate_AnEvenMix_IsBalanced()
    {
        // With equal resistances and an even mix, ten of either buys the same, so neither is "better".
        Assert.Equal(LiveDefensePriority.Balanced, LiveDefenseAdvice.Calculate(Stats(), physicalShare: 0.5).Priority);
    }

    // ---- the effective health of each kind ----

    [Fact]
    public void Calculate_NoResistance_EffectiveHealthIsTheHealthItself()
    {
        // Traced: the damage multiplier at zero resistance is 100/100 = 1, so effective health is the health.
        LiveDefenseAdvice advice = LiveDefenseAdvice.Calculate(Stats(health: 1000), physicalShare: 0.5);

        Assert.Equal(1000, advice.PhysicalHealth, 3);
        Assert.Equal(1000, advice.MagicHealth, 3);
    }

    [Fact]
    public void Calculate_EachHundredResistance_DoublesThatEffectiveHealth()
    {
        // Traced: the multiplier at 100 is 100/200 = 0.5, so twice as much damage is needed.
        LiveDefenseAdvice advice = LiveDefenseAdvice.Calculate(
            Stats(health: 1000, armor: 100, magicResist: 50),
            physicalShare: 0.5
        );

        Assert.Equal(2000, advice.PhysicalHealth, 3);
        Assert.Equal(1500, advice.MagicHealth, 3);
    }

    [Fact]
    public void Calculate_NegativeResistance_CostsEffectiveHealth()
    {
        // Traced: below zero the multiplier is 2 − 100/(100 − r), so −100 gives 2 − 0.5 = 1.5 taken,
        // and 1000 health of that is worth 1000 / 1.5 ≈ 667.
        LiveDefenseAdvice advice = LiveDefenseAdvice.Calculate(Stats(health: 1000, armor: -100), physicalShare: 0.5);

        Assert.Equal(1000 / 1.5, advice.PhysicalHealth, 3);
    }

    // ---- the share ----

    [Fact]
    public void Calculate_AWeightedMix_IsReportedAsGiven()
    {
        Assert.Equal(
            0.3,
            LiveDefenseAdvice.Calculate(Stats(), physicalShare: 0.3).PhysicalShare.GetValueOrDefault(),
            3
        );
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    public void Calculate_AShareOutsideZeroToOne_IsClamped(double given, double reported)
    {
        // A share is a proportion; a value outside it is a caller's mistake, and clamping keeps the maths sane.
        Assert.Equal(
            reported,
            LiveDefenseAdvice.Calculate(Stats(), physicalShare: given).PhysicalShare.GetValueOrDefault(),
            3
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Calculate_NoUsableShare_SaysItDoesNotKnow(double? share)
    {
        // Without a mix there is nothing to compare, so no advice is given rather than a guess.
        LiveDefenseAdvice advice = LiveDefenseAdvice.Calculate(Stats(armor: 50, magicResist: 40), share);

        Assert.Equal(LiveDefensePriority.Unknown, advice.Priority);
        Assert.Null(advice.PhysicalShare);
        Assert.Equal(0, advice.ArmorGain);
        Assert.Equal(0, advice.MagicGain);
    }

    [Fact]
    public void Calculate_NoUsableShare_StillReportsTheEffectiveHealth()
    {
        // The two kinds of effective health do not need the mix, so they are still filled in.
        LiveDefenseAdvice advice = LiveDefenseAdvice.Calculate(Stats(health: 1000, armor: 100), physicalShare: null);

        Assert.Equal(2000, advice.PhysicalHealth, 3);
        Assert.Equal(1000, advice.MagicHealth, 3);
    }

    // ---- the margin ----

    [Fact]
    public void Calculate_AnAlmostEvenMix_StaysBalanced()
    {
        // Traced at share 0.52 against zero resistances: armour buys 1000/0.95273 − 1000 = 49.62 and magic
        // 1000/0.95636 − 1000 = 45.63, a ratio of 1.09 - inside the 1.15 margin, so either is fine.
        Assert.Equal(LiveDefensePriority.Balanced, LiveDefenseAdvice.Calculate(Stats(), physicalShare: 0.52).Priority);
    }

    [Fact]
    public void Calculate_JustOutsideTheEvenBand_NamesTheBetterOne()
    {
        // Traced at share 0.55: 52.63 against 42.66, a ratio of 1.23 - past the margin, so the advice is real.
        Assert.Equal(LiveDefensePriority.Armor, LiveDefenseAdvice.Calculate(Stats(), physicalShare: 0.55).Priority);
    }

    [Fact]
    public void Calculate_ALopsidedMix_NamesTheBetterOne()
    {
        // Outside the margin the advice is worth acting on.
        Assert.Equal(LiveDefensePriority.Armor, LiveDefenseAdvice.Calculate(Stats(), physicalShare: 0.8).Priority);
        Assert.Equal(
            LiveDefensePriority.MagicResist,
            LiveDefenseAdvice.Calculate(Stats(), physicalShare: 0.2).Priority
        );
    }

    [Fact]
    public void Step_IsTenPointsOfResistance()
    {
        // The two options are priced the same, so the step is what makes them comparable.
        Assert.Equal(10, LiveDefenseAdvice.Step);
    }

    // ---- the two advice paths agree about positive resistance ----

    [Fact]
    public void Calculate_PositiveResistance_AgreesWithTheHealthEstimate()
    {
        // The same effective health is computed in two places; at zero and above they must give the same answer.
        LiveDefenseAdvice advice = LiveDefenseAdvice.Calculate(Stats(health: 1000, armor: 50), physicalShare: 0.5);
        HealthEstimate estimate = new(1000, 50, 0);

        Assert.Equal(estimate.PhysicalEffective, advice.PhysicalHealth, 3);
    }
}
