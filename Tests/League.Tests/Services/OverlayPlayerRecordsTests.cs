using League.Models;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// Joins a roster seat to the main window's cached player history. The join is deliberately cautious: a seat gets a
/// history only when its account can be named, is the only seat in the roster with that account, and matches exactly
/// one cache entry whose owner is also unique - so a history is never shown against the wrong player.
///
/// Which account a seat "has" is decided by <c>LoadingRosterIdentityService.HasPuuid</c>, whose rule is worth stating
/// because it reads backwards: a real GUID is *not* treated as a PUUID (the client fills that field with a GUID when
/// it has nothing better), so an ordinary string is a PUUID and a GUID falls back to the summoner id.
/// </summary>
public class OverlayPlayerRecordsTests
{
    private static LoadingPlayer Seat(
        string key,
        long summonerId = 0,
        string puuid = "",
        bool identityHidden = false
    ) =>
        new(
            Key: key,
            Name: key,
            Puuid: puuid,
            SummonerId: summonerId,
            ChampionId: 1,
            Team: 100,
            Self: false,
            IdentityHidden: identityHidden
        );

    private static LoadingRoster Roster(params LoadingPlayer[] players) => new("game", 420, "CLASSIC", players);

    private static PlayerMatchInfo Info(string puuid = "", long summonerId = 0) =>
        new()
        {
            Player = new PlayerInfo { Puuid = puuid, SummonerId = summonerId },
        };

    // ---- which account a seat has ----

    [Fact]
    public void Find_ASeatWithAPuuid_IsMatchedByThatPuuid()
    {
        LoadingPlayer seat = Seat("a", summonerId: 7, puuid: "puuid-a");

        PlayerMatchInfo? found = OverlayPlayerRecords.Find(
            Roster(seat),
            seat,
            [Info(puuid: "puuid-a"), Info(summonerId: 7)]
        );

        Assert.NotNull(found);
        Assert.Equal("puuid-a", found.Player.Puuid);
    }

    [Fact]
    public void Find_APuuidThatIsAGuid_IsStillAPuuid()
    {
        // The field is only rejected when it is blank or the all-zero GUID, which the client writes when it has
        // nothing better; an ordinary GUID is a real puuid and is matched by.
        const string guid = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
        LoadingPlayer seat = Seat("a", summonerId: 7, puuid: guid);

        PlayerMatchInfo? found = OverlayPlayerRecords.Find(Roster(seat), seat, [Info(puuid: guid)]);

        Assert.NotNull(found);
        Assert.Equal(guid, found.Player.Puuid);
    }

    [Fact]
    public void Find_APuuidThatIsTheEmptyGuid_IsIgnoredAndTheSummonerIdIsUsedInstead()
    {
        // The all-zero GUID means "no puuid", so the seat falls back to its summoner id and a cache entry
        // sharing only that GUID is not a match.
        const string empty = "00000000-0000-0000-0000-000000000000";
        LoadingPlayer seat = Seat("a", summonerId: 7, puuid: empty);

        Assert.Null(OverlayPlayerRecords.Find(Roster(seat), seat, [Info(puuid: empty)]));

        PlayerMatchInfo? bySummonerId = OverlayPlayerRecords.Find(Roster(seat), seat, [Info(summonerId: 7)]);
        Assert.NotNull(bySummonerId);
        Assert.Equal(7, bySummonerId.Player.SummonerId);
    }

    [Fact]
    public void Find_ASeatWithNeitherPuuidNorSummonerId_HasNoHistory()
    {
        LoadingPlayer seat = Seat("a", summonerId: 0, puuid: "");

        Assert.Null(OverlayPlayerRecords.Find(Roster(seat), seat, [Info(summonerId: 0)]));
    }

    [Fact]
    public void Find_ASeatWhoseIdentityIsHidden_HasNoHistory()
    {
        // The flag is about the account not being public, so it wins even when the id is there and unique.
        LoadingPlayer seat = Seat("a", summonerId: 7, identityHidden: true);

        Assert.Null(OverlayPlayerRecords.Find(Roster(seat), seat, [Info(summonerId: 7)]));
    }

    // ---- the identity has to be unique in the roster ----

    [Fact]
    public void Find_TwoSeatsCarryingTheSameSummonerIdOnly_LeaveBothWithoutAHistory()
    {
        // Both seats have the same identity string, so the first check already refuses them.
        LoadingPlayer one = Seat("a", summonerId: 7);
        LoadingPlayer other = Seat("b", summonerId: 7);
        LoadingRoster roster = Roster(one, other);

        Assert.Null(OverlayPlayerRecords.Find(roster, one, [Info(summonerId: 7)]));
    }

