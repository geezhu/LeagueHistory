using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The live game snapshot's own lookups: which numeric side a team name means, which player a log line's alias belongs
/// to, which live row a client roster entry is, and what to call a player's side.
///
/// Three of them answer only when the answer is unambiguous. An alias shared by two players is left out rather than
/// guessed; a roster entry that matches two live rows is treated as no match; and a side is only called "ours" or
/// "theirs" when the game has told us which one is ours.
/// </summary>
public class LiveMatchSnapshotTests
{
    private static LiveMatchPlayer Player(
        string key,
        string name,
        string team = "ORDER",
        bool self = false,
        params string[] aliases
    ) =>
        new(
            Key: key,
            Name: name,
            ChampionAlias: "Champion",
            ChampionName: "英雄",
            Team: team,
            Self: self,
            Level: 11,
            Items: []
        )
        {
            EventAliases = aliases,
        };

    private static LiveMatchSnapshot Snapshot(params LiveMatchPlayer[] players) => new("CLASSIC", 11, 600, players);

    // ---- the sides ----

    [Theory]
    [InlineData("ORDER", 100)]
    [InlineData("CHAOS", 200)]
    public void TeamId_AKnownSideName_IsThatSidesNumber(string team, int expected)
    {
        Assert.Equal(expected, LiveMatchSnapshot.TeamId(team));
    }

    [Theory]
    [InlineData("order")]
    [InlineData("Order")]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    public void TeamId_AnythingElse_IsZero(string team)
    {
        // The client reports these two names in capitals; anything else is not a side with a number.
        Assert.Equal(0, LiveMatchSnapshot.TeamId(team));
    }

    // ---- the log aliases ----

    [Fact]
    public void EventAliasMap_AnAliasOnlyOnePlayerHas_IsMappedToThem()
    {
        IReadOnlyDictionary<string, string> map = Snapshot(Player("a", "甲", aliases: "甲#1"), Player("b", "乙"))
            .EventAliasMap();

        Assert.Equal("a", map["甲#1"]);
    }

    [Fact]
    public void EventAliasMap_AnAliasTwoPlayersShare_IsLeftOut()
    {
        // A shared alias cannot be resolved, and guessing would put the line under the wrong champion.
        IReadOnlyDictionary<string, string> map = Snapshot(
                Player("a", "甲", aliases: "同名"),
                Player("b", "乙", aliases: "同名")
            )
            .EventAliasMap();

        Assert.False(map.ContainsKey("同名"));
    }

    [Fact]
    public void EventAliasMap_TheSameAliasTwiceForOnePlayer_IsStillTheirs()
    {
        // A duplicate for the same player is not an ambiguity.
        IReadOnlyDictionary<string, string> map = Snapshot(Player("a", "甲", "ORDER", false, "甲#1", "甲#1"))
            .EventAliasMap();

        Assert.Equal("a", map["甲#1"]);
    }

    [Fact]
    public void EventAliasMap_ABlankOrHashAlias_IsLeftOut()
    {
        IReadOnlyDictionary<string, string> map = Snapshot(Player("a", "甲", "ORDER", false, "", "   ", "#"))
            .EventAliasMap();

        Assert.Empty(map);
    }

    [Fact]
    public void EventAliasMap_AHashPlaceholderName_ContributesNothing()
    {
        // A player the client has not named yet is written as "#", and its aliases mean nothing.
        IReadOnlyDictionary<string, string> map = Snapshot(Player("a", "#", aliases: "甲#1")).EventAliasMap();

        Assert.Empty(map);
    }

    [Fact]
    public void EventAliasMap_TheNameIsTrimmedBeforeTheComparison()
    {
        // " # " is the same placeholder with padding.
        Assert.Empty(Snapshot(Player("a", " # ", aliases: "甲#1")).EventAliasMap());
    }

    [Fact]
    public void EventAliasMap_SeveralPlayers_EachKeepsTheirOwn()
    {
        IReadOnlyDictionary<string, string> map = Snapshot(
                Player("a", "甲", aliases: "甲#1"),
                Player("b", "乙", aliases: "乙#1")
            )
            .EventAliasMap();

        Assert.Equal(2, map.Count);
        Assert.Equal("a", map["甲#1"]);
        Assert.Equal("b", map["乙#1"]);
    }

