using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// The active player's ability cooldowns: the rank the game reports, the base seconds at that rank, and what ability
/// haste makes of them.
///
/// Four rules shape the result. Only the ultimate can be sealed, and a sealed ultimate has no cooldown at all. A rank
/// of zero means the ability is not learned, so everything about it is zero rather than the first rank's numbers. The
/// rank is clamped to the ranks the data actually has, so a report of "five ranks" for a three-rank ultimate does not
/// run off the end. And a charge ability shows one charge's return time instead of the gap between casts.
/// </summary>
public class AbilityCooldownsTests
{
    private static SpellCooldown Spell(string key = "Q", params double[] cooldowns) =>
        new(key, "技能" + key, cooldowns);

    private static Dictionary<string, int> Ranks(string key = "Q", int rank = 2) => new() { [key] = rank };

    // ---- the haste formula ----

    [Theory]
    [InlineData(10, 0, 10)]
    [InlineData(10, 100, 5)]
    [InlineData(10, 300, 2.5)]
    [InlineData(3, 50, 2)]
    public void Hasted_ShortensTheCooldownToAHundredOverAHundredPlusHaste(double seconds, double haste, double expected)
    {
        Assert.Equal(expected, AbilityCooldowns.Hasted(seconds, haste), 3);
    }

    [Theory]
    [InlineData(-50)]
    [InlineData(-1)]
    public void Hasted_NegativeHaste_CountsAsNone(double haste)
    {
        // A cooldown never grows because of a nonsense value.
        Assert.Equal(10, AbilityCooldowns.Hasted(10, haste), 3);
    }

    // ---- the ranks ----

    [Fact]
    public void Describe_ALearnedAbility_ReadsTheBaseAtItsRank()
    {
        AbilityCooldown[] shown = AbilityCooldowns.Describe([Spell("Q", 10, 8, 6)], Ranks(rank: 2), haste: 0);

        AbilityCooldown ability = Assert.Single(shown);
        Assert.Equal(2, ability.Rank);
        Assert.Equal(3, ability.Ranks);
        Assert.Equal(8, ability.Base);
        Assert.Equal(8, ability.Current);
    }

