using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The "记录" detail tab as cards: the notice a hidden or unloaded player gets instead of a history, the summary tiles
/// with their rate, streak and rank rules, the behavior chips and signal thresholds, and the recent-game cards. The
/// expected values come from Tests/StatisticsChecks/OverlayRecordChecks.cs, which this class replaces; the input is a
/// history built here and the output a JArray, so nothing reads a client, a game or the disk.
/// </summary>
public class OverlayRecordSectionsTests
{
    [Fact]
    public void Tab_TheDetailTabTheWidgetReads_StaysAtRecord()
    {
        Assert.Equal("record", OverlayRecordSections.Tab);
    }

    [Fact]
    public void TagRules_TheUpstreamTable_ExplainsNineTags()
    {
        Assert.Equal(9, OverlayRecordSections.TagRules.Count);
    }

    [Theory]
    [InlineData("爱投降", "以投降结束的对局 ≥ 38%")]
    [InlineData("容易投降", "以投降结束的对局 ≥ 25%")]
    [InlineData("爱发问号", "每场平均问号（敌人消失）≥ 3 次")]
    [InlineData("爱发提醒", "每场平均视野提醒 ≥ 2.5 次")]
    [InlineData("爱发撤退", "每场平均撤退 ≥ 3.5 次")]
    [InlineData("爱发危险", "每场平均危险 ≥ 2.5 次")]
    [InlineData("玻璃心", "四种信号每场合计 ≥ 13 次，或撤退 ≥ 4 次")]
    [InlineData("心态容易崩", "四种信号每场合计 ≥ 9 次")]
    [InlineData("爱抱怨", "撤退 ≥ 4.5 次且四种信号合计 ≥ 11 次")]
    public void TagRules_EveryTagTheUpstreamSummaryWrites_CarriesItsThreshold(string tag, string rule)
    {
        Assert.Equal(rule, OverlayRecordSections.TagRules[tag]);
    }

    [Fact]
    public void Tags_AHistoryWithoutABehaviour_ReturnsNothing()
    {
        Assert.Empty(OverlayRecordSections.Tags(null));
    }

    [Fact]
    public void Tags_AnEmptySummary_ReturnsNothing()
    {
        Assert.Empty(OverlayRecordSections.Tags(new PlayerBehavior()));
    }

    [Theory]
    [InlineData("[玻璃心、爱发问号]", new[] { "玻璃心", "爱发问号" })]
    [InlineData("玻璃心", new[] { "玻璃心" })]
    [InlineData("[ 玻璃心 、 爱发问号 ]", new[] { "玻璃心", "爱发问号" })]
    [InlineData("[玻璃心、、爱发问号]", new[] { "玻璃心", "爱发问号" })]
    [InlineData("[]", new string[0])]
    public void Tags_AWrittenSummary_IsSplitAtTheEnumerators(string summary, string[] expected)
    {
        Assert.Equal(expected, OverlayRecordSections.Tags(new PlayerBehavior { BehaviorSummary = summary }));
    }

    [Fact]
    public void Build_APlayerWhoHidTheirName_ShowsTheHiddenNotice()
    {
        JArray sections = Build(null, hidden: true);

        JObject notice = Assert.Single(sections.OfType<JObject>());
        Assert.Equal("record", (string?)notice["tab"]);
        Assert.Equal("近期战绩", (string?)notice["title"]);
        Assert.Null(notice["layout"]);
        Assert.Equal(
            "该玩家隐藏了名称，无法对应到战绩。",
            (string?)Row((JArray)notice["rows"]!, "隐藏玩家")["description"]
        );
    }

    [Fact]
    public void Build_AHiddenPlayerWithACachedHistory_StillShowsTheHiddenNotice()
    {
        JArray sections = Build(History(Games("win,win")), hidden: true);

        JObject notice = Assert.Single(sections.OfType<JObject>());
        Assert.Equal("隐藏玩家", (string?)Row((JArray)notice["rows"]!, "隐藏玩家")["title"]);
    }

