using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The loading-screen roster: the client session's flow parsed into players, the live game's own list layered over
/// it, and the phase texts the overlay shows. The expected values come from the checks in
/// Tests/StatisticsChecks/LoadingRosterIdentityChecks.cs, StaleSessionChecks.cs and LiveChampionChecks.cs.
/// </summary>
public class LoadingRosterTests
{
    private const string SelfPuuid = "a";

    private static LoadingRoster Roster(params LoadingPlayer[] players) => new("game-1", 450, "ARAM", players);

    private static LoadingRoster RosterWithSelf(int selfTeam) =>
        Roster(new LoadingPlayer("session-key", "玩家甲#01", SelfPuuid, 7L, 99, selfTeam, true));

    private static JObject Row(
        string puuid = "",
        long summonerId = 0,
        int championId = 0,
        string gameName = "",
        string summonerName = "",
        string tagLine = ""
    ) =>
        new()
        {
            ["puuid"] = puuid,
            ["summonerId"] = summonerId,
            ["championId"] = championId,
            ["gameName"] = gameName,
            ["summonerName"] = summonerName,
            ["tagLine"] = tagLine,
        };

    private static JObject Pick(string puuid, int championId, long summonerId = 0) =>
        new()
        {
            ["puuid"] = puuid,
            ["championId"] = championId,
            ["summonerId"] = summonerId,
        };

    private static JObject Flow(JObject[] teamOne, JObject[] teamTwo, params JObject[] selections) =>
        new()
        {
            ["gameData"] = new JObject
            {
                ["gameId"] = "game-1",
                ["queue"] = new JObject { ["id"] = 450, ["gameMode"] = "ARAM" },
                ["teamOne"] = new JArray(teamOne),
                ["teamTwo"] = new JArray(teamTwo),
                ["playerChampionSelections"] = new JArray(selections),
            },
        };

    private static LiveMatchPlayer LivePlayer(string key, string name, string team, bool self = false) =>
        new(key, name, "Lux", "拉克丝", team, self, 1, []);

    private static LiveMatchSnapshot Live(params LiveMatchPlayer[] players) => new("ARAM", 12, 0, players);

    [Fact]
    public void LoadingPlayer_AllFields_KeepTheConstructorOrder()
    {
        var player = new LoadingPlayer("session-key", "玩家甲#01", SelfPuuid, 7L, 99, 200, true, true);

        Assert.Equal("session-key", player.Key);
        Assert.Equal("玩家甲#01", player.Name);
        Assert.Equal(SelfPuuid, player.Puuid);
        Assert.Equal(7L, player.SummonerId);
        Assert.Equal(99, player.ChampionId);
        Assert.Equal(200, player.Team);
        Assert.True(player.Self);
        Assert.True(player.IdentityHidden);
    }

    [Fact]
    public void LoadingPlayer_IdentityHiddenOmitted_DefaultsToFalse()
    {
        var player = new LoadingPlayer("session-key", "玩家甲#01", SelfPuuid, 7L, 99, 100, true);

        Assert.True(player.Self);
        Assert.False(player.IdentityHidden);
    }

