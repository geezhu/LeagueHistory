using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The "本局" detail tab as cards: which cards a match produces, what their tiles say, and how the three value
/// formatters print item gold, the lead and a share. The expected values come from the live-panel checks in
/// Tests/StatisticsChecks/LiveInsightChecks.cs, CooldownChecks.cs and CooldownOptionChecks.cs, which this class
/// replaces; the shape of the input follows the OverlayDetailPreview sample. Everything is in memory: the input is a
/// record built here, the output a JArray, and no client, game or disk is read.
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
    public void Build_AFinishedMatchWithEveryInput_ListsTheCardsInOrder()
    {
        var input = Panel(
            Match("Win"),
            self: Self,
            stats: Champion,
            defense: LiveDefenseAdvice.Calculate(Champion, 0.9),
            build: new LiveBuildAdvice(new LiveBuildOption([3071, 3053, 3742], 0.543, 0.12, 1800), 1, 3053),
            owned: [3071],
            runes: [8010, 9111],
            runeSource: "来自游戏内数据。",
            cooldowns: [new AbilityCooldown("Q", "光之束缚", 3, 5, 7, 4.7)]
        );

        JArray sections = LivePanelSections.Build(input, LayoutItem);

        Assert.Equal(
            ["本局结束", "战况", "你的状态", "技能冷却", "防装建议", "出装对照", "符文页"],
            sections.Select(section => (string?)section["title"])
        );
        Assert.All(sections, section => Assert.Equal(LivePanelSections.Tab, (string?)section["tab"]));
        Assert.All(sections, section => Assert.Equal("tiles", (string?)section["layout"]));
    }

    [Fact]
    public void Build_AMatchWithoutOptionalInputs_LeavesOutTheirCards()
    {
        JArray sections = LivePanelSections.Build(Panel(Match()), LayoutItem);

        Assert.Equal(["战况"], sections.Select(section => (string?)section["title"]));
    }

    [Fact]
    public void Build_ADefenseReadWithoutSelfOrChampionStats_ShowsTheAdviceWithoutTheStateCard()
    {
        JArray sections = LivePanelSections.Build(
            Panel(Match(), defense: LiveDefenseAdvice.Calculate(Champion, 0.9)),
            LayoutItem
        );

        Assert.Equal(["战况", "防装建议"], sections.Select(section => (string?)section["title"]));
    }

    [Theory]
    [InlineData("Win", "胜利", "good")]
    [InlineData("Lose", "失败", "bad")]
    [InlineData("Remake", "Remake", "")]
    public void Build_AMatchThatEnded_NamesTheResultTheToneAndTheScore(string result, string expected, string tone)
    {
        JArray sections = LivePanelSections.Build(Panel(Match(result), self: Self), LayoutItem);

        JObject tile = Tile(sections, "本局结束", "结果");

        Assert.Equal(expected, (string?)tile["value"]);
        Assert.Equal(tone, (string?)tile["tone"]);
        Assert.Equal("你 5/2/9", (string?)tile["stats"]);
    }

    [Fact]
    public void Build_ASelfWithoutAScore_LeavesTheResultSubtitleEmpty()
    {
        var self = new LivePlayerInsight(5200, null, null, null, null, 0);

        JArray sections = LivePanelSections.Build(Panel(Match("Win"), self: self), LayoutItem);

        Assert.Equal("", (string?)Tile(sections, "本局结束", "结果")["stats"]);
    }

    [Fact]
    public void Build_AFinishedMatch_ComparesKillsFightsAndGoldInTheEnding()
    {
        JArray sections = LivePanelSections.Build(Panel(Match("Win")), LayoutItem);

        Assert.Equal("18 : 14", (string?)Tile(sections, "本局结束", "击杀")["value"]);
        Assert.Equal("我方 : 敌方", (string?)Tile(sections, "本局结束", "击杀")["stats"]);
        Assert.Equal("2 胜 1 负", (string?)Tile(sections, "本局结束", "团战")["value"]);
        Assert.Equal("领先 2.3k", (string?)Tile(sections, "本局结束", "经济")["value"]);
        Assert.Equal("good", (string?)Tile(sections, "本局结束", "经济")["tone"]);
    }

    [Fact]
    public void Build_TheEndingCard_PointsAtTheHistoryPage()
    {
        JArray sections = LivePanelSections.Build(Panel(Match("Win")), LayoutItem);

        Assert.Equal("完整数据在结算后进入战绩，可在主程序战绩页查看。", (string?)Card(sections, "本局结束")["tip"]);
    }

    [Fact]
    public void Build_TheBattleCard_ComparesBothTeams()
    {
        JArray sections = LivePanelSections.Build(Panel(Match()), LayoutItem);

        Assert.Equal("18 : 14", (string?)Tile(sections, "战况", "击杀")["value"]);
        Assert.Equal("good", (string?)Tile(sections, "战况", "击杀")["tone"]);
        Assert.Equal("装备 41.2k : 38.9k", (string?)Tile(sections, "战况", "经济")["stats"]);
        Assert.Equal("1 : 0", (string?)Tile(sections, "战况", "团灭")["value"]);
        Assert.Equal("3 : 1", (string?)Tile(sections, "战况", "多杀")["value"]);
    }

    [Fact]
    public void Build_TheBattleCard_DatesTheMostRecentFight()
    {
        JArray sections = LivePanelSections.Build(Panel(Match()), LayoutItem);

        Assert.Equal("2 胜 1 负", (string?)Tile(sections, "战况", "团战")["value"]);
        Assert.Equal("9:48 杀 4 亡 2", (string?)Tile(sections, "战况", "团战")["stats"]);
    }

    [Fact]
    public void Build_AMatchWithoutFights_SaysThereAreNoneYet()
    {
        JArray sections = LivePanelSections.Build(Panel(Match(fights: [])), LayoutItem);

        Assert.Equal("暂无", (string?)Tile(sections, "战况", "团战")["value"]);
        Assert.Equal("", (string?)Tile(sections, "战况", "团战")["stats"]);
    }

    [Fact]
    public void Build_TheStateCard_ShowsScoreParticipationAndItemGold()
    {
        JArray sections = LivePanelSections.Build(Panel(Match(), self: Self, stats: Champion), LayoutItem);

        Assert.Equal("5/2/9", (string?)Tile(sections, "你的状态", "KDA")["value"]);
        Assert.Equal("78%", (string?)Tile(sections, "你的状态", "参团")["value"]);
        Assert.Equal("5.2k", (string?)Tile(sections, "你的状态", "装备")["value"]);
        Assert.Equal("装备总价", (string?)Tile(sections, "你的状态", "装备")["stats"]);
    }

    [Fact]
    public void Build_ASelfWithoutKillsOrParticipation_ShowsDashes()
    {
        var self = new LivePlayerInsight(0, null, null, null, null, 0);

        JArray sections = LivePanelSections.Build(Panel(Match(), self: self), LayoutItem);

        Assert.Equal("—", (string?)Tile(sections, "你的状态", "KDA")["value"]);
        Assert.Equal("—", (string?)Tile(sections, "你的状态", "参团")["value"]);
    }

    [Fact]
    public void Build_ADefenseAdvice_PutsBothEffectiveHealthsInTheStateCard()
    {
        JArray sections = LivePanelSections.Build(
            Panel(Match(), stats: Champion, defense: LiveDefenseAdvice.Calculate(Champion, 0.9)),
            LayoutItem
        );

        Assert.Equal("4000", (string?)Tile(sections, "你的状态", "物理有效生命")["value"]);
        Assert.Equal("4000", (string?)Tile(sections, "你的状态", "魔法有效生命")["value"]);
    }

    [Fact]
    public void Build_ChampionStatsWithoutADamageMix_ShowsHealthAndSaysTheSplitIsUnavailable()
    {
        JArray sections = LivePanelSections.Build(Panel(Match(), stats: Champion), LayoutItem);

        Assert.Equal("2000", (string?)Tile(sections, "你的状态", "生命")["value"]);
        Assert.Equal("敌方伤害构成暂不可用，仅显示当前有效生命。", (string?)Card(sections, "你的状态")["tip"]);
    }

    [Fact]
    public void Build_ChampionStats_PutsTheAttributesUnderTheStateCard()
    {
        JArray sections = LivePanelSections.Build(Panel(Match(), stats: Champion), LayoutItem);

        JArray signals = Signals(sections, "你的状态");

        Assert.Equal(5, signals.Count);
        Assert.Equal("100", (string?)Tile(signals, "护甲")["value"]);
        Assert.Equal("100", (string?)Tile(signals, "魔抗")["value"]);
        Assert.Equal("25", (string?)Tile(signals, "技能急速")["value"]);
        Assert.Equal("冷却 −20%", (string?)Tile(signals, "技能急速")["stats"]);
        Assert.Equal("12 + 18%", (string?)Tile(signals, "物理穿透")["value"]);
        Assert.Equal("固定 + 百分比", (string?)Tile(signals, "物理穿透")["stats"]);
        Assert.Equal("0 + 0%", (string?)Tile(signals, "法术穿透")["value"]);
    }

    [Fact]
    public void Build_ChampionStatsWithoutAbilityHaste_LeavesTheCooldownNoteEmpty()
    {
        var stats = new LiveChampionStats(2000, 100, 100, 0, 12, 0.18, 0, 0);

        JArray sections = LivePanelSections.Build(Panel(Match(), stats: stats), LayoutItem);

        Assert.Equal("0", (string?)Tile(Signals(sections, "你的状态"), "技能急速")["value"]);
        Assert.Equal("", (string?)Tile(Signals(sections, "你的状态"), "技能急速")["stats"]);
    }

    [Fact]
    public void Build_CooldownsAfterHaste_ListEachAbilitysRankAndBase()
    {
        var stats = new LiveChampionStats(2000, 50, 40, 50, 0, 0, 0, 0);
        AbilityCooldown[] cooldowns =
        [
            new("Q", "光之束缚", 3, 6, 7, 4.7),
            new("W", "棱光屏障", 0, 5, 0, 0),
            new("E", "透光奇点", 4, 5, 15, 10) { Charges = 3 },
            new("R", "终极闪光", 1, 3, 80, 53),
        ];

        JArray sections = LivePanelSections.Build(Panel(Match(), stats: stats, cooldowns: cooldowns), LayoutItem);

        JArray tiles = (JArray)Card(sections, "技能冷却")["rows"]!;

        Assert.Equal(
            [
                "4.7 秒|3/6 级 · 基础 7 秒",
                "未学|",
                "充能 10 秒|4/5 级 · 3 层充能 · 基础 15 秒",
                "53 秒|1/3 级 · 基础 80 秒",
            ],
            tiles.Select(tile => (string?)tile["value"] + "|" + (string?)tile["stats"])
        );
    }

    [Fact]
    public void Build_TheCooldownCard_ExplainsTheHasteAndWhatIsNotCounted()
    {
        var stats = new LiveChampionStats(2000, 50, 40, 50, 0, 0, 0, 0);
        AbilityCooldown[] cooldowns = [new("R", "终极闪光", 1, 3, 80, 53)];

        JArray sections = LivePanelSections.Build(Panel(Match(), stats: stats, cooldowns: cooldowns), LayoutItem);

        string tip = (string?)Card(sections, "技能冷却")["tip"] ?? "";

        Assert.Contains("技能急速 50", tip);
        Assert.Contains("冷却 = 基础冷却 × 100 / (100 + 急速)", tip);
        Assert.Contains("技能命中后的冷却返还未计入", tip);
    }

    [Fact]
    public void Build_ASealedUltimate_SaysWhatSealedIt()
    {
        AbilityCooldown[] cooldowns = [new("R", "终极闪光", 0, 3, 0, 0) { Sealed = true }];

        JArray sections = LivePanelSections.Build(Panel(Match(), cooldowns: cooldowns), LayoutItem);

        Assert.Equal("已封印", (string?)Tile(sections, "技能冷却", "R 终极闪光")["value"]);
        Assert.Equal("回归基本功", (string?)Tile(sections, "技能冷却", "R 终极闪光")["stats"]);
    }

    [Fact]
    public void Build_AnAbilityWithoutCooldownData_LeavesOutTheSeconds()
    {
        AbilityCooldown[] cooldowns = [new("Q", "光之束缚", 3, 5, 0, 0)];

        JArray sections = LivePanelSections.Build(Panel(Match(), cooldowns: cooldowns), LayoutItem);

        Assert.Equal("—", (string?)Tile(sections, "技能冷却", "Q 光之束缚")["value"]);
        Assert.Equal("3/5 级 · 数据里没有冷却", (string?)Tile(sections, "技能冷却", "Q 光之束缚")["stats"]);
    }

    [Fact]
    public void Build_RecordedAugments_AreNamedInTheCooldownTip()
    {
        var options = new CooldownOptions([true, false, false], false, "", 0, false, 0);
        AbilityCooldown[] cooldowns = [new("Q", "光之束缚", 3, 5, 7, 4.7)];

        JArray sections = LivePanelSections.Build(Panel(Match(), cooldowns: cooldowns, options: options), LayoutItem);

        Assert.Contains("已计入「海克斯」页的记录：面包和黄油", (string?)Card(sections, "技能冷却")["tip"] ?? "");
    }

    [Theory]
    [InlineData(0.9, "优先护甲")]
    [InlineData(0.5, "收益接近")]
    [InlineData(0.1, "优先魔抗")]
    public void Build_AnEnemyDamageMix_RecommendsTheResistanceThatAddsMore(double share, string expected)
    {
        JArray sections = LivePanelSections.Build(
            Panel(Match(), stats: Champion, defense: LiveDefenseAdvice.Calculate(Champion, share)),
            LayoutItem
        );

        Assert.Equal(expected, (string?)Tile(sections, "防装建议", "下一件防装")["value"]);
        Assert.Equal("info", (string?)Tile(sections, "防装建议", "下一件防装")["tone"]);
    }

    [Fact]
    public void Build_TheDefenseCard_NamesTheMixAndWhatEachResistanceAdds()
    {
        JArray sections = LivePanelSections.Build(
            Panel(Match(), stats: Champion, defense: LiveDefenseAdvice.Calculate(Champion, 0.9)),
            LayoutItem
        );

        Assert.Equal("90%", (string?)Tile(sections, "防装建议", "敌方物理伤害")["value"]);
        Assert.Equal("魔法 10%", (string?)Tile(sections, "防装建议", "敌方物理伤害")["stats"]);
        Assert.Equal("多抵 179", (string?)Tile(sections, "防装建议", "+10 护甲")["value"]);
        Assert.Equal("多抵 19", (string?)Tile(sections, "防装建议", "+10 魔抗")["value"]);
        Assert.Equal("点伤害", (string?)Tile(sections, "防装建议", "+10 护甲")["stats"]);
    }

    [Fact]
    public void Build_TheEquipmentCard_NamesTheNextItemThroughTheDelegate()
    {
        var advice = new LiveBuildAdvice(new LiveBuildOption([3071, 3053, 3742], 0.543, 0.12, 1800), 1, 3053);

        JArray sections = LivePanelSections.Build(Panel(Match(), build: advice, owned: [3071]), LayoutItem);

        Assert.Equal("布局装备 3053", (string?)Tile(sections, "出装对照", "下一件")["value"]);
        Assert.Equal("info", (string?)Tile(sections, "出装对照", "下一件")["tone"]);
        Assert.Equal("1/3", (string?)Tile(sections, "出装对照", "进度")["value"]);
    }

    [Fact]
    public void Build_ACompletedBuild_SaysThereIsNothingLeftToBuy()
    {
        var advice = new LiveBuildAdvice(new LiveBuildOption([3071], 0.5, 0.1, 100), 1, null);

        JArray sections = LivePanelSections.Build(Panel(Match(), build: advice, owned: [3071]), LayoutItem);

        Assert.Equal("已全部完成", (string?)Tile(sections, "出装对照", "下一件")["value"]);
        Assert.Equal("good", (string?)Tile(sections, "出装对照", "下一件")["tone"]);
    }

    [Fact]
    public void Build_TheReferenceBuild_ShowsItsWinAndPickRates()
    {
        var advice = new LiveBuildAdvice(new LiveBuildOption([3071, 3053, 3742], 0.543, 0.12, 1800), 1, 3053);

        JArray sections = LivePanelSections.Build(Panel(Match(), build: advice), LayoutItem);

        Assert.Equal("胜率 54%", (string?)Tile(sections, "出装对照", "参照组合")["value"]);
        Assert.Equal("使用率 12%", (string?)Tile(sections, "出装对照", "参照组合")["stats"]);
    }

    [Fact]
    public void Build_AReferenceBuildWithoutRates_LeavesTheNumbersOut()
    {
        var advice = new LiveBuildAdvice(new LiveBuildOption([3071], null, null, null), 0, 3071);

        JArray sections = LivePanelSections.Build(Panel(Match(), build: advice), LayoutItem);

        Assert.Equal("胜率 —", (string?)Tile(sections, "出装对照", "参照组合")["value"]);
        Assert.Equal("", (string?)Tile(sections, "出装对照", "参照组合")["stats"]);
    }

    [Fact]
    public void Build_TheEquipmentCard_MarksTheItemsThePlayerOwns()
    {
        var advice = new LiveBuildAdvice(new LiveBuildOption([3071, 3053, 3742], 0.543, 0.12, 1800), 1, 3053);

        JArray sections = LivePanelSections.Build(Panel(Match(), build: advice, owned: [3071]), LayoutItem);

        JArray icons = Icons(sections, "出装对照");

        Assert.Equal(3, icons.Count);
        Assert.True((bool)icons[0]!["owned"]!);
        Assert.False((bool)icons[1]!["owned"]!);
        Assert.Equal("item", (string?)icons[1]!["kind"]);
        Assert.Equal(3053, (int)icons[1]!["id"]!);
        Assert.Equal("装备 3053", (string?)icons[1]!["name"]);
    }

    [Fact]
    public void Build_TheRunePage_KeepsTheRunesAsIconsAndTheSourceNote()
    {
        var input = Panel(Match(), runes: [8010, 9111, 9104], runeSource: "来自游戏内数据。");

        JArray sections = LivePanelSections.Build(input, LayoutItem);

        JObject card = Card(sections, "符文页");
        JArray icons = Icons(sections, "符文页");

        Assert.Empty((JArray)card["rows"]!);
        Assert.Equal(3, icons.Count);
        Assert.Equal("rune", (string?)icons[0]!["kind"]);
        Assert.Equal(8010, (int)icons[0]!["id"]!);
        Assert.Equal("符文 8010", (string?)icons[0]!["name"]);
        Assert.Equal("来自游戏内数据。", (string?)card["tip"]);
    }

    [Fact]
    public void Build_ARunePageWithoutASourceNote_LeavesOutTheTip()
    {
        JArray sections = LivePanelSections.Build(Panel(Match(), runes: [8010]), LayoutItem);

        Assert.Null(Card(sections, "符文页")["tip"]);
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
        LiveChampionStats? stats = null,
        LiveDefenseAdvice? defense = null,
        LiveBuildAdvice? build = null,
        int[]? owned = null,
        int[]? runes = null,
        string runeSource = "",
        AbilityCooldown[]? cooldowns = null,
        CooldownOptions? options = null
    ) =>
        new(insight, self, stats, defense, build, owned ?? [], runes ?? [], runeSource)
        {
            Cooldowns = cooldowns ?? [],
            CooldownOptions = options ?? CooldownOptions.None,
        };

    private static JObject Card(JArray sections, string title) =>
        sections.OfType<JObject>().Single(section => (string?)section["title"] == title);

    private static JObject Tile(JArray sections, string card, string title) =>
        Tile((JArray)Card(sections, card)["rows"]!, title);

    private static JObject Tile(JArray tiles, string title) =>
        tiles.OfType<JObject>().Single(tile => (string?)tile["title"] == title);

    private static JArray Icons(JArray sections, string card) => (JArray)Card(sections, card)["icons"]!;

    private static JArray Signals(JArray sections, string card) => (JArray)Card(sections, card)["signals"]!;
}
