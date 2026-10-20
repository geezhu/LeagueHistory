using League.Models;

namespace League.Tests.Models;

/// <summary>
/// A player's ability haste: what the game reports for the active player, and an estimate for everyone else.
///
/// The estimate is built from what 2999 can see for another player - items and the augments the user recorded - and is
/// therefore only ever a floor. The panel says so, and lists the parts it added so the reader can judge it. Purist -
/// Caster is the one augment that turns bonus attack speed into haste, and a negative attack speed contribution is not
/// a cost: the conversion starts at zero.
/// </summary>
public class PlayerHasteTests
{
    private static readonly AugmentEffects NoAugments = AugmentEffects.None;

    private static LiveChampionStats Actual(double haste = 40) => new(1000, 0, 0, haste, 0, 0, 0, 0);

    private static ItemStats Items(double haste) => new(Health: 0, Armor: 0, MagicResist: 0, AbilityHaste: haste);

    private static AugmentEffects Purist() => NoAugments with { Cooldown = NoAugments.Cooldown with { Purist = true } };

    [Fact]
    public void PuristHastePerAttackSpeed_IsSixtyFiveHundredths()
    {
        Assert.Equal(0.65, PlayerHaste.PuristHastePerAttackSpeed);
    }

    // ---- the player's own value ----

    [Fact]
    public void Describe_TheRealValue_IsUsedAsIsAndIsNotAnEstimate()
    {
        PlayerHaste haste = PlayerHaste.Describe(Items(20), NoAugments, Actual(haste: 40));

        Assert.Equal(40, haste.Value, 3);
        Assert.False(haste.Estimated);
    }

    [Fact]
    public void Describe_TheRealValue_IgnoresTheItems()
    {
        // The game's number already holds the items; adding them again would double count.
        PlayerHaste haste = PlayerHaste.Describe(Items(999), NoAugments, Actual(haste: 40));

        Assert.Equal(40, haste.Value, 3);
    }

    [Fact]
    public void Describe_TheRealValue_SaysItMatchesTheGamePanel()
    {
        Assert.Contains(
            "与游戏属性面板相同",
            PlayerHaste.Describe(Items(0), NoAugments, Actual()).Details,
            StringComparison.Ordinal
        );
    }

    // ---- the estimate ----

    [Fact]
    public void Describe_NoRealValue_IsAnEstimate()
    {
        Assert.True(PlayerHaste.Describe(Items(20), NoAugments, null).Estimated);
    }

    [Fact]
    public void Describe_TheEstimate_AddsTheItemsAndTheAugments()
    {
        AugmentEffects augments = NoAugments with { AbilityHaste = 15 };

        Assert.Equal(35, PlayerHaste.Describe(Items(20), augments, null).Value, 3);
    }

    [Fact]
    public void Describe_TheEstimate_ListsThePartsItAdded()
    {
        AugmentEffects augments = NoAugments with { AbilityHaste = 15 };

        string details = PlayerHaste.Describe(Items(20), augments, null).Details;

        Assert.Contains("装备 20", details, StringComparison.Ordinal);
        Assert.Contains("+ 海克斯 15", details, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_TheEstimate_DoesNotMentionAugmentsWhenThereAreNone()
    {
        // The closing sentence names the things it could not see, one of which is unrecorded augments - so it is
        // the added part that must be absent, not the word.
        string details = PlayerHaste.Describe(Items(20), NoAugments, null).Details;

        Assert.DoesNotContain("+ 海克斯", details, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_TheEstimate_SaysWhatItCouldNotSee()
    {
        // It is only ever a floor; the panel has to say so or the reader will take it as the real figure.
        string details = PlayerHaste.Describe(Items(20), NoAugments, null).Details;

        Assert.Contains("未含符文", details, StringComparison.Ordinal);
        Assert.Contains("实际可能更高", details, StringComparison.Ordinal);
    }

    // ---- Purist - Caster ----

    [Fact]
    public void Describe_WithPurist_ConvertsBonusAttackSpeed()
    {
        // Traced: 20% bonus attack speed times 0.65 is 13 haste, so 20 + 13 = 33.
        PlayerHaste haste = PlayerHaste.Describe(Items(20), Purist(), null, bonusAttackSpeed: 20);

        Assert.Equal(33, haste.Value, 3);
    }

    [Fact]
    public void Describe_WithPurist_NamesTheConversionInTheParts()
    {
        string details = PlayerHaste.Describe(Items(0), Purist(), null, bonusAttackSpeed: 20).Details;

        Assert.Contains("纯粹主义者附加攻速 20%", details, StringComparison.Ordinal);
        Assert.Contains("转换 13", details, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_WithoutPurist_BonusAttackSpeedIsWorthNothing()
    {
        // The conversion belongs to that augment; nothing else turns attack speed into haste.
        Assert.Equal(
            PlayerHaste.Describe(Items(20), NoAugments, null, bonusAttackSpeed: 20).Value,
            PlayerHaste.Describe(Items(20), NoAugments, null).Value,
            3
        );
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    public void Describe_WithPurist_AnAttackSpeedThatIsNotPositive_ConvertsToNothing(double attackSpeed)
    {
        // Traced: the conversion starts at zero, so a negative contribution cannot take haste away.
        Assert.Equal(20, PlayerHaste.Describe(Items(20), Purist(), null, bonusAttackSpeed: attackSpeed).Value, 3);
    }

    [Fact]
    public void Describe_WithPurist_AndNoAttackSpeed_DoesNotMentionTheConversion()
    {
        string details = PlayerHaste.Describe(Items(20), Purist(), null).Details;

        Assert.DoesNotContain("纯粹主义者", details, StringComparison.Ordinal);
    }
}
