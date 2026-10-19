using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// Cooldowns read out of CommunityDragon's champion data: one entry per ability, in QWER order, from the spell the
/// character record names.
///
/// Three things about the shape of that data drive the rules pinned here. The ability's key comes from its position in
/// the record's spell list - the first script is Q, the fourth is R - not from the script's own name. A spell's ranked
/// values carry an extra leading entry, so the first value is dropped and the rest are the ranks. And the extraction
/// leaves out values equal to the game's own default, so a spell with no cooldowns is ten seconds.
/// </summary>
public class ChampionCooldownsTests
{
    /// <summary>Builds the two documents <c>Parse</c> reads: the bin keyed by script, and the champion record.</summary>
    private static (JObject Bin, JObject Champion) Data(
        string[] scripts,
        params (string Script, JObject Spell)[] spells
    )
    {
        var bin = new JObject
        {
            ["Characters/TEST/CharacterRecords/Root"] = new JObject { ["spellNames"] = new JArray(scripts) },
        };
        foreach ((string script, JObject spell) in spells)
        {
            bin["Characters/TEST/Spells/" + script] = new JObject { ["mSpell"] = spell };
        }

        // The record's spell keys are the ability keys the parser looks names up by, so they match the script names.
        var champion = new JObject
        {
            ["alias"] = "TEST",
            ["spells"] = new JArray(
                spells.Select(s => new JObject { ["spellKey"] = s.Script, ["name"] = "名字" + s.Script })
            ),
        };
        return (bin, champion);
    }

    /// <summary>A spell whose ranked values are the given ranks, preceded by the leading entry the extraction writes.</summary>
    private static JObject Spell(params double[] ranks) =>
        new()
        {
            ["Cooldown"] = new JObject { ["values"] = new JArray(new[] { 0d }.Concat(ranks).Cast<object>().ToArray()) },
        };

    /// <summary>Declares how many ranks the ability has, the way the tooltip data does.</summary>
    private static JObject WithRanks(JObject spell, int rankCount)
    {
        spell["mClientData"] = new JObject
        {
            ["mTooltipData"] = new JObject
            {
                ["mLists"] = new JObject { ["LevelUp"] = new JObject { ["levelCount"] = rankCount } },
            },
        };
        return spell;
    }

    private static JObject WithValues(JObject spell, params object[] values) => WithValues(spell, "Cooldown", values);

    private static JObject WithValues(JObject spell, string field, params object[] values)
    {
        spell[field] = new JObject { ["values"] = new JArray(values) };
        return spell;
    }

    // ---- the ability list ----

    [Fact]
    public void Parse_FourScripts_ProduceFourAbilitiesInQwerOrder()
    {
        (JObject bin, JObject champion) = Data(
            ["Q", "W", "E", "R"],
            ("Q", Spell(1, 2, 3, 4, 5)),
            ("W", Spell(2, 3, 4, 5, 6)),
            ("E", Spell(3, 4, 5, 6, 7)),
            ("R", Spell(4, 5, 6))
        );

        SpellCooldown[] parsed = ChampionCooldowns.Parse(bin, champion);

        Assert.Equal(["Q", "W", "E", "R"], parsed.Select(s => s.Key));
    }

    [Fact]
    public void Parse_TheKeyComesFromThePositionNotFromTheScriptsName()
    {
        // A record that names its spells oddly still yields Q, W, E and R in order.
        (JObject bin, JObject champion) = Data(["SpellA", "SpellB"], ("SpellA", Spell(1, 2)), ("SpellB", Spell(3, 4)));

        Assert.Equal(["Q", "W"], ChampionCooldowns.Parse(bin, champion).Select(s => s.Key));
    }

    [Fact]
    public void Parse_MoreThanFourScripts_ReadsOnlyTheFirstFour()
    {
        // A fifth script has no key to be named by, so it is not read even if its spell is there.
        (JObject bin, JObject champion) = Data(
            ["Q", "W", "E", "R", "X"],
            ("Q", Spell(1)),
            ("W", Spell(1)),
            ("E", Spell(1)),
            ("R", Spell(1)),
            ("X", Spell(9))
        );

        Assert.Equal(4, ChampionCooldowns.Parse(bin, champion).Length);
    }

    [Fact]
    public void Parse_AScriptWithoutASpell_LeavesThatAbilityOut()
    {
        // A missing spell skips the ability rather than emitting an empty one, so the list can be shorter than four.
        (JObject bin, JObject champion) = Data(["Q", "W", "E"], ("Q", Spell(1, 2)), ("E", Spell(3, 4)));

        Assert.Equal(["Q", "E"], ChampionCooldowns.Parse(bin, champion).Select(s => s.Key));
    }

