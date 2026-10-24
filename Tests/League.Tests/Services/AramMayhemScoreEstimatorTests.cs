using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The estimate of WeGame's ARAM: Mayhem score. The shipped model is checked against one sampled game whose
/// predictions were computed by the script that fitted it; the rules are checked on small hand-made models.
/// </summary>
public class AramMayhemScoreEstimatorTests
{
    // Estimates are compared to three decimals: the fixture was written by Python from the same rounded model.
    private static double[] Rounded(IEnumerable<double> values) => values.Select(v => Math.Round(v, 3)).ToArray();

    private static AramMayhemScoreEstimator Model(
        double intercept = 0,
        double gold = 0,
        double deaths = 0,
        string offsets = "{}"
    ) =>
        AramMayhemScoreEstimator.Parse(
            $$"""
            {
              "queues": [2400],
              "intercept": {{intercept}},
              "weights": {
                "goldShareOfBest": {{gold}},
                "deathsZScore": {{deaths}},
                "damageTakenShareOfBest": 0,
                "championDamageShareOfBest": 0,
                "killsShareOfBest": 0,
                "crowdControlToEnemyAverage": 0
              },
              "championOffsets": {{offsets}}
            }
            """
        );

    private static JObject Player(int team, int champion, int gold, int deaths = 0) =>
        new()
        {
            ["teamId"] = team,
            ["championId"] = champion,
            ["kills"] = 0,
            ["deaths"] = deaths,
            ["goldEarned"] = gold,
            ["totalDamageTaken"] = 0,
            ["totalDamageDealtToChampions"] = 0,
            ["timeCCingOthers"] = 0,
        };

    private static JObject Player(bool? win, string puuid = "") =>
        new()
        {
            ["teamId"] = win == false ? 200 : 100,
            ["championId"] = 1,
            ["puuid"] = puuid,
            ["win"] = win is bool w ? w : JValue.CreateNull(),
            ["kills"] = 0,
            ["deaths"] = 0,
            ["goldEarned"] = 0,
            ["totalDamageTaken"] = 0,
            ["totalDamageDealtToChampions"] = 0,
            ["timeCCingOthers"] = 0,
        };

