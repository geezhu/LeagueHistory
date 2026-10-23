using League.Infrastructure;
using League.Models;
using League.PrimaryElection;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The Game Bar's record message: the F9/F11 line of every seat on the chosen side, from the main window's cached
/// histories, held back until at least one of them has a record and cut to the chat limit. The expected values come
/// from Tests/StatisticsChecks/OverlayRecordChecks.cs (Message), which this class replaces; the roster, the request
/// and the cache are built here, so nothing reads a client, a game or the disk.
///
/// The old check reached the private <c>ChatMessageBuilder.BuildSimpleLine</c> through reflection and compared its
/// line with this one after folding the doubled brackets. Both public writers are driven here instead, from the same
/// cached history: the F9/F11 line carries two bracket pairs around the tags and the record line carries one.
/// Nothing in <c>OverlayRecordChatMessage</c> replaces brackets, and <c>Build</c> (lines 13-43) never looks at one:
/// the single pair comes from <c>OverlayRecordSections.Tags</c> trimming the summary's own brackets
/// (OverlayRecordSections.cs:80-83) and <c>Line</c> writing one pair back (OverlayRecordChatMessage.cs:85).
/// </summary>
public class OverlayRecordChatMessageTests
{
    private static readonly LoadingRoster Roster = new(
        "1",
        450,
        "ARAM",
        [
            new("self-puuid", "自己#1", "self-puuid", 1, 103, 100, true),
            new("ally-puuid", "队友#2", "ally-puuid", 2, 103, 100, false),
            new("100:2", "隐藏玩家", "", 0, 103, 100, false, true),
            new("later-puuid", "稍后#3", "later-puuid", 4, 103, 100, false),
            new("enemy-puuid", "对手#4", "enemy-puuid", 3, 103, 200, false),
        ]
    );

    /// <summary>The champion name the request asks for by id; the id comes from the roster seat.</summary>
    private static string Hero(int id) => $"英雄{id}";

    // A cached history that is not the one the seat asks for: the third seat of the side is hidden and the fourth
    // has never been looked up, so both read as "暂无战绩" while the first two carry a record.
    private static PlayerMatchInfo[] Cached() =>
        [
            History([true], puuid: "self-puuid", gameName: "自己", summonerId: 1),
            History(
                [true, true, false, false, true],
                Behavior("玻璃心、爱发问号", missing: 16, getBack: 21),
                "ally-puuid",
                "队友",
                2
            ),
            History([false, true], puuid: "enemy-puuid", gameName: "对手", summonerId: 3),
        ];

    [Fact]
    public void Line_NoCachedHistory_SaysThereIsNoRecord()
    {
        string line = OverlayRecordChatMessage.Line("自己", null);

        Assert.Equal("自己 暂无战绩", line);
    }

    [Fact]
    public void Line_ACachedHistoryWithNoGames_SaysThereIsNoRecord()
    {
        string line = OverlayRecordChatMessage.Line("自己", History([]));

        Assert.Equal("自己 暂无战绩", line);
    }

    // The actual contract of that branch: a player nobody has looked up and a player whose record is empty read the
    // same, so the caller cannot tell them apart and the message says the same thing for both.
    [Fact]
    public void Line_NoCachedHistoryAndACachedHistoryWithNoGames_ReadTheSame()
    {
        string unknown = OverlayRecordChatMessage.Line("自己", null);
        string empty = OverlayRecordChatMessage.Line("自己", History([]));

        Assert.Equal(unknown, empty);
    }

    [Theory]
    [InlineData(new[] { true }, "自己 近1场 胜率 [100%]")]
    [InlineData(new[] { false }, "自己 近1场 胜率 [0%]")]
    [InlineData(new[] { true, false }, "自己 近2场 胜率 [50%]")]
    [InlineData(new[] { true, true, false }, "自己 近3场 胜率 [67%]")]
    [InlineData(new[] { false, false, true }, "自己 近3场 胜率 [33%]")]
    [InlineData(new[] { true, true, false, false, true }, "自己 近5场 胜率 [60%]")]
    [InlineData(new[] { true, true, false, false, false, false, false }, "自己 近7场 胜率 [29%]")]
    public void Line_SomeGames_PrintsTheGameCountAndTheWinRate(bool[] wins, string expected)
    {
        string line = OverlayRecordChatMessage.Line("自己", History(wins));

        Assert.Equal(expected, line);
    }

