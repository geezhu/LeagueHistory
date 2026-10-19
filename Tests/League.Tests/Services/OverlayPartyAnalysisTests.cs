using League.Models;
using League.Networking;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// Marks the groups a side looks like it queued in: two seats belong to the same group once they share at least
/// <c>PartyDetector.DefaultMatchThreshold</c> recent games, and a group needs at least two seats to be marked at all.
///
/// The colour comes from the seat's team - team 100 starts at the first colour, team 200 at the third - so the two
/// sides never share a colour. Since a side is looked at five seats deep and a group needs two seats, a side can hold
/// at most two groups, which is exactly the number of colours it was given.
/// </summary>
public class OverlayPartyAnalysisTests
{
    private static LoadingPlayer Seat(string key, int team = 100, bool self = false, bool identityHidden = false) =>
        new(
            Key: key,
            Name: key,
            Puuid: $"puuid-{key}",
            SummonerId: 0,
            ChampionId: 1,
            Team: team,
            Self: self,
            IdentityHidden: identityHidden
        );

    private static LoadingRoster Roster(params LoadingPlayer[] players) => new("game", 420, "CLASSIC", players);

    private static PlayerMatchInfo Info(string key, params string[] matches) =>
        new()
        {
            Player = new PlayerInfo { Puuid = $"puuid-{key}" },
            MatchKeys = [.. matches],
        };

    // ---- the degenerate inputs ----

    [Fact]
    public void ForRoster_NoRoster_MarksNothing()
    {
        Assert.Empty(OverlayPartyAnalysis.ForRoster(null, [Info("a", "m1", "m2")]));
    }

    [Fact]
    public void ForRoster_NoCachedHistory_MarksNothing()
    {
        // Without a history a seat has no games to share, so no pair can reach the threshold.
        LoadingRoster roster = Roster(Seat("a"), Seat("b"));

        Assert.Empty(OverlayPartyAnalysis.ForRoster(roster, []));
    }

    [Fact]
    public void ForRoster_OneSeatAlone_MarksNothing()
    {
        // A group needs two seats, so a seat whose games match nobody is left unmarked.
        LoadingRoster roster = Roster(Seat("a"), Seat("b"));

        Assert.Empty(OverlayPartyAnalysis.ForRoster(roster, [Info("a", "m1", "m2", "m3"), Info("b", "x1", "x2")]));
    }

    // ---- the threshold ----

    [Fact]
    public void ForRoster_OneSharedGame_IsBelowTheThreshold()
    {
        LoadingRoster roster = Roster(Seat("a"), Seat("b"));

        Assert.Empty(OverlayPartyAnalysis.ForRoster(roster, [Info("a", "m1", "p1"), Info("b", "m1", "p2")]));
    }

    [Fact]
    public void ForRoster_TwoSharedGames_MakeAGroup()
    {
        LoadingRoster roster = Roster(Seat("a"), Seat("b"));

        IReadOnlyDictionary<string, OverlayPartyMarker> marks = OverlayPartyAnalysis.ForRoster(
            roster,
            [Info("a", "m1", "m2", "p1"), Info("b", "m1", "m2", "p2")]
        );

        Assert.Equal(2, marks.Count);
        Assert.Equal(2, marks["a"].Size);
    }

    [Fact]
    public void ForRoster_TheWholeGroupSharesOneMarker()
    {
        // Every seat of a group gets the same marker instance, so the badge and the colour cannot drift apart.
        LoadingRoster roster = Roster(Seat("a"), Seat("b"), Seat("c"));
        string[] shared = ["m1", "m2"];

        IReadOnlyDictionary<string, OverlayPartyMarker> marks = OverlayPartyAnalysis.ForRoster(
            roster,
            [Info("a", [.. shared, "p1"]), Info("b", [.. shared, "p2"]), Info("c", [.. shared, "p3"])]
        );

        Assert.Equal(3, marks.Count);
        Assert.Same(marks["a"], marks["b"]);
        Assert.Same(marks["b"], marks["c"]);
        Assert.Equal(3, marks["a"].Size);
        Assert.Equal("3人", marks["a"].Badge);
    }

    [Fact]
    public void ForRoster_AGameKeyThatIsBlank_DoesNotCount()
    {
        // Blank keys are dropped before the comparison, so they cannot make two strangers look like a group.
        LoadingRoster roster = Roster(Seat("a"), Seat("b"));

        Assert.Empty(OverlayPartyAnalysis.ForRoster(roster, [Info("a", "m1", ""), Info("b", "m1", "   ")]));
    }

    [Fact]
    public void ForRoster_TheSameKeyTwiceInOneSeat_CountsOnce()
    {
        // The keys are a set, so a repeated game is still one shared game and stays below the threshold.
        LoadingRoster roster = Roster(Seat("a"), Seat("b"));

        Assert.Empty(OverlayPartyAnalysis.ForRoster(roster, [Info("a", "m1", "m1"), Info("b", "m1", "m2")]));
    }

    // ---- two groups ----