    [Fact]
    public void Build_AHistoryTheMainWindowHasNotLoaded_ExplainsHowToRetry()
    {
        JObject notice = Assert.Single(Build(null).OfType<JObject>());

        Assert.Equal("近期战绩", (string?)notice["title"]);
        Assert.Equal(
            "主程序还没有这名玩家的战绩：选人或加载时主窗口查询过的玩家才有。读取完成后点「重试」；对局中途启动 League 时只有自己。",
            (string?)Row((JArray)notice["rows"]!, "暂无战绩")["description"]
        );
    }

    [Theory]
    [InlineData("隐藏", "该玩家的战绩设为隐藏。")]
    [InlineData("公开", "最近没有这个模式的对局。")]
    public void Build_ACachedHistoryWithoutGames_ExplainsTheEmptyHistory(string isPublic, string description)
    {
        JArray sections = Build(History([], isPublic: isPublic));

        Assert.Equal(description, (string?)Row((JArray)Card(sections, "近期战绩")["rows"]!, "暂无战绩")["description"]);
    }

    [Fact]
    public void Build_AWinHistoryWithoutRecordedGames_SaysThereIsNoRecentGame()
    {
        PlayerMatchInfo info = History(Games("win,win"));
        info.RecentMatches.Clear();

        Assert.Equal(
            "最近没有这个模式的对局。",
            (string?)Row(Rows(Build(info), "近期战绩"), "暂无战绩")["description"]
        );
    }

    [Fact]
    public void Build_ACachedHistoryWithGames_ReturnsTheSummaryTheBehaviourAndTheGamesCards()
    {
        JArray sections = Build(History(Games("win,win,remake,loss,win")));

        Assert.Equal(["近期战绩", "行为特征", "最近对局"], sections.Select(section => (string?)section["title"]));
        Assert.Equal(["tiles", "chips", "games"], sections.Select(section => (string?)section["layout"]));
        Assert.All(sections, section => Assert.Equal("record", (string?)section["tab"]));
    }

    [Fact]
    public void Build_TheSummaryCard_ListsItsTilesInOrder()
    {
        JArray sections = Build(History(Games("win,win,remake,loss,win")));

        Assert.Equal(
            ["胜率", "近况", "平均 KDA", "单双排", "灵活组排"],
            Rows(sections, "近期战绩").Select(tile => (string?)tile["title"])
        );
    }

    [Theory]
    [InlineData("win,win,win", "100%", "3 胜 0 负", "good")]
    [InlineData("loss,loss,loss,loss", "0%", "0 胜 4 负", "bad")]
    [InlineData("win,win,loss,loss", "50%", "2 胜 2 负", "")]
    [InlineData("win,win,remake,loss,win", "60%", "3 胜 1 负 1 重开", "good")]
    [InlineData(
        "win,win,win,win,win,win,win,win,win,loss,loss,loss,loss,loss,loss,loss,loss,loss,loss,loss",
        "45%",
        "9 胜 11 负",
        "bad"
    )]
    [InlineData(
        "win,win,win,win,win,win,win,win,win,win,win,loss,loss,loss,loss,loss,loss,loss,loss,loss",
        "55%",
        "11 胜 9 负",
        "good"
    )]
    public void Build_TheRateTile_PrintsTheRateTheRecordAndTheTone(
        string results,
        string rate,
        string record,
        string tone
    )
    {
        JObject tile = Tile(Build(History(Games(results))), "近期战绩", "胜率");

        Assert.Equal(rate, (string?)tile["value"]);
        Assert.Equal(record, (string?)tile["stats"]);
        Assert.Equal(tone, (string?)tile["tone"]);
    }

