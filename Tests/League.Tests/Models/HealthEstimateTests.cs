using League.Models;

namespace League.Tests.Models;

/// <summary>
/// How much damage of each kind it takes to kill through a player's health and resistances, and the augment that
/// raises the health itself.
///
/// The multiplier is the part with a trap in it. Above zero resistance, each hundred points is another whole health
/// bar. Below zero it stops being linear: the formula becomes 100 / (100 - resistance), which grows faster the further
/// below zero it goes - so a hundred points of negative resistance does not mean "half as much health", it means half.
///
/// The full estimate grows base stats by level, adds the items and the recorded augments, applies the percentage
/// increases and then Glass Cannon's cap; a later section covers it with a real champion's numbers so the growth
/// curve is checked against the game rather than against itself.
/// </summary>
public class HealthEstimateTests
{
    private static HealthEstimate Estimate(double health, double armor = 0, double magicResist = 0) =>
        new(health, armor, magicResist);

    // ---- the augment that raises health ----

    [Fact]
    public void TankEngineHealth_IsFivePercentPerStack()
    {
        Assert.Equal(0.05, HealthEstimate.TankEngineHealth);
    }

    // ---- positive resistance ----

    [Fact]
    public void PhysicalEffective_NoArmour_IsTheHealthItself()
    {
        Assert.Equal(1000, Estimate(1000).PhysicalEffective, 3);
    }

    [Theory]
    [InlineData(100, 2000)]
    [InlineData(50, 1500)]
    [InlineData(200, 3000)]
    public void PhysicalEffective_EachHundredArmour_AddsAWholeHealthBar(double armor, double expected)
    {
        Assert.Equal(expected, Estimate(1000, armor: armor).PhysicalEffective, 3);
    }

    [Fact]
    public void MagicEffective_UsesTheMagicResistance()
    {
        // The two resistances are read separately: armour is nothing to magic damage.
        Assert.Equal(1000, Estimate(1000, armor: 100).MagicEffective, 3);
        Assert.Equal(2000, Estimate(1000, magicResist: 100).MagicEffective, 3);
    }

    // ---- negative resistance ----

    [Fact]
    public void PhysicalEffective_MinusAHundredArmour_TakesHalfAgainAsMuchDamage()
    {
        // League's damage multiplier below zero is 2 − 100 / (100 − r): at −100 that is 1.5, so 1000 health falls
        // to 1000 / 1.5 ≈ 666.67 - the same rule the live defence advice and the turret siege use.
        Assert.Equal(1000 / 1.5, Estimate(1000, armor: -100).PhysicalEffective, 3);
    }

    [Fact]
    public void PhysicalEffective_NegativeResistance_IsWorthLessThanTheSamePositiveOne()
    {
        // The trap: the two sides are not symmetric, so −50 is not "as good as" +50.
        double positive = Estimate(1000, armor: 50).PhysicalEffective;
        double negative = Estimate(1000, armor: -50).PhysicalEffective;

        Assert.True(negative < 1000, $"{negative} should be under the plain health.");
        Assert.True(negative < positive, "Negative resistance must cost more than the same positive one gains.");
    }

    [Fact]
    public void PhysicalEffective_VeryNegativeArmour_NeverFallsBelowHalfTheHealth()
    {
        // The damage multiplier 2 − 100 / (100 − r) rises towards 2 and never reaches it, so however far armour
        // falls, 1000 health stays worth more than 500. At −900 it is 2 − 0.1 = 1.9, giving 1000 / 1.9 ≈ 526.
        Assert.Equal(1000 / 1.9, Estimate(1000, armor: -900).PhysicalEffective, 3);
    }

    [Fact]
    public void MagicEffective_NegativeResistance_FollowsTheSameRule()
    {
        Assert.Equal(1000 / 1.5, Estimate(1000, magicResist: -100).MagicEffective, 3);
    }

    // ---- zero health ----

    [Fact]
    public void Effective_NoHealth_IsZeroWhateverTheResistance()
    {
        // A player with no health left has nothing to get through.
        Assert.Equal(0, Estimate(0, armor: 500).PhysicalEffective);
        Assert.Equal(0, Estimate(0, magicResist: 500).MagicEffective);
    }

    // ---- the record itself ----

    [Fact]
    public void TheEstimate_KeepsWhatItWasBuiltFrom()
    {
        HealthEstimate estimate = Estimate(1500, armor: 80, magicResist: 60);

        Assert.Equal(1500, estimate.Health);
        Assert.Equal(80, estimate.Armor);
        Assert.Equal(60, estimate.MagicResist);
    }