    [Fact]
    public void Line_AHistoryWithoutBehaviourTags_EndsAtTheWinRate()
    {
        string line = OverlayRecordChatMessage.Line("自己", History([true, false], Behavior("")));

        Assert.Equal("自己 近2场 胜率 [50%]", line);
    }

    [Fact]
    public void Line_AHistoryWithoutABehaviourRecord_EndsAtTheWinRate()
    {
        PlayerMatchInfo info = History([true, false]);
        info.Behavior = null!;

        string line = OverlayRecordChatMessage.Line("自己", info);

        Assert.Equal("自己 近2场 胜率 [50%]", line);
    }

    [Fact]
    public void Line_ABehaviourTag_IsWrittenUnderTheFeatureHeading()
    {
        string line = OverlayRecordChatMessage.Line("自己", History([true, false], Behavior("玻璃心")));

        Assert.Equal("自己 近2场 胜率 [50%] 特征 [玻璃心]", line);
    }

    [Fact]
    public void Line_SeveralBehaviourTags_AreJoinedByTheEnumerators()
    {
        string line = OverlayRecordChatMessage.Line(
            "自己",
            History([true, false], Behavior("玻璃心、爱发问号、爱抱怨"))
        );

        Assert.Equal("自己 近2场 胜率 [50%] 特征 [玻璃心、爱发问号、爱抱怨]", line);
    }

    // The summary carries no brackets of its own since upstream b1d561c, so the F9/F11 line and this one agree.
    [Fact]
    public void Line_ATaggedHistory_ReadsLikeTheTeamSummary()
    {
        PlayerMatchInfo info = History(
            [true, true, false, false, true],
            Behavior("玻璃心、爱发问号", missing: 16, getBack: 21)
        );

        string teamSummary = TeamSummary(info);
        string line = OverlayRecordChatMessage.Line("队友", info);

        Assert.Equal(teamSummary, line);
    }