    [Theory]
    [InlineData("win,win,win", "3 连胜")]
    [InlineData("loss,loss", "2 连败")]
    [InlineData("win", "上一场胜")]
    [InlineData("loss", "上一场负")]
    [InlineData("remake,remake", "暂无")]
    [InlineData("loss,remake,win", "上一场负")]
    [InlineData("win,remake,win", "2 连胜")]
    public void Build_TheStreakTile_CountsTheRunOfTheNewestPlayedGames(string results, string streak)
    {
        JArray sections = Build(History(Games(results)));

        Assert.Equal(streak, (string?)Tile(sections, "近期战绩", "近况")["value"]);
    }

    [Theory]
    [InlineData("win,win", "good")]
    [InlineData("loss", "bad")]
    [InlineData("remake,remake", "")]
    public void Build_TheStreakTile_TonesTheRun(string results, string tone)
    {
        JArray sections = Build(History(Games(results)));

        Assert.Equal(tone, (string?)Tile(sections, "近期战绩", "近况")["tone"]);
    }

    [Fact]
    public void Build_TheStreakTile_MarksTheNewestGameOnTheLeft()
    {
        JArray sections = Build(History(Games("win,win")));

        Assert.Equal("左侧色块为最新", (string?)Tile(sections, "近期战绩", "近况")["stats"]);
    }

    [Fact]
    public void Build_TheKdaTile_AveragesTheScoreOfEveryCountedGame()
    {
        CachedGame[] games =
        [
            Win() with
            {
                Kills = 5,
                Deaths = 3,
                Assists = 7,
            },
            Win() with
            {
                Kills = 1,
                Deaths = 1,
                Assists = 3,
            },
        ];

        JObject tile = Tile(Build(History(games)), "近期战绩", "平均 KDA");

        Assert.Equal("3.0/2.0/5.0", (string?)tile["value"]);
        Assert.Equal("KDA 4.0", (string?)tile["stats"]);
    }

    [Fact]
    public void Build_TheKdaTile_WithNoDeaths_DividesByOne()
    {
        JObject tile = Tile(Build(History([Win() with { Deaths = 0 }])), "近期战绩", "平均 KDA");

        Assert.Equal("5.0/0.0/7.0", (string?)tile["value"]);
        Assert.Equal("KDA 12.0", (string?)tile["stats"]);
    }

    [Theory]
    [InlineData("黄金 II(45)", "黄金 II", "45 胜点")]
    [InlineData("黄金 II (45)", "黄金 II", "45 胜点")]
    [InlineData("大师(12)", "大师", "12 胜点")]
    [InlineData("宗师 300", "宗师 300", "")]
    [InlineData("未知", "未知", "")]
    [InlineData("", "未知", "")]
    [InlineData(null, "未知", "")]
    public void Build_TheSoloRankTile_PrintsTheTierAndTheLeaguePoints(string? rank, string value, string points)
    {
        JObject tile = Tile(Build(History(Games("win"), soloRank: rank)), "近期战绩", "单双排");

        Assert.Equal(value, (string?)tile["value"]);
        Assert.Equal(points, (string?)tile["stats"]);
    }

    [Fact]
    public void Build_TheSummaryTip_CountsTheGamesItRead()
    {
        JArray sections = Build(History(Games("win,win,win,win,win")));

        Assert.Equal("主窗口查询的最近 5 场，只读主程序缓存，不额外请求。", (string?)Card(sections, "近期战绩")["tip"]);
    }

    [Fact]
    public void Build_APlayerWhoseProfileIsHidden_AddsThatToTheSummaryTip()
    {
        JArray sections = Build(History(Games("win"), isPublic: "隐藏"));

        Assert.Equal(
            "主窗口查询的最近 1 场，只读主程序缓存，不额外请求。\n该玩家的资料设为隐藏。",
            (string?)Card(sections, "近期战绩")["tip"]
        );
    }