    [Fact]
    public void Describe_AbilityHaste_ShortensTheCurrentCooldown()
    {
        // Traced: rank two is eight seconds; a hundred haste halves it.
        AbilityCooldown[] shown = AbilityCooldowns.Describe([Spell("Q", 10, 8, 6)], Ranks(rank: 2), haste: 100);

        Assert.Equal(8, shown[0].Base);
        Assert.Equal(4, shown[0].Current, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Describe_AnUnlearnedAbility_IsAllZeroes(int rank)
    {
        // Rank zero is "not learned"; the first rank's numbers would be a lie.
        AbilityCooldown ability = Assert.Single(
            AbilityCooldowns.Describe([Spell("Q", 10, 8)], Ranks(rank: rank), haste: 0)
        );

        Assert.Equal(0, ability.Rank);
        Assert.Equal(0, ability.Base);
        Assert.Equal(0, ability.Current);
        Assert.Equal(2, ability.Ranks);
    }

    [Fact]
    public void Describe_ARankBeyondTheData_IsClampedToTheLastOne()
    {
        // The game reports how many ranks it thinks there are; the data may hold fewer.
        AbilityCooldown ability = Assert.Single(
            AbilityCooldowns.Describe([Spell("R", 120, 100, 80)], Ranks("R", 5), haste: 0)
        );

        Assert.Equal(3, ability.Rank);
        Assert.Equal(80, ability.Base);
    }

    [Fact]
    public void Describe_AnAbilityTheRanksDoNotMention_IsUnlearned()
    {
        // A key the player has no rank in is not learned either.
        AbilityCooldown ability = Assert.Single(
            AbilityCooldowns.Describe([Spell("Q", 10)], new Dictionary<string, int>(), haste: 0)
        );

        Assert.Equal(0, ability.Rank);
    }

    [Fact]
    public void Describe_EverySpell_IsReportedInOrder()
    {
        AbilityCooldown[] shown = AbilityCooldowns.Describe(
            [Spell("Q", 10), Spell("W", 12), Spell("E", 14), Spell("R", 120)],
            new Dictionary<string, int>
            {
                ["Q"] = 1,
                ["W"] = 1,
                ["E"] = 1,
                ["R"] = 1,
            },
            haste: 0
        );

        Assert.Equal(["Q", "W", "E", "R"], shown.Select(a => a.Key));
    }

    // ---- charge abilities ----

    [Fact]
    public void Describe_AChargeAbility_ShowsOneChargesReturnTime()
    {
        // The gap between casts is short; what limits the ability is how long a charge takes to come back.
        SpellCooldown spell = new("Q", "技能Q", [3, 3, 3]) { Recharge = [20, 15, 10] };

        AbilityCooldown ability = Assert.Single(AbilityCooldowns.Describe([spell], Ranks(rank: 2), haste: 0));

        Assert.Equal(15, ability.Base);
    }

    [Fact]
    public void Describe_AChargeAbility_ReportsHowManyCharges()
    {
        SpellCooldown spell = new("Q", "技能Q", [3, 3, 3]) { Recharge = [20, 15, 10], Charges = [2, 2, 3] };

        Assert.Equal(2, AbilityCooldowns.Describe([spell], Ranks(rank: 2), haste: 0)[0].Charges);
    }

    [Fact]
    public void Describe_AChargeAbilityWithNoChargeCount_ReportsNone()
    {
        // The recharge says it is charged; the charge count array may still be missing or short.
        SpellCooldown spell = new("Q", "技能Q", [3, 3]) { Recharge = [20, 15] };

        Assert.Equal(0, AbilityCooldowns.Describe([spell], Ranks(rank: 2), haste: 0)[0].Charges);
    }

    [Fact]
    public void Describe_ARechargeOfZero_IsNotAChargeAbility()
    {
        SpellCooldown spell = new("Q", "技能Q", [8, 8]) { Recharge = [0, 0], Charges = [2, 2] };

        AbilityCooldown ability = AbilityCooldowns.Describe([spell], Ranks(rank: 2), haste: 0)[0];

        Assert.Equal(8, ability.Base);
        Assert.Equal(0, ability.Charges);
    }

    // ---- the sealed ultimate ----

    [Fact]
    public void Describe_ASealedUltimate_HasNoCooldownAtAll()
    {
        // Back to Basics seals it: there is nothing to cast, so nothing to count down.
        CooldownOptions sealedOptions = CooldownOptions.None with
        {
            UltimateSealed = true,
        };

        AbilityCooldown ability = AbilityCooldowns.Describe(
            [Spell("R", 120, 100, 80)],
            Ranks("R", 1),
            haste: 0,
            sealedOptions
        )[0];

        Assert.True(ability.Sealed);
        Assert.Equal(0, ability.Base);
        Assert.Equal(0, ability.Current);
    }

    [Fact]
    public void Describe_AsealedUltimate_LeavesTheOtherAbilitiesAlone()
    {
        // Only the ultimate is sealed; the basic abilities are still there.
        CooldownOptions sealedOptions = CooldownOptions.None with
        {
            UltimateSealed = true,
        };

        AbilityCooldown[] shown = AbilityCooldowns.Describe(
            [Spell("Q", 10), Spell("R", 120)],
            new Dictionary<string, int> { ["Q"] = 1, ["R"] = 1 },
            haste: 0,
            sealedOptions
        );

        Assert.False(shown[0].Sealed);
        Assert.Equal(10, shown[0].Base);
        Assert.True(shown[1].Sealed);
    }

    [Fact]
    public void Describe_SealingAnOrdinaryAbility_DoesNothing()
    {
        // The option names the ultimate; a "Q" carrying the key "R" is the only one affected.
        CooldownOptions sealedOptions = CooldownOptions.None with
        {
            UltimateSealed = true,
        };

        AbilityCooldown ability = AbilityCooldowns.Describe([Spell("Q", 10)], Ranks("Q", 1), haste: 0, sealedOptions)[
            0
        ];

        Assert.False(ability.Sealed);
        Assert.Equal(10, ability.Base);
    }

    // ---- the options ----

    [Fact]
    public void Describe_AnOptionThatShortensCooldowns_MultipliesAfterTheHaste()
    {
        // Traced: eight seconds, a hundred haste halves it to four, Purist takes a tenth to 3.6.
        CooldownOptions purist = CooldownOptions.None with
        {
            Purist = true,
        };

        AbilityCooldown ability = AbilityCooldowns.Describe([Spell("Q", 10, 8)], Ranks(rank: 2), haste: 100, purist)[0];

        Assert.Equal(3.6, ability.Current, 3);
        Assert.Equal(8, ability.Base);
    }

    [Fact]
    public void Describe_NoOptions_BehavesLikeTheNeutralOnes()
    {
        AbilityCooldown omitted = AbilityCooldowns.Describe([Spell("Q", 10, 8)], Ranks(rank: 2), haste: 50)[0];
        AbilityCooldown neutral = AbilityCooldowns.Describe(
            [Spell("Q", 10, 8)],
            Ranks(rank: 2),
            haste: 50,
            CooldownOptions.None
        )[0];

        Assert.Equal(neutral.Current, omitted.Current);
    }
}
