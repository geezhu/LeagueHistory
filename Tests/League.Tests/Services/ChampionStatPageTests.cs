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

    [Theory]
    [InlineData("暴击几率", "crit")]
    [InlineData("攻击力", "ad")]
    [InlineData("技能急速", "haste")]
    [InlineData("法力值", "mana")]
    public void Build_AStatRow_NamesItsIconInTheGamesPanel(string name, string icon)
    {
        var sheet = new ChampionStatSheet(1, true) { Mana = new(418, 418) };

        var page = ChampionStatPage.Build(Source, sheet, AnnieTexts, "");

        Assert.Equal(icon, (string?)page["stats"]!.Single(row => (string?)row["name"] == name)["icon"]);
    }

    [Fact]
    public void Build_EveryStatRowIcon_IsOneTheAtlasCuts()
    {
        using var atlas = new Bitmap(StatIconAtlas.AtlasSize, StatIconAtlas.AtlasSize);
        var cut = StatIconAtlas.Cut(atlas);

        var page = ChampionStatPage.Build(Source, new ChampionStatSheet(1, false), AnnieTexts, "");

        Assert.All(
            page["stats"]!.Select(row => (string?)row["icon"]).Where(icon => icon is { Length: > 0 }),
            icon => Assert.True(cut.ContainsKey(icon!), icon)
        );
    }

    [Theory]
    [InlineData("法力值", "mana")]
    [InlineData("", "mana")]
    [InlineData("能量", "energy")]
    [InlineData("怒气", "fury")]
    [InlineData("热量", "fury")]
    public void ResourceBar_TheResource_IsColouredAsTheGamesHud(string resource, string bar)
    {
        Assert.Equal(bar, ChampionStatPage.ResourceBar(resource));
    }

    private static readonly StatPageSource Tryndamere = Source with
    {
        BaseStats = Source.BaseStats! with { Resource = "怒气" },
    };

    [Fact]
    public void Build_Fury_HasNoRegenerationRow()
    {
        var sheet = new ChampionStatSheet(1, true) { Mana = new(100, 100) };

        var page = ChampionStatPage.Build(Tryndamere, sheet, AnnieTexts, "");

        Assert.DoesNotContain("怒气回复", page["stats"]!.Select(row => (string?)row["name"]));
    }

    [Fact]
    public void Build_Fury_IsARedBarWithoutTheManaIcon()
    {
        var sheet = new ChampionStatSheet(1, true) { Mana = new(100, 100) };

        var page = ChampionStatPage.Build(Tryndamere, sheet, AnnieTexts, "");

        var fury = page["stats"]!.Single(row => (string?)row["name"] == "怒气");
        Assert.Equal(("fury", ""), ((string?)fury["bar"], (string?)fury["icon"]));
    }

    [Fact]
    public void Build_Health_IsTheHealthBar()
    {
        var page = ChampionStatPage.Build(Source, new ChampionStatSheet(1, true), AnnieTexts, "");

        Assert.Equal("health", (string?)page["stats"]!.Single(row => (string?)row["name"] == "生命值")["bar"]);
    }
}