    [Fact]
    public void Build_TheSummaryFormStrip_ListsEveryGameNewestFirst()
    {
        JArray form = (JArray)Card(Build(History(Games("win,win,remake,loss,win"))), "近期战绩")["form"]!;

        Assert.Equal(["win", "win", "remake", "loss", "win"], form.Select(entry => (string?)entry["result"]));
        Assert.Equal(
            ["胜 · 阿狸", "胜 · 阿狸", "重开 · 阿狸", "负 · 阿狸", "胜 · 阿狸"],
            form.Select(entry => (string?)entry["description"])
        );
    }

    [Fact]
    public void Build_TheBehaviourCard_ListsTheSignalsInOrder()
    {
        JArray signals = Signals(Build(History(Games("win"))));

        Assert.Equal(
            ["投降率", "问号", "撤退", "危险", "提醒", "信号合计"],
            signals.Select(signal => (string?)signal["title"])
        );
    }

    [Fact]
    public void Build_TheBehaviourCard_ListsTheTagsTheSummaryWrote()
    {
        JArray sections = Build(History(Games("win"), summary: "[爱发问号、玻璃心]"));

        Assert.Equal(["爱发问号", "玻璃心"], Rows(sections, "行为特征").Select(chip => (string?)chip["title"]));
    }

    [Theory]
    [InlineData("爱投降", "bad")]
    [InlineData("容易投降", "warn")]
    [InlineData("爱发问号", "warn")]
    [InlineData("爱发提醒", "info")]
    [InlineData("爱发撤退", "warn")]
    [InlineData("爱发危险", "info")]
    [InlineData("玻璃心", "bad")]
    [InlineData("心态容易崩", "warn")]
    [InlineData("爱抱怨", "bad")]
    public void Build_OneBehaviourTag_MarksTheChipWithItsSeverity(string tag, string tone)
    {
        JArray sections = Build(History(Games("win"), summary: "[" + tag + "]"));

        Assert.Equal(tone, (string?)Single(Rows(sections, "行为特征"))["tone"]);
    }

    [Theory]
    [InlineData("爱投降", "以投降结束的对局 ≥ 38%")]
    [InlineData("容易投降", "以投降结束的对局 ≥ 25%")]
    [InlineData("爱发问号", "每场平均问号（敌人消失）≥ 3 次")]
    [InlineData("爱发提醒", "每场平均视野提醒 ≥ 2.5 次")]
    [InlineData("爱发撤退", "每场平均撤退 ≥ 3.5 次")]
    [InlineData("爱发危险", "每场平均危险 ≥ 2.5 次")]
    [InlineData("玻璃心", "四种信号每场合计 ≥ 13 次，或撤退 ≥ 4 次")]
    [InlineData("心态容易崩", "四种信号每场合计 ≥ 9 次")]
    [InlineData("爱抱怨", "撤退 ≥ 4.5 次且四种信号合计 ≥ 11 次")]
    public void Build_OneBehaviourTag_ExplainsTheRuleUnderTheChip(string tag, string rule)
    {
        JArray sections = Build(History(Games("win"), summary: "[" + tag + "]"));

        Assert.Equal(rule, (string?)Single(Rows(sections, "行为特征"))["description"]);
    }

    [Fact]
    public void Build_ABehaviourChip_ShowsTheTagWithoutAValue()
    {
        JObject chip = Single(Rows(Build(History(Games("win"), summary: "[玻璃心]")), "行为特征"));

        Assert.Equal("", (string?)chip["value"]);
        Assert.Equal("", (string?)chip["stats"]);
    }

    [Fact]
    public void Build_ABehaviourTagWithoutARule_SaysTheRuleIsNotStated()
    {
        JObject chip = Single(Rows(Build(History(Games("win"), summary: "[新标签]")), "行为特征"));

        Assert.Equal("info", (string?)chip["tone"]);
        Assert.Equal("规则未说明", (string?)chip["description"]);
    }

