using League.Models;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>A champion's spell numbers read from its game data; the expected values are Annie's in the fixture.</summary>
public class ChampionSpellScalingsTests
{
    private static readonly ChampionSpellScalings Annie = ChampionSpellScalings.Parse(
        CommunityDragonFixture.AnnieBin(),
        "Annie"
    );

    [Fact]
    public void Parse_CharacterRecord_FillsAllFiveSlots()
    {
        var slots = Annie.Slots;

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["P"] = "anniepassive",
                ["Q"] = "annieq",
                ["W"] = "anniew",
                ["E"] = "anniee",
                ["R"] = "annier",
            },
            slots
        );
    }

    [Fact]
    public void Parse_AnnieQ_KeepsValuesByRankWithLowerCaseNames()
    {
        var q = Annie.Slot("Q")!;

        Assert.Equal([35.0, 80, 125, 170, 215, 260, 305], q.Values["basedamage"]);
    }

    [Fact]
    public void Parse_AnnieQ_KeepsTheTooltipKeyAndCalculations()
    {
        var q = Annie.Slot("Q")!;

        Assert.Equal(("spell_annieq_tooltip", true), (q.TooltipKey, q.Calculations["totaldamage"] is JObject));
    }

    [Theory]
    [InlineData("Q", 5)]
    [InlineData("R", 3)]
    public void Parse_RankCount_FollowsTheTooltipData(string key, int ranks)
    {
        var spell = Annie.Slot(key)!;

        Assert.Equal(ranks, spell.Ranks);
    }

    [Fact]
    public void Parse_ManaCost_StartsAtRankOne()
    {
        var q = Annie.Slot("Q")!;

        Assert.Equal([60.0, 65, 70, 75, 80], q.Costs.Take(5));
    }

    [Fact]
    public void Parse_CostField_IsAlsoAValueTooltipsCanCite()
    {
        var q = Annie.Slot("Q")!;

        Assert.Equal(65, q.Value("Cost", 2, ""));
    }

    [Fact]
    public void Parse_MissingCharacterRecord_ReturnsNone()
    {
        var scalings = ChampionSpellScalings.Parse(new JObject(), "Annie");

        Assert.Same(ChampionSpellScalings.None, scalings);
    }
}
