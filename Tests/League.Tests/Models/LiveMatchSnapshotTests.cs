using League.Models;

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
}