    [Fact]
    public void Build_ACachedHistoryWithoutABehaviour_ShowsTheCardWithoutChips()
    {
        PlayerMatchInfo info = History(Games("win"));
        info.Behavior = null!;

        JArray sections = Build(info);

        Assert.Empty(Rows(sections, "行为特征"));
        Assert.Equal("0%", (string?)SignalTile(sections, "投降率")["value"]);
    }

    [Fact]
    public void Build_TheBehaviourTip_CountsTheCachedRecentGames()
    {
        PlayerMatchInfo info = History(Games("win,win,win"));
        info.RecentMatches.AddRange(Repeats(4, Loss()).Select(Stat));

        JArray sections = Build(info);

        Assert.Equal(
            "按主窗口查询的最近 7 场估算，仅供参考。\n投降率统计以投降结束的对局，不区分是哪一方投降；信号为每场平均次数。",
            (string?)Card(sections, "行为特征")["tip"]
        );
    }

    [Theory]
    [InlineData("投降率", 24, "")]
    [InlineData("投降率", 25, "warn")]
    [InlineData("投降率", 38, "bad")]
    [InlineData("问号", 2.9, "")]
    [InlineData("问号", 3, "warn")]
    [InlineData("撤退", 3.4, "")]
    [InlineData("撤退", 3.5, "warn")]
    [InlineData("撤退", 4, "bad")]
    [InlineData("危险", 2.4, "")]
    [InlineData("危险", 2.5, "warn")]
    [InlineData("提醒", 2.4, "")]
    [InlineData("提醒", 2.5, "info")]
    public void Build_OneSignal_TakesTheToneOfItsThreshold(string title, double value, string tone)
    {
        JArray sections = Build(History(Games("win"), behavior: Behavior(title, value)));

        Assert.Equal(tone, (string?)SignalTile(sections, title)["tone"]);
    }

    [Theory]
    [InlineData("投降率", 40, "40%")]
    [InlineData("问号", 3.2, "3.2")]
    [InlineData("撤退", 4.1, "4.1")]
    [InlineData("危险", 2.5, "2.5")]
    [InlineData("提醒", 2.5, "2.5")]
    public void Build_OneSignal_PrintsItsValue(string title, double value, string printed)
    {
        JArray sections = Build(History(Games("win"), behavior: Behavior(title, value)));

        Assert.Equal(printed, (string?)SignalTile(sections, title)["value"]);
    }

    [Fact]
    public void Build_ThePingSignals_CountPerGame()
    {
        JArray sections = Build(History(Games("win"), behavior: Behavior("问号", 3.2)));

        Assert.Equal("次 / 场", (string?)SignalTile(sections, "问号")["stats"]);
    }

    [Fact]
    public void Build_TheSurrenderSignal_NamesTheShortGameCount()
    {
        var behavior = new PlayerBehavior { SurrenderRate = 40, ShortGameCount = 5 };

        JArray sections = Build(History(Games("win"), behavior: behavior));

        Assert.Equal("40%", (string?)SignalTile(sections, "投降率")["value"]);
        Assert.Equal("短局 5 场", (string?)SignalTile(sections, "投降率")["stats"]);
    }

    [Fact]
    public void Build_TheSignalTiles_ExplainTheirThresholds()
    {
        JArray sections = Build(History(Games("win")));

        Assert.Equal(
            "以投降结束的对局占比（不区分哪一方投降）。≥ 25% 容易投降，≥ 38% 爱投降。\n短局：18 分钟内结束的对局。",
            (string?)SignalTile(sections, "投降率")["description"]
        );
        Assert.Equal(
            "四种信号合计：≥ 9 次心态容易崩，≥ 13 次玻璃心。",
            (string?)SignalTile(sections, "信号合计")["description"]
        );
    }

