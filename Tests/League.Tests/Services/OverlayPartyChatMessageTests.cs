using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// One chat line with a side's suspected premades: the largest history group first, ungrouped players split into solo
/// (the main window loaded their history) and unknown (it did not), the whole line cut to the chat limit. The roster
/// follows Tests/StatisticsChecks/OverlayPartyChecks.cs; roster, cache and champion names are built here, so nothing
/// reads a client, a game or the disk.
/// </summary>
public class OverlayPartyChatMessageTests
{
    /// <summary>The chat limit the class cuts at: a longer line is shortened, never sent whole.</summary>
    private const int Limit = 180;

    /// <summary>U+2026, the single character a cut line ends with — not three dots.</summary>
    private const string Ellipsis = "\u2026";

    /// <summary>Ten seats: five on the own side, a pair and a trio on the other. Everyone picked a champion.</summary>
    private static readonly LoadingRoster Roster = new(
        "match",
        450,
        "ARAM",
        [
            new("k0", "同名", "account-0", 1, 100, 100, true),
            new("k1", "同名", "account-1", 2, 101, 100, false),
            new("k2", "同名", "account-2", 3, 102, 100, false),
            new("k3", "同名", "account-3", 4, 103, 100, false),
            new("k4", "同名", "account-4", 5, 104, 100, false),
            new("k5", "同名", "account-5", 6, 105, 200, false),
            new("k6", "同名", "account-6", 7, 106, 200, false),
            new("k7", "同名", "account-7", 8, 107, 200, false),
            new("k8", "同名", "account-8", 9, 108, 200, false),
            new("k9", "同名", "account-9", 10, 109, 200, false),
        ]
    );

    [Fact]
    public void Build_EnemyPremades_AreListedLargestFirst()
    {
        var messages = OverlayPartyChatMessage.Build(Roster, Request("enemy"), Cached(), Hero);

        Assert.Equal(
            "【疑似组排·对方】3排：[英雄107、英雄108、英雄109]；2排：[英雄105、英雄106]（按近期同队战绩推测）",
            Assert.Single(messages)
        );
    }

    [Theory]
    [InlineData("英雄", "英雄100、英雄101、英雄102、英雄103、英雄104")]
    [InlineData("CH", "CH100、CH101、CH102、CH103、CH104")]
    [InlineData("", "100、101、102、103、104")]
    public void Build_OwnGroup_PrintsTheNameTheDelegateGivesForEachChampion(string prefix, string names)
    {
        var messages = OverlayPartyChatMessage.Build(Roster, Request("own"), Cached(), id => prefix + id);

        Assert.Equal("【疑似组排·我方】5排：[" + names + "]（按近期同队战绩推测）", Assert.Single(messages));
    }

    [Fact]
    public void Build_APlayerWhoHidTheirName_IsUnknownRatherThanSolo()
    {
        var roster = Roster with
        {
            Players = [.. Roster.Players.Select(p => p.Key == "k4" ? p with { IdentityHidden = true } : p)],
        };

        var messages = OverlayPartyChatMessage.Build(roster, Request("own"), Cached(), Hero);

        Assert.Equal(
            "【疑似组排·我方】4排：[英雄100、英雄101、英雄102、英雄103]；未知：[英雄104]（按近期同队战绩推测）",
            Assert.Single(messages)
        );
    }

    [Fact]
    public void Build_APlayerTheCacheDoesNotHold_IsUnknownRatherThanSolo()
    {
        var cached = Cached().Where(info => info.Player.Puuid != "account-4");

        var messages = OverlayPartyChatMessage.Build(Roster, Request("own"), cached, Hero);

        Assert.Equal(
            "【疑似组排·我方】4排：[英雄100、英雄101、英雄102、英雄103]；未知：[英雄104]（按近期同队战绩推测）",
            Assert.Single(messages)
        );
    }

