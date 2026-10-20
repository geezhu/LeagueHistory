using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// One side's totals for a finished match: the four sums from the participants, and the objectives from the teams.
///
/// Two rules shape it. A sum is all or nothing - if any member's number is missing the total is unknown, because a
/// partial total would read as a complete one. And an objective is read from the modern shape with a fallback to the
/// older flat fields, so a match from either era reads the same.
/// </summary>
public class TeamSummaryTests
{
    private static JObject Participant(int teamId, int kills = 1, int deaths = 2, int assists = 3, int gold = 100) =>
        new()
        {
            ["teamId"] = teamId,
            ["kills"] = kills,
            ["deaths"] = deaths,
            ["assists"] = assists,
            ["goldEarned"] = gold,
        };

    /// <summary>A match whose raw data has the modern nested shape.</summary>
    private static MatchInfo Match(
        int mapId = 11,
        string queueId = "420",
        JArray? teams = null,
        params JObject[] participants
    ) =>
        new()
        {
            QueueId = queueId,
            AllParticipants = [.. participants],
            RawGameData = new JObject
            {
                ["json"] = new JObject
                {
                    ["mapId"] = mapId,
                    ["info"] = new JObject { ["teams"] = teams ?? [] },
                },
            },
        };

    private static JObject Team(int teamId, JObject objectives, params (string Field, int Value)[] legacy)
    {
        var team = new JObject { ["teamId"] = teamId, ["objectives"] = objectives };
        foreach ((string field, int value) in legacy)
        {
            team[field] = value;
        }

        return team;
    }

    private static JObject Objectives(
        int tower = 3,
        int inhibitor = 1,
        int baron = 1,
        int dragon = 2,
        int herald = 0
    ) =>
        new()
        {
            ["tower"] = new JObject { ["kills"] = tower },
            ["inhibitor"] = new JObject { ["kills"] = inhibitor },
            ["baron"] = new JObject { ["kills"] = baron },
            ["dragon"] = new JObject { ["kills"] = dragon },
            ["riftHerald"] = new JObject { ["kills"] = herald },
        };

    // ---- the sums ----

    [Fact]
    public void From_ASide_AddsUpItsMembers()
    {
        using MatchInfo match = Match(
            420,
            "420",
            null,
            Participant(100, kills: 1, deaths: 2, assists: 3, gold: 100),
            Participant(100, kills: 4, deaths: 5, assists: 6, gold: 200),
            Participant(200)
        );

        TeamSummary summary = TeamSummary.From(match, 100);

        Assert.Equal(5, summary.Kills);
        Assert.Equal(7, summary.Deaths);
        Assert.Equal(9, summary.Assists);
        Assert.Equal(300, summary.Gold);
    }

    [Fact]
    public void From_OnlyThatSidesMembersAreCounted()
    {
        using MatchInfo match = Match(11, "420", null, Participant(100, kills: 1), Participant(200, kills: 99));

        Assert.Equal(1, TeamSummary.From(match, 100).Kills);
    }

    [Fact]
    public void From_NoMembersAtAll_LeavesEverySumUnknown()
    {
        // An empty side has no total; zero would claim it scored nothing.
        using MatchInfo match = Match(11, "420", null, Participant(200));

        TeamSummary summary = TeamSummary.From(match, 100);

        Assert.Null(summary.Kills);
        Assert.Null(summary.Deaths);
        Assert.Null(summary.Assists);
        Assert.Null(summary.Gold);
    }

    [Fact]
    public void From_OneMemberMissingANumber_LeavesThatSumUnknown()
    {
        // A partial total would read as a complete one, so one missing number makes the whole sum unknown.
        JObject incomplete = Participant(100);
        incomplete.Remove("assists");
        using MatchInfo match = Match(11, "420", null, Participant(100), incomplete);

        TeamSummary summary = TeamSummary.From(match, 100);

        Assert.Equal(2, summary.Kills);
        Assert.Null(summary.Assists);
    }

    [Fact]
    public void From_ANegativeNumber_CountsAsMissing()
    {
        using MatchInfo match = Match(11, "420", null, Participant(100, kills: -1));

        Assert.Null(TeamSummary.From(match, 100).Kills);
    }

