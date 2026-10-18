using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// Slows read out of CommunityDragon's champion data: which values count as an enemy slow, how long each rank lasts,
/// and how a champion's level caps the rank it can have. The game documents below are the same simulated extraction
/// the old harness used — Q states six ranks in its tooltip data, R states none so an ultimate's three apply. The
/// expected values come from the checks in Tests/StatisticsChecks/ControlChecks.cs and CooldownChecks.cs, which this
/// class replaces.
/// </summary>
public class ChampionSlowSkillsTests
{
    [Fact]
    public void Parse_OnlyTheAbilitiesThatMentionASlow_AreRead()
    {
        SlowSkill[] slows = ChampionSlowSkills.Parse(Bin(), Champion());

        Assert.Equal(new[] { "Q", "W", "R" }, slows.Select(slow => slow.Key));
    }

    // Q also carries an attack-speed slow of 90 and R a self slow of 30; neither is an enemy slow, so the values
    // below come from the movement-speed slows alone. W is centred on the caster and so has no reach.
    [Theory]
    [InlineData("Q", 70, 900)]
    [InlineData("W", 70, 0)]
    [InlineData("R", 70, 1200)]
    public void Parse_TheTopRankSlowAndReach_AreRead(string key, int percent, int range)
    {
        SlowSkill slow = Slow(Bin(), key);

        Assert.Equal(percent, slow.Percent);
        Assert.Equal(range, slow.Range);
    }

    // Q's own slow duration wins over its general duration and its multiplier; W has only a cloud; the slow R
    // applies is its sub-spell's debuff, and the wall that outlasts it is kept apart.
    [Theory]
    [InlineData("Q", 2.5, 0)]
    [InlineData("W", 4, 0)]
    [InlineData("R", 2, 3)]
    public void Parse_TheDurationComesFromTheSlow_AndAZoneIsKeptApart(string key, double duration, double zone)
    {
        SlowSkill slow = Slow(Bin(), key);

        Assert.Equal(duration, slow.Duration);
        Assert.Equal(zone, slow.Zone);
    }

    [Fact]
    public void Parse_EachRankOfABasicAbility_KeepsItsOwnSlowAndDuration()
    {
        SlowSkill q = Slow(Bin(), "Q");

        Assert.Equal(
            new[] { "20/1", "30/1.5", "40/2", "50/2", "60/2", "70/2.5" },
            q.Ranks.Select(rank => $"{rank.Percent}/{rank.Duration}")
        );
    }

    [Fact]
    public void Parse_EachRankOfASubSpellSlow_KeepsItsZone()
    {
        SlowSkill r = Slow(Bin(), "R");

        Assert.Equal(
            new[] { "50/1/3", "60/1.5/3", "70/2/3" },
            r.Ranks.Select(rank => $"{rank.Percent}/{rank.Duration}/{rank.Zone}")
        );
    }

    [Fact]
    public void Parse_AnAbilityWithoutASlowValue_IsLeftOut()
    {
        var bin = Bin();
        bin["Characters/Test/Spells/TestWAbility/TestW"]!["mSpell"]!["DataValues"]![0]!["name"] = "SomethingElse";

        SlowSkill[] slows = ChampionSlowSkills.Parse(bin, Champion());

        Assert.Equal(new[] { "Q", "R" }, slows.Select(slow => slow.Key));
    }

    [Fact]
    public void Parse_AScriptNameTheChampionHasNoAbilityFor_IsLeftOut()
    {
        var champion = Champion();
        champion["spells"] = new JArray(
            ((JArray)champion["spells"]!).Where(spell => (string?)spell["spellKey"] != "q")
        );

        SlowSkill[] slows = ChampionSlowSkills.Parse(Bin(), champion);

        Assert.Equal(new[] { "W", "R" }, slows.Select(slow => slow.Key));
    }

