using League.Infrastructure;
using League.Models;
using League.PrimaryElection;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The F9/F11 team summary: which seats are left out, and the name, win rate and ping averages each line shows. The
/// expected lines come from Tests/StatisticsChecks/ChampionDataChecks.cs (TeamSummaries), whose helper fills its
/// champion manager through the private <c>_allChampions</c> field.
///
/// The manager is filled through <c>AllChampions</c> instead, which hands out the manager's own list - the one
/// <c>GetChampionById</c> reads - so no reflection is needed. <c>InitializeAsync</c> would <c>Clear()</c> and replace
/// that list, so it is never called on a manager filled this way, and nothing here downloads.
///
/// OverlayRecordChecks.cs:170-172 reaches the private <c>BuildSimpleLine</c> through
/// <c>RuntimeHelpers.GetUninitializedObject</c>. Every branch it drives that way - no champion names, no raw seat
/// data, with and without a win history, with and without a behaviour summary - is reachable through
/// <c>BuildMyTeamSummary</c>, which calls that same method for each seat, so no behaviour of <c>BuildSimpleLine</c> is
/// skipped here. What cannot be repeated without reflection is the assertion around it: the line
/// <c>OverlayRecordChatMessage.Line</c> writes from the same cached history, its 180-character truncation and its
/// refusal to send before any history is loaded. Those belong to <c>OverlayRecordChatMessage</c>, not to
/// <c>ChatMessageBuilder</c>; this class pins this class's side of that contract, the double brackets.
///
/// The two sides disagree on the brackets, and it is worth naming why: this class writes
/// <c>特征 [[玻璃心、爱发问号]]</c> (the summary carries its own pair), while <c>Line</c> writes one pair,
/// <c>特征 [玻璃心、爱发问号]</c>. Nothing in the application collapses one into the other - <c>Line</c> takes the
/// tags apart with <c>OverlayRecordSections.Tags</c> (<c>.Trim('[', ']', ' ')</c>) and writes them back out inside a
/// single pair of its own. The old check's <c>overlay.Replace("[[", "[")</c> is what used to make the two look like a
/// collapse. Both sides are pinned in <c>OverlayRecordChatMessageTests</c>.
/// </summary>
public class ChatMessageBuilderTests
{
    [Fact]
    public void Constructor_NullLookup_Throws()
    {
        var build = () => new ChatMessageBuilder(null!, Manager());

        var exception = Assert.Throws<ArgumentNullException>(build);

        Assert.Equal("getCachedPlayerInfos", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullChampionManager_Throws()
    {
        var build = () => new ChatMessageBuilder(() => new Dictionary<long, PlayerMatchInfo>(), null!);

        var exception = Assert.Throws<ArgumentNullException>(build);

        Assert.Equal("championManager", exception.ParamName);
    }

    [Fact]
    public void BuildMyTeamSummary_NullTeam_SaysThereIsNoData()
    {
        var builder = Builder(hideSelf: true, Known());

        string summary = builder.BuildMyTeamSummary(null);

        Assert.Equal("暂无数据", summary);
    }

    [Fact]
    public void BuildMyTeamSummary_EmptyTeam_SaysThereIsNoData()
    {
        var builder = Builder(hideSelf: true, Known());

        string summary = builder.BuildMyTeamSummary(new JArray());

        Assert.Equal("暂无数据", summary);
    }

    [Theory]
    [InlineData(new[] { true, true, false, true }, "玩家甲 近4场 胜率 [75%]")]
    [InlineData(new[] { true, false }, "玩家甲 近2场 胜率 [50%]")]
    [InlineData(new[] { true, true, false }, "玩家甲 近3场 胜率 [67%]")]
    [InlineData(new[] { false, false }, "玩家甲 近2场 胜率 [0%]")]
    public void BuildMyTeamSummary_WinHistory_GivesTheGameCountAndTheWinRate(bool[] wins, string expected)
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [.. wins];
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")));

        Assert.Equal(expected, summary);
    }

    [Fact]
    public void BuildMyTeamSummary_EmptyWinHistory_SaysThereAreNoGames()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [];
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")));

        Assert.Equal("玩家甲 暂无战绩", summary);
    }

    [Fact]
    public void BuildMyTeamSummary_NullWinHistory_SaysThereAreNoGames()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = null!;
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")));

