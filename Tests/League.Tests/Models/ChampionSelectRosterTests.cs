using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The champion select roster as the client describes it: five seats, the bench, and whether the bench is in use.
///
/// A seat's champion comes from three places in order of trustworthiness: the seat's own champion id, then the pick
/// intent, then the champion on the pick action for that cell. Each step down is a preview rather than a lock, which
/// is what the <c>preview</c> flag records. The action is also what makes a locked-in pick still read as a preview
/// while it is incomplete, so the panel does not claim a champion before the client has committed to it.
/// </summary>
public class ChampionSelectRosterTests
{
    private static JObject Seat(int cell, int? champion = null, int? intent = null, string? puuid = null)
    {
        var seat = new JObject { ["cellId"] = cell };
        if (champion.HasValue)
        {
            seat["championId"] = champion.Value;
        }

        if (intent.HasValue)
        {
            seat["championPickIntent"] = intent.Value;
        }

        if (puuid != null)
        {
            seat["puuid"] = puuid;
        }

        return seat;
    }

    private static JObject Action(int cell, int champion, bool? completed = null, string type = "pick")
    {
        var action = new JObject
        {
            ["actorCellId"] = cell,
            ["championId"] = champion,
            ["type"] = type,
        };
        if (completed.HasValue)
        {
            action["completed"] = completed.Value;
        }

        return action;
    }

    private static JObject Session(
        int own = 0,
        JObject[]? team = null,
        JArray[]? actions = null,
        JObject[]? bench = null,
        bool? benchEnabled = null
    )
    {
        var session = new JObject { ["localPlayerCellId"] = own };
        session["myTeam"] = new JArray((team ?? [Seat(0)]).Cast<object>().ToArray());
        if (actions != null)
        {
            session["actions"] = new JArray(actions.Cast<object>().ToArray());
        }

        if (bench != null)
        {
            session["benchChampions"] = new JArray(bench.Cast<object>().ToArray());
        }

        if (benchEnabled.HasValue)
        {
            session["benchEnabled"] = benchEnabled.Value;
        }

        return session;
    }

    private static JObject BenchChampion(int id) => new() { ["championId"] = id };

    // ---- the seats ----

    [Fact]
    public void Parse_ASeatWithAChampion_HasItAndIsNotAPreview()
    {
        ChampionSelectRosterEntry seat = Assert.Single(
            ChampionSelectRoster.Parse(Session(team: [Seat(0, champion: 11)])).Team
        );

        Assert.Equal(11, seat.ChampionId);
        Assert.False(seat.Preview);
    }

    [Fact]
    public void Parse_TheSelfSeat_IsMarked()
    {
        ChampionSelectRoster roster = ChampionSelectRoster.Parse(
            Session(own: 2, team: [Seat(0, champion: 11), Seat(2, champion: 13)])
        );

        Assert.False(roster.Team[0].IsSelf);
        Assert.True(roster.Team[1].IsSelf);
    }

    [Fact]
    public void Parse_NoLocalPlayer_NoSeatIsTheSelf()
    {
        // Without a local cell nobody is the user, rather than everyone being.
        ChampionSelectRoster roster = ChampionSelectRoster.Parse(Session(own: -1, team: [Seat(0, champion: 11)]));

        Assert.False(roster.Team[0].IsSelf);
    }

    [Fact]
    public void Parse_ThePuuid_IsCarriedWhenThere()
    {
        ChampionSelectRosterEntry seat = Assert.Single(
            ChampionSelectRoster.Parse(Session(team: [Seat(0, champion: 11, puuid: "p")])).Team
        );

        Assert.Equal("p", seat.Puuid);
    }

    [Fact]
    public void Parse_AChampionOnlyIntended_IsAPreview()
    {
        // The intent is what the user hovered, not what they locked in.
        ChampionSelectRosterEntry seat = Assert.Single(
            ChampionSelectRoster.Parse(Session(team: [Seat(0, intent: 11)])).Team
        );

        Assert.Equal(11, seat.ChampionId);
        Assert.True(seat.Preview);
    }

    [Fact]
    public void Parse_AnEmptySeat_HasNoChampionAndIsNotAPreview()
    {
        ChampionSelectRosterEntry seat = Assert.Single(ChampionSelectRoster.Parse(Session(team: [Seat(0)])).Team);

        Assert.Equal(0, seat.ChampionId);
        Assert.False(seat.Preview);
    }

    // ---- the pick actions ----

    [Fact]
    public void Parse_AnEmptySeatWithAPickAction_TakesTheChampionAsAPreview()
    {
        // The action is where the champion appears before the seat itself is updated.
        ChampionSelectRoster roster = ChampionSelectRoster.Parse(
            Session(
                team: [Seat(0)],
                actions:
                [
                    [Action(0, 11)],
                ]
            )
        );

        Assert.Equal(11, roster.Team[0].ChampionId);
        Assert.True(roster.Team[0].Preview);
    }

    [Fact]
    public void Parse_APickNotYetComplete_StaysAPreviewEvenOnTheSeat()
    {
        // The seat already names the champion, but the client has not committed; the panel must not claim it.
        ChampionSelectRoster roster = ChampionSelectRoster.Parse(
            Session(
                team: [Seat(0, champion: 11)],
                actions:
                [
                    [Action(0, 11, completed: false)],
                ]
            )
        );

        Assert.Equal(11, roster.Team[0].ChampionId);
        Assert.True(roster.Team[0].Preview);
    }