    [Fact]
    public void Parse_AnAbilityWithoutAScriptName_IsLeftOut()
    {
        var bin = Bin();
        ((JArray)bin["Characters/Test/CharacterRecords/Root"]!["spellNames"]!)[0] = "";

        SlowSkill[] slows = ChampionSlowSkills.Parse(bin, Champion());

        Assert.Equal(new[] { "W", "R" }, slows.Select(slow => slow.Key));
    }

    [Fact]
    public void Parse_ReachFallsBackToTheSubSpell_WhenTheMainSpellHasNone()
    {
        var bin = Bin();
        SpellAt(bin, "TestQ")["mSpell"]!["castRange"] = Ranges(0);
        bin["Characters/Test/Spells/TestQAbility/TestQMine"] = Spell(
            700,
            ("SlowAmount", [0, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7])
        );

        SlowSkill q = Slow(bin, "Q");

        Assert.Equal(700, q.Range);
    }

    [Fact]
    public void Parse_ARangePastTheGlobalCap_IsNotAReach()
    {
        var bin = Bin();
        var champion = Champion();
        ((JObject)((JArray)champion["spells"]!)[2]!)["description"] = "造成减速。";
        SpellAt(bin, "TestE")["mSpell"]!["castRange"] = Ranges(25000);

        SlowSkill e = ChampionSlowSkills.Parse(bin, champion).Single(slow => slow.Key == "E");

        Assert.Equal(80, e.Percent);
        Assert.Equal(0, e.Range);
    }

    [Fact]
    public void Merge_RangedStrongSlows_JoinTheControlSkills()
    {
        var champion = Champion();
        ControlSkill[] merged = ChampionSlowSkills.Merge(
            [new("Q", "Q 技能", ["晕眩"], "")],
            ChampionSlowSkills.Parse(Bin(), champion),
            champion
        );

        Assert.Equal(
            new[] { "Q:晕眩:70:2.5:0", "R::70:2:3" },
            merged.Select(skill =>
                $"{skill.Key}:{string.Join("/", skill.Effects)}:{skill.Slow}:{skill.SlowDuration}:{skill.SlowZone}"
            )
        );
    }

    [Fact]
    public void Merge_ASlowForAnAbilityWithoutAControlSkill_TakesItsNameFromTheChampion()
    {
        var champion = Champion();
        ControlSkill[] merged = ChampionSlowSkills.Merge([], ChampionSlowSkills.Parse(Bin(), champion), champion);

        ControlSkill r = merged.Single(skill => skill.Key == "R");
        Assert.Equal("r 技能", r.Name);
        Assert.Equal("释放锁链，减速敌人。", r.Description);
    }

    [Fact]
    public void Merge_SlowsAndControlSkills_KeepAbilityOrder()
    {
        ControlSkill[] merged = ChampionSlowSkills.Merge(
            [new("E", "E 技能", [], "")],
            [new("R", 70, 1200), new("Q", 70, 900)],
            Champion()
        );

        Assert.Equal(new[] { "Q", "E", "R" }, merged.Select(skill => skill.Key));
    }

    [Theory]
    [InlineData("Q", 5, 1, 1)]
    [InlineData("Q", 5, 3, 2)]
    [InlineData("Q", 5, 8, 4)]
    [InlineData("Q", 5, 18, 5)]
    [InlineData("Q", 6, 11, 6)]
    [InlineData("Q", 5, 0, 0)]
    [InlineData("R", 3, 5, 0)]
    [InlineData("R", 3, 6, 1)]
    [InlineData("R", 3, 15, 2)]
    [InlineData("R", 3, 16, 3)]
    [InlineData("R", 4, 1, 1)]
    [InlineData("R", 4, 16, 4)]
    [InlineData("R", 6, 11, 6)]
    public void RankAt_KeyRanksAndLevel_CapsTheRankTheLevelAllows(string key, int ranks, int level, int expected)
    {
        int reached = ChampionSlowSkills.RankAt(key, ranks, level);

        Assert.Equal(expected, reached);
    }