        Assert.Equal("玩家甲 暂无战绩", summary);
    }

    [Fact]
    public void BuildMyTeamSummary_PingAverages_AddsTheOnesItWasGiven()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, true, false, true];
        player.Behavior = new PlayerBehavior
        {
            BehaviorSummary = "稳健",
            AvgMissingPings = 2.5,
            AvgVisionPings = 1.5,
        };
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")));

        Assert.Equal("玩家甲 近4场 胜率 [75%] | 发问号 2.5 | 发提醒 1.5 特征 [稳健]", summary);
    }

    [Fact]
    public void BuildMyTeamSummary_EveryPingAverage_ListsThemInOrder()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, true, false, true];
        player.Behavior = new PlayerBehavior
        {
            BehaviorSummary = "稳健",
            AvgMissingPings = 2.5,
            AvgVisionPings = 1.5,
            AvgGetBackPings = 3.5,
            AvgDangerPings = 4.5,
        };
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")));

        Assert.Equal(
            "玩家甲 近4场 胜率 [75%] | 发问号 2.5 | 发提醒 1.5 | 发撤退 3.5 | 发危险 4.5 特征 [稳健]",
            summary
        );
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void BuildMyTeamSummary_PingAverageNotAboveZero_LeavesItOut(double pings)
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, true, false, true];
        player.Behavior = new PlayerBehavior { BehaviorSummary = "稳健", AvgMissingPings = pings };
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")));

        Assert.Equal("玩家甲 近4场 胜率 [75%] 特征 [稳健]", summary);
    }

    [Fact]
    public void BuildMyTeamSummary_EmptyBehaviorSummary_EndsAtTheWinRate()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, true, false, true];
        player.Behavior = new PlayerBehavior { BehaviorSummary = "" };
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")));

        Assert.Equal("玩家甲 近4场 胜率 [75%]", summary);
    }

    [Fact]
    public void BuildMyTeamSummary_NullBehaviorSummary_EndsAtTheWinRate()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, true, false, true];
        player.Behavior = new PlayerBehavior { BehaviorSummary = null! };
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")));

        Assert.Equal("玩家甲 近4场 胜率 [75%]", summary);
    }

    [Fact]
    public void BuildMyTeamSummary_NoBehaviorRecord_EndsAtTheWinRate()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, true, false, true];
        player.Behavior = null!;
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")));

        Assert.Equal("玩家甲 近4场 胜率 [75%]", summary);
    }

    // MatchQueryProcessor.GenerateBehaviorSummary brackets its tags itself ("[玻璃心、爱发问号]"), so the line ends
    // up with two pairs. The Game Bar's own record message collapses them to one; this is the pair written here.
    [Fact]
    public void BuildMyTeamSummary_BracketedBehaviorSummary_KeepsBothBrackets()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, true, false, false, true];
        player.Behavior = new PlayerBehavior
        {
            BehaviorSummary = "[玻璃心、爱发问号]",
            AvgMissingPings = 3.2,
            AvgGetBackPings = 4.1,
        };
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")));

        Assert.Equal("玩家甲 近5场 胜率 [60%] | 发问号 3.2 | 发撤退 4.1 特征 [[玻璃心、爱发问号]]", summary);
    }

    [Theory]
    [InlineData(42, "迅捷斥候 近2场 胜率 [50%]")]
    [InlineData(999, "玩家甲 近2场 胜率 [50%]")]
    [InlineData(0, "玩家甲 近2场 胜率 [50%]")]
    public void BuildMyTeamSummary_ChampionNameRequested_UsesTheChampionWhenItIsKnown(int championId, string expected)
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, false];
        var builder = Builder(hideSelf: true, Known(player), Champion(42, "迅捷斥候"));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a", championId)), useChampionName: true);

        Assert.Equal(expected, summary);
    }

    [Fact]
    public void BuildMyTeamSummary_SeatWithoutAChampion_UsesTheCachedChampion()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, false];
        player.Player.ChampionId = 42;
        var builder = Builder(hideSelf: true, Known(player), Champion(42, "迅捷斥候"));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")), useChampionName: true);

        Assert.Equal("迅捷斥候 近2场 胜率 [50%]", summary);
    }

    [Fact]
    public void BuildMyTeamSummary_SeatChampionAndCachedChampion_NamesTheSeatChampion()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, false];
        player.Player.ChampionId = 999;
        var builder = Builder(hideSelf: true, Known(player), Champion(42, "迅捷斥候"));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a", 42)), useChampionName: true);

        Assert.Equal("迅捷斥候 近2场 胜率 [50%]", summary);
    }

    [Fact]
    public void BuildMyTeamSummary_ChampionWithoutAName_UsesThePlayerName()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, false];
        var builder = Builder(hideSelf: true, Known(player), Champion(42, "  "));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a", 42)), useChampionName: true);

        Assert.Equal("玩家甲 近2场 胜率 [50%]", summary);
    }

    [Fact]
    public void BuildMyTeamSummary_PlayerWithoutAGameName_SaysUnknown()
    {
        var player = Info(7, null!);
        player.WinHistory = [true, false];
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")));

        Assert.Equal("未知 近2场 胜率 [50%]", summary);
    }

    [Fact]
    public void BuildMyTeamSummary_PaddedGameName_IsTrimmed()
    {
        var player = Info(7, " 玩家甲 ");
        player.WinHistory = [true, false];
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")));

        Assert.Equal("玩家甲 近2场 胜率 [50%]", summary);
    }

    [Theory]
    [InlineData("PUUID-SELF")]
    [InlineData(" puuid-self ")]
    public void BuildMyTeamSummary_SenderPuuidInAnotherCaseOrPadded_IsStillTheSender(string myPuuid)
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, false];
        var builder = Builder(hideSelf: true, Known(player));
        builder.SetMyPuuid(myPuuid);

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-self")));

        Assert.Equal(string.Empty, summary);
    }

    [Fact]
    public void BuildMyTeamSummary_HideSelfOff_KeepsTheSender()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, false];
        var builder = Builder(hideSelf: false, Known(player));
        builder.SetMyPuuid("puuid-self");

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-self")));

        Assert.Equal("玩家甲 近2场 胜率 [50%]", summary);
    }

    [Fact]
    public void BuildMyTeamSummary_EmptySenderPuuid_KeepsEveryone()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, false];
        var builder = Builder(hideSelf: true, Known(player));
        builder.SetMyPuuid("");

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-self")));

        Assert.Equal("玩家甲 近2场 胜率 [50%]", summary);
    }

    [Fact]
    public void BuildMyTeamSummary_NullSenderPuuid_KeepsEveryone()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, false];
        var builder = Builder(hideSelf: true, Known(player));
        builder.SetMyPuuid(null!);

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-self")));

        Assert.Equal("玩家甲 近2场 胜率 [50%]", summary);
    }

    // A seat the sender's own puuid does not match but that has no cached history is left out for another reason.
    [Fact]
    public void BuildMyTeamSummary_SeatWithoutCachedHistory_IsLeftOutEvenWhenTheSenderIsShown()
    {
        var builder = Builder(hideSelf: false, Known());
        builder.SetMyPuuid("puuid-self");

        string summary = builder.BuildMyTeamSummary(new JArray(Row(8, "puuid-b")));

        Assert.Equal(string.Empty, summary);
    }

    [Fact]
    public void BuildMyTeamSummary_SeatWithoutAPuuid_IsLeftOut()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, false];
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "")));

        Assert.Equal(string.Empty, summary);
    }

    [Fact]
    public void BuildMyTeamSummary_SeatWithoutASummoner_IsLeftOut()
    {
        var player = Info(7, "玩家甲");
        player.WinHistory = [true, false];
        var builder = Builder(hideSelf: true, Known(player));

        string summary = builder.BuildMyTeamSummary(new JArray(Row(0, "puuid-c")));

        Assert.Equal(string.Empty, summary);
    }

    [Fact]
    public void BuildMyTeamSummary_CachedRecordWithoutAPlayer_IsLeftOut()
    {
        var known = new Dictionary<long, PlayerMatchInfo> { [7] = new PlayerMatchInfo { Player = null! } };
        var builder = Builder(hideSelf: true, known);

        string summary = builder.BuildMyTeamSummary(new JArray(Row(7, "puuid-a")));

        Assert.Equal(string.Empty, summary);
    }

    [Fact]
    public void BuildFullTeamSummary_BothTeams_SeparatesThemWithABlankLine()
    {
        var mine = Info(7, "玩家甲");
        mine.WinHistory = [true, false];
        var enemy = Info(8, "玩家乙");
        var builder = Builder(hideSelf: true, Known(mine, enemy));

        string summary = builder.BuildFullTeamSummary(new JArray(Row(7, "puuid-a")), new JArray(Row(8, "puuid-b")));

        // AppendLine writes Environment.NewLine, so the separator is spelled through it rather than as a literal; the
        // empty middle line is the blank line between the two teams.
        Assert.Equal(string.Join(Environment.NewLine, "玩家甲 近2场 胜率 [50%]", "", "玩家乙 暂无战绩"), summary);
    }

    [Fact]
    public void BuildFullTeamSummary_EnemyWithPings_UsesTheSameLineAsOurTeam()
    {
        var mine = Info(7, "玩家甲");
        mine.WinHistory = [true, false];
        var enemy = Info(8, "玩家乙");
        enemy.WinHistory = [true, true, false, false, true];
        enemy.Behavior = new PlayerBehavior
        {
            BehaviorSummary = "[玻璃心、爱发问号]",
            AvgMissingPings = 3.2,
            AvgGetBackPings = 4.1,
        };
        var builder = Builder(hideSelf: true, Known(mine, enemy));

        string summary = builder.BuildFullTeamSummary(new JArray(Row(7, "puuid-a")), new JArray(Row(8, "puuid-b")));

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "玩家甲 近2场 胜率 [50%]",
                "",
                "玩家乙 近5场 胜率 [60%] | 发问号 3.2 | 发撤退 4.1 特征 [[玻璃心、爱发问号]]"
            ),
            summary
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildFullTeamSummary_NoEnemyTeam_EndsAtOurLine(bool emptyEnemy)
    {
        var mine = Info(7, "玩家甲");
        mine.WinHistory = [true, false];
        var builder = Builder(hideSelf: true, Known(mine));

        string summary = builder.BuildFullTeamSummary(new JArray(Row(7, "puuid-a")), Enemy(emptyEnemy));

        Assert.Equal("玩家甲 近2场 胜率 [50%]", summary);
    }

    [Fact]
    public void BuildFullTeamSummary_NoTeamsAtAll_SaysThereIsNoData()
    {
        var builder = Builder(hideSelf: true, Known());

        string summary = builder.BuildFullTeamSummary(null, null);

        Assert.Equal("暂无数据", summary);
    }

    [Fact]
    public void BuildFullTeamSummary_OwnTeamWithNobody_StartsWithTheEnemyLine()
    {
        var enemy = Info(8, "玩家乙");
        enemy.WinHistory = [true, false];
        var builder = Builder(hideSelf: true, Known(enemy));

        string summary = builder.BuildFullTeamSummary(new JArray(Row(7, "puuid-a")), new JArray(Row(8, "puuid-b")));

        Assert.Equal("玩家乙 近2场 胜率 [50%]", summary);
    }

    [Fact]
    public void BuildFullTeamSummary_ChampionNames_ApplyToBothTeams()
    {
        var mine = Info(7, "玩家甲");
        mine.WinHistory = [true, false];
        var enemy = Info(8, "玩家乙");
        enemy.WinHistory = [true, false];
        var builder = Builder(hideSelf: true, Known(mine, enemy), Champion(42, "迅捷斥候"));

        string summary = builder.BuildFullTeamSummary(
            new JArray(Row(7, "puuid-a", 42)),
            new JArray(Row(8, "puuid-b", 42)),
            useChampionName: true
        );

        Assert.Equal(
            string.Join(Environment.NewLine, "迅捷斥候 近2场 胜率 [50%]", "", "迅捷斥候 近2场 胜率 [50%]"),
            summary
        );
    }

    private static ChatMessageBuilder Builder(
        bool hideSelf,
        Dictionary<long, PlayerMatchInfo> known,
        params PrimaryChampion[] champions
    ) => new(() => known, Manager(champions), hideSelf);

    /// <summary>A manager holding only the given champions; nothing is loaded or downloaded.</summary>
    private static ChampionManager Manager(params PrimaryChampion[] champions)
    {
        var manager = new ChampionManager(new HttpClient(), new ResourceLoading());
        foreach (PrimaryChampion champion in champions)
        {
            // AllChampions hands out the manager's own list, so this fills the one GetChampionById reads - the same
            // field the old check writes through reflection. InitializeAsync() would Clear() and replace that list,
            // so a manager filled this way is never initialized, and its unused client never reaches the network.
            manager.AllChampions.Add(champion);
        }

        return manager;
    }

    private static Dictionary<long, PlayerMatchInfo> Known(params PlayerMatchInfo[] players) =>
        players.ToDictionary(player => player.Player.SummonerId);

    private static PrimaryChampion Champion(int id, string name, bool active = true) =>
        new()
        {
            Id = id,
            Name = name,
            Alias = name,
            IsActive = active,
        };

    private static JObject Row(long summonerId, string puuid) =>
        new() { ["summonerId"] = summonerId, ["puuid"] = puuid };

    private static JObject Row(long summonerId, string puuid, int championId)
    {
        var row = Row(summonerId, puuid);
        row["championId"] = championId;
        return row;
    }

    private static PlayerMatchInfo Info(long summonerId, string gameName) =>
        new()
        {
            Player = new PlayerInfo
            {
                SummonerId = summonerId,
                GameName = gameName,
                Puuid = $"puuid-{summonerId}",
            },
        };

    /// <summary>An enemy list of the given emptiness; null stands for a client that sent no enemy team at all.</summary>
    private static JArray? Enemy(bool empty) => empty ? new JArray() : null;
}