    [Theory]
    [InlineData(2, "8.0", "")]
    [InlineData(2.25, "9.0", "warn")]
    [InlineData(3.25, "13.0", "bad")]
    public void Build_TheCombinedSignals_TakeTheToneOfTheTotal(double each, string total, string tone)
    {
        JArray sections = Build(History(Games("win"), behavior: Pings(each)));

        Assert.Equal(total, (string?)SignalTile(sections, "信号合计")["value"]);
        Assert.Equal(tone, (string?)SignalTile(sections, "信号合计")["tone"]);
    }

    [Fact]
    public void Build_TheGamesCard_ListsTheCachedGamesNewestFirst()
    {
        CachedGame[] games = [Win() with { Day = "2026-10-20" }, Loss() with { ChampionId = 1, Day = "2026-10-19" }];

        JArray rows = Rows(Build(History(games)), "最近对局");

        Assert.Equal(
            ["胜 · 阿狸 · 5/3/7 · 大乱斗 · 2026-10-20", "负 · 英雄 1 · 5/3/7 · 大乱斗 · 2026-10-19"],
            rows.Select(row => (string?)row["description"])
        );
    }

    [Fact]
    public void Build_AGameCard_NamesTheChampionTheScoreTheLengthAndTheDay()
    {
        JObject card = Single(Rows(Build(History([Win()])), "最近对局"));

        Assert.Equal("阿狸", (string?)card["title"]);
        Assert.Equal("5/3/7", (string?)card["value"]);
        Assert.Equal("21 分钟 · 10-10", (string?)card["stats"]);
    }

    [Theory]
    [InlineData("win", "胜", "win")]
    [InlineData("loss", "负", "loss")]
    [InlineData("remake", "重开", "remake")]
    public void Build_AGameCard_CarriesTheOutcomeLabelAndTone(string result, string label, string tone)
    {
        JObject card = Single(Rows(Build(History(Games(result))), "最近对局"));

        Assert.Equal(label, (string?)card["result"]);
        Assert.Equal(tone, (string?)card["tone"]);
    }

    [Fact]
    public void Build_AGameEndedInSurrender_CarriesTheSurrenderBadge()
    {
        JObject card = Single(Rows(Build(History([Win() with { Surrendered = true }])), "最近对局"));

        Assert.Equal("投降", (string?)card["badge"]);
        Assert.Equal("胜 · 阿狸 · 5/3/7 · 大乱斗 · 2026-10-10 · 投降结束", (string?)card["description"]);
    }

    [Fact]
    public void Build_ARemakeThatEndedInSurrender_LeavesTheBadgeOff()
    {
        JObject card = Single(Rows(Build(History([Remake() with { Surrendered = true }])), "最近对局"));

        Assert.Equal("", (string?)card["badge"]);
    }

    [Fact]
    public void Build_AGameWithoutADuration_LeavesTheLengthOffTheCard()
    {
        JObject card = Single(Rows(Build(History([Win() with { Minutes = 0 }])), "最近对局"));

        Assert.Equal("10-10", (string?)card["stats"]);
    }

    [Fact]
    public void Build_AGameWhoseDayIsNotTenCharacters_KeepsTheRawText()
    {
        JObject card = Single(Rows(Build(History([Win() with { Day = "2026-10-1" }])), "最近对局"));

        Assert.Equal("21 分钟 · 2026-10-1", (string?)card["stats"]);
    }

    [Fact]
    public void Build_AGameWithoutADay_LeavesTheDayOffTheCard()
    {
        JObject card = Single(Rows(Build(History([Win() with { Day = "" }])), "最近对局"));

        Assert.Equal("21 分钟", (string?)card["stats"]);
        Assert.Equal("胜 · 阿狸 · 5/3/7 · 大乱斗", (string?)card["description"]);
    }

    [Fact]
    public void Build_AGameWithoutAMode_LeavesTheModeOffTheTooltip()
    {
        JObject card = Single(Rows(Build(History([Win()], mode: "")), "最近对局"));

        Assert.Equal("胜 · 阿狸 · 5/3/7 · 2026-10-10", (string?)card["description"]);
    }