    [Theory]
    [InlineData("R", 3, 6)]
    [InlineData("R", 1, 6)]
    [InlineData("R", 2, 6)]
    [InlineData("R", 0, 1)]
    [InlineData("R", 4, 1)]
    [InlineData("R", 6, 1)]
    [InlineData("Q", 5, 1)]
    public void FirstLevel_KeyAndRanks_SaysWhenTheAbilityOpens(string key, int ranks, int expected)
    {
        int level = ChampionSlowSkills.FirstLevel(key, ranks);

        Assert.Equal(expected, level);
    }

    [Fact]
    public void AtLevel_LevelWithinTheRanks_UsesTheHighestRankItAllows()
    {
        SlowAtLevel? at = ChampionSlowSkills.AtLevel(Ultimate(), 12);

        Assert.Equal(60, at!.Slow.Percent);
        Assert.Equal(2, at.Rank);
        Assert.Equal("12 级时该技能最高 2/3 级", at.Basis);
    }

    [Fact]
    public void AtLevel_LevelBeforeTheAbilityOpens_ShowsTheFirstRank()
    {
        SlowAtLevel? at = ChampionSlowSkills.AtLevel(Ultimate(), 4);

        Assert.Equal(50, at!.Slow.Percent);
        Assert.Equal(1, at.Rank);
        Assert.Equal("4 级还不能学（6 级可学），按 1 级显示", at.Basis);
    }

    [Fact]
    public void AtLevel_LevelUnknown_UsesTheTopRank()
    {
        SlowAtLevel? at = ChampionSlowSkills.AtLevel(Ultimate(), 0);

        Assert.Equal(70, at!.Slow.Percent);
        Assert.Equal(3, at.Rank);
        Assert.Equal("等级未知，按满级 3/3 级", at.Basis);
    }

    [Fact]
    public void AtLevel_SkillWithoutASlow_HasNothingToShow()
    {
        Assert.Null(ChampionSlowSkills.AtLevel(Ultimate() with { Slow = null }, 12));
    }

    [Fact]
    public void AtLevel_WithoutRanks_UsesTheTopRankValues()
    {
        SlowAtLevel? at = ChampionSlowSkills.AtLevel(Ultimate() with { SlowRanks = [] }, 12);

        Assert.Equal(70, at!.Slow.Percent);
        Assert.Equal(2, at.Slow.Duration);
        Assert.Equal(1, at.Rank);
        Assert.Equal("按满级数值", at.Basis);
    }

    [Fact]
    public void AtLevel_ARankWithoutASlowValue_UsesTheTopRankValues()
    {
        SlowAtLevel? at = ChampionSlowSkills.AtLevel(Ultimate() with { SlowRanks = [new(50, 1, 0), new(0, 1, 0)] }, 12);

        Assert.Equal(70, at!.Slow.Percent);
        Assert.Equal(2, at.Rank);
        Assert.Equal("按满级数值", at.Basis);
    }

    [Theory]
    [InlineData(70, 900, true)]
    [InlineData(40, 600, true)]
    [InlineData(40, 599, false)]
    [InlineData(39, 900, false)]
    [InlineData(100, 5000, true)]
    public void IsRangedStrong_PercentAndReach_SaysWhetherTheSlowLeadsThePage(int percent, int range, bool expected)
    {
        Assert.Equal(expected, ChampionSlowSkills.IsRangedStrong(new SlowSkill("Q", percent, range)));
    }

    [Theory]
    [InlineData(6, "Q", 6)]
    [InlineData(1, "Q", 1)]
    [InlineData(0, "Q", 5)]
    [InlineData(7, "Q", 5)]
    [InlineData(2, "Q", 2)]
    [InlineData(0, "R", 3)]
    [InlineData(0, "W", 5)]
    public void Ranks_TooltipRankCount_SaysHowManyRanksToRead(int levelCount, string key, int expected)
    {
        var spell = new JObject
        {
            ["mClientData"] = new JObject
            {
                ["mTooltipData"] = new JObject
                {
                    ["mLists"] = new JObject { ["LevelUp"] = new JObject { ["levelCount"] = levelCount } },
                },
            },
        };

        Assert.Equal(expected, ChampionSlowSkills.Ranks(spell, key));
    }

