using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>
/// The JSON the Game Bar widget reads: the clock, the upcoming spawns with their bounty, the lane bars with their
/// markers and the siege numbers. The expected values come from the checks in
/// Tests/StatisticsChecks/MinionWaveChecks.cs and Tests/StatisticsChecks/TurretChecks.cs.
/// </summary>
public class MinionWaveForecastTests
{
    private static JObject Sample() => Forecast(LiveMatchFixture.RiftGame(), 0);

    [Fact]
    public void ToJson_ClockAndGameTime_TravelTogether()
    {
        var json = Forecast(LiveMatchFixture.RiftGame(), 1234);

        Assert.Equal(1234, (long?)json["clock"]);
        Assert.Equal(846.0, (double?)json["gameTime"]);
    }

    [Fact]
    public void ToJson_FallenTurret_MovesTheLaneTarget()
    {
        var game = LiveMatchFixture.RiftGame() with
        {
            Time = 700.5,
            Events = [LiveMatchFixture.Structure("TurretKilled", 612.3, "Turret_T2_L_03_A")],
        };

        var top = Lane(Forecast(game, 0), "上路");

        Assert.Equal("敌方二塔", (string?)top["ally"]?["target"]);
    }

    [Fact]
    public void ToJson_MissedPoll_StillCarriesFourSpawns()
    {
        var json = Sample();

        Assert.Equal(4, ((JArray)json["spawns"]!).Count);
    }

    [Theory]
    [InlineData(0, 141, 292)]
    [InlineData(1, 102, 279)]
    public void ToJson_UpcomingSpawns_CarryTheirGoldAndExperience(int index, int gold, int experience)
    {
        var spawns = (JArray)Sample()["spawns"]!;

        Assert.Equal(gold, (int?)spawns[index]!["gold"]);
        Assert.Equal(experience, (int?)spawns[index]!["xp"]);
    }

    [Fact]
    public void ToJson_Current_WritesTheLatestWaveThatSpawned()
    {
        var json = Sample();

        Assert.Equal(28, (int?)json["current"]?["wave"]);
    }

    [Fact]
    public void ToJson_BeforeTheFirstWave_WritesNoCurrentWave()
    {
        var json = Forecast(LiveMatchFixture.WaveGame("ARAM", 12, 10, "ORDER"), 0);

        Assert.Equal(JTokenType.Null, json["current"]!.Type);
    }

    [Fact]
    public void ToJson_LaneCarriesItsNameAndBothArrivals()
    {
        var mid = Lane(Forecast(LiveMatchFixture.WaveGame("CLASSIC", 11, 31, "ORDER"), 0), "中路");

        Assert.Equal("中路", (string?)mid["name"]);
        Assert.Equal("敌方一塔", (string?)mid["ally"]?["target"]);
        Assert.Equal("我方一塔", (string?)mid["enemy"]?["target"]);
    }

    [Fact]
    public void ToJson_Progress_CarriesTheBarEndsAndTheLaneLength()
    {
        var json = Progress(Sample(), "中路")!;

        Assert.Equal(16451.0, (double?)json["length"]);
        Assert.Equal(0.0, (double?)json["spawn"]);
        Assert.Equal(1.0, (double?)json["enemySpawn"]);
        Assert.Equal(12, ((JArray)json["markers"]!).Count);
    }

    [Fact]
    public void ToJson_Progress_CarriesTheHintAboutWhereTheLaneSits()
    {
        var json = Progress(Sample(), "中路")!;

        Assert.Equal("推测：我方这一路少 1 座建筑，兵线多在我方半场", (string?)json["hint"]);
    }

    [Theory]
    [InlineData(0, "base")]
    [InlineData(1, "nexus")]
    [InlineData(2, "inhibitor")]
    [InlineData(3, "turret")]
    public void ToJson_ProgressMarkers_NameTheirIconKind(int index, string expected)
    {
        var markers = (JArray)Progress(Sample(), "中路")!["markers"]!;

        Assert.Equal(expected, (string?)markers[index]!["kind"]);
    }

    [Theory]
    [InlineData(2, 29, true)]
    [InlineData(3, 30, false)]
    public void ToJson_ProgressWaves_CarryNumberAndCannonFlag(int index, int wave, bool cannon)
    {
        var waves = (JArray)Progress(Sample(), "中路")!["waves"]!;

        Assert.Equal(wave, (int?)waves[index]!["wave"]);
        Assert.Equal(cannon, (bool?)waves[index]!["cannon"]);
    }

    [Fact]
    public void ToJson_FortifiedInnerTurret_CarriesItsWindowAndReduction()
    {
        var game = LiveMatchFixture.SinglePlayer(
            "ARAM",
            530,
            null,
            LiveMatchFixture.Structure("TurretKilled", 500, "Turret_TChaos_L1_P3_671196429_0")
        );

        var marker = ((JArray)Progress(Forecast(game, 0), "兵线")!["markers"]!).First(row =>
            (bool?)row["ally"] == false && (double?)row["fortifiedUntil"] == 560
        );

        Assert.Equal(0.3, (double?)marker["fortification"]);
        Assert.Contains("满血 3900", ((string?)marker["details"])!);
    }

    [Fact]
    public void ToJson_DeathAdvice_IsCarriedByTheForecastSnapshot()
    {
        var advice = new DeathTimingAdvice(DeathTimingVerdict.Good, [], [new(3, "对面 2 人阵亡")]);

        var forecast = MinionWaveForecaster.Calculate(LiveMatchFixture.RiftGame(), 0)! with { DeathAdvice = advice };

        Assert.Equal("good", (string?)forecast.ToJson()["deathAdvice"]?["verdict"]);
        Assert.Equal("＋ 对面 2 人阵亡", (string?)forecast.ToJson()["deathAdvice"]?["details"]?[0]);
    }

    [Fact]
    public void ToJson_Siege_CarriesTheCalculatorsNumbers()
    {
        var game = LiveMatchFixture.SinglePlayer("ARAM", 300, LiveMatchFixture.Attacker(200, 0, 550));

        var json = Forecast(game, 0);

        Assert.Equal(57, (int?)Lane(json, "兵线")["siege"]?["hitsAlone"]);
    }

    [Fact]
    public void ToJson_Deaths_ListEveryoneWithTheirRespawnSeconds()
    {
        var deaths = (JArray)Sample()["deaths"]!;

        Assert.Equal(10, deaths.Count);
        Assert.Equal(13, (int?)deaths[0]!["level"]);
        Assert.Equal(true, (bool?)deaths[0]!["ally"]);
        Assert.Equal(1, deaths.Count(row => (bool?)row["self"] == true));
    }

    private static JObject Forecast(LiveMatchSnapshot snapshot, long clock) =>
        MinionWaveForecaster.Calculate(snapshot, clock)!.ToJson();

    private static JObject Lane(JObject forecast, string name) =>
        (JObject)((JArray)forecast["lanes"]!).Single(row => (string?)row["name"] == name);

    private static JObject? Progress(JObject forecast, string name) => Lane(forecast, name)["progress"] as JObject;
}