    [Fact]
    public void From_ANumberThatIsNotANumber_CountsAsMissing()
    {
        JObject participant = Participant(100);
        participant["kills"] = "many";
        using MatchInfo match = Match(11, "420", null, participant);

        Assert.Null(TeamSummary.From(match, 100).Kills);
    }

    [Fact]
    public void From_TheNumbersMaySitUnderStats()
    {
        // The two shapes the client sends: flat fields, and the same fields inside "stats".
        var participant = new JObject
        {
            ["teamId"] = 100,
            ["stats"] = new JObject { ["kills"] = 7 },
        };
        using MatchInfo match = Match(11, "420", null, participant);

        Assert.Equal(7, TeamSummary.From(match, 100).Kills);
    }

    // ---- the objectives ----

    [Fact]
    public void From_TheModernShape_IsReadForEveryObjective()
    {
        using MatchInfo match = Match(
            11,
            "420",
            new JArray(Team(100, Objectives(tower: 3, inhibitor: 1, baron: 1, dragon: 2, herald: 0))),
            Participant(100)
        );

        TeamSummary summary = TeamSummary.From(match, 100);

        Assert.Equal(5, summary.Objectives.Length);
        Assert.Equal(3, summary.Objectives[0].Count);
        Assert.Equal("摧毁防御塔", summary.Objectives[0].Name);
    }

    [Fact]
    public void From_TheOlderShape_IsUsedWhenTheModernOneIsAbsent()
    {
        // A match from before the nested shape has the counts on the team itself.
        using MatchInfo match = Match(
            11,
            "420",
            new JArray(Team(100, [], ("towerKills", 4), ("baronKills", 2))),
            Participant(100)
        );

        TeamSummary summary = TeamSummary.From(match, 100);

        Assert.Equal(4, summary.Objectives[0].Count);
        Assert.Equal(2, summary.Objectives[2].Count);
    }

    [Fact]
    public void From_AnObjectiveTheTeamHasNoCountFor_IsUnknown()
    {
        using MatchInfo match = Match(11, "420", new JArray(Team(100, new JObject())), Participant(100));

        Assert.All(TeamSummary.From(match, 100).Objectives, objective => Assert.Null(objective.Count));
    }

    [Fact]
    public void From_OnTheHowlingAbyss_TheBaronAndDragonAreLeftOut()
    {
        // Those objectives do not exist there, so listing them would show three permanent zeroes.
        using MatchInfo match = Match(12, "450", new JArray(Team(100, Objectives())), Participant(100));

        TeamSummary summary = TeamSummary.From(match, 100);

        Assert.Equal(2, summary.Objectives.Length);
        Assert.Equal(["tower", "inhibitor"], summary.Objectives.Select(o => o.Key));
    }

    [Theory]
    [InlineData("450")]
    [InlineData("2400")]
    public void From_AHowlingAbyssQueueWithoutTheMapId_IsStillRecognised(string queueId)
    {
        // The map id is not always in the payload, so the queue says it too.
        using MatchInfo match = Match(0, queueId, new JArray(Team(100, Objectives())), Participant(100));

        Assert.Equal(2, TeamSummary.From(match, 100).Objectives.Length);
    }

    [Fact]
    public void From_ASummonersRiftMatch_KeepsAllFiveObjectives()
    {
        using MatchInfo match = Match(11, "420", new JArray(Team(100, Objectives())), Participant(100));

        Assert.Equal(
            ["tower", "inhibitor", "baron", "dragon", "riftHerald"],
            TeamSummary.From(match, 100).Objectives.Select(o => o.Key)
        );
    }

    [Fact]
    public void From_TheOtherSidesObjectives_AreNotUsed()
    {
        using MatchInfo match = Match(
            11,
            "420",
            new JArray(Team(100, Objectives(tower: 3)), Team(200, Objectives(tower: 9))),
            Participant(100)
        );

        Assert.Equal(3, TeamSummary.From(match, 100).Objectives[0].Count);
    }

    [Fact]
    public void From_NoTeamsInThePayload_LeavesTheObjectivesUnknown()
    {
        using MatchInfo match = Match(11, "420", null, Participant(100));

        Assert.All(TeamSummary.From(match, 100).Objectives, objective => Assert.Null(objective.Count));
    }
}