    // ---- finding the live row for a roster entry ----

    private static LoadingPlayer Seat(string name, int team = 100, bool hidden = false) =>
        new("seat", name, "puuid", 1, 1, team, Self: false, IdentityHidden: hidden);

    [Fact]
    public void Find_OneRowMatchingNameAndSide_IsThatRow()
    {
        LiveMatchPlayer row = Player("a", "甲");
        LiveMatchSnapshot snapshot = Snapshot(row);

        Assert.Same(row, snapshot.Find(Seat("甲")));
    }

    [Fact]
    public void Find_ASeatWhoseIdentityIsHidden_HasNoRow()
    {
        // Nothing can be matched against a nameless seat without risking the wrong player.
        Assert.Null(Snapshot(Player("a", "甲")).Find(Seat("甲", hidden: true)));
    }

    [Fact]
    public void Find_NoRowWithThatName_IsNull()
    {
        Assert.Null(Snapshot(Player("a", "甲")).Find(Seat("乙")));
    }

    [Fact]
    public void Find_TheSameNameOnTheOtherSide_IsNull()
    {
        // The side is part of the comparison: two players may share a name.
        Assert.Null(Snapshot(Player("a", "甲", team: "CHAOS")).Find(Seat("甲", team: 100)));
    }

    [Fact]
    public void Find_TwoRowsThatBothMatch_HaveNoAnswer()
    {
        // The client's roster should hold one entry per account; two identical ones cannot be told apart.
        LiveMatchSnapshot snapshot = Snapshot(Player("a", "甲"), Player("b", "甲"));

        Assert.Null(snapshot.Find(Seat("甲")));
    }

    // ---- naming a side ----

    [Fact]
    public void TeamLabel_OurOwnSide_IsOurs()
    {
        LiveMatchSnapshot snapshot = Snapshot(
            Player("a", "甲", team: "ORDER", self: true),
            Player("b", "乙", team: "CHAOS")
        );

        Assert.Equal("我方", snapshot.TeamLabel(snapshot.Players[0]));
    }

    [Fact]
    public void TeamLabel_TheOtherSide_IsTheirs()
    {
        LiveMatchSnapshot snapshot = Snapshot(
            Player("a", "甲", team: "ORDER", self: true),
            Player("b", "乙", team: "CHAOS")
        );

        Assert.Equal("对方", snapshot.TeamLabel(snapshot.Players[1]));
    }

    [Fact]
    public void TeamLabel_NoSelfRow_KeepsTheRawSideName()
    {
        // Without knowing which side is ours there is no ours or theirs to name.
        LiveMatchSnapshot snapshot = Snapshot(Player("a", "甲", team: "ORDER"));

        Assert.Equal("ORDER", snapshot.TeamLabel(snapshot.Players[0]));
    }

    [Fact]
    public void TeamLabel_WhenOurSideIsUnknown_KeepsTheRawSideName()
    {
        // The game sometimes reports the sides before it reports which one is ours.
        LiveMatchSnapshot snapshot = Snapshot(
            Player("a", "甲", team: "UNKNOWN", self: true),
            Player("b", "乙", team: "ORDER")
        );

        Assert.Equal("ORDER", snapshot.TeamLabel(snapshot.Players[1]));
    }

    [Fact]
    public void TeamLabel_APlayerWhoseSideIsUnknown_KeepsTheRawSideName()
    {
        LiveMatchSnapshot snapshot = Snapshot(
            Player("a", "甲", team: "ORDER", self: true),
            Player("b", "乙", team: "UNKNOWN")
        );

        Assert.Equal("UNKNOWN", snapshot.TeamLabel(snapshot.Players[1]));
    }

    // ---- parsing a live game document ----

