using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The "本局" detail tab: which sections a match produces, what their items say, and how the three value formatters
/// print item gold, the lead and a share. Everything is in memory: the input is a record built here, the output a
/// JArray, and no client, game or disk is read.
/// </summary>
public class LivePanelSectionsTests
{
    private static readonly LiveChampionStats Champion = new(2000, 100, 100, 25, 12, 0.18, 0, 0);
    private static readonly LivePlayerInsight Self = new(5200, 5, 2, 9, 0.78, 0);

    [Fact]
    public void Tab_TheDetailTabTheWidgetReads_StaysAtLive()
    {
        Assert.Equal("live", LivePanelSections.Tab);
    }

    [Theory]
    [InlineData(0, "0%")]
    [InlineData(0.125, "13%")]
    [InlineData(0.5, "50%")]
    [InlineData(0.543, "54%")]
    [InlineData(0.78, "78%")]
    [InlineData(1, "100%")]
    public void Percent_AShareOfOne_PrintsWholePercentWithTheSignAppended(double value, string expected)
    {
        Assert.Equal(expected, LivePanelSections.Percent(value));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1.0k")]
    [InlineData(2300, "2.3k")]
    [InlineData(2400, "2.4k")]
    [InlineData(41200, "41.2k")]
    public void Gold_AnItemValue_ShortensAThousandOrMoreToOneDecimalAndK(int value, string expected)
    {
        Assert.Equal(expected, LivePanelSections.Gold(value));
    }

    [Fact]
    public void Gold_ANegativeItemValue_KeepsTheRawDigitsWithoutTheKForm()
    {
        Assert.Equal("-1500", LivePanelSections.Gold(-1500));
    }

    [Theory]
    [InlineData(0, "经济持平")]
    [InlineData(1, "领先 1")]
    [InlineData(500, "领先 500")]
    [InlineData(1000, "领先 1.0k")]
    [InlineData(2400, "领先 2.4k")]
    [InlineData(-1, "落后 1")]
    [InlineData(-2300, "落后 2.3k")]
    public void Lead_AnItemGoldDifference_NamesWhoIsAhead(int lead, string expected)
    {
        Assert.Equal(expected, LivePanelSections.Lead(lead));
    }

    [Fact]
    public void Build_AFinishedMatchWithEveryInput_ListsTheSectionsInOrder()
    {
        var input = Panel(
            Match("Win"),
            self: Self,
            defense: LiveDefenseAdvice.Calculate(Champion, 0.9),
            build: new LiveBuildAdvice(new LiveBuildOption([3071, 3053, 3742], 0.543, 0.12, 1800), 1, 3053),
            owned: [3071]
        );

        JArray sections = LivePanelSections.Build(input, LayoutItem);

        Assert.Equal(["本局结束", "战况", "防装", "出装"], sections.Select(section => (string?)section["title"]));
        Assert.All(sections, section => Assert.Equal(LivePanelSections.Tab, (string?)section["tab"]));
    }

    [Fact]
    public void Build_AMatchWithoutOptionalInputs_ShowsOnlyTheBattle()
    {
        JArray sections = LivePanelSections.Build(Panel(Match()), LayoutItem);

        Assert.Equal(["战况"], sections.Select(section => (string?)section["title"]));
    }

    [Theory]
    [InlineData("Win", "胜利", "good")]
    [InlineData("Lose", "失败", "bad")]
    [InlineData("Remake", "Remake", "")]
    public void Build_AMatchThatEnded_NamesTheResultTheToneAndTheScore(string result, string expected, string tone)
    {
        JArray sections = LivePanelSections.Build(Panel(Match(result), self: Self), LayoutItem);

        JObject tile = Item(sections, "本局结束", "结果");

        Assert.Equal(
            (expected, tone, "你 5/2/9"),
            ((string?)tile["value"], (string?)tile["tone"], (string?)tile["stats"])
        );
    }

    [Fact]
    public void Build_TheEnding_PointsAtTheHistoryPage()
    {
        JArray sections = LivePanelSections.Build(Panel(Match("Win")), LayoutItem);

        Assert.Equal("完整数据在结算后进入战绩，可在主程序战绩页查看。", (string?)Card(sections, "本局结束")["tip"]);
    }

    [Fact]
    public void Build_TheBattle_IsAStrip()
    {
        JArray sections = LivePanelSections.Build(Panel(Match()), LayoutItem);

        Assert.Equal(LivePanelSections.Strip, (string?)Card(sections, "战况")["layout"]);
    }

    [Fact]
    public void Build_TheBattle_ComparesKillsAndGoldOnTheFirstLine()
    {
        JArray sections = LivePanelSections.Build(Panel(Match()), LayoutItem);

        Assert.Equal(
            ["击杀 18 : 14 good 0", "经济 领先 2.3k good 0", "团战 2 胜 1 负 good 0"],
            Rows(sections, "战况").Select(Describe)
        );
    }

    [Fact]
    public void Build_TheBattle_DatesTheMostRecentFight()
    {
        JArray sections = LivePanelSections.Build(Panel(Match()), LayoutItem);

        Assert.Equal("最近 9:48 杀 4 亡 2", (string?)Item(sections, "战况", "团战")["stats"]);
    }

    [Fact]
    public void Build_TheBattle_PutsBothTeamsItemGoldInTheEconomyHover()
    {
        JArray sections = LivePanelSections.Build(Panel(Match()), LayoutItem);

        Assert.StartsWith("装备总价 41.2k : 38.9k", (string?)Item(sections, "战况", "经济")["description"]);
    }

    [Fact]
    public void Build_AMatchWithoutFights_SaysThereAreNoneYet()
    {
        JArray sections = LivePanelSections.Build(Panel(Match(fights: [])), LayoutItem);

        Assert.Equal(
            ("暂无", ""),
            ((string?)Item(sections, "战况", "团战")["value"], (string?)Item(sections, "战况", "团战")["stats"])
        );
    }

    [Fact]
    public void Build_ThePlayer_IsTheBattlesSecondLine()
    {
        JArray sections = LivePanelSections.Build(Panel(Match(), self: Self), LayoutItem);

        Assert.Equal(["你 5/2/9  1", "参团 78%  1", "装备 5.2k  1"], Rows(sections, "战况").Skip(3).Select(Describe));
    }

    [Fact]
    public void Build_APlayerWithoutKillsOrParticipation_ShowsDashes()
    {
        var self = new LivePlayerInsight(0, null, null, null, null, 0);

        JArray sections = LivePanelSections.Build(Panel(Match(), self: self), LayoutItem);

        Assert.Equal(
            ("—", "—"),
            ((string?)Item(sections, "战况", "你")["value"], (string?)Item(sections, "战况", "参团")["value"])
        );
    }

    [Theory]
    [InlineData(0.9, "下一件优先护甲")]
    [InlineData(0.5, "护甲魔抗收益接近")]
    [InlineData(0.1, "下一件优先魔抗")]
    public void Build_AnEnemyDamageMix_StatesTheResistanceToBuy(double share, string expected)
    {
        JArray sections = LivePanelSections.Build(
            Panel(Match(), defense: LiveDefenseAdvice.Calculate(Champion, share)),
            LayoutItem
        );

        Assert.Equal(expected, (string?)Card(sections, "防装")["verdict"]);
    }

    [Fact]
    public void Build_TheDefense_SplitsTheEnemysDamage()
    {
        JArray sections = LivePanelSections.Build(
            Panel(Match(), defense: LiveDefenseAdvice.Calculate(Champion, 0.9)),
            LayoutItem
        );

        JObject card = Card(sections, "防装");

        Assert.Equal(
            (LivePanelSections.Split, 0.9, "物理 90%", "魔法 10%"),
            ((string?)card["layout"], (double?)card["share"], (string?)card["left"], (string?)card["right"])
        );
    }

    [Fact]
    public void Build_TheDefense_ListsEffectiveHealthAndWhatEachResistanceAdds()
    {
        JArray sections = LivePanelSections.Build(
            Panel(Match(), defense: LiveDefenseAdvice.Calculate(Champion, 0.9)),
            LayoutItem
        );

        Assert.Equal(
            ["有效生命 物理 4000 · 魔法 4000  0", "+10 护甲 多抵 179  0", "+10 魔抗 多抵 19  0"],
            Rows(sections, "防装").Select(Describe)
        );
    }

    [Fact]
    public void Build_ADefenseWithoutTheDamageMix_ShowsOnlyEffectiveHealthAndSaysWhy()
    {
        JArray sections = LivePanelSections.Build(
            Panel(Match(), defense: LiveDefenseAdvice.Calculate(Champion, null)),
            LayoutItem
        );

        JObject card = Card(sections, "防装");

        Assert.Equal(
            (LivePanelSections.Strip, "敌方伤害构成暂不可用，仅显示当前有效生命。"),
            ((string?)card["layout"], (string?)card["tip"])
        );
    }

    [Fact]
    public void Build_TheEquipment_NamesTheNextItemThroughTheDelegate()
    {
        var advice = new LiveBuildAdvice(new LiveBuildOption([3071, 3053, 3742], 0.543, 0.12, 1800), 1, 3053);

        JArray sections = LivePanelSections.Build(Panel(Match(), build: advice, owned: [3071]), LayoutItem);

        Assert.Equal("布局装备 3053", (string?)Card(sections, "出装")["nextName"]);
    }

    [Fact]
    public void Build_TheEquipment_CarriesTheNextItemsIcon()
    {
        var advice = new LiveBuildAdvice(new LiveBuildOption([3071, 3053], 0.5, 0.1, 100), 1, 3053);

        JArray sections = LivePanelSections.Build(Panel(Match(), build: advice, owned: [3071]), LayoutItem);

        JObject next = (JObject)Card(sections, "出装")["next"]!;

        Assert.Equal(("item", 3053), ((string?)next["kind"], (int?)next["id"]));
    }

    [Fact]
    public void Build_ACompletedBuild_SaysThereIsNothingLeftToBuy()
    {
        var advice = new LiveBuildAdvice(new LiveBuildOption([3071], 0.5, 0.1, 100), 1, null);

        JArray sections = LivePanelSections.Build(Panel(Match(), build: advice, owned: [3071]), LayoutItem);

        Assert.Equal(
            ("已全部完成", null),
            ((string?)Card(sections, "出装")["nextName"], Card(sections, "出装")["next"])
        );
    }

    [Fact]
    public void Build_TheEquipment_ShowsProgressAndTheReferenceRates()
    {
        var advice = new LiveBuildAdvice(new LiveBuildOption([3071, 3053, 3742], 0.543, 0.12, 1800), 1, 3053);

        JArray sections = LivePanelSections.Build(Panel(Match(), build: advice), LayoutItem);

        Assert.Equal(["进度 1/3  0", "参照组合 胜率 54%  0"], Rows(sections, "出装").Select(Describe));
    }

    [Fact]
    public void Build_AReferenceBuildWithoutRates_LeavesTheNumbersOut()
    {
        var advice = new LiveBuildAdvice(new LiveBuildOption([3071], null, null, null), 0, 3071);

        JArray sections = LivePanelSections.Build(Panel(Match(), build: advice), LayoutItem);

        Assert.Equal(
            ("胜率 —", ""),
            ((string?)Item(sections, "出装", "参照组合")["value"], (string?)Item(sections, "出装", "参照组合")["stats"])
        );
    }

    [Fact]
    public void Build_TheEquipment_MarksTheItemsThePlayerOwns()
    {
        var advice = new LiveBuildAdvice(new LiveBuildOption([3071, 3053, 3742], 0.543, 0.12, 1800), 1, 3053);

        JArray sections = LivePanelSections.Build(Panel(Match(), build: advice, owned: [3071]), LayoutItem);

        Assert.Equal(
            [true, false, false],
            ((JArray)Card(sections, "出装")["icons"]!).Select(icon => (bool)icon["owned"]!)
        );
    }

    /// <summary>The item name the page is handed; the delegate decides what the build card calls the next item.</summary>
    private static string LayoutItem(int id) => "布局装备 " + id;

    private static LiveMatchInsight Match(string result = "", LiveFight[]? fights = null) =>
        new(
            612,
            new(18, 41200, 1, 3, 3),
            new(14, 38900, 0, 1, 2),
            new Dictionary<string, LivePlayerInsight>(),
            fights ?? [new(301, 3, 1), new(455, 1, 3), new(588, 4, 2)],
            result
        );

    private static LivePanelInput Panel(
        LiveMatchInsight insight,
        LivePlayerInsight? self = null,
        LiveDefenseAdvice? defense = null,
        LiveBuildAdvice? build = null,
        int[]? owned = null
    ) => new(insight, self, defense, build, owned ?? []);

    private static JObject Card(JArray sections, string title) =>
        sections.OfType<JObject>().Single(section => (string?)section["title"] == title);

    private static IEnumerable<JObject> Rows(JArray sections, string card) =>
        ((JArray)Card(sections, card)["rows"]!).OfType<JObject>();

    private static JObject Item(JArray sections, string card, string title) =>
        Rows(sections, card).Single(item => (string?)item["title"] == title);

    // Title, value, tone and line in one string; only the fields every item has.
    private static string Describe(JObject item) => $"{item["title"]} {item["value"]} {item["tone"]} {item["line"]}";
}