    [Fact]
    public void Parse_NoScriptsAtAll_ProducesNothing()
    {
        (JObject bin, JObject champion) = Data([], ("Q", Spell(1)));

        Assert.Empty(ChampionCooldowns.Parse(bin, champion));
    }

    [Fact]
    public void Parse_TheAbilityIsFoundByTheScriptTheRecordNames()
    {
        // The bin is keyed by path, so the match is on the tail of the key, not on equality.
        (JObject bin, JObject champion) = Data(["Q"], ("Q", Spell(7, 8)));

        Assert.Equal(7, Assert.Single(ChampionCooldowns.Parse(bin, champion)).Cooldowns[0]);
    }

    [Fact]
    public void Parse_TheNameComesFromTheChampionRecord()
    {
        (JObject bin, JObject champion) = Data(["Q"], ("Q", Spell(1, 2)));
        champion["spells"] = new JArray(new JObject { ["spellKey"] = "q", ["name"] = "秘术射击" });

        Assert.Equal("秘术射击", Assert.Single(ChampionCooldowns.Parse(bin, champion)).Name);
    }

    [Fact]
    public void Parse_ASpellTheRecordDoesNotName_FallsBackToTheKey()
    {
        (JObject bin, JObject champion) = Data(["Q"], ("Q", Spell(1, 2)));
        champion["spells"] = new JArray();

        Assert.Equal("Q", Assert.Single(ChampionCooldowns.Parse(bin, champion)).Name);
    }

    // ---- which values become the ranks ----

    [Fact]
    public void Parse_TheLeadingValueIsDroppedAndTheRestAreTheRanks()
    {
        // The extraction's first entry is not a rank; index 1 is rank one.
        (JObject bin, JObject champion) = Data(["W"], ("W", WithRanks(Spell(11, 12, 13), 3)));

        Assert.Equal([11d, 12d, 13d], Assert.Single(ChampionCooldowns.Parse(bin, champion)).Cooldowns);
    }

    [Fact]
    public void Parse_AnUltimateWithThreeRanks_IsCutToThreeAndAFiveRankAbilityToFive()
    {
        (JObject bin, JObject champion) = Data(
            ["Q", "R"],
            ("Q", WithRanks(Spell(1, 2, 3, 4, 5, 6), 5)),
            ("R", WithRanks(Spell(1, 2, 3, 4, 5, 6), 3))
        );

        SpellCooldown[] parsed = ChampionCooldowns.Parse(bin, champion);

        Assert.Equal(5, parsed[0].Cooldowns.Length);
        Assert.Equal(3, parsed[1].Cooldowns.Length);
    }

    [Fact]
    public void Parse_WithoutATooltipCount_AnUltimateGetsThreeRanksAndTheRestFive()
    {
        // The client's own maxLevel is always 0, so the fallback is by key - and the key is the ability's position,
        // which is why the ultimate has to sit fourth in the record to be read as one.
        (JObject bin, JObject champion) = Data(
            ["Q", "W", "E", "R"],
            ("Q", Spell(1, 2, 3, 4, 5, 6)),
            ("W", Spell(1, 2, 3, 4, 5, 6)),
            ("E", Spell(1, 2, 3, 4, 5, 6)),
            ("R", Spell(1, 2, 3, 4, 5, 6))
        );

        SpellCooldown[] parsed = ChampionCooldowns.Parse(bin, champion);

        Assert.Equal(["Q", "W", "E", "R"], parsed.Select(s => s.Key));
        Assert.Equal([5, 5, 5, 3], parsed.Select(s => s.Cooldowns.Length));
    }

    [Theory]
    [InlineData(0.4, 0.4)]
    [InlineData(0.4000000059604645, 0.4)]
    [InlineData(1.23456, 1.235)]
    public void Parse_SinglePrecisionValues_AreRoundedToThreeDecimals(double stored, double expected)
    {
        (JObject bin, JObject champion) = Data(["Q"], ("Q", WithRanks(Spell(stored), 1)));

        Assert.Equal(expected, Assert.Single(ChampionCooldowns.Parse(bin, champion)).Cooldowns[0]);
    }

