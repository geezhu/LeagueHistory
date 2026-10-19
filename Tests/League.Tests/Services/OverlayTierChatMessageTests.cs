using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// Which seats a tier request names. <c>Select</c> is the shared chooser behind several chat messages, so its two
/// shapes are pinned separately: a side ("own"/"enemy") is everyone on that side with a champion, and a single seat
/// is addressed as a pair of coordinates into the side - <c>slot / 5</c> picks the team and <c>slot % 5</c> picks the
/// place in it - and is only returned when the champion named in the request is the one that seat holds.
///
/// <c>Build</c> is not covered here: it renders through <c>ChampionTierChatMessage</c>, which has no test class yet.
/// </summary>
public class OverlayTierChatMessageTests
{
    private static LoadingPlayer Player(int team, int championId, bool self = false, string? key = null) =>
        new(
            Key: key ?? $"p{team}-{championId}",
            Name: $"名字{championId}",
            Puuid: $"puuid-{team}-{championId}",
            SummonerId: championId,
            ChampionId: championId,
            Team: team,
            Self: self
        );

    private static LoadingRoster Roster(params LoadingPlayer[] players) => new("game", 420, "CLASSIC", players);

    private static OverlayChatRequest Request(string target, int slot = -1, int championId = 0) =>
        new(
            Id: "i",
            Session: "s",
            Target: target,
            Slot: slot,
            ChampionId: championId,
            CreatedAt: 1,
            Dismissed: false,
            Cancelled: false
        );

    private static int[] Keys(IEnumerable<LoadingPlayer> players) => players.Select(p => p.ChampionId).ToArray();

    // ---- a side ----

    [Fact]
    public void Select_OwnSide_ReturnsEverySeatOnItThatHasAChampion()
    {
        LoadingRoster roster = Roster(Player(100, 1, self: true), Player(100, 2), Player(100, 0), Player(200, 3));

        LoadingPlayer[] selected = OverlayTierChatMessage.Select(roster, Request("own"));

        Assert.Equal([1, 2], Keys(selected));
    }

    [Fact]
    public void Select_EnemySide_ReturnsTheOtherTeam()
    {
        LoadingRoster roster = Roster(Player(100, 1, self: true), Player(200, 3), Player(200, 4));

        LoadingPlayer[] selected = OverlayTierChatMessage.Select(roster, Request("enemy"));

        Assert.Equal([3, 4], Keys(selected));
    }

    [Fact]
    public void Select_ASideWithoutChampions_ReturnsNothing()
    {
        // A loading screen lists all ten seats before the champions are known; team 0 seats name no champion.
        LoadingRoster roster = Roster(Player(100, 1, self: true), Player(100, 0), Player(200, 0));

        Assert.Empty(OverlayTierChatMessage.Select(roster, Request("enemy")));
    }

    [Fact]
    public void Select_ASideStopsAtFiveSeats()
    {
        // Six seats on one side is not a real game, but the cap is part of the rule and worth pinning.
        LoadingRoster roster = Roster(
            Player(100, 1, self: true),
            Player(100, 2),
            Player(100, 3),
            Player(100, 4),
            Player(100, 5),
            Player(100, 6)
        );

        LoadingPlayer[] selected = OverlayTierChatMessage.Select(roster, Request("own"));

        Assert.Equal(5, selected.Length);
        Assert.Equal([1, 2, 3, 4, 5], Keys(selected));
    }

    [Fact]
    public void Select_WithoutASelfSeat_ReturnsNothingForASide()
    {
        // "own" and "enemy" are relative to a seat; with nobody marked as self there is no side to pick.
        LoadingRoster roster = Roster(Player(100, 1), Player(200, 3));

        Assert.Empty(OverlayTierChatMessage.Select(roster, Request("own")));
        Assert.Empty(OverlayTierChatMessage.Select(roster, Request("enemy")));
    }

    [Theory]
    [InlineData("all")]
    [InlineData("")]
    [InlineData("single")]
    public void Select_AnUnknownSideName_ReturnsNothing(string target)
    {
        LoadingRoster roster = Roster(Player(100, 1, self: true), Player(200, 3));

        Assert.Empty(OverlayTierChatMessage.Select(roster, Request(target)));
    }

    // ---- one seat, addressed by a pair of coordinates ----