    [Fact]
    public void Ranks_WithoutASpell_UsesTheFallbackForItsKey()
    {
        Assert.Equal(3, ChampionSlowSkills.Ranks(null, "R"));
        Assert.Equal(5, ChampionSlowSkills.Ranks(null, "Q"));
    }

    [Fact]
    public void Ranks_TooltipRankCountThatIsNotANumber_FallsBack()
    {
        var spell = new JObject
        {
            ["mClientData"] = new JObject
            {
                ["mTooltipData"] = new JObject
                {
                    ["mLists"] = new JObject { ["LevelUp"] = new JObject { ["levelCount"] = "many" } },
                },
            },
        };

        Assert.Equal(5, ChampionSlowSkills.Ranks(spell, "Q"));
    }

    [Fact]
    public void ParseModes_TheUsualSlow_IsNotAMode()
    {
        SlowSkill[] slows = ChampionSlowSkills.Parse(ModeBin(), ModeChampion());

        Assert.Equal(50, slows.Single().Percent);
    }

    [Fact]
    public void ParseModes_ModesThatChangeTheSlow_AreKept()
    {
        var bin = ModeBin();
        var champion = ModeChampion();
        SlowSkill[] slows = ChampionSlowSkills.Parse(bin, champion);

        Dictionary<string, SlowSkill[]> modes = ChampionSlowSkills.ParseModes(bin, champion, slows);

        Assert.Equal(new[] { "aram", "kiwi" }, modes.Keys.Order());
        Assert.Equal(20, modes["aram"].Single().Percent);
    }

    [Fact]
    public void ParseModes_ModesThatLeaveTheSlowAlone_AreLeftOut()
    {
        var bin = ModeBin();
        var champion = ModeChampion();
        SlowSkill[] slows = ChampionSlowSkills.Parse(bin, champion);

        Dictionary<string, SlowSkill[]> modes = ChampionSlowSkills.ParseModes(bin, champion, slows);

        Assert.DoesNotContain("urf", modes.Keys);
    }

    private static SlowSkill Slow(JObject bin, string key) =>
        ChampionSlowSkills.Parse(bin, Champion()).Single(slow => slow.Key == key);

    private static ControlSkill Ultimate() =>
        ChampionSlowSkills
            .Merge([], ChampionSlowSkills.Parse(Bin(), Champion()), Champion())
            .Single(skill => skill.Key == "R");

    // E never mentions a slow, so it has none to read; the other three do.
    private static JObject Champion() =>
        new()
        {
            ["alias"] = "Test",
            ["spells"] = new JArray(
                Ability("q", "命中敌人并造成减速。"),
                Ability("w", "对周围敌人造成减速。"),
                Ability("e", "造成伤害。"),
                Ability("r", "释放锁链，减速敌人。")
            ),
        };

    private static JObject Ability(string key, string description) =>
        new()
        {
            ["spellKey"] = key,
            ["name"] = key + " 技能",
            ["description"] = description,
        };