    private static (List<JObject> Players, int Queue, double[] Expected) Fixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "Scores", "aram-mayhem-game.json");
        var root = JObject.Parse(File.ReadAllText(path));
        var players = ((JArray)root["participants"]!).Cast<JObject>().ToList();
        return (
            players,
            root["queueId"]!.Value<int>(),
            players.Select(p => p["expectedEstimate"]!.Value<double>()).ToArray()
        );
    }

    [Fact]
    public void Estimate_TheSampledGame_MatchesTheFittingScript()
    {
        var (players, queue, expected) = Fixture();

        var estimates = AramMayhemScoreEstimator.Shared.Estimate(players, queue);

        Assert.Equal(Rounded(expected), Rounded(estimates!));
    }

    [Theory]
    [InlineData(2400, true)]
    [InlineData(3270, true)]
    [InlineData(450, false)]
    [InlineData(420, false)]
    public void Supports_TheShippedModel_CoversOnlyMayhem(int queue, bool supported)
    {
        Assert.Equal(supported, AramMayhemScoreEstimator.Shared.Supports(queue));
    }

    [Fact]
    public void Estimate_AnotherQueue_IsNull()
    {
        var players = new List<JObject> { Player(100, 1, 1000), Player(200, 2, 500) };

        Assert.Null(Model(gold: 10).Estimate(players, 450));
    }

    [Fact]
    public void Estimate_AParticipantMissingAStat_IsNull()
    {
        var players = new List<JObject> { Player(100, 1, 1000), Player(200, 2, 500) };
        players[1].Remove("timeCCingOthers");

        Assert.Null(Model(gold: 10).Estimate(players, 2400));
    }

    [Fact]
    public void Estimate_Gold_IsComparedWithTheGamesBest()
    {
        var players = new List<JObject> { Player(100, 1, 1000), Player(200, 2, 500) };

        var estimates = Model(gold: 10).Estimate(players, 2400);

        Assert.Equal(Rounded([10.0, 5.0]), Rounded(estimates!));
    }

    [Fact]
    public void Estimate_DeathsEqualForEveryone_AddNothing()
    {
        var players = new List<JObject> { Player(100, 1, 0, deaths: 4), Player(200, 2, 0, deaths: 4) };

        var estimates = Model(intercept: 3, deaths: -1).Estimate(players, 2400);

        Assert.Equal(Rounded([3.0, 3.0]), Rounded(estimates!));
    }

    [Fact]
    public void Estimate_DeathsAboveTheGamesAverage_CostPerStandardDeviation()
    {
        var players = new List<JObject> { Player(100, 1, 0, deaths: 2), Player(200, 2, 0, deaths: 6) };

        var estimates = Model(intercept: 5, deaths: -1).Estimate(players, 2400);

        Assert.Equal(Rounded([6.0, 4.0]), Rounded(estimates!));
    }

    [Fact]
    public void Estimate_AChampionWithAnOffset_IsShiftedByIt()
    {
        var players = new List<JObject> { Player(100, 1, 1000), Player(200, 2, 1000) };

        var estimates = Model(intercept: 5, offsets: """{"1": 2.5}""").Estimate(players, 2400);

        Assert.Equal(Rounded([7.5, 5.0]), Rounded(estimates!));
    }

    [Theory]
    [InlineData(1, MatchHonour.Mvp)]
    [InlineData(0, MatchHonour.None)]
    [InlineData(3, MatchHonour.Svp)]
    [InlineData(2, MatchHonour.None)]
    public void Honour_TheBestOfEachTeam_IsMvpForTheWinnersAndSvpForTheLosers(int index, MatchHonour expected)
    {
        var players = new List<JObject> { Player(true), Player(true), Player(false), Player(false) };
        double[] estimates = [9.0, 12.0, 8.0, 10.0];

        Assert.Equal(expected, AramMayhemScoreEstimator.Honour(players, estimates, index));
    }

    [Theory]
    [InlineData(0, MatchHonour.Mvp)]
    [InlineData(1, MatchHonour.None)]
    public void Honour_TiedBestScores_GoToTheFirstListed(int index, MatchHonour expected)
    {
        var players = new List<JObject> { Player(true), Player(true) };

        Assert.Equal(expected, AramMayhemScoreEstimator.Honour(players, [11.0, 11.0], index));
    }

    [Fact]
    public void Honour_APlayerWithoutAResult_IsNone()
    {
        var players = new List<JObject> { Player(null) };

        Assert.Equal(MatchHonour.None, AramMayhemScoreEstimator.Honour(players, [11.0], 0));
    }

    [Theory]
    [InlineData("winner", MatchHonour.Mvp)]
    [InlineData("loser", MatchHonour.Svp)]
    [InlineData("someone else", MatchHonour.None)]
    public void OwnHonour_AMayhemGame_IsTheHonourOfTheCurrentPlayer(string puuid, MatchHonour expected)
    {
        using var match = new MatchInfo
        {
            QueueId = "q_2400",
            CurrPuuid = puuid,
            AllParticipants = [Player(true, "winner"), Player(false, "loser")],
        };

        Assert.Equal(expected, AramMayhemScoreEstimator.Shared.OwnHonour(match));
    }

    [Fact]
    public void OwnHonour_ASummonersRiftGame_IsNone()
    {
        using var match = new MatchInfo
        {
            QueueId = "q_420",
            CurrPuuid = "winner",
            AllParticipants = [Player(true, "winner"), Player(false, "loser")],
        };

        Assert.Equal(MatchHonour.None, AramMayhemScoreEstimator.Shared.OwnHonour(match));
    }

    [Fact]
    public void ChampionOffset_AChampionTheSampleNeverSaw_IsZero()
    {
        Assert.Equal(0, Model(offsets: """{"1": 2.5}""").ChampionOffset(999));
    }

    [Theory]
    [InlineData(25, 20)]
    [InlineData(-3, 0)]
    public void Estimate_BeyondTheScale_IsClampedToZeroToTwenty(double intercept, double expected)
    {
        var players = new List<JObject> { Player(100, 1, 0) };

        var estimates = Model(intercept: intercept).Estimate(players, 2400);

        Assert.Equal(Rounded([expected]), Rounded(estimates!));
    }
}