    [Fact]
    public void Build_ACachedHistoryLongerThanTwentyGames_KeepsTheNewestTwenty()
    {
        JArray sections = Build(History(Repeats(21, Win())));

        Assert.Equal(20, Rows(sections, "最近对局").Count);
        Assert.Equal(20, ((JArray)Card(sections, "近期战绩")["form"]!).Count);
        Assert.Equal("100%", (string?)Tile(sections, "近期战绩", "胜率")["value"]);
    }

    [Fact]
    public void Build_AGameWhoseRowHasNoChampion_NamesTheCachedIconKey()
    {
        JObject card = Single(Rows(Build(History([Win() with { ChampionId = 0 }])), "最近对局"));

        Assert.Equal("Ahri", (string?)card["title"]);
    }

    [Fact]
    public void Build_AGameWhoseRowNamesAnotherChampion_AsksTheDelegateForThatName()
    {
        JObject card = Single(Rows(Build(History([Win() with { ChampionId = 1 }])), "最近对局"));

        Assert.Equal("英雄 1", (string?)card["title"]);
    }

    [Fact]
    public void Build_AGameWithoutACachedRow_NamesAnUnknownChampion()
    {
        PlayerMatchInfo info = History([Win()]);
        info.MatchItems.Clear();

        JObject card = Single(Rows(Build(info), "最近对局"));

        Assert.Equal("未知英雄", (string?)card["title"]);
    }

    [Fact]
    public void Build_WithoutIcons_LeavesTheImageEmpty()
    {
        JObject card = Single(Rows(Build(History([Win()])), "最近对局"));

        Assert.Equal("", (string?)card["image"]);
    }

    [Fact]
    public void Build_WithIconsButNothingCached_LeavesTheImageEmpty()
    {
        JObject card = Single(Rows(Build(History([Win()]), icons: true), "最近对局"));

        Assert.Equal("", (string?)card["image"]);
    }

    [Fact]
    public void Build_WithACachedIcon_EncodesItAsABase64Png()
    {
        using var icons = new ImageList { ImageSize = new Size(20, 20) };
        using var bitmap = new Bitmap(20, 20);
        icons.Images.Add("Ahri", bitmap);
        PlayerMatchInfo info = History([Win()]);
        info.HeroIcons = icons;

        JObject card = Single(Rows(Build(info, icons: true), "最近对局"));

        Assert.StartsWith("iVBORw0KGgo", (string?)card["image"]);
    }

    [Fact]
    public void Build_TheGamesCard_CarriesNoTip()
    {
        JArray sections = Build(History(Games("win")));

        Assert.Null(Card(sections, "最近对局")["tip"]);
    }

    /// <summary>The champion name the widget asks for by id; the id comes from the row's participant when it has one.</summary>
    private static string Champion(int id) => id == 103 ? "阿狸" : "英雄 " + id;

    private static JArray Build(PlayerMatchInfo? info, bool icons = false, bool hidden = false) =>
        OverlayRecordSections.Build(
            new LoadingPlayer("ally-puuid", "队友#2", "ally-puuid", 2, 103, 100, false, hidden),
            info,
            Champion,
            icons
        );

    /// <summary>One behavior whose named signal sits at the value under test, every other signal below its threshold.</summary>
    private static PlayerBehavior Behavior(string title, double value) =>
        new()
        {
            SurrenderRate = title == "投降率" ? value : 0,
            AvgMissingPings = title == "问号" ? value : 0,
            AvgVisionPings = title == "提醒" ? value : 0,
            AvgGetBackPings = title == "撤退" ? value : 0,
            AvgDangerPings = title == "危险" ? value : 0,
        };

    private static PlayerBehavior Pings(double each) =>
        new()
        {
            AvgMissingPings = each,
            AvgVisionPings = each,
            AvgGetBackPings = each,
            AvgDangerPings = each,
        };

