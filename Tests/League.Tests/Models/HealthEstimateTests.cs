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
/// The full estimate (base stats, items, augments, Glass Cannon's cap) needs three more records and is not covered
/// here; what is covered is the arithmetic that estimate ends in.
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
    public void PhysicalEffective_MinusAHundredArmour_HalvesTheHealth()
    {
        // Traced: 1000 × 100 / (100 − (−100)) = 1000 × 100 / 200 = 500.
        Assert.Equal(500, Estimate(1000, armor: -100).PhysicalEffective, 3);
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
    public void PhysicalEffective_ApproachingMinusAHundredArmour_ShrinksTowardsNothing()
    {
        // Traced: the multiplier is 100 / (100 − r), which falls as r goes below zero. At −90 it is 100 / 190, so
        // 1000 health is worth 1000 × 100 / 190 ≈ 526 - less than half. It tends to zero as r approaches −100.
        Assert.Equal(1000 * 100 / 190d, Estimate(1000, armor: -90).PhysicalEffective, 3);
    }

    [Fact]
    public void MagicEffective_NegativeResistance_FollowsTheSameRule()
    {
        Assert.Equal(500, Estimate(1000, magicResist: -100).MagicEffective, 3);
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
}
