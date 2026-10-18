using League.Infrastructure;
using League.Models;
using League.Parsers;
using League.Tests.Support;
using Newtonsoft.Json.Linq;
using static League.FormMain;

namespace League.Tests.Parsers;

/// <summary>
/// The local history reader that turns a list of game documents into the rows the history list shows. The resource
/// table is left empty, so a champion icon lookup misses without reading a file or the network. The expected values
/// come from the checks in Tests/StatisticsChecks/MatchParsingChecks.cs, which this class replaces.
/// </summary>
[Collection(EnvironmentCollection.Name)]
public class MatchDataParserTests : IDisposable
{
    private const string SelfPuuid = "self-puuid-0001";
    private const string ChampionName = "Parse Check's Champion";
    private const long GameStart = 1767225600000L;

    private readonly MatchDataParser _parser = new();
    private readonly ResourceLoading _previousResources = Globals.resLoading;

    public MatchDataParserTests()
    {
        Globals.resLoading = new ResourceLoading();
    }

    public void Dispose() => Globals.resLoading = _previousResources;

    public static TheoryData<int, string> QueueModes =>
        new()
        {
            { 420, "单双排" },
            { 440, "灵活排位" },
            { 400, "匹配征召" },
            { 430, "匹配盲选" },
            { 450, "大乱斗" },
            { 900, "无限乱斗" },
            { 1900, "自选无限火力" },
            { 480, "快速模式" },
            { 1700, "斗魂竞技场" },
            { 2400, "海克斯乱斗" },
            { 2450, "经典海克斯乱斗" },
            { 1020, "克隆大作战" },
            { 1300, "极限闪击" },
            { 1400, "终极魔典" },
            { 700, "冠军杯赛" },
            { 830, "人机入门" },
            { 840, "人机新手" },
            { 850, "人机中等" },
            { 870, "人机入门" },
            { 880, "人机新手" },
            { 890, "人机中等" },
            { 2000, "新手教程1" },
            { 2010, "新手教程2" },
            { 2020, "新手教程3" },
            { 1090, "云顶匹配" },
            { 1100, "云顶排位" },
            { 1130, "云顶疾速" },
            { 1160, "云顶双人" },
            { 3100, "自定义峡谷" },
            { 3140, "训练模式" },
            { 3270, "自定义海克斯" },
            { 4310, "经典峡谷" },
            { 4320, "经典人机" },
            { 950, "末日人机" },
            { 960, "末日人机" },
        };

