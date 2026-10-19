using League.Models;

namespace League.Tests.Models;

/// <summary>
/// What a player's recorded augments add up to: the flat and multiplying health, haste, resistances, the choice each
/// augment was taken with, the hand-entered ultimate haste and the words each sum is reported in. The expected values
/// come from the checks in Tests/StatisticsChecks/CooldownOptionChecks.cs and HealthEstimateChecks.cs.
/// </summary>
public class AugmentEffectsTests
{
    // The old suite's Record helper: one augment selected for a player, at its stacks and choice.
    private static LiveAugmentStack Recorded(TrackedAugment augment, int stacks = 0, string choice = "") =>
        new(new("p", augment, new(0, stacks)) { Choice = choice }, new(stacks, 0, 0, true, ""));

    [Fact]
    public void Constants_RecordedAugmentValues_MatchTheWiki()
    {
        Assert.Equal(20d, AugmentEffects.SoulEaterHealth);
        Assert.Equal(300d, AugmentEffects.SoulEaterLater);
        Assert.Equal(0.7, AugmentEffects.GlassCannonShare);
        Assert.Equal(30d, AugmentEffects.UltimateAwakeningHaste);
        Assert.Equal(20d, AugmentEffects.StayResoluteLater);
        Assert.Equal(0.24, AugmentEffects.DrawYourSwordHealth);
    }

    [Fact]
    public void None_NoAugments_IsTheNeutralSum()
    {
        var none = AugmentEffects.None;

        Assert.Equal(0d, none.AbilityHaste);
        Assert.Equal(0d, none.Health);
        // Health is multiplied by this, so the neutral value is 1 and not 0.
        Assert.Equal(1d, none.HealthMultiplier);
        Assert.False(none.GlassCannon);
        Assert.Equal(0d, none.Armor);
        Assert.Equal(0d, none.MagicResist);
        Assert.Equal(0d, none.AttackRange);
        Assert.Empty(none.Parts);
        Assert.Same(CooldownOptions.None, none.Cooldown);
        Assert.Empty(none.Summary);
    }

    [Fact]
    public void Summary_SeveralParts_JoinsThemWithTheIdeographicComma()
    {
        var effects = AugmentEffects.None with { Parts = ["护甲 +12", "技能急速 +60"] };

        Assert.Equal("护甲 +12、技能急速 +60", effects.Summary);
    }

    [Theory]
    [InlineData(0d, 0.24)]
    [InlineData(499d, 0.24)]
    [InlineData(500d, 0.24)]
    [InlineData(515d, 0.264)]
    [InlineData(550d, 0.312)]
    [InlineData(649d, 0.456)]
    [InlineData(650d, 0.48)]
    [InlineData(1000d, 0.48)]
    public void DrawYourSwordShare_DefaultAttackRange_RisesTenPercentPerFifteenFromFiveHundred(
        double attackRange,
        double expected
    )
    {
        double share = AugmentEffects.DrawYourSwordShare(attackRange);

        Assert.Equal(expected, share, 9);
    }