    [Fact]
    public void Select_ASelfOnTeamOne_AddressesSeatsZeroToFourAsTeamOne()
    {
        // With self on team 100 the order is [100, 200], so slots 0-4 are team 100 and 5-9 are team 200.
        LoadingRoster roster = Roster(Player(100, 11, self: true), Player(100, 12), Player(200, 21), Player(200, 22));

        Assert.Equal(11, Assert.Single(OverlayTierChatMessage.Select(roster, Request("single", 0, 11))).ChampionId);
        Assert.Equal(12, Assert.Single(OverlayTierChatMessage.Select(roster, Request("single", 1, 12))).ChampionId);
        Assert.Equal(21, Assert.Single(OverlayTierChatMessage.Select(roster, Request("single", 5, 21))).ChampionId);
        Assert.Equal(22, Assert.Single(OverlayTierChatMessage.Select(roster, Request("single", 6, 22))).ChampionId);
    }

    [Fact]
    public void Select_ASelfOnTeamTwo_FlipsWhichTeamTheFirstFiveSlotsAddress()
    {
        // Self on team 200 makes the order [200, 100], so slots 0-4 are team 200 - the same seat must not
        // address the same team regardless of which side self is on.
        LoadingRoster roster = Roster(Player(200, 21, self: true), Player(200, 22), Player(100, 11), Player(100, 12));

        Assert.Equal(21, Assert.Single(OverlayTierChatMessage.Select(roster, Request("single", 0, 21))).ChampionId);
        Assert.Equal(11, Assert.Single(OverlayTierChatMessage.Select(roster, Request("single", 5, 11))).ChampionId);
        Assert.Empty(OverlayTierChatMessage.Select(roster, Request("single", 0, 11)));
    }

    [Fact]
    public void Select_ASeatHoldingADifferentChampion_ReturnsNothing()
    {
        // The seat is found, but the request named another champion, so nothing is selected rather than that seat.
        LoadingRoster roster = Roster(Player(100, 11, self: true), Player(100, 12));

        Assert.Empty(OverlayTierChatMessage.Select(roster, Request("single", 1, 99)));
    }

    [Fact]
    public void Select_ASeatBeyondTheListedSeats_ReturnsNothing()
    {
        // slot % 5 runs past the end when a side has fewer than five known champions.
        LoadingRoster roster = Roster(Player(100, 11, self: true));

        Assert.Empty(OverlayTierChatMessage.Select(roster, Request("single", 3, 11)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(100)]
    public void Select_ASlotOutsideTheTenSeats_ReturnsNothing(int slot)
    {
        LoadingRoster roster = Roster(Player(100, 11, self: true));

        Assert.Empty(OverlayTierChatMessage.Select(roster, Request("single", slot, 11)));
    }

    [Fact]
    public void Select_ASingleSeatWithoutASelfSeat_StillAddressesTeamOneFirst()
    {
        // A single seat does not need to know which side is "ours"; with no self the order stays [100, 200].
        LoadingRoster roster = Roster(Player(100, 11), Player(200, 21));

        Assert.Equal(11, Assert.Single(OverlayTierChatMessage.Select(roster, Request("single", 0, 11))).ChampionId);
        Assert.Equal(21, Assert.Single(OverlayTierChatMessage.Select(roster, Request("single", 5, 21))).ChampionId);
    }

    [Fact]
    public void Select_ASingleSeatCountsSeatsWithoutAChampion_WhileASideLeavesThemOut()
    {
        // The two branches treat an empty seat differently, and it shows here. A side keeps only seats that name a
        // champion (".Where(p => p.ChampionId > 0)"), while a single seat takes the side as the loading screen listed
        // it, so an empty seat still takes up its place and shifts the ones after it.
        LoadingRoster roster = Roster(
            Player(100, 11, self: true),
            Player(100, 0),
            Player(100, 12),
            Player(100, 0),
            Player(100, 13)
        );

        Assert.Equal([11, 12, 13], Keys(OverlayTierChatMessage.Select(roster, Request("own"))));

        Assert.Equal(11, Assert.Single(OverlayTierChatMessage.Select(roster, Request("single", 0, 11))).ChampionId);
        Assert.Equal(12, Assert.Single(OverlayTierChatMessage.Select(roster, Request("single", 2, 12))).ChampionId);
        Assert.Equal(13, Assert.Single(OverlayTierChatMessage.Select(roster, Request("single", 4, 13))).ChampionId);
        // Index 1 is the empty seat, so naming the champion that sits behind it addresses the empty seat instead.
        Assert.Empty(OverlayTierChatMessage.Select(roster, Request("single", 1, 12)));
    }
}