    /// <summary>One cached game: the row the history parser wrote plus the stat it read for that row.</summary>
    private sealed record CachedGame(
        bool Win,
        bool Remake = false,
        bool Surrendered = false,
        int Kills = 5,
        int Deaths = 3,
        int Assists = 7,
        int Minutes = 21,
        string Day = "2026-10-10",
        int ChampionId = 103
    );

    private static CachedGame Win() => new(true);

    private static CachedGame Loss() => new(false);

    private static CachedGame Remake() => new(false, Remake: true);

    private static CachedGame[] Games(string results) =>
        [
            .. results
                .Split(',', StringSplitOptions.TrimEntries)
                .Select(result =>
                    result switch
                    {
                        "win" => Win(),
                        "loss" => Loss(),
                        _ => Remake(),
                    }
                ),
        ];

    private static CachedGame[] Repeats(int count, CachedGame game) =>
        [.. Enumerable.Range(0, count).Select(_ => game)];

    /// <summary>
    /// A cached history as the main window holds it: results newest first, the behavior the summary was generated for,
    /// and the ranks it read. The remake colour is what the section reads the result from.
    /// </summary>
    private static PlayerMatchInfo History(
        CachedGame[] games,
        string summary = "",
        PlayerBehavior? behavior = null,
        string puuid = "ally-puuid",
        string? soloRank = "黄金 II(45)",
        string isPublic = "公开",
        string mode = "大乱斗"
    )
    {
        var info = new PlayerMatchInfo
        {
            Player = new PlayerInfo
            {
                Puuid = puuid,
                SummonerId = 2,
                GameName = "队友",
                SoloRank = soloRank!,
                FlexRank = "未知",
                IsPublic = isPublic,
            },
            Behavior =
                behavior
                ?? new PlayerBehavior
                {
                    SurrenderRate = 40,
                    AvgMissingPings = 3.2,
                    AvgGetBackPings = 4.1,
                    BehaviorSummary = summary,
                },
        };
        info.WinHistory.AddRange(games.Select(game => game.Win && !game.Remake));
        info.RecentMatches.AddRange(games.Select(Stat));
        info.MatchItems.AddRange(games.Select(game => Row(game, puuid, mode)));
        return info;
    }

    private static MatchStat Stat(CachedGame game) =>
        new()
        {
            Kills = game.Kills,
            Deaths = game.Deaths,
            Assists = game.Assists,
            GameDurationMinutes = game.Minutes,
            GameEndedInSurrender = game.Surrendered,
        };

    private static ListViewItem Row(CachedGame game, string puuid, string mode)
    {
        var item = new ListViewItem
        {
            ImageKey = "Ahri",
            ForeColor = game.Remake ? MatchResult.RemakeColor : Color.Green,
            Tag = new MatchMetadata
            {
                GameData = new JObject
                {
                    ["participants"] = new JArray(new JObject { ["puuid"] = puuid, ["championId"] = game.ChampionId }),
                },
            },
        };
        item.SubItems.AddRange([mode, $"{game.Kills}/{game.Deaths}/{game.Assists}", game.Day]);
        return item;
    }

    private static JObject Card(JArray sections, string title) =>
        sections.OfType<JObject>().Single(section => (string?)section["title"] == title);

    private static JArray Rows(JArray sections, string card) => (JArray)Card(sections, card)["rows"]!;

    private static JObject Tile(JArray sections, string card, string title) => Row(Rows(sections, card), title);

    private static JObject Row(JArray rows, string title) => Single(rows, title);

    private static JObject Single(JArray rows) => rows.OfType<JObject>().Single();

    private static JObject Single(JArray rows, string title) =>
        rows.OfType<JObject>().Single(row => (string?)row["title"] == title);

    private static JArray Signals(JArray sections) => (JArray)Card(sections, "行为特征")["signals"]!;

    private static JObject SignalTile(JArray sections, string title) => Row(Signals(sections), title);
}