    [Fact]
    public void Parse_APickThatIsComplete_IsNotAPreview()
    {
        ChampionSelectRoster roster = ChampionSelectRoster.Parse(
            Session(
                team: [Seat(0, champion: 11)],
                actions:
                [
                    [Action(0, 11, completed: true)],
                ]
            )
        );

        Assert.False(roster.Team[0].Preview);
    }

    [Fact]
    public void Parse_SeveralActionsForOneSeat_TheLastOneWins()
    {
        // A player may hover and re-hover; the latest is what they are on now.
        ChampionSelectRoster roster = ChampionSelectRoster.Parse(
            Session(
                team: [Seat(0)],
                actions:
                [
                    [Action(0, 11)],
                    [Action(0, 12)],
                ]
            )
        );

        Assert.Equal(12, roster.Team[0].ChampionId);
    }

    [Fact]
    public void Parse_AnActionOfAnotherType_IsIgnored()
    {
        // A ban or a trade is not what the seat is picking.
        ChampionSelectRoster roster = ChampionSelectRoster.Parse(
            Session(
                team: [Seat(0)],
                actions:
                [
                    [Action(0, 11, type: "ban")],
                ]
            )
        );

        Assert.Equal(0, roster.Team[0].ChampionId);
    }

    [Fact]
    public void Parse_AnActionForAnotherSeat_IsIgnored()
    {
        ChampionSelectRoster roster = ChampionSelectRoster.Parse(
            Session(
                team: [Seat(0), Seat(1)],
                actions:
                [
                    [Action(1, 11)],
                ]
            )
        );

        Assert.Equal(0, roster.Team[0].ChampionId);
        Assert.Equal(11, roster.Team[1].ChampionId);
    }

    // ---- the bench ----

    [Fact]
    public void Parse_TheBench_KeepsOnlyRealChampions()
    {
        // A zero is an empty slot, not a champion to offer.
        ChampionSelectRoster roster = ChampionSelectRoster.Parse(
            Session(bench: [BenchChampion(11), BenchChampion(0), BenchChampion(12)])
        );

        Assert.Equal([11, 12], roster.Bench);
    }

    [Fact]
    public void Parse_TheBench_IsDeDuplicated()
    {
        // The client may list the same champion twice; it is one choice.
        ChampionSelectRoster roster = ChampionSelectRoster.Parse(
            Session(bench: [BenchChampion(11), BenchChampion(11), BenchChampion(12)])
        );

        Assert.Equal([11, 12], roster.Bench);
    }

    [Fact]
    public void Parse_NoBenchList_HasNoBenchAndItIsNotEnabled()
    {
        ChampionSelectRoster roster = ChampionSelectRoster.Parse(Session());

        Assert.Empty(roster.Bench);
        Assert.False(roster.BenchEnabled);
    }

    [Fact]
    public void Parse_ABenchListWithoutTheFlag_CountsAsEnabled()
    {
        // Sending a bench at all is the client saying the mode has one.
        Assert.True(ChampionSelectRoster.Parse(Session(bench: [BenchChampion(11)])).BenchEnabled);
    }

    [Fact]
    public void Parse_TheFlagIsBelievedOverTheList()
    {
        // If the client says the bench is off, an empty list is not evidence either way.
        Assert.False(ChampionSelectRoster.Parse(Session(bench: [BenchChampion(11)], benchEnabled: false)).BenchEnabled);
    }

    // ---- the signature ----

    [Fact]
    public void Signature_TwoIdenticalRosters_AreTheSameSignature()
    {
        // The signature is what tells the panel whether it needs to redraw.
        JObject session = Session(team: [Seat(0, champion: 11)], bench: [BenchChampion(12)]);

        Assert.Equal(
            ChampionSelectRoster.Parse(session).Signature,
            ChampionSelectRoster.Parse((JObject)session.DeepClone()).Signature
        );
    }

    [Fact]
    public void Signature_AChangedChampion_ChangesIt()
    {
        string before = ChampionSelectRoster.Parse(Session(team: [Seat(0, champion: 11)])).Signature;
        string after = ChampionSelectRoster.Parse(Session(team: [Seat(0, champion: 12)])).Signature;

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Signature_APreviewBecomingAPick_ChangesIt()
    {
        // The panel draws previews differently, so that change has to count.
        string preview = ChampionSelectRoster.Parse(Session(team: [Seat(0, intent: 11)])).Signature;
        string picked = ChampionSelectRoster.Parse(Session(team: [Seat(0, champion: 11)])).Signature;

        Assert.NotEqual(preview, picked);
    }

    [Fact]
    public void Signature_AChangedBench_ChangesIt()
    {
        string before = ChampionSelectRoster.Parse(Session(bench: [BenchChampion(11)])).Signature;
        string after = ChampionSelectRoster.Parse(Session(bench: [BenchChampion(12)])).Signature;

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Signature_TheBenchFlag_IsPartOfIt()
    {
        string on = ChampionSelectRoster.Parse(Session(bench: [BenchChampion(11)], benchEnabled: true)).Signature;
        string off = ChampionSelectRoster.Parse(Session(bench: [BenchChampion(11)], benchEnabled: false)).Signature;

        Assert.NotEqual(on, off);
    }
}