    [Fact]
    public void Build_ALoadedPlayerWithoutASharedGame_IsSolo()
    {
        var cached = Cached();
        cached[4].MatchKeys.Clear();

        var messages = OverlayPartyChatMessage.Build(Roster, Request("own"), cached, Hero);

        Assert.Equal(
            "【疑似组排·我方】4排：[英雄100、英雄101、英雄102、英雄103]；单排：[英雄104]（按近期同队战绩推测）",
            Assert.Single(messages)
        );
    }

    [Fact]
    public void Build_NoSharedGamesAtAll_ReportsNoPremadeAndListsEveryoneAsSolo()
    {
        var messages = OverlayPartyChatMessage.Build(Roster, Request("own"), CachedWithoutSharedGames(), Hero);

        Assert.Equal(
            "【疑似组排·我方】未发现组排；单排：[英雄100、英雄101、英雄102、英雄103、英雄104]（按近期同队战绩推测）",
            Assert.Single(messages)
        );
    }

    [Theory]
    [InlineData("single")]
    [InlineData("all")]
    [InlineData("")]
    public void Build_ATargetThatIsNotASide_IsRefused(string target)
    {
        var build = () => OverlayPartyChatMessage.Build(Roster, Request(target), Cached(), Hero);

        Assert.Equal("未发送：组排情况只能按我方或对方发送。", Assert.Throws<InvalidOperationException>(build).Message);
    }

    [Fact]
    public void Build_AnEmptyRoster_IsRefused()
    {
        var build = () => OverlayPartyChatMessage.Build(Roster with { Players = [] }, Request("own"), Cached(), Hero);

        Assert.Equal(
            "未发送：无法确定所选阵营，请等待名单更新。",
            Assert.Throws<InvalidOperationException>(build).Message
        );
    }

    [Fact]
    public void Build_NoChampionChosenYet_IsRefused()
    {
        var roster = Roster with { Players = [.. Roster.Players.Select(p => p with { ChampionId = 0 })] };
        var build = () => OverlayPartyChatMessage.Build(roster, Request("own"), Cached(), Hero);

        Assert.Equal(
            "未发送：无法确定所选阵营，请等待名单更新。",
            Assert.Throws<InvalidOperationException>(build).Message
        );
    }

    [Fact]
    public void Build_NoCachedHistory_IsRefused()
    {
        var build = () => OverlayPartyChatMessage.Build(Roster, Request("own"), [], Hero);

        Assert.Equal(
            "未发送：主程序还没有读取这些玩家的战绩，无法推测组排。",
            Assert.Throws<InvalidOperationException>(build).Message
        );
    }

    [Fact]
    public void Build_OnlyTheOtherSidesHistoryCached_IsRefused()
    {
        var cached = Cached().Where(info => info.Player.SummonerId > 5);
        var build = () => OverlayPartyChatMessage.Build(Roster, Request("own"), cached, Hero);

        Assert.Equal(
            "未发送：主程序还没有读取这些玩家的战绩，无法推测组排。",
            Assert.Throws<InvalidOperationException>(build).Message
        );
    }

    [Fact]
    public void Build_ALineOfExactlyTheChatLimit_IsSentWhole()
    {
        string[] names = [WideHero(100), WideHero(101), WideHero(102), WideHero(103), WideHero(104)];
        string full = "【疑似组排·我方】5排：[" + string.Join("、", names) + "]（按近期同队战绩推测）";

        var messages = OverlayPartyChatMessage.Build(Roster, Request("own"), Cached(), WideHero);

        // The five names and the frame are 180 characters together, so "at the limit is sent whole" is what runs.
        Assert.Equal(Limit, full.Length);
        Assert.Equal(full, Assert.Single(messages));
    }