    [Fact]
    public void Parse_ANegativeValue_IsClampedToZero()
    {
        (JObject bin, JObject champion) = Data(["Q"], ("Q", WithRanks(Spell(-5), 1)));

        Assert.Equal(0, Assert.Single(ChampionCooldowns.Parse(bin, champion)).Cooldowns[0]);
    }

    [Fact]
    public void Parse_ANonNumericValue_ReadsAsZero()
    {
        // A null in the array is not a number, so it becomes zero rather than throwing.
        (JObject bin, JObject champion) = Data(
            ["Q"],
            ("Q", WithRanks(WithValues(new JObject(), 0d, 5d, JValue.CreateNull(), 6d), 3))
        );

        Assert.Equal([5d, 0d, 6d], Assert.Single(ChampionCooldowns.Parse(bin, champion)).Cooldowns);
    }

    // ---- the two cooldown fields ----

    [Fact]
    public void Parse_TheCurrentFieldIsPreferredOverTheLegacyOne()
    {
        // cooldownTime is still carried by some spells with outdated numbers, so it is only the fallback.
        JObject spell = Spell(120, 110, 100);
        spell["cooldownTime"] = new JArray(0d, 140, 120, 100);
        (JObject bin, JObject champion) = Data(["R"], ("R", WithRanks(spell, 3)));

        Assert.Equal([120d, 110d, 100d], Assert.Single(ChampionCooldowns.Parse(bin, champion)).Cooldowns);
    }

    [Fact]
    public void Parse_WithoutTheCurrentField_TheLegacyOneIsUsed()
    {
        JObject spell = new() { ["cooldownTime"] = new JArray(0d, 16, 15, 14) };
        (JObject bin, JObject champion) = Data(["Q"], ("Q", WithRanks(spell, 3)));

        Assert.Equal([16d, 15d, 14d], Assert.Single(ChampionCooldowns.Parse(bin, champion)).Cooldowns);
    }

    [Fact]
    public void Parse_WithoutEitherField_TenSecondsIsUsedForEveryRank()
    {
        // The extraction omits values equal to the game's default, which is what Data Dragon also lists.
        (JObject bin, JObject champion) = Data(["Q"], ("Q", WithRanks(new JObject(), 3)));

        Assert.Equal([10d, 10d, 10d], Assert.Single(ChampionCooldowns.Parse(bin, champion)).Cooldowns);
    }

    // ---- charges ----

    [Fact]
    public void Parse_ASpellWithARecharge_CarriesItsCharges()
    {
        JObject spell = Spell(3, 3, 3);
        spell["mAmmoRechargeTime"] = new JArray(0d, 8, 7, 6);
        spell["mMaxAmmo"] = new JArray(0d, 2, 2, 2);
        (JObject bin, JObject champion) = Data(["Q"], ("Q", WithRanks(spell, 3)));

        SpellCooldown parsed = Assert.Single(ChampionCooldowns.Parse(bin, champion));

        Assert.Equal([8d, 7d, 6d], parsed.Recharge);
        Assert.Equal([2, 2, 2], parsed.Charges);
    }

    [Fact]
    public void Parse_ARechargeOfZeroEverywhere_LeavesChargesAndRechargeEmpty()
    {
        // A zero recharge is not a charge mechanic, so both lists stay empty rather than carrying zeroes about.
        JObject spell = Spell(3, 3);
        spell["mAmmoRechargeTime"] = new JArray(0d, 0, 0);
        spell["mMaxAmmo"] = new JArray(0d, 2, 2);
        (JObject bin, JObject champion) = Data(["Q"], ("Q", WithRanks(spell, 2)));

        SpellCooldown parsed = Assert.Single(ChampionCooldowns.Parse(bin, champion));

        Assert.Empty(parsed.Recharge);
        Assert.Empty(parsed.Charges);
    }

    [Fact]
    public void Parse_AmmoThatHasteDoesNotShorten_IsNotARecharge()
    {
        // Rakan E, Cho'Gath E and LeBlanc R hold ammo for a recast or passive, which is not the ability's cooldown.
        JObject spell = Spell(3, 3);
        spell["mAmmoRechargeTime"] = new JArray(0d, 8, 7);
        spell["mMaxAmmo"] = new JArray(0d, 2, 2);
        spell["mAmmoNotAffectedByCDR"] = true;
        (JObject bin, JObject champion) = Data(["Q"], ("Q", WithRanks(spell, 2)));

        SpellCooldown parsed = Assert.Single(ChampionCooldowns.Parse(bin, champion));

        Assert.Empty(parsed.Recharge);
        Assert.Empty(parsed.Charges);
    }

    // ---- per-mode cooldowns ----