    // Q: six ranks of 20-70% lasting 1-2.5 s, beside an attack-speed slow and a general duration.
    // W: 50-75% with a cloud that lasts four seconds. E: a slow nothing is said about.
    // R: a three-rank ultimate whose sub-spell slows, beside a wall, a self slow and a minute-long recast window.
    private static JObject Bin()
    {
        var bin = new JObject
        {
            ["Characters/Test/CharacterRecords/Root"] = new JObject
            {
                ["spellNames"] = new JArray("TestQ", "TestW", "TestE", "TestR"),
            },
            ["Characters/Test/Spells/TestQAbility/TestQ"] = Spell(
                900,
                ("SlowAmount", [0, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7]),
                ("AttackSlowPercent", [0, 90, 90, 90, 90, 90, 90]),
                ("SlowDuration", [0, 1, 1.5, 2, 2, 2, 2.5]),
                ("SlowDurationMultiplier", [0, 9, 9, 9, 9, 9, 9]),
                ("Duration", [0, 8, 8, 8, 8, 8, 8])
            ),
            ["Characters/Test/Spells/TestWAbility/TestW"] = Spell(
                0,
                ("SlowPercent", [0, 50, 55, 60, 65, 70, 75]),
                ("CloudDuration", [0, 4, 4, 4, 4, 4, 4])
            ),
            ["Characters/Test/Spells/TestEAbility/TestE"] = Spell(1000, ("SlowAmount", [0, 80, 80, 80, 80, 80, 80])),
            ["Characters/Test/Spells/TestRAbility/TestR"] = Spell(
                1200,
                ("SelfSlow", [0, 30, 30, 30, 30, 30, 30]),
                ("WallDuration", [0, 3, 3, 3, 3, 3, 3]),
                ("RecastDuration", [0, 60, 60, 60, 60, 60, 60])
            ),
            ["Characters/Test/Spells/TestRAbility/TestRMissile"] = Spell(
                25000,
                ("MoveSpeedMod", [0, -0.5, -0.6, -0.7, -0.9, -0.9, -0.9]),
                ("DebuffDuration", [0, 1, 1.5, 2, 2, 2, 2])
            ),
        };
        bin["Characters/Test/Spells/TestQAbility/TestQ"]!["mSpell"]!["mClientData"] = JObject.Parse(
            """{"mTooltipData":{"mLists":{"LevelUp":{"levelCount":6}}}}"""
        );
        return bin;
    }

    // One ability whose slow drops to 20% in ARAM and Mayhem, one with no override at all, an override naming a
    // value the usual data does not have, and two override keys that are not game modes.
    private static JObject ModeBin()
    {
        var bin = new JObject
        {
            ["Characters/Test/CharacterRecords/Root"] = new JObject { ["spellNames"] = new JArray("TestQ", "TestE") },
            ["Characters/Test/Spells/TestQAbility/TestQ"] = Spell(
                900,
                ("SlowAmount", [0, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5]),
                ("SlowDuration", [0, 2, 2, 2, 2, 2, 2])
            ),
            ["Characters/Test/Spells/TestEAbility/TestE"] = Spell(
                900,
                ("SlowAmount", [0, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4])
            ),
        };
        bin["Characters/Test/Spells/TestQAbility/TestQ"]!["mSpell"]!["DataValuesModeOverride"] = JObject.Parse(
            """
            {
              "__type": "ignored",
              "{a110bc47}": { "SpellDataValues": [{ "name": "SlowAmount", "values": [0, 0.9, 0.9, 0.9, 0.9, 0.9, 0.9] }] },
              "ARAM": { "SpellDataValues": [
                { "name": "SlowAmount", "values": [0, 0.2, 0.2, 0.2, 0.2, 0.2, 0.2] },
                { "name": "ExtraSlowAmount", "values": [0, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1] }] },
              "Kiwi": { "SpellDataValues": [{ "name": "SlowAmount", "values": [0, 0.2, 0.2, 0.2, 0.2, 0.2, 0.2] }] },
              "URF": { "SpellDataValues": [{ "name": "Unrelated", "values": [0, 1, 1, 1, 1, 1, 1] }] }
            }
            """
        );
        return bin;
    }

    private static JObject ModeChampion() =>
        new() { ["alias"] = "Test", ["spells"] = new JArray(Ability("q", "命中敌人并造成减速。")) };

    private static JObject Spell(int range, params (string Name, double[] Values)[] values) =>
        new()
        {
            ["mSpell"] = new JObject
            {
                ["castRange"] = Ranges(range),
                ["DataValues"] = new JArray(
                    values.Select(value => new JObject { ["name"] = value.Name, ["values"] = new JArray(value.Values) })
                ),
            },
        };

    private static JArray Ranges(int range) => new() { 0, range, range, range, range, range, range };

    private static JObject SpellAt(JObject bin, string script) =>
        (JObject)
            bin.Properties().Single(property => property.Name.EndsWith("/" + script, StringComparison.Ordinal)).Value;
}