    [Fact]
    public void ForRoster_TwoGroupsOnOneSide_GetDifferentColours()
    {
        LoadingRoster roster = Roster(Seat("a"), Seat("b"), Seat("c"), Seat("d"));

        IReadOnlyDictionary<string, OverlayPartyMarker> marks = OverlayPartyAnalysis.ForRoster(
            roster,
            [Info("a", "m1", "m2"), Info("b", "m1", "m2"), Info("c", "n1", "n2"), Info("d", "n1", "n2")]
        );

        Assert.Equal(4, marks.Count);
        Assert.Equal(marks["a"].Color, marks["b"].Color);
        Assert.Equal(marks["c"].Color, marks["d"].Color);
        Assert.NotEqual(marks["a"].Color, marks["c"].Color);
    }

    [Fact]
    public void ForRoster_AGroupOnEachSide_NeverSharesAColour()
    {
        // Team 100 draws from the first half of the palette and team 200 from the second, so a group on one side
        // can never be painted the same as a group on the other. The palette itself is private, so this asserts the
        // property rather than the values: with one group per side the two colours must still differ.
        LoadingRoster roster = Roster(Seat("a"), Seat("b"), Seat("c", team: 200), Seat("d", team: 200));

        IReadOnlyDictionary<string, OverlayPartyMarker> marks = OverlayPartyAnalysis.ForRoster(
            roster,
            [Info("a", "m1", "m2"), Info("b", "m1", "m2"), Info("c", "n1", "n2"), Info("d", "n1", "n2")]
        );

        Assert.Equal(4, marks.Count);
        Assert.Equal(marks["a"].Color, marks["b"].Color);
        Assert.Equal(marks["c"].Color, marks["d"].Color);
        Assert.NotEqual(marks["a"].Color, marks["c"].Color);
    }

    [Fact]
    public void ForRoster_TwoGroupsOnTheSameSideDoNotMergeThroughAChain()
    {
        // a-b share two games and c-d share two, but b and c share only one, so the four do not become one group.
        LoadingRoster roster = Roster(Seat("a"), Seat("b"), Seat("c"), Seat("d"));

        IReadOnlyDictionary<string, OverlayPartyMarker> marks = OverlayPartyAnalysis.ForRoster(
            roster,
            [Info("a", "m1", "m2"), Info("b", "m1", "m2", "n1"), Info("c", "n1", "n2"), Info("d", "n1", "n2")]
        );

        Assert.Equal(4, marks.Count);
        Assert.NotSame(marks["a"], marks["c"]);
    }

    // ---- who is looked at ----

    [Fact]
    public void ForRoster_ASeatWithoutACachedHistory_IsLeftOut()
    {
        // No cache entry means no games, so the seat cannot join a group even if its partners could.
        LoadingRoster roster = Roster(Seat("a"), Seat("b"), Seat("c"));

        IReadOnlyDictionary<string, OverlayPartyMarker> marks = OverlayPartyAnalysis.ForRoster(
            roster,
            [Info("a", "m1", "m2"), Info("b", "m1", "m2")]
        );

        Assert.Equal(2, marks.Count);
        Assert.DoesNotContain("c", marks.Keys);
    }

    [Fact]
    public void ForRoster_ASeatWhoseIdentityIsHidden_IsLeftOut()
    {
        // A hidden seat gets no history from the record join, so it cannot be marked.
        LoadingRoster roster = Roster(Seat("a"), Seat("b", identityHidden: true));

        Assert.Empty(OverlayPartyAnalysis.ForRoster(roster, [Info("a", "m1", "m2"), Info("b", "m1", "m2")]));
    }

    [Fact]
    public void ForRoster_OnlyTheFirstFiveSeatsOfASideAreLookedAt()
    {
        // The sixth seat is beyond the join, so its games are never compared.
        LoadingRoster roster = Roster(Seat("a"), Seat("b"), Seat("c"), Seat("d"), Seat("e"), Seat("f"));

        IReadOnlyDictionary<string, OverlayPartyMarker> marks = OverlayPartyAnalysis.ForRoster(
            roster,
            [Info("e", "m1", "m2"), Info("f", "m1", "m2")]
        );

        Assert.Empty(marks);
    }

    [Fact]
    public void ForRoster_ATeamThatIsNeitherSide_IsIgnored()
    {
        // Only teams 100 and 200 take part, so a seat left on another team is not compared even with a history.
        LoadingRoster roster = Roster(Seat("a", team: 0), Seat("b", team: 0));

        Assert.Empty(OverlayPartyAnalysis.ForRoster(roster, [Info("a", "m1", "m2"), Info("b", "m1", "m2")]));
    }

    [Fact]
    public void ForRoster_TheMarkerIsKeyedByTheSeatNotTheCacheEntry()
    {
        LoadingRoster roster = Roster(Seat("seat-a"), Seat("seat-b"));

        IReadOnlyDictionary<string, OverlayPartyMarker> marks = OverlayPartyAnalysis.ForRoster(
            roster,
            [Info("seat-a", "m1", "m2"), Info("seat-b", "m1", "m2")]
        );

        Assert.Equal(["seat-a", "seat-b"], [.. marks.Keys.OrderBy(k => k, StringComparer.Ordinal)]);
    }
}