    [Fact]
    public void Parse_ModeCooldowns_AreKeyedInLowerCase()
    {
        JObject spell = Spell(20, 19);
        spell["{f9c2333e}"] = new JObject
        {
            ["ARAM"] = new JObject { ["value"] = new JArray(0d, 16, 15) },
            ["kiwi"] = new JObject { ["value"] = new JArray(0d, 12, 11) },
        };
        (JObject bin, JObject champion) = Data(["Q"], ("Q", WithRanks(spell, 2)));

        SpellCooldown parsed = Assert.Single(ChampionCooldowns.Parse(bin, champion));

        Assert.Equal([16d, 15d], parsed.Modes["aram"]);
        Assert.Equal([12d, 11d], parsed.Modes["kiwi"]);
    }

    [Fact]
    public void Parse_TheTypeMarkerAndBracedEntries_AreNotModes()
    {
        JObject spell = Spell(20, 19);
        spell["{f9c2333e}"] = new JObject
        {
            ["__type"] = "Some.Type",
            ["{abc}"] = new JObject { ["value"] = new JArray(0d, 1, 1) },
            ["aram"] = new JObject { ["value"] = new JArray(0d, 16, 15) },
        };
        (JObject bin, JObject champion) = Data(["Q"], ("Q", WithRanks(spell, 2)));

        Assert.Equal(["aram"], Assert.Single(ChampionCooldowns.Parse(bin, champion)).Modes.Keys);
    }

    [Fact]
    public void Parse_AModeWithoutAValueArray_IsSkipped()
    {
        JObject spell = Spell(20, 19);
        spell["{f9c2333e}"] = new JObject
        {
            ["broken"] = new JObject { ["value"] = "not an array" },
            ["aram"] = new JObject { ["value"] = new JArray(0d, 16, 15) },
        };
        (JObject bin, JObject champion) = Data(["Q"], ("Q", WithRanks(spell, 2)));

        Assert.Equal(["aram"], Assert.Single(ChampionCooldowns.Parse(bin, champion)).Modes.Keys);
    }

    [Fact]
    public void Parse_WithoutTheModeField_ThereAreNoModeCooldowns()
    {
        (JObject bin, JObject champion) = Data(["Q"], ("Q", WithRanks(Spell(20, 19), 2)));

        Assert.Empty(Assert.Single(ChampionCooldowns.Parse(bin, champion)).Modes);
    }

    // ---- ForMode ----

    [Fact]
    public void ForMode_AModeTheAbilityHas_ReturnsItsOwnCooldowns()
    {
        SpellCooldown spell = new("Q", "秘术射击", [20, 19, 18])
        {
            Modes = new Dictionary<string, double[]> { ["aram"] = [16, 15, 14] },
        };

        Assert.Equal([16d, 15d, 14d], spell.ForMode("aram").Cooldowns);
    }

    [Fact]
    public void ForMode_TheModeNameIsMatchedWhateverItsCase()
    {
        SpellCooldown spell = new("Q", "秘术射击", [20, 19, 18])
        {
            Modes = new Dictionary<string, double[]> { ["aram"] = [16, 15, 14] },
        };

        Assert.Equal([16d, 15d, 14d], spell.ForMode("ARAM").Cooldowns);
    }

    [Fact]
    public void ForMode_AModeTheAbilityDoesNotHave_ReturnsTheUsualCooldowns()
    {
        SpellCooldown spell = new("Q", "秘术射击", [20, 19, 18]);

        Assert.Same(spell, spell.ForMode("urf"));
        Assert.Equal([20d, 19d, 18d], spell.ForMode("urf").Cooldowns);
    }

    [Fact]
    public void ForMode_KeepsTheOtherMembersOfTheAbility()
    {
        // The mode changes only the cooldowns; the key, name, charges and the mode table itself stay as they were.
        SpellCooldown spell = new("Q", "秘术射击", [20, 19, 18])
        {
            Modes = new Dictionary<string, double[]> { ["aram"] = [16, 15, 14] },
            Recharge = [8, 7, 6],
            Charges = [2, 2, 2],
        };

        SpellCooldown inAram = spell.ForMode("aram");

        Assert.Equal("Q", inAram.Key);
        Assert.Equal("秘术射击", inAram.Name);
        Assert.Equal([8d, 7d, 6d], inAram.Recharge);
        Assert.Equal([2, 2, 2], inAram.Charges);
        Assert.Equal(["aram"], inAram.Modes.Keys);
    }
}