    [Theory]
    [InlineData(16, 0, 0, 0, "自己 近2场 胜率 [50%] | 发问号 16次 特征 [稳健]")]
    [InlineData(0, 12, 0, 0, "自己 近2场 胜率 [50%] | 发提醒 12次 特征 [稳健]")]
    [InlineData(0, 0, 21, 0, "自己 近2场 胜率 [50%] | 发撤退 21次 特征 [稳健]")]
    [InlineData(0, 0, 0, 12, "自己 近2场 胜率 [50%] | 发危险 12次 特征 [稳健]")]
    [InlineData(
        16,
        12,
        21,
        12,
        "自己 近2场 胜率 [50%] | 发问号 16次 | 发提醒 12次 | 发撤退 21次 | 发危险 12次 特征 [稳健]"
    )]
    public void Line_PingCountsAboveZero_AreListedInOrder(
        int missing,
        int vision,
        int getBack,
        int danger,
        string expected
    )
    {
        var behavior = Behavior("稳健", missing, vision, getBack, danger);

        string line = OverlayRecordChatMessage.Line("自己", History([true, false], behavior));

        Assert.Equal(expected, line);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Line_PingCountsNotAboveZero_AreLeftOffTheLine(int each)
    {
        var behavior = Behavior("稳健", each, each, each, each);

        string line = OverlayRecordChatMessage.Line("自己", History([true, false], behavior));

        Assert.Equal("自己 近2场 胜率 [50%] 特征 [稳健]", line);
    }

    // The tag list gates the whole suffix: a player without tags sends the win rate alone.
    [Fact]
    public void Line_PingCountsWithoutABehaviourTag_AreLeftOffTheLine()
    {
        var behavior = Behavior("", missing: 16, vision: 12, getBack: 21, danger: 12);

        string line = OverlayRecordChatMessage.Line("自己", History([true, false], behavior));

        Assert.Equal("自己 近2场 胜率 [50%]", line);
    }

    [Fact]
    public void Build_OwnSide_WritesTheLineOfEverySeatInTheOrderTheRosterListsThem()
    {
        var lines = OverlayRecordChatMessage.Build(Roster, Own(), Cached(), Hero, false, false);

        Assert.Equal(
            [
                "自己 近1场 胜率 [100%]",
                "队友 近5场 胜率 [60%] | 发问号 16次 | 发撤退 21次 特征 [玻璃心、爱发问号]",
                "英雄103 暂无战绩",
                "稍后 暂无战绩",
            ],
            lines
        );
    }

    // Hiding yourself drops your own seat from a side summary; the champion-name setting renames every seat that is
    // left, including the one that hid its name, which has no history to show either way.
    [Theory]
    [InlineData(false, false, "自己,队友,英雄103,稍后")]
    [InlineData(false, true, "英雄103,英雄103,英雄103,英雄103")]
    [InlineData(true, false, "队友,英雄103,稍后")]
    [InlineData(true, true, "英雄103,英雄103,英雄103")]
    public void Build_HideSelfAndChampionNames_ChooseTheSeatsAndTheNames(
        bool hideSelf,
        bool useChampionName,
        string names
    )
    {
        var lines = OverlayRecordChatMessage.Build(Roster, Own(), Cached(), Hero, hideSelf, useChampionName);

        Assert.Equal(names.Split(','), lines.Select(line => line.Split(' ')[0]));
    }

    [Fact]
    public void Build_OwnSide_NamesAHiddenSeatAfterItsChampion()
    {
        var lines = OverlayRecordChatMessage.Build(Roster, Own(), Cached(), Hero, false, false);

        Assert.Equal("英雄103 暂无战绩", lines[2]);
    }

    [Fact]
    public void Build_EnemySide_WritesTheOpponentsLine()
    {
        var lines = OverlayRecordChatMessage.Build(Roster, Enemy(), Cached(), Hero, false, false);

        Assert.Equal("对手 近2场 胜率 [50%]", Assert.Single(lines));
    }

    // Picking your own hero sends it on purpose, so hiding yourself does not apply to that request.
    [Fact]
    public void Build_YourOwnHero_SendsItEvenWhenYouHideYourself()
    {
        var lines = OverlayRecordChatMessage.Build(Roster, Single(), Cached(), Hero, true, false);

        Assert.Equal("自己 近1场 胜率 [100%]", Assert.Single(lines));
    }

    [Fact]
    public void Build_NoSeatOnTheChosenSide_IsRefused()
    {
        var build = () => OverlayRecordChatMessage.Build(EnemyOnly(), Own(), Cached(), Hero, false, false);

        var exception = Assert.Throws<InvalidOperationException>(build);

        Assert.Equal("未发送：无法确定所选阵营或英雄，请等待名单更新。", exception.Message);
    }

    [Fact]
    public void Build_NoCachedHistoryAtAll_IsRefused()
    {
        var build = () => OverlayRecordChatMessage.Build(Roster, Own(), [], Hero, false, false);

        var exception = Assert.Throws<InvalidOperationException>(build);

        Assert.Equal("未发送：主程序还没有读取这些玩家的战绩，请稍后重试。", exception.Message);
    }

    // One loaded history is enough: only a side where nothing at all was read is held back.
    [Fact]
    public void Build_OneSeatWithACachedHistory_IsEnoughToSendTheSide()
    {
        var lines = OverlayRecordChatMessage.Build(Roster, Own(), [Cached()[0]], Hero, false, false);

        Assert.Equal(4, lines.Count);
        Assert.Equal("自己 近1场 胜率 [100%]", lines[0]);
    }

    [Fact]
    public void Build_ALineOfExactlyTheLimit_IsSentWhole()
    {
        string name = new string('甲', 166);
        PlayerMatchInfo info = History([true], puuid: "self-puuid", gameName: name, summonerId: 1);
        string full = OverlayRecordChatMessage.Line(name, info);

        var lines = OverlayRecordChatMessage.Build(OneSelf(name), Single(), [info], Hero, false, false);

        Assert.Equal(180, full.Length);
        Assert.Equal(full, Assert.Single(lines));
    }

    [Fact]
    public void Build_ALineOverTheLimit_IsCutToTheLimitWithAnEllipsis()
    {
        string name = new string('甲', 167);
        PlayerMatchInfo info = History([true], puuid: "self-puuid", gameName: name, summonerId: 1);
        string full = OverlayRecordChatMessage.Line(name, info);

        var lines = OverlayRecordChatMessage.Build(OneSelf(name), Single(), [info], Hero, false, false);

        string line = Assert.Single(lines);
        Assert.Equal(181, full.Length);
        Assert.Equal(180, line.Length);
        Assert.Equal(full[..179] + "…", line);
    }

    /// <summary>The F9/F11 team summary for the same cached history, through the public entry point the Game Bar's record message copies.</summary>
    private static string TeamSummary(PlayerMatchInfo info)
    {
        using var client = new HttpClient();
        // The manager is never initialized, so its client never reaches the network and the id and name mappings it
        // would hold are not asked for: player names are read from the cached record itself.
        var builder = new ChatMessageBuilder(
            () => new Dictionary<long, PlayerMatchInfo> { [info.Player.SummonerId] = info },
            new ChampionManager(client, new ResourceLoading())
        );
        var team = new JArray(new JObject { ["summonerId"] = info.Player.SummonerId, ["puuid"] = info.Player.Puuid });

        return builder.BuildMyTeamSummary(team);
    }

    /// <summary>One behavior record with the tags it was summarised to and the ping counts the line sends.</summary>
    private static PlayerBehavior Behavior(
        string summary,
        int missing = 0,
        int vision = 0,
        int getBack = 0,
        int danger = 0
    ) =>
        new()
        {
            BehaviorSummary = summary,
            TotalMissingPings = missing,
            TotalVisionPings = vision,
            TotalGetBackPings = getBack,
            TotalDangerPings = danger,
        };

    /// <summary>One seat's cached history: the results newest first and the behavior the summary was generated for.</summary>
    private static PlayerMatchInfo History(
        bool[] wins,
        PlayerBehavior? behavior = null,
        string puuid = "ally-puuid",
        string gameName = "队友",
        long summonerId = 2
    )
    {
        var info = new PlayerMatchInfo
        {
            Player = new PlayerInfo
            {
                Puuid = puuid,
                SummonerId = summonerId,
                GameName = gameName,
                SoloRank = "黄金 II(45)",
                FlexRank = "未知",
                IsPublic = "公开",
            },
            Behavior = behavior ?? new PlayerBehavior(),
        };
        info.WinHistory.AddRange(wins);
        return info;
    }

    /// <summary>A roster holding one seat, whose name is the only line the request can produce.</summary>
    private static LoadingRoster OneSelf(string name) =>
        new("1", 450, "ARAM", [new("self-puuid", name, "self-puuid", 1, 103, 100, true)]);

    /// <summary>A roster whose only seat is on the other side, so the side itself cannot be determined.</summary>
    private static LoadingRoster EnemyOnly() =>
        new("1", 450, "ARAM", [new("enemy-puuid", "对手#4", "enemy-puuid", 3, 103, 200, false)]);

    // The requests are built directly: Parse would add an age window against the wall clock that the message does not
    // read, so a request made here can never go stale.
    private static OverlayChatRequest Own() => Request("own", -1, 0);

    private static OverlayChatRequest Enemy() => Request("enemy", -1, 0);

    private static OverlayChatRequest Single() => Request("single", 0, 103);

    private static OverlayChatRequest Request(string target, int slot, int championId) =>
        new("command", "match", target, slot, championId, 1000, false, false, "record");
}