    // ---- the full estimate, against a real champion ----
    //
    // Maokai's Data Dragon 16.20.1 numbers, the same reference Tests/StatisticsChecks/HealthEstimateChecks.cs used:
    // hp 665 + 109, armour 35 + 5.2, magic resistance 32 + 2.05. At level 17 the growth term is
    // 109 × 16 × (0.7025 + 0.0175 × 16) = 1713.48, so the grown health is 2378.48.

    private static readonly ChampionBaseStats Maokai = new(
        Health: 665,
        HealthPerLevel: 109,
        Armor: 35,
        ArmorPerLevel: 5.2,
        MagicResist: 32,
        MagicResistPerLevel: 2.05
    );

    [Fact]
    public void Estimate_TheBaseStatsGrownToTheLevel_FollowTheGameCurve()
    {
        HealthEstimate estimate = HealthEstimate.Estimate(Maokai, 17, ItemStats.None, AugmentEffects.None);

        Assert.Equal(2378.48, estimate.Health, 2);
    }

    [Fact]
    public void Estimate_AtLevelOne_IsTheBaseValue()
    {
        // Traced: no steps up, so no growth term.
        Assert.Equal(665, HealthEstimate.Estimate(Maokai, 1, ItemStats.None, AugmentEffects.None).Health, 3);
    }

    [Fact]
    public void Estimate_ItemsAddTheirFlatHealth()
    {
        ItemStats items = new(Health: 550, Armor: 0, MagicResist: 0, AbilityHaste: 0);

        Assert.Equal(2928.48, HealthEstimate.Estimate(Maokai, 17, items, AugmentEffects.None).Health, 2);
    }

    [Fact]
    public void Estimate_TankEngineMultipliesTheWholeHealth()
    {
        // Traced: (2378.48 + 550) × 1.1 = 3221.328 - the augment multiplies the item health too.
        ItemStats items = new(Health: 550, Armor: 0, MagicResist: 0, AbilityHaste: 0);
        AugmentEffects augments = AugmentEffects.None with { HealthMultiplier = 1.1 };

        Assert.Equal(3221.328, HealthEstimate.Estimate(Maokai, 17, items, augments).Health, 2);
    }

    [Fact]
    public void Estimate_GlassCannon_CapsTheHealthAtSeventyPercent()
    {
        // Traced: the multiplier applies first, then the cap: 2928.48 × 1.1 × 0.7 = 2254.93.
        ItemStats items = new(Health: 550, Armor: 0, MagicResist: 0, AbilityHaste: 0);
        AugmentEffects augments = AugmentEffects.None with { HealthMultiplier = 1.1, GlassCannon = true };

        Assert.Equal(
            2928.48 * 1.1 * AugmentEffects.GlassCannonShare,
            HealthEstimate.Estimate(Maokai, 17, items, augments).Health,
            2
        );
    }

    [Fact]
    public void Estimate_ResistancesAreTheGrownBasePlusItemsPlusAugments()
    {
        // Traced: armour 35 + 5.2 × 16 × 0.9825 = 116.744, plus 40 from the items and 10 from the augments.
        ItemStats items = new(Health: 0, Armor: 40, MagicResist: 25, AbilityHaste: 0);
        AugmentEffects augments = AugmentEffects.None with { Armor = 10, MagicResist = 5 };

        HealthEstimate estimate = HealthEstimate.Estimate(Maokai, 17, items, augments);

        Assert.Equal(166.744, estimate.Armor, 3);
        Assert.Equal(94.226, estimate.MagicResist, 2);
    }

    [Fact]
    public void Estimate_TheEffectiveHealth_FollowsFromTheEstimatedStats()
    {
        // The estimate's own resistances are what the effective health is computed from, not the base ones.
        ItemStats items = new(Health: 550, Armor: 40, MagicResist: 0, AbilityHaste: 0);
        AugmentEffects augments = AugmentEffects.None with { HealthMultiplier = 1.1 };

        HealthEstimate estimate = HealthEstimate.Estimate(Maokai, 17, items, augments);

        Assert.Equal(estimate.Health * (1 + estimate.Armor / 100), estimate.PhysicalEffective, 2);
        Assert.Equal(estimate.Health * (1 + estimate.MagicResist / 100), estimate.MagicEffective, 2);
    }
}
