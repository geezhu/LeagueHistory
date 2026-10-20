using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// Whether the player won a game, for the outcome filter: true, false, or nothing when the payload cannot say.
///
/// "Cannot say" covers three cases and they are all real - a remake is neither a win nor a loss, a payload without
/// participants has nobody to look up, and a player whose statistics carry no win flag says nothing either. Returning
/// false for any of them would file the game under "lost".
///
/// The player is found by puuid where the payload allows it, and by participant id where the puuid sits in a separate
/// identities list instead.
/// </summary>
public class MatchOutcomeFilterTests
{
    private static JObject Player(string puuid, bool? win, int participantId = 1)
    {
        var player = new JObject { ["puuid"] = puuid, ["participantId"] = participantId };
        if (win.HasValue)
        {
            player["stats"] = new JObject { ["win"] = win.Value };
        }

        return player;
    }

    private static JObject Game(params JObject[] players) =>
        new() { ["info"] = new JObject { ["participants"] = new JArray(players) } };

    // ---- the three ways of not knowing ----

    [Fact]
    public void GetWin_ARemake_IsNeitherAWinnerNorALoser()
    {
        // A remake is not a game; filing it under either would be wrong.
        var entry = new JObject
        {
            ["json"] = new JObject
            {
                ["info"] = new JObject
                {
                    ["gameEndedInEarlySurrender"] = true,
                    ["participants"] = new JArray(Player("p", win: true)),
                },
            },
        };

        Assert.Null(MatchOutcomeFilter.GetWin(entry, "p"));
    }

    [Fact]
    public void GetWin_NoParticipants_IsNotKnown()
    {
        Assert.Null(MatchOutcomeFilter.GetWin(new JObject { ["info"] = new JObject() }, "p"));
    }

    [Fact]
    public void GetWin_TheWinningPlayer_IsAWinner()
    {
        Assert.True(MatchOutcomeFilter.GetWin(Game(Player("p", true)), "p"));
    }

    [Fact]
    public void GetWin_TheLosingPlayer_IsALoser()
    {
        Assert.False(MatchOutcomeFilter.GetWin(Game(Player("p", false)), "p"));
    }

    [Fact]
    public void GetWin_APlayerThatIsNotInTheGame_IsNotKnown()
    {
        Assert.Null(MatchOutcomeFilter.GetWin(Game(Player("other", true)), "p"));
    }

    [Fact]
    public void GetWin_AWinFlagThatIsNotABoolean_IsNotKnown()
    {
        var player = new JObject
        {
            ["puuid"] = "p",
            ["stats"] = new JObject { ["win"] = "true" },
        };

        Assert.Null(MatchOutcomeFilter.GetWin(Game(player), "p"));
    }

    [Fact]
    public void GetWin_AWinFlagOnThePlayerItself_IsRead()
    {
        // Some payloads put the flag outside "stats".
        var player = new JObject { ["puuid"] = "p", ["win"] = true };

        Assert.True(MatchOutcomeFilter.GetWin(Game(player), "p"));
    }

    // ---- where the payload puts the player ----

    [Fact]
    public void GetWin_TheEntryMayBeTheGameItself()
    {
        // The wrapper is optional; the same game reads whether or not it is there.
        var entry = new JObject { ["info"] = new JObject { ["participants"] = new JArray(Player("p", true)) } };

        Assert.True(MatchOutcomeFilter.GetWin(entry, "p"));
    }

    [Fact]
    public void GetWin_ThePlayerIsFoundInTheIdentitiesList()
    {
        // When participants do not carry a puuid, the identities list pairs one with a participant id.
        var game = new JObject
        {
            ["info"] = new JObject
            {
                ["participants"] = new JArray(
                    new JObject
                    {
                        ["participantId"] = 1,
                        ["stats"] = new JObject { ["win"] = false },
                    },
                    new JObject
                    {
                        ["participantId"] = 2,
                        ["stats"] = new JObject { ["win"] = true },
                    }
                ),
                ["participantIdentities"] = new JArray(
                    new JObject
                    {
                        ["participantId"] = 1,
                        ["player"] = new JObject { ["puuid"] = "p" },
                    }
                ),
            },
        };

        Assert.False(MatchOutcomeFilter.GetWin(game, "p"));
    }

    [Fact]
    public void GetWin_NoIdentityForThePuuid_IsNotKnown()
    {
        var game = new JObject
        {
            ["info"] = new JObject
            {
                ["participants"] = new JArray(Player("", true)),
                ["participantIdentities"] = new JArray(),
            },
        };

        Assert.Null(MatchOutcomeFilter.GetWin(game, "p"));
    }
}