    [Fact]
    public async Task ParsePlayerMatchInfo_MissingHistory_IsEmpty()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, null!);

        Assert.Empty(history.RecentMatches);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_EmptyHistory_IsEmpty()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, new JArray());

        Assert.Empty(history.RecentMatches);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_OnlyTheGamesThePlayerTookPartIn_AreRead()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Equal(2, history.RecentMatches.Count);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_Duration_IsReadInWholeMinutes()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Equal(22, history.RecentMatches[0].GameDurationMinutes);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_EveryGame_RecordsWhetherItWasWon()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Equal(new[] { true, false }, history.WinHistory);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_EveryKey_IsTheGameAndTheTeam()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Equal(new[] { "7412345678_100", "2_100" }, history.MatchKeys);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_ChampionName_NamesTheIcon()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Equal("ParseChecksChampion", history.MatchItems[0].ImageKey);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_AWin_IsShownInGreen()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Equal(Color.Green, history.MatchItems[0].ForeColor);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_ALoss_IsShownInRed()
    {
        var loss = Match(3L, win: false);

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, new JArray(loss));

        Assert.Equal(Color.Red, history.MatchItems[0].ForeColor);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_ARemake_HasItsOwnColour()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Equal(MatchResult.RemakeColor, history.MatchItems[1].ForeColor);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_TheRow_NamesTheMode()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Equal("单双排", history.MatchItems[0].SubItems[1].Text);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_TheRow_ShowsTheScore()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Equal("10/2/7", history.MatchItems[0].SubItems[2].Text);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_TheRow_ShowsTheDayTheGameStarted()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Equal("2026-01-01", history.MatchItems[0].SubItems[3].Text);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_TheRow_CarriesItsGameIdAndTeam()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        var metadata = (MatchMetadata)history.MatchItems[0].Tag!;
        Assert.Equal(7412345678L, metadata.GameId);
        Assert.Equal(100, metadata.TeamId);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_TheRow_CarriesItsOwnCopyOfTheGame()
    {
        var game = Match(7412345678L);
        var history = new JArray(game);

        PlayerMatchInfo info = await _parser.ParsePlayerMatchInfo(SelfPuuid, history);

        game["gameId"] = 7L;
        Assert.Equal(7412345678L, ((MatchMetadata)info.MatchItems[0].Tag!).GameData!["gameId"]!.Value<long>());
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_AnIconThatCannotBeLoaded_IsLeftOut()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Empty(history.HeroIcons.Images);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_ThePlayer_IsLeftForTheCallerToFillIn()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Null(history.Player);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_AnExplicitZeroGameId_IsSkippedWhileAMissingOneIsKept()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, new JArray(Match(0L), Matchless()));

        Assert.Equal(new[] { "0_-1" }, history.MatchKeys);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_ParticipantsNestedUnderInfo_AreNotRows()
    {
        var nested = new JArray(JObject.Parse("""{"json":{"info":{"participants":[{"puuid":"self-puuid-0001"}]}}}"""));

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, nested);

        Assert.Empty(history.RecentMatches);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_GameWrappedInJson_IsRead()
    {
        var wrapped = new JArray(new JObject { ["json"] = Match(7412345678L) });

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, wrapped);

        Assert.Equal(new[] { "7412345678_100" }, history.MatchKeys);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_GameWithoutParticipants_IsNotARow()
    {
        var game = Match(7412345678L);
        game.Remove("participants");

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, new JArray(game));

        Assert.Empty(history.RecentMatches);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_Stat_ReadsTheScore()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Equal(10, history.RecentMatches[0].Kills);
        Assert.Equal(2, history.RecentMatches[0].Deaths);
        Assert.Equal(7, history.RecentMatches[0].Assists);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_Stat_ReadsThePings()
    {
        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        MatchStat stat = history.RecentMatches[0];
        Assert.Equal(4, stat.EnemyMissingPings);
        Assert.Equal(3, stat.EnemyVisionPings);
        Assert.Equal(5, stat.GetBackPings);
        Assert.Equal(1, stat.DangerPings);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_Stat_ReadsSurrenderFlagsFromTheGame()
    {
        var surrendered = Match(5L);
        surrendered["gameEndedInSurrender"] = true;
        surrendered["gameEndedInEarlySurrender"] = true;

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, new JArray(surrendered));

        Assert.True(history.RecentMatches[0].GameEndedInSurrender);
        Assert.True(history.RecentMatches[0].GameEndedInEarlySurrender);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_WithoutADuration_ReadsZeroMinutes()
    {
        var game = Match(6L);
        game.Remove("gameDuration");

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, new JArray(game));

        Assert.Equal(0, history.RecentMatches[0].GameDurationMinutes);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_DurationUnderGameLength_FallsBackToIt()
    {
        var game = Match(7L);
        game.Remove("gameDuration");
        game["gameLength"] = 605;

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, new JArray(game));

        Assert.Equal(10, history.RecentMatches[0].GameDurationMinutes);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_StartTimeUnderGameCreation_FallsBackToIt()
    {
        var game = Match(8L);
        game.Remove("gameStartTimestamp");
        game["gameCreation"] = GameStart;

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, new JArray(game));

        Assert.Equal("2026-01-01", history.MatchItems[0].SubItems[3].Text);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_WithoutAStartTime_ShowsNoDay()
    {
        var game = Match(9L);
        game.Remove("gameStartTimestamp");

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, new JArray(game));

        Assert.Equal("未知", history.MatchItems[0].SubItems[3].Text);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_WithoutATeam_KeepsMinusOne()
    {
        var game = Match(10L);
        ((JObject)game["participants"]![0]!).Remove("teamId");

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, new JArray(game));

        Assert.Equal(new[] { "10_-1" }, history.MatchKeys);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_WithoutAChampionName_LeavesTheIconKeyEmpty()
    {
        var game = Match(11L);
        game["participants"]![0]!["championName"] = null;

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, new JArray(game));

        Assert.Equal(string.Empty, history.MatchItems[0].ImageKey);
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_Cancelled_StopsInsteadOfReturningAPartialHistory()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _parser.ParsePlayerMatchInfo(SelfPuuid, History(), cancelled.Token)
        );
    }

    [Fact]
    public async Task ParsePlayerMatchInfo_AFailure_AbandonsTheGamesAfterIt()
    {
        Globals.resLoading = null!;

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, History());

        Assert.Equal(1, history.RecentMatches.Count);
        Assert.Equal(1, history.MatchItems.Count);
    }

    [Theory]
    [MemberData(nameof(QueueModes))]
    public async Task ParsePlayerMatchInfo_QueueId_NamesTheMode(int queueId, string expected)
    {
        var game = Match(12L);
        game["queueId"] = queueId;
        game.Remove("metadata");

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, new JArray(game));

        Assert.Equal(expected, history.MatchItems[0].SubItems[1].Text);
    }

    [Theory]
    [InlineData(420, "q_450", "CLASSIC", "大乱斗")]
    [InlineData(2400, "q_420", "", "单双排")]
    [InlineData(2400, "", "", "海克斯乱斗")]
    [InlineData(420, "q_999,type_CUSTOM_GAME", "", "自定义")]
    [InlineData(420, "mode_kiwi", "", "自定义海克斯")]
    [InlineData(420, "mode_practicetool", "", "训练模式")]
    [InlineData(420, "mdoe_jade", "", "经典峡谷")]
    [InlineData(2400, "nonsense", "", "海克斯乱斗")]
    [InlineData(-1, "", "CLASSIC", "自定义模式")]
    [InlineData(-1, "", "", "未知(queueId=-1)")]
    [InlineData(99999, "", "CLASSIC", "召唤师峡谷")]
    [InlineData(99999, "", "SWIFTPLAY", "快速模式")]
    [InlineData(99999, "", "TFT", "云顶之弈")]
    [InlineData(99999, "", "NONSENSE", "未知(99999)")]
    [InlineData(99999, "", "", "未知(queueId=99999)")]
    public async Task ParsePlayerMatchInfo_TagsQueueAndGameMode_NameTheMode(
        int queueId,
        string tags,
        string gameMode,
        string expected
    )
    {
        var game = Match(13L);
        game["queueId"] = queueId;
        game["gameMode"] = gameMode;
        game["metadata"] = new JObject { ["tags"] = new JArray(tags.Split(',')) };

        PlayerMatchInfo history = await _parser.ParsePlayerMatchInfo(SelfPuuid, new JArray(game));

        Assert.Equal(expected, history.MatchItems[0].SubItems[1].Text);
    }

    private static JArray History()
    {
        var remake = Match(2L, win: false);
        remake["gameEndedInEarlySurrender"] = true;
        var other = Match(3L);
        other["participants"]![0]!["puuid"] = "someone-else";
        return new JArray(Match(7412345678L), remake, other);
    }

    private static JObject Match(long gameId, bool win = true) =>
        new()
        {
            ["gameId"] = gameId,
            ["gameDuration"] = 1325,
            ["gameStartTimestamp"] = GameStart,
            ["queueId"] = 420,
            ["gameMode"] = "CLASSIC",
            ["participants"] = new JArray(
                new JObject
                {
                    ["puuid"] = SelfPuuid,
                    ["teamId"] = 100,
                    ["win"] = win,
                    ["championId"] = 103,
                    ["championName"] = ChampionName,
                    ["kills"] = 10,
                    ["deaths"] = 2,
                    ["assists"] = 7,
                    ["enemyMissingPings"] = 4,
                    ["enemyVisionPings"] = 3,
                    ["getBackPings"] = 5,
                    ["dangerPings"] = 1,
                }
            ),
        };

    private static JObject Matchless() =>
        new() { ["gameDuration"] = 600, ["participants"] = new JArray(new JObject { ["puuid"] = SelfPuuid }) };
}