    // Three players: an enemy Lux with one item, ourselves with an empty bag, and an ally Lux the game sent no
    // items for. Four kills give us a death streak that reaches two, breaks, and is one again.
    private const string Game = """
        {"activePlayer":{"riotId":"Self#CN"},"gameData":{"gameMode":"ARAM","mapNumber":12,"gameTime":125},"allPlayers":[
        {"riotId":"Enemy#CN","rawChampionName":"game_character_displayname_Lux","championName":"拉克丝","team":"ORDER","level":8,"scores":{"kills":3,"deaths":1},"items":[{"itemID":3089,"displayName":"灭世者的死亡之帽","count":1}]},
        {"riotId":"Self#CN","rawChampionName":"game_character_displayname_Morgana","championName":"莫甘娜","team":"CHAOS","level":9,"scores":{"kills":1,"deaths":3},"items":[]},
        {"riotId":"Ally#CN","rawChampionName":"game_character_displayname_Lux","championName":"拉克丝","team":"CHAOS","level":8,"scores":{"kills":0,"deaths":0}}
        ],"events":{"Events":[{"EventID":0,"EventName":"GameStart","EventTime":0},
        {"EventID":1,"EventName":"ChampionKill","EventTime":10,"KillerName":"Enemy#CN","VictimName":"Self#CN"},
        {"EventID":2,"EventName":"ChampionKill","EventTime":20,"KillerName":"Enemy#CN","VictimName":"Self#CN"},
        {"EventID":3,"EventName":"ChampionKill","EventTime":30,"KillerName":"Self#CN","VictimName":"Enemy#CN"},
        {"EventID":4,"EventName":"ChampionKill","EventTime":40,"KillerName":"Enemy#CN","VictimName":"Self#CN"}]}}
        """;

    [Fact]
    public void Parse_RawChampionName_KeepsTheAliasAndTheItemIds()
    {
        LiveMatchSnapshot snapshot = LiveMatchSnapshot.Parse(JObject.Parse(Game));

        Assert.Equal("Lux", snapshot.Players[0].ChampionAlias);
        Assert.Equal(3089, snapshot.Players[0].Items![0].Id);
    }

    [Fact]
    public void Parse_EmptyItemList_IsNotTheSameAsMissingItems()
    {
        LiveMatchSnapshot snapshot = LiveMatchSnapshot.Parse(JObject.Parse(Game));

        Assert.Empty(snapshot.Players[1].Items!);
        Assert.Null(snapshot.Players[2].Items);
    }

    [Fact]
    public void Parse_SameChampionTwice_KeepsTwoSeparatePlayers()
    {
        LiveMatchSnapshot snapshot = LiveMatchSnapshot.Parse(JObject.Parse(Game));

        Assert.Equal(3, snapshot.Players.Length);
        Assert.NotEqual(snapshot.Players[0].Key, snapshot.Players[2].Key);
    }

    [Fact]
    public void Parse_ActivePlayerKnown_NamesTheSidesFromOurs()
    {
        LiveMatchSnapshot snapshot = LiveMatchSnapshot.Parse(JObject.Parse(Game));

        Assert.Equal("对方", snapshot.TeamLabel(snapshot.Players[0]));
        Assert.Equal("我方", snapshot.TeamLabel(snapshot.Players[1]));
    }

    [Fact]
    public void Parse_NoActivePlayer_MarksNobodyAsSelf()
    {
        var game = JObject.Parse(Game);
        game.Remove("activePlayer");

        LiveMatchSnapshot snapshot = LiveMatchSnapshot.Parse(game);

        Assert.DoesNotContain(snapshot.Players, player => player.Self);
        Assert.Equal("ORDER", snapshot.TeamLabel(snapshot.Players[0]));
    }

    [Fact]
    public void Parse_MidgameEvents_RebuildsTheCurrentAndHighestDeathStreak()
    {
        LiveMatchSnapshot snapshot = LiveMatchSnapshot.Parse(JObject.Parse(Game));

        var streak = snapshot.DeathStreaks[snapshot.Players[1].Key];

        Assert.Equal(1, streak.Current);
        Assert.Equal(2, streak.Highest);
        Assert.True(streak.Complete);
    }

    [Fact]
    public void Parse_DocumentWithoutGameData_IsRejected()
    {
        var empty = new JObject();

        Assert.Throws<InvalidDataException>(() => LiveMatchSnapshot.Parse(empty));
    }
}