    [Theory]
    [InlineData(TrackedAugment.InfiniteRecursion, 4, "4 层 · 技能急速 +72")]
    [InlineData(TrackedAugment.InfiniteRecursion, 0, "0 层 · 技能急速 +60")]
    [InlineData(TrackedAugment.ShrinkEngine, 3, "3 层 · 技能急速 +24")]
    public void Bonus_TakedownAugment_CountsItsStacksIntoAbilityHaste(
        TrackedAugment augment,
        int stacks,
        string expected
    )
    {
        string text = AugmentEffects.Bonus(augment, stacks, "");

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(TrackedAugment.TankEngine, 2, "", "2 层 · 最大生命 +10%")]
    [InlineData(TrackedAugment.SoulEater, 5, "", "5 层 · 生命 +100")]
    [InlineData(TrackedAugment.SoulEater, 5, "later", "5 层 · 生命 +400")]
    [InlineData(TrackedAugment.SoulEater, 0, "later", "0 层 · 生命 +300")]
    public void Bonus_HealthAugment_ReportsTheHealthItAdds(
        TrackedAugment augment,
        int stacks,
        string choice,
        string expected
    )
    {
        string text = AugmentEffects.Bonus(augment, stacks, choice);

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(550d, "生命 +31%（攻击距离 550）")]
    [InlineData(650d, "生命 +48%（攻击距离 650）")]
    [InlineData(0d, "生命 +24%（攻击距离未知，按 24%）")]
    public void Bonus_DrawYourSword_ReportsTheShareAndTheRangeItUsed(double attackRange, string expected)
    {
        string text = AugmentEffects.Bonus(TrackedAugment.DrawYourSword, 0, "", attackRange);

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData("armor", "护甲 +12")]
    [InlineData("magic", "魔抗 +12")]
    [InlineData("armor-later", "护甲 +12，另加护甲和魔抗各 20")]
    [InlineData("magic-later", "魔抗 +12，另加护甲和魔抗各 20")]
    public void Bonus_StayResolute_NamesTheChosenResistanceAndTheLaterBonus(string choice, string expected)
    {
        string text = AugmentEffects.Bonus(TrackedAugment.StayResolute, 12, choice);

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(2, "W", "W 急速 +160")]
    [InlineData(1, "Q", "Q 急速 +80")]
    [InlineData(0, "W", "未完成")]
    public void Bonus_PursuitOfHaste_ReportsTheFinishedStepOrNone(int stacks, string choice, string expected)
    {
        string text = AugmentEffects.Bonus(TrackedAugment.PursuitOfHaste, stacks, choice);

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData("haste", "变小 · 技能急速 +70")]
    [InlineData("health", "变大 · 生命 +20%")]
    [InlineData("tiny", "变大 · 生命 +20%")]
    public void Bonus_MadScientist_DescribesTheFormItRolled(string choice, string expected)
    {
        string text = AugmentEffects.Bonus(TrackedAugment.MadScientist, 0, choice);

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(TrackedAugment.Recursion, "技能急速 +60")]
    [InlineData(TrackedAugment.BreadAndButter, "Q 技能急速 +100")]
    [InlineData(TrackedAugment.Goliath, "生命 +35%，适应之力 +15%")]
    [InlineData(TrackedAugment.GlassCannon, "生命上限锁定为最大生命的 70%")]
    public void Bonus_AugmentWithNoOwnText_FallsBackToTheCatalog(TrackedAugment augment, string expected)
    {
        string text = AugmentEffects.Bonus(augment, 0, "");

        Assert.Equal(expected, text);
    }

    [Fact]
    public void From_NoAugmentsAndNoUltimateHaste_ReturnsTheNeutralSum()
    {
        var effects = AugmentEffects.From([], 0);

        Assert.Equal(0d, effects.AbilityHaste);
        Assert.Equal(0d, effects.Health);
        Assert.Equal(1d, effects.HealthMultiplier);
        Assert.Equal(0d, effects.Armor);
        Assert.Equal(0d, effects.MagicResist);
        Assert.Equal(0d, effects.AttackRange);
        Assert.Empty(effects.Parts);
        Assert.Empty(effects.Summary);
    }

    [Fact]
    public void From_ChampionsDefaultAttackRange_IsKeptOnTheSum()
    {
        var effects = AugmentEffects.From([Recorded(TrackedAugment.DrawYourSword)], 0, 550);

        Assert.Equal(550d, effects.AttackRange);
    }

    [Theory]
    [InlineData(TrackedAugment.CelestialBody, 0, "", 1500d)]
    [InlineData(TrackedAugment.SoulEater, 5, "", 100d)]
    [InlineData(TrackedAugment.SoulEater, 5, "later", 400d)]
    [InlineData(TrackedAugment.SoulEater, 0, "later", 300d)]
    public void From_FlatHealthAugment_AddsItsHealth(TrackedAugment augment, int stacks, string choice, double expected)
    {
        var effects = AugmentEffects.From([Recorded(augment, stacks, choice)], 0);

        Assert.Equal(expected, effects.Health);
    }

