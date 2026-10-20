using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// The words beside a player's health: the active player's real numbers from the game, and everyone else's estimate,
/// labelled as one and listing what it could not see.
///
/// The comparison is the useful part of the real branch. When there is also an estimate for the same player, the two
/// are put side by side as a percentage, which is what tells the reader how far the estimate for the <em>other</em>
/// players may be off. It is only drawn when the estimate has health at all, because a ratio against zero says
/// nothing.
/// </summary>
public class PlayerHealthTextTests
{
    private static readonly ItemStats NoItems = ItemStats.None;
    private static readonly AugmentEffects NoAugments = AugmentEffects.None;

    private static LiveChampionStats Actual(double health = 1200, double armor = 50, double magicResist = 40) =>
        new(health, armor, magicResist, 30, 0, 0, 0, 0);

    private static HealthEstimate Estimate(double health = 1000, double armor = 50, double magicResist = 40) =>
        new(health, armor, magicResist);

    // ---- nothing to say ----

    [Fact]
    public void Describe_NoEstimateAndNoRealStats_SaysNothing()
    {
        // Before the game reports stats there is nothing to show, and an empty line is better than a wrong one.
        Assert.Null(PlayerHealthText.Describe(null, 11, NoItems, NoAugments, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Describe_ALevelThatIsNotALevel_SaysNothing(int level)
    {
        // The estimate is grown from base stats, so without a level there is no estimate to make.
        Assert.Null(PlayerHealthText.Describe(Estimate(), level, NoItems, NoAugments, null));
    }

    // ---- the player's own numbers ----

    [Fact]
    public void Describe_TheRealStats_AreShownWithoutAnApproximation()
    {
        PlayerHealth health = PlayerHealthText.Describe(null, 11, NoItems, NoAugments, Actual())!;

        Assert.Equal("1200 · 物理 1800 · 法术 1680", health.Text);
    }

    [Fact]
    public void Describe_TheRealBranch_SaysTheyComeFromTheGame()
    {
        PlayerHealth health = PlayerHealthText.Describe(null, 11, NoItems, NoAugments, Actual())!;

        Assert.Contains("2999 报告的实际属性", health.Details, StringComparison.Ordinal);
        Assert.Contains("已包含游戏里的全部加成", health.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_WithAnEstimateToo_NamesHowFarItWasOff()
    {
        // Traced: the estimate says 1000 and the game says 1200, so the estimate was 20% low.
        PlayerHealth health = PlayerHealthText.Describe(
            Estimate(health: 1000),
            11,
            NoItems,
            NoAugments,
            Actual(health: 1200)
        )!;

        Assert.Contains("同一公式估算生命 1000", health.Details, StringComparison.Ordinal);
        Assert.Contains("实际高 20%", health.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_AnEstimateWithNoHealth_IsNotComparedAgainst()
    {
        // A ratio against zero says nothing, so the comparison is left out rather than printed as infinity.
        PlayerHealth health = PlayerHealthText.Describe(Estimate(health: 0), 11, NoItems, NoAugments, Actual())!;

        Assert.DoesNotContain("同一公式估算生命", health.Details, StringComparison.Ordinal);
    }

    // ---- everyone else's estimate ----

    [Fact]
    public void Describe_TheEstimate_IsLabelledAsOne()
    {
        // The reader has to know this is not what the game reports.
        PlayerHealth health = PlayerHealthText.Describe(Estimate(), 11, NoItems, NoAugments, null)!;

        Assert.StartsWith("约 ", health.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_TheEstimate_ListsWhatItCouldNotSee()
    {
        PlayerHealth health = PlayerHealthText.Describe(Estimate(), 11, NoItems, NoAugments, null)!;

        Assert.Contains("未含未记录的海克斯", health.Details, StringComparison.Ordinal);
        Assert.Contains("实际通常更高", health.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_TheEstimate_NamesTheLevelAndTheItems()
    {
        ItemStats items = new(Health: 500, Armor: 40, MagicResist: 30, AbilityHaste: 20);

        PlayerHealth health = PlayerHealthText.Describe(Estimate(), 13, items, NoAugments, null)!;

        Assert.Contains("13 级基础属性", health.Details, StringComparison.Ordinal);
        Assert.Contains("生命 +500", health.Details, StringComparison.Ordinal);
        Assert.Contains("护甲 +40", health.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_RecordedAugments_AreNamedInTheEstimate()
    {
        // The reader needs to know which of the extra health was actually counted.
        AugmentEffects augments = NoAugments with
        {
            Parts = ["坦克引擎"],
        };

        PlayerHealth health = PlayerHealthText.Describe(Estimate(), 11, NoItems, augments, null)!;

        Assert.Contains("已记录的海克斯：坦克引擎", health.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_NoRecordedAugments_AddsNothingAboutThem()
    {
        PlayerHealth health = PlayerHealthText.Describe(Estimate(), 11, NoItems, NoAugments, null)!;

        Assert.DoesNotContain("已记录的海克斯", health.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_TheEstimate_ShowsEffectiveHealthForBothKinds()
    {
        // Traced: 1000 × (1 + 50/100) = 1500 physical, 1000 × (1 + 40/100) = 1400 magic.
        PlayerHealth health = PlayerHealthText.Describe(
            Estimate(armor: 50, magicResist: 40),
            11,
            NoItems,
            NoAugments,
            null
        )!;

        Assert.Equal("约 1000 · 物理 1500 · 法术 1400", health.Text);
    }
}