    [Fact]
    public void Find_ASeatWhoseSummonerIdIsSharedButWhosePuuidIsNot_IsStillMatched()
    {
        // Uniqueness is decided on the identity *string* - "p:puuid-a" against "s:7" - so these two seats are each
        // unique and the first check passes. The owner count is the guard that actually matters here, and it counts
        // by account: the other seat has no puuid, so it would fall back to the summoner id, which the matched entry
        // does not carry. One owner, so the history is used.
        LoadingPlayer withPuuid = Seat("a", summonerId: 7, puuid: "puuid-a");
        LoadingPlayer withId = Seat("b", summonerId: 7);
        LoadingRoster roster = Roster(withPuuid, withId);

        PlayerMatchInfo? found = OverlayPlayerRecords.Find(roster, withPuuid, [Info(puuid: "puuid-a")]);

        Assert.NotNull(found);
        Assert.Equal("puuid-a", found.Player.Puuid);
    }

    [Fact]
    public void Find_ASeatThatIsTheOnlyOneWithItsAccount_IsMatched()
    {
        LoadingPlayer one = Seat("a", summonerId: 7);
        LoadingPlayer other = Seat("b", summonerId: 8);
        LoadingRoster roster = Roster(one, other);

        Assert.NotNull(OverlayPlayerRecords.Find(roster, one, [Info(summonerId: 7), Info(summonerId: 8)]));
    }

    // ---- exactly one cache entry ----

    [Fact]
    public void Find_NoCacheEntry_HasNoHistory()
    {
        LoadingPlayer seat = Seat("a", summonerId: 7);

        Assert.Null(OverlayPlayerRecords.Find(Roster(seat), seat, []));
    }

    [Fact]
    public void Find_TwoCacheEntriesForTheSameAccount_HaveNoHistory()
    {
        // Two entries cannot be told apart, so neither is trusted.
        LoadingPlayer seat = Seat("a", summonerId: 7);

        Assert.Null(OverlayPlayerRecords.Find(Roster(seat), seat, [Info(summonerId: 7), Info(summonerId: 7)]));
    }

    [Fact]
    public void Find_ACacheEntryWithoutAPlayer_IsNotAMatch()
    {
        LoadingPlayer seat = Seat("a", summonerId: 7);

        Assert.Null(OverlayPlayerRecords.Find(Roster(seat), seat, [new PlayerMatchInfo { Player = null! }]));
    }

    [Fact]
    public void Find_ACacheEntryForAnotherAccount_IsNotAMatch()
    {
        LoadingPlayer seat = Seat("a", summonerId: 7);

        Assert.Null(OverlayPlayerRecords.Find(Roster(seat), seat, [Info(summonerId: 8)]));
    }

    // ---- the owner count ----

    [Fact]
    public void Find_ASecondSeatWithTheSameSummonerId_RefusesTheHistoryEvenWhenHidden()
    {
        // The identity check runs before the hidden flag is consulted and does not skip hidden seats, so a hidden
        // seat carrying the same id still makes the identity ambiguous and both seats go without a history.
        LoadingPlayer asking = Seat("a", summonerId: 7);
        LoadingPlayer hidden = Seat("b", summonerId: 7, identityHidden: true);
        LoadingRoster roster = Roster(asking, hidden);

        Assert.Null(OverlayPlayerRecords.Find(roster, asking, [Info(summonerId: 7)]));
    }

    [Fact]
    public void Find_ACacheEntryAlsoClaimedByAnotherSeat_IsNotUsed()
    {
        // The second guard: the asking seat has a puuid, so it matches by puuid, while the other seat falls back to
        // the summoner id. The matched entry carries both, so both seats would claim it - two owners, no history.
        LoadingPlayer asking = Seat("a", summonerId: 7, puuid: "puuid-a");
        LoadingPlayer other = Seat("b", summonerId: 7);
        LoadingRoster roster = Roster(asking, other);
        PlayerMatchInfo shared = Info(puuid: "puuid-a", summonerId: 7);

        Assert.Null(OverlayPlayerRecords.Find(roster, asking, [shared]));
    }

    [Fact]
    public void Find_ReturnsTheEntryItself()
    {
        LoadingPlayer seat = Seat("a", summonerId: 7);
        PlayerMatchInfo entry = Info(summonerId: 7);

        Assert.Same(entry, OverlayPlayerRecords.Find(Roster(seat), seat, [entry]));
    }
}