    [Theory]
    [InlineData(TrackedAugment.Goliath, 0, "", 0d, 1.35)]
    [InlineData(TrackedAugment.TankEngine, 2, "", 0d, 1.1)]
    [InlineData(TrackedAugment.TankEngine, 4, "", 0d, 1.2)]
    [InlineData(TrackedAugment.MadScientist, 0, "health", 0d, 1.2)]
    // The other form grants haste instead, so it leaves the multiplier alone.
    [InlineData(TrackedAugment.MadScientist, 0, "haste", 0d, 1d)]
    [InlineData(TrackedAugment.DrawYourSword, 0, "", 550d, 1.312)]
    [InlineData(TrackedAugment.DrawYourSword, 0, "", 650d, 1.48)]
    [InlineData(TrackedAugment.DrawYourSword, 0, "", 0d, 1.24)]
    public void From_HealthIncreaseAugment_MultipliesTheHealth(
        TrackedAugment augment,
        int stacks,
        string choice,
        double attackRange,
        double expected
    )
    {
        var effects = AugmentEffects.From([Recorded(augment, stacks, choice)], 0, attackRange);

        Assert.Equal(expected, effects.HealthMultiplier, 9);
    }

    [Theory]
    [InlineData(TrackedAugment.Recursion, 0, "", 60d)]
    [InlineData(TrackedAugment.BackToBasics, 0, "", 70d)]
    [InlineData(TrackedAugment.MadScientist, 0, "haste", 70d)]
    [InlineData(TrackedAugment.MadScientist, 0, "health", 0d)]
    [InlineData(TrackedAugment.InfiniteRecursion, 4, "", 72d)]
    [InlineData(TrackedAugment.ShrinkEngine, 3, "", 24d)]
    [InlineData(TrackedAugment.Goliath, 0, "", 0d)]
    public void From_HasteAugment_AddsItsHaste(TrackedAugment augment, int stacks, string choice, double expected)
    {
        var effects = AugmentEffects.From([Recorded(augment, stacks, choice)], 0);

        Assert.Equal(expected, effects.AbilityHaste);
    }

    [Theory]
    [InlineData("armor", 12, 12d, 0d)]
    [InlineData("magic", 12, 0d, 12d)]
    [InlineData("armor-later", 12, 32d, 20d)]
    [InlineData("magic-later", 12, 20d, 32d)]
    // An augment taken without a choice keeps its first one, which for Stay Resolute is armor.
    [InlineData("", 7, 7d, 0d)]
    public void From_StayResolute_SendsTheStacksToTheChosenResistance(
        string choice,
        int stacks,
        double armor,
        double magicResist
    )
    {
        var effects = AugmentEffects.From([Recorded(TrackedAugment.StayResolute, stacks, choice)], 0);

        Assert.Equal(armor, effects.Armor);
        Assert.Equal(magicResist, effects.MagicResist);
    }

    [Fact]
    public void From_GlassCannon_LocksTheHealthShareWithoutChangingTheMultiplier()
    {
        var effects = AugmentEffects.From([Recorded(TrackedAugment.GlassCannon)], 0);

        Assert.True(effects.GlassCannon);
        Assert.Equal(1d, effects.HealthMultiplier);
        Assert.Equal(0d, effects.AbilityHaste);
    }

    [Fact]
    public void From_ChoiceTheAugmentDoesNotOffer_FallsBackToItsFirstChoice()
    {
        var effects = AugmentEffects.From([Recorded(TrackedAugment.MadScientist, 0, "tiny")], 0);

        Assert.Equal(1.2, effects.HealthMultiplier, 9);
        Assert.Equal("科学狂人（变大 · 生命 +20%）", Assert.Single(effects.Parts));
    }

    [Theory]
    [InlineData(TrackedAugment.SoulEater, 5, "later", 0d, "吞噬灵魂（5 层 · 生命 +400）")]
    [InlineData(TrackedAugment.TankEngine, 2, "", 0d, "坦克引擎（2 层 · 最大生命 +10%）")]
    [InlineData(TrackedAugment.Recursion, 0, "", 0d, "循环往复（技能急速 +60）")]
    [InlineData(TrackedAugment.DrawYourSword, 0, "", 550d, "亮出你的剑（生命 +31%（攻击距离 550））")]
    public void From_Augment_RecordsItsNameAndBonusInParts(
        TrackedAugment augment,
        int stacks,
        string choice,
        double attackRange,
        string expected
    )
    {
        var effects = AugmentEffects.From([Recorded(augment, stacks, choice)], 0, attackRange);

        Assert.Equal(new[] { expected }, effects.Parts);
    }

