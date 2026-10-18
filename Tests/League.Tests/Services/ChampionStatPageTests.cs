using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>The stat page the widget reads, built from Annie's fixture data and tooltips.</summary>
public class ChampionStatPageTests
{
    private static readonly JObject Champion = JObject.Parse(
        """{"alias":"Annie","passive":{"name":"嗜火"},"spells":[{"spellKey":"q","name":"碎裂之火"}]}"""
    );

    private static readonly StatPageSource Source = new(
        ChampionSpellFacts.None with
        {
            Record = ChampionRecord.Parse(CommunityDragonFixture.AnnieBin(), "Annie"),
            Scalings = ChampionSpellScalings.Parse(CommunityDragonFixture.AnnieBin(), "Annie"),
        },
        Champion,
        new(560, 96, 23, 4, 30, 1.3)
        {
            Mana = 418,
            ManaPerLevel = 25,
            Resource = "法力值",
        }
    );

    private static readonly Dictionary<string, string> AnnieTexts = JObject.Parse(
        File.ReadAllText(CommunityDragonFixture.AnnieStringTablePath)
    )["entries"]!
        .ToObject<Dictionary<string, string>>()!
        .Where(entry => SpellTextData.Keeps(entry.Key))
        .ToDictionary(entry => entry.Key, entry => entry.Value);

    private static JObject Spell(JObject page, string key) =>
        page["spells"]!.OfType<JObject>().Single(spell => (string?)spell["key"] == key);

    [Fact]
    public void Build_Spells_FollowThePassiveAndQWER()
    {
        var page = ChampionStatPage.Build(Source, new ChampionStatSheet(1, true), AnnieTexts, "");

        Assert.Equal(["P", "Q", "W", "E", "R"], page["spells"]!.Select(spell => (string?)spell["key"]));
    }

    [Fact]
    public void Build_AnnieQ_ComputesTheDamageWithAbilityPower()
    {
        var sheet = new ChampionStatSheet(9, true) { AbilityPower = 100 };

        var page = ChampionStatPage.Build(Source, sheet, AnnieTexts, "");

        Assert.Equal(
            ["160", "205", "250", "295", "340"],
            Spell(page, "Q")["segments"]!.Single(s => s["values"] != null)["values"]!.Values<string>()
        );
    }

    [Fact]
    public void Build_AnnieQ_NamesTheSpellsFromTheClientData()
    {
        var page = ChampionStatPage.Build(Source, new ChampionStatSheet(1, true), AnnieTexts, "");

        Assert.Equal(("嗜火", "碎裂之火"), ((string?)Spell(page, "P")["name"], (string?)Spell(page, "Q")["name"]));
    }

    [Fact]
    public void Build_ManaCosts_ListOnePerRank()
    {
        var page = ChampionStatPage.Build(Source, new ChampionStatSheet(1, true), AnnieTexts, "");

        Assert.Equal(["60", "65", "70", "75", "80"], Spell(page, "Q")["costs"]!.Values<string>());
    }

    [Fact]
    public void Build_TextsStillLoading_SaysSoAndLeavesTheSegmentsEmpty()
    {
        var page = ChampionStatPage.Build(Source, new ChampionStatSheet(1, true), null, "");

        Assert.Equal(("技能说明加载中…", 0), ((string?)page["textSource"], Spell(page, "Q")["segments"]!.Count()));
    }

    [Fact]
    public void Build_NoSheet_LeavesTheStatTableEmpty()
    {
        var page = ChampionStatPage.Build(Source, null, AnnieTexts, "");

        Assert.Empty(page["stats"]!);
    }

    [Fact]
    public void Build_EstimatedSheet_LeavesOutWhatOnly2999Reports()
    {
        var page = ChampionStatPage.Build(Source, new ChampionStatSheet(1, true), AnnieTexts, "");

        Assert.DoesNotContain("生命偷取", page["stats"]!.Select(row => (string?)row["name"]));
    }

    [Fact]
    public void Build_Resource_NamesTheManaRowAfterIt()
    {
        var sheet = new ChampionStatSheet(1, true) { Mana = new(418, 418) };

        var page = ChampionStatPage.Build(Source, sheet, AnnieTexts, "");

        Assert.Contains("法力值", page["stats"]!.Select(row => (string?)row["name"]));
    }
}