    [Fact]
    public void Build_ALineOneCharacterOverTheChatLimit_IsCutAtTheLimit()
    {
        string[] names = [EdgeHero(100), EdgeHero(101), EdgeHero(102), EdgeHero(103), EdgeHero(104)];
        string full = "【疑似组排·我方】5排：[" + string.Join("、", names) + "]（按近期同队战绩推测）";

        var messages = OverlayPartyChatMessage.Build(Roster, Request("own"), Cached(), EdgeHero);

        // One character over. Together with the test above this pins the limit at 180, not 179 and not 181.
        Assert.Equal(Limit + 1, full.Length);
        Assert.Equal(full[..(Limit - 1)] + Ellipsis, Assert.Single(messages));
    }

    [Fact]
    public void Build_ALineWellOverTheChatLimit_IsCutInsideAChampionNameAndEndsWithAnEllipsis()
    {
        string[] names = [LongHero(100), LongHero(101), LongHero(102), LongHero(103), LongHero(104)];
        string full = "【疑似组排·我方】5排：[" + string.Join("、", names) + "]（按近期同队战绩推测）";

        var messages = OverlayPartyChatMessage.Build(Roster, Request("own"), Cached(), LongHero);

        // 60 characters per name: character 180 falls inside the third name, and the closing hint is dropped.
        Assert.True(full.Length > Limit, "预设文案必须超过 180 字，截断规则才有被验证的机会。");
        Assert.Equal(Limit, Assert.Single(messages).Length);
        Assert.Equal(full[..(Limit - 1)] + Ellipsis, Assert.Single(messages));
    }

    private static string Hero(int id) => "英雄" + id;

    /// <summary>
    /// A name of 30 characters, except the first seat's 31: the five of them and the frame fill the 180-character
    /// limit exactly, so the message is the longest one that is still sent whole (179 would leave the limit untested).
    /// </summary>
    private static string WideHero(int id) => $"英雄{id:D4}" + new string('甲', id == 100 ? 25 : 24);

    /// <summary>A name of 30 or 31 characters, two of them longer: the line lands one character over the limit.</summary>
    private static string EdgeHero(int id) => $"英雄{id:D4}" + new string('甲', id <= 101 ? 25 : 24);

    /// <summary>A 60-character name, so the cut for a five-player group falls inside a name rather than between two.</summary>
    private static string LongHero(int id) => $"英雄{id:D4}" + new string('甲', 54);

    private static OverlayChatRequest Request(string target) =>
        new("id", "match", target, -1, 0, 0, false, false, "party");

    /// <summary>
    /// The main window's cache: five own players who share one pair of games, plus a pair and a trio on the other side.
    /// The party metadata is identical for everyone, so a line built from it instead of from these games cannot match.
    /// </summary>
    private static PlayerMatchInfo[] Cached() =>
        [
            Info(0, ["a_100", "b_100"]),
            Info(1, ["a_100", "b_100"]),
            Info(2, ["a_100", "b_100"]),
            Info(3, ["a_100", "b_100"]),
            Info(4, ["a_100", "b_100"]),
            Info(5, ["c_200", "d_200"]),
            Info(6, ["c_200", "d_200"]),
            Info(7, ["e_200", "f_200"]),
            Info(8, ["e_200", "f_200"]),
            Info(9, ["e_200", "f_200"]),
        ];

    /// <summary>The same cache, but the main window has not read a single game for any of the ten players.</summary>
    private static PlayerMatchInfo[] CachedWithoutSharedGames() =>
        [
            Info(0, []),
            Info(1, []),
            Info(2, []),
            Info(3, []),
            Info(4, []),
            Info(5, []),
            Info(6, []),
            Info(7, []),
            Info(8, []),
            Info(9, []),
        ];

    private static PlayerMatchInfo Info(int index, string[] matches)
    {
        var info = new PlayerMatchInfo
        {
            Player = new PlayerInfo
            {
                Puuid = "account-" + index,
                SummonerId = index + 1,
                GameName = "同名",
                GroupKey = "unchanged",
                NameColor = Color.Blue,
            },
            PartyId = "unchanged",
        };
        info.MatchKeys.AddRange(matches);
        return info;
    }
}