    [Fact]
    public void From_SeveralAugments_KeepsTheirOrderWithTheHandEnteredUltimateHasteLast()
    {
        var effects = AugmentEffects.From(
            [Recorded(TrackedAugment.SoulEater, 5, "later"), Recorded(TrackedAugment.Recursion)],
            30
        );

        Assert.Equal("吞噬灵魂（5 层 · 生命 +400）、循环往复（技能急速 +60）、手动大招急速 30", effects.Summary);
    }

    [Theory]
    [InlineData(0d, "", 0d)]
    [InlineData(30d, "手动大招急速 30", 30d)]
    [InlineData(45.6, "手动大招急速 46", 45.6)]
    [InlineData(-5d, "", 0d)]
    public void From_HandEnteredUltimateHaste_BecomesAPartAndTheCooldownValue(
        double ultimateHaste,
        string summary,
        double expected
    )
    {
        var effects = AugmentEffects.From([], ultimateHaste);

        Assert.Equal(expected, effects.Cooldown.UltimateHaste);
        Assert.Equal(summary, effects.Summary);
    }

    [Theory]
    [InlineData(TrackedAugment.BreadAndButter, true, false, false)]
    [InlineData(TrackedAugment.BreadAndJam, false, true, false)]
    [InlineData(TrackedAugment.BreadAndCheese, false, false, true)]
    public void From_BreadAugment_TurnsOnItsOwnAbilitySlot(TrackedAugment augment, bool q, bool w, bool e)
    {
        var effects = AugmentEffects.From([Recorded(augment)], 0);

        Assert.Equal(new[] { q, w, e }, effects.Cooldown.Bread);
    }

    [Fact]
    public void From_PuristCaster_TurnsOnThePuristRule()
    {
        var effects = AugmentEffects.From([Recorded(TrackedAugment.PuristCaster)], 0);

        Assert.True(effects.Cooldown.Purist);
    }

    [Theory]
    [InlineData(2, "W", "W", 160d)]
    [InlineData(5, "W", "W", 160d)]
    [InlineData(1, "Q", "Q", 80d)]
    // Not finished yet: no choice is kept and no haste is added.
    [InlineData(0, "W", "", 0d)]
    public void From_PursuitOfHaste_KeepsTheChosenAbilityAndCapsItsTwoSteps(
        int stacks,
        string choice,
        string pursuit,
        double haste
    )
    {
        var effects = AugmentEffects.From([Recorded(TrackedAugment.PursuitOfHaste, stacks, choice)], 0);

        Assert.Equal(pursuit, effects.Cooldown.Pursuit);
        Assert.Equal(haste, effects.Cooldown.PursuitHaste);
    }

    [Fact]
    public void From_BackToBasics_SealsTheUltimate()
    {
        var effects = AugmentEffects.From([Recorded(TrackedAugment.BackToBasics)], 0);

        Assert.True(effects.Cooldown.UltimateSealed);
    }

    [Fact]
    public void From_UltBot_TurnsOnTheUltimateHasteRule()
    {
        var effects = AugmentEffects.From([Recorded(TrackedAugment.UltBot)], 0);

        Assert.True(effects.Cooldown.UltBot);
    }

    [Theory]
    [InlineData(0d, 30d)]
    [InlineData(30d, 60d)]
    // The hand-entered value is floored at zero before Ultimate Awakening's 30 is added.
    [InlineData(-10d, 30d)]
    public void From_UltimateAwakening_AddsThirtyToTheUltimateHaste(double entered, double expected)
    {
        var effects = AugmentEffects.From([Recorded(TrackedAugment.UltimateAwakening)], entered);

        Assert.Equal(expected, effects.Cooldown.UltimateHaste);
    }
}