    [Theory]
    [InlineData("GameStart")]
    [InlineData("InProgress")]
    [InlineData("Reconnect")]
    public void CanDisplay_LoadingPhases_ReturnsTrue(string phase)
    {
        Assert.True(LoadingRoster.CanDisplay(phase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ChampSelect")]
    [InlineData("gamestart")]
    [InlineData("inprogress")]
    [InlineData("GameStart ")]
    [InlineData("Reconnect ")]
    [InlineData("Lobby")]
    public void CanDisplay_OtherPhases_ReturnsFalse(string phase)
    {
        Assert.False(LoadingRoster.CanDisplay(phase));
    }

    [Theory]
    [InlineData("", "等待连接英雄联盟客户端…")]
    [InlineData("ChampSelect", "选人中 · 进入加载界面后显示双方英雄。")]
    [InlineData("EndOfGame", "本局已结束 · 等待下一场对局。")]
    [InlineData("PreEndOfGame", "本局已结束 · 等待下一场对局。")]
    [InlineData("WaitingForStats", "本局已结束 · 等待下一场对局。")]
    [InlineData("Lobby", "暂无进行中的对局 · 进入加载界面后自动读取。")]
    [InlineData("InProgress", "暂无进行中的对局 · 进入加载界面后自动读取。")]
    public void WaitingStatus_Phase_ReturnsTheExactText(string phase, string expected)
    {
        Assert.Equal(expected, LoadingRoster.WaitingStatus(phase));
    }

    [Fact]
    public void Parse_TwoTeamFlow_ReadsMetadataIdentitiesAndOrder()
    {
        var flow = Flow(
            [Row(SelfPuuid, 1, gameName: "玩家甲", tagLine: "01")],
            [Row("b", 2, 25, gameName: "玩家乙", tagLine: "02"), Row("c", 3, summonerName: "旧名称丙")]
        );

        var roster = LoadingRoster.Parse(flow, SelfPuuid);

        Assert.Equal("game-1", roster.GameId);
        Assert.Equal(450, roster.Queue);
        Assert.Equal("ARAM", roster.Mode);
        Assert.Equal(new[] { SelfPuuid, "b", "c" }, roster.Players.Select(p => p.Puuid));
        Assert.Equal(new[] { "玩家甲#01", "玩家乙#02", "旧名称丙" }, roster.Players.Select(p => p.Name));
        Assert.Equal(new[] { 100, 200, 200 }, roster.Players.Select(p => p.Team));
        Assert.Equal(new[] { true, false, false }, roster.Players.Select(p => p.Self));
    }

    [Fact]
    public void Parse_RowWithoutAName_DoesNotAppendTheTag()
    {
        var flow = Flow([Row(SelfPuuid, 1, tagLine: "01")], []);

        var roster = LoadingRoster.Parse(flow, SelfPuuid);

        Assert.Equal("", Assert.Single(roster.Players).Name);
    }

    [Theory]
    [InlineData("a", 0L, "a")]
    [InlineData("", 9L, "9")]
    [InlineData("", 0L, "100:0")]
    public void Parse_IdentityKeys_FallBackFromPuuidToSummonerIdToPosition(
        string puuid,
        long summonerId,
        string expected
    )
    {
        var flow = Flow([Row(puuid, summonerId)], []);

        var roster = LoadingRoster.Parse(flow, "own");

        Assert.Equal(expected, Assert.Single(roster.Players).Key);
    }

    [Fact]
    public void Parse_ZeroPuuid_RecoversIdentityFromASelectionMatchedBySummonerId()
    {
        var flow = Flow([Row(Guid.Empty.ToString(), 1)], [], Pick(SelfPuuid, 99, 1));

        var player = Assert.Single(LoadingRoster.Parse(flow, SelfPuuid).Players);

        Assert.Equal(SelfPuuid, player.Puuid);
        Assert.Equal(99, player.ChampionId);
        Assert.True(player.Self);
    }

    [Fact]
    public void Parse_ChampionMissingOnTheRow_ComesFromThePickMatchedByPuuid()
    {
        var flow = Flow(
            [Row(SelfPuuid, 1, gameName: "玩家甲", tagLine: "01")],
            [Row("b", 2, 25)],
            Pick(SelfPuuid, 99),
            Pick("unknown", 111)
        );

        var roster = LoadingRoster.Parse(flow, SelfPuuid);

        Assert.Equal(new[] { 99, 25 }, roster.Players.Select(p => p.ChampionId));
    }

    [Fact]
    public void Parse_AccountLeftOverFromAnEarlierGame_KeepsOneEntryAndMarksConflicted()
    {
        var flow = Flow(
            [Row(SelfPuuid, 9, 120)],
            [
                Row("leftover-1", 1, 53),
                Row("leftover-2", 2, 22),
                Row(SelfPuuid, 9, 4),
                Row("leftover-3", 3, 223),
                Row("leftover-4", 4, 63),
            ],
            Pick(SelfPuuid, 120)
        );

        var roster = LoadingRoster.Parse(flow, SelfPuuid);
        var self = roster.Players.Where(p => p.Self).ToArray();

        Assert.True(roster.Conflicted);
        Assert.Equal(5, roster.Players.Length);
        Assert.Single(self);
        Assert.Equal(120, self[0].ChampionId);
        Assert.Equal(100, self[0].Team);
    }

    [Fact]
    public void Parse_DuplicateAccountWithoutAMatchingPick_KeepsTheFirstEntry()
    {
        var flow = Flow([Row(SelfPuuid, 9, 120, gameName: "玩家甲")], [Row(SelfPuuid, 9, 4, gameName: "玩家甲")]);

        var roster = LoadingRoster.Parse(flow, SelfPuuid);

        Assert.True(roster.Conflicted);
        Assert.Equal(100, Assert.Single(roster.Players).Team);
    }

    [Fact]
    public void Parse_NoDuplicateAccount_IsNotConflicted()
    {
        var flow = Flow([Row(SelfPuuid, 1, 120)], [Row("b", 2, 25)]);

        var roster = LoadingRoster.Parse(flow, SelfPuuid);

        Assert.Equal(2, roster.Players.Length);
        Assert.False(roster.Conflicted);
    }

    [Fact]
    public void Parse_EmptyFlow_ReturnsAnEmptyRoster()
    {
        var roster = LoadingRoster.Parse(new JObject(), SelfPuuid);

        Assert.Empty(roster.Players);
        Assert.Equal("", roster.GameId);
        Assert.Equal(0, roster.Queue);
        Assert.Equal("", roster.Mode);
    }

    [Fact]
    public void Parse_QueueWithoutAMode_FallsBackToTheGameDataMode()
    {
        var flow = new JObject
        {
            ["gameData"] = new JObject
            {
                ["gameMode"] = "CLASSIC",
                ["queue"] = new JObject { ["id"] = 420 },
            },
        };

        Assert.Equal("CLASSIC", LoadingRoster.Parse(flow, SelfPuuid).Mode);
    }

    [Theory]
    [InlineData("ORDER", 100)]
    [InlineData("CHAOS", 200)]
    [InlineData("UNKNOWN", 0)]
    public void FromLive_TeamName_MapsToTheSideNumber(string team, int expected)
    {
        var roster = Roster().FromLive(Live(LivePlayer("live-1", "玩家甲#01", team)), _ => 7);

        Assert.Equal(expected, Assert.Single(roster.Players).Team);
    }

    [Fact]
    public void FromLive_ChampionId_ComesFromTheInjectedMapping()
    {
        var livePlayer = LivePlayer("live-1", "玩家甲#01", "ORDER", true);
        LiveMatchPlayer? seen = null;

        var roster = Roster()
            .FromLive(
                Live(livePlayer),
                player =>
                {
                    seen = player;
                    return 42;
                }
            );

        Assert.Same(livePlayer, seen);
        Assert.Equal(42, Assert.Single(roster.Players).ChampionId);
    }

    [Fact]
    public void FromLive_NameMatchedEntry_CarriesItsKeyAndConfirmedIdentity()
    {
        var roster = RosterWithSelf(100);

        var merged = roster.FromLive(Live(LivePlayer("live-1", "玩家甲#01", "ORDER", true)), _ => 42);

        var player = Assert.Single(merged.Players);
        Assert.Equal("session-key", player.Key);
        Assert.Equal(SelfPuuid, player.Puuid);
        Assert.Equal(7L, player.SummonerId);
        Assert.Equal("玩家甲#01", player.Name);
        Assert.Equal(42, player.ChampionId);
        Assert.True(player.Self);
    }

    [Theory]
    [InlineData(100, "ORDER", 99)]
    [InlineData(0, "ORDER", 99)]
    [InlineData(200, "CHAOS", 99)]
    [InlineData(200, "ORDER", 0)]
    [InlineData(100, "UNKNOWN", 0)]
    public void FromLive_UnavailableLiveChampion_FallsBackOnlyForAConfirmedSameTeamPick(
        int sessionTeam,
        string liveTeam,
        int expected
    )
    {
        var roster = Roster(new LoadingPlayer("session-key", "玩家甲#01", SelfPuuid, 7L, 99, sessionTeam, true));

        var merged = roster.FromLive(Live(LivePlayer("live-1", "玩家甲#01", liveTeam, true)), _ => 0);

        Assert.Equal(expected, Assert.Single(merged.Players).ChampionId);
    }

    [Fact]
    public void FromLive_UnconfirmedSessionAccount_CannotSupplyTheChampion()
    {
        var roster = Roster(new LoadingPlayer("session-key", "玩家甲#01", "", 0, 99, 100, true));

        var merged = roster.FromLive(Live(LivePlayer("live-1", "玩家甲#01", "ORDER", true)), _ => 0);

        Assert.Equal(0, Assert.Single(merged.Players).ChampionId);
    }

    [Fact]
    public void FromLive_AmbiguousFullName_LeavesIdentityPendingAndNeverInfersTheChampion()
    {
        var roster = Roster(
            new LoadingPlayer("key-1", "同名#01", SelfPuuid, 1L, 99, 100, true),
            new LoadingPlayer("key-2", "同名#01", "b", 2L, 99, 200, false)
        );

        var merged = roster.FromLive(Live(LivePlayer("live-1", "同名#01", "ORDER", true)), _ => 7);

        var player = Assert.Single(merged.Players);
        Assert.Equal("", player.Puuid);
        Assert.Equal(0L, player.SummonerId);
        Assert.False(player.IdentityHidden);
        Assert.Equal(7, player.ChampionId);
    }

    [Fact]
    public void FromLive_NoNameMatch_LeavesIdentityPendingInsteadOfHidden()
    {
        var roster = RosterWithSelf(100);

        var merged = roster.FromLive(Live(LivePlayer("live-1", "不匹配的玩家#01", "ORDER")), _ => 0);

        var player = Assert.Single(merged.Players);
        Assert.Equal("live-1", player.Key);
        Assert.Equal("", player.Puuid);
        Assert.False(player.IdentityHidden);
        Assert.Equal(0, player.ChampionId);
    }

    [Theory]
    [InlineData("#")]
    [InlineData(" # ")]
    public void FromLive_HiddenSlot_ShowsThePlaceholderAndInheritsNothing(string liveName)
    {
        var roster = Roster(new LoadingPlayer("session-key", "#", SelfPuuid, 7L, 99, 100, true));

        var merged = roster.FromLive(Live(LivePlayer("live-1", liveName, "ORDER", true)), _ => 0);

        var player = Assert.Single(merged.Players);
        Assert.Equal("隐藏玩家", player.Name);
        Assert.Equal("", player.Puuid);
        Assert.Equal(0L, player.SummonerId);
        Assert.True(player.IdentityHidden);
        Assert.Equal(0, player.ChampionId);
    }

    [Fact]
    public void FromLive_LiveList_ReplacesTheSessionLeftovers()
    {
        var roster = Roster(
            new LoadingPlayer("leftover", "上一局玩家", "leftover-1", 1L, 1, 100, false),
            new LoadingPlayer("session-key", "玩家甲#01", SelfPuuid, 7L, 99, 100, true)
        );

        var merged = roster.FromLive(
            Live(LivePlayer("live-1", "玩家甲#01", "ORDER", true), LivePlayer("live-2", "玩家乙#02", "CHAOS")),
            _ => 42
        );

        Assert.Equal(new[] { "玩家甲#01", "玩家乙#02" }, merged.Players.Select(p => p.Name));
        Assert.Equal(new[] { "session-key", "live-2" }, merged.Players.Select(p => p.Key));
    }

    [Theory]
    [InlineData(100, 100, "我方")]
    [InlineData(100, 200, "对方")]
    [InlineData(200, 100, "对方")]
    [InlineData(200, 200, "我方")]
    public void TeamLabel_KnownSelfTeam_ReturnsOwnOrEnemy(int selfTeam, int team, string expected)
    {
        Assert.Equal(expected, RosterWithSelf(selfTeam).TeamLabel(team));
    }

    [Fact]
    public void TeamLabel_UnknownSelf_DoesNotAssumeBlue()
    {
        var roster = LoadingRoster.Parse(Flow([Row("b", 1, 120)], []), SelfPuuid);

        Assert.Equal("蓝方", roster.TeamLabel(100));
        Assert.Equal("红方", roster.TeamLabel(200));
    }
}
