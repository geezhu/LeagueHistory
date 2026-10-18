using League.Models;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The wave schedule, the compositions, the arrival times and the death costs. The expected values come from the
/// checks in Tests/StatisticsChecks/MinionWaveChecks.cs.
/// </summary>
public class MinionWaveForecasterTests
{
    private static MinionWaveRules Rift => MinionWaveRules.SummonersRift;

    private static MinionWaveRules Abyss => MinionWaveRules.HowlingAbyss;

    [Theory]
    [InlineData(30, 60)]
    [InlineData(810, 840)]
    [InlineData(840, 865)]
    [InlineData(1490, 1515)]
    [InlineData(1790, 1810)]
    public void NextSpawn_GameTime_ReturnsTheFollowingWave(double time, double expected)
    {
        double next = MinionWaveForecaster.NextSpawn(Rift, time);

        Assert.Equal(expected, next);
    }

    [Fact]
    public void Spawns_SummonersRift_StartsAtThirtySecondsEveryThirty()
    {
        var times = Times(Rift, 0, 3);

        Assert.Equal([30.0, 60.0, 90.0], times);
    }

    [Fact]
    public void Spawns_AfterFourteenMinutes_SwitchesToTwentyFiveSeconds()
    {
        var times = Times(Rift, 810, 3);

        Assert.Equal([810.0, 840.0, 865.0], times);
    }

    [Fact]
    public void Spawns_AfterThirtyMinutes_PullsTheWaveIn()
    {
        var times = Times(Rift, 1765, 3);

        Assert.Equal([1765.0, 1790.0, 1810.0], times);
    }

    [Fact]
    public void Spawns_HowlingAbyss_StartsAtFiftySecondsEveryTwentyFive()
    {
        var times = Times(Abyss, 0, 3);

        Assert.Equal([50.0, 75.0, 100.0], times);
    }

    [Fact]
    public void Spawns_SummonersRiftEarly_AddsACannonEveryThirdWave()
    {
        var siege = MinionWaveForecaster.Spawns(Rift, 30).Take(6).Select(wave => wave.Siege);

        Assert.Equal([0, 0, 1, 0, 0, 1], siege);
    }

    [Fact]
    public void Spawns_HowlingAbyssAfterTenFortyFive_AlternatesCannons()
    {
        var siege = MinionWaveForecaster
            .Spawns(Abyss, 50)
            .Where(wave => wave.Time > 645)
            .Take(4)
            .Select(wave => wave.Siege);

        Assert.Equal([0, 1, 0, 1], siege);
    }

    [Theory]
    [InlineData(840, 3, 3, 0)]
    [InlineData(865, 2, 3, 1)]
    [InlineData(1515, 2, 3, 1)]
    [InlineData(1810, 2, 2, 1)]
    public void Spawns_SummonersRift_CompositionFollowsTheStageTables(double from, int melee, int caster, int siege)
    {
        var wave = Wave(Rift, from);

        Assert.Equal((melee, caster, siege), (wave.Melee, wave.Caster, wave.Siege));
    }

    [Theory]
    [InlineData(840, 28)]
    [InlineData(865, 29)]
    public void Spawns_SummonersRift_NumberingContinuesAcrossTheIntervalChange(double from, int number)
    {
        var wave = Wave(Rift, from);

        Assert.Equal(number, wave.Number);
    }

    [Fact]
    public void Spawns_HowlingAbyss_ThirdWaveCarriesTheFirstCannon()
    {
        var wave = Wave(Abyss, 100);

        Assert.Equal((3, 1), (wave.Number, wave.Siege));
    }

    [Fact]
    public void Calculate_SummonersRiftMidLane_TimesBothWavesToTheOuterTurrets()
    {
        var forecast = Calculate(LiveMatchFixture.WaveGame("CLASSIC", 11, 31, "ORDER"));

        var mid = forecast!.Lanes.Single(lane => lane.Name == "中路");

        Assert.Equal(60 + (7804 + 1717 + 560) / 350.0, mid.Ally.From, 9);
        Assert.Equal("敌方一塔", mid.Ally.Target);
        Assert.Equal(60 + (7791 + 1710 + 560) / 350.0, mid.Enemy.From, 9);
        Assert.Equal("我方一塔", mid.Enemy.Target);
    }

    [Fact]
    public void Calculate_WaveOfSixMinions_ArrivesOverFourSeconds()
    {
        var forecast = Calculate(LiveMatchFixture.WaveGame("CLASSIC", 11, 31, "ORDER"));

        var mid = forecast!.Lanes.Single(lane => lane.Name == "中路");

        Assert.Equal(5 * 0.8, mid.Ally.To - mid.Ally.From, 9);
    }

    [Fact]
    public void Calculate_EarlySideLane_CoversTheSpeedBonusBeforeArriving()
    {
        var forecast = Calculate(LiveMatchFixture.WaveGame("CLASSIC", 11, 31, "ORDER"));

        var top = forecast!.Lanes.Single(lane => lane.Name == "上路");

        // The second wave is the first upcoming one; 111 bonus points at 6.25 units each, faded in four stages.
        Assert.Equal(60 + (13138 + 1735 + 560 - 111 * 6.25 * 2.5) / 350.0, top.Ally.From, 9);
    }

    [Fact]
    public void Calculate_TeamChaos_SeesItsOwnWaveAsTheAllyOne()
    {
        var forecast = Calculate(LiveMatchFixture.WaveGame("CLASSIC", 11, 31, "CHAOS"));

        var mid = forecast!.Lanes.Single(lane => lane.Name == "中路");

        Assert.Equal("敌方一塔", mid.Ally.Target);
    }

    [Fact]
    public void Calculate_FirstWaveEventNearTheRule_CalibratesTheSchedule()
    {
        var game = LiveMatchFixture.WaveGame(
            "KIWI",
            12,
            10,
            "ORDER",
            LiveMatchFixture.Structure("MinionsSpawning", 49, "")
        );

        var forecast = Calculate(game);

        Assert.Equal(49, forecast!.Spawns[0].Time);
        Assert.Equal(1, forecast.Lanes.Length);
        Assert.Equal("敌方一塔", forecast.Lanes[0].Ally.Target);
    }

    [Fact]
    public void Calculate_FirstWaveEventFarFromTheRule_IsIgnored()
    {
        var game = LiveMatchFixture.WaveGame(
            "ARAM",
            12,
            10,
            "ORDER",
            LiveMatchFixture.Structure("MinionsSpawning", 5, "")
        );

        var forecast = Calculate(game);

        Assert.Equal(50, forecast!.Spawns[0].Time);
    }

    [Fact]
    public void Calculate_FallenInhibitor_NotesTheRespawnTime()
    {
        var bot = LaneOf(LiveMatchFixture.RiftGame(), "下路");

        Assert.Contains("敌方小水晶被破", bot.Structures);
        Assert.Contains("18:00 重生", bot.Structures);
    }

    [Fact]
    public void Calculate_SummonersRift_NotesTheCurrentInterval()
    {
        var forecast = Calculate(LiveMatchFixture.RiftGame());

        Assert.Equal(["每 25 秒一波 · 每 2 波 1 个炮车"], forecast!.Notes);
    }

    [Fact]
    public void Calculate_UpcomingIntervalChange_NotesItWithinTwoMinutes()
    {
        var forecast = Calculate(LiveMatchFixture.WaveGame("CLASSIC", 11, 1790, "ORDER"));

        Assert.Equal(["每 25 秒一波 · 每波都有炮车", "30:00 起每 20 秒一波"], forecast!.Notes);
    }

    [Fact]
    public void Calculate_UpcomingSiegeChange_NotesItWithinTwoMinutes()
    {
        var forecast = Calculate(LiveMatchFixture.WaveGame("CLASSIC", 11, 1450, "ORDER"));

        Assert.Equal(["每 25 秒一波 · 每 2 波 1 个炮车", "25:00 起每波都有炮车"], forecast!.Notes);
    }

    [Fact]
    public void Calculate_TheFirstUpcomingWaves_ValueCarriesBountyAndExperience()
    {
        var forecast = Calculate(LiveMatchFixture.RiftGame());

        Assert.Equal("普通波 102 金 / 279 经验 · 炮车波 141 金 / 292 经验", forecast!.Value);
    }

    [Fact]
    public void Calculate_UnknownStructureEvent_ReportsItOnTheForecast()
    {
        var game = LiveMatchFixture.WaveGame(
            "ARAM",
            12,
            300,
            "ORDER",
            LiveMatchFixture.Structure("TurretKilled", 200, "Odd_Name")
        );

        var forecast = Calculate(game);

        Assert.Equal(["Odd_Name"], forecast!.UnknownStructures);
    }

    [Fact]
    public void Calculate_NoWaveYet_LeavesCurrentEmpty()
    {
        var forecast = Calculate(LiveMatchFixture.WaveGame("ARAM", 12, 10, "ORDER"));

        Assert.Null(forecast!.Current);
    }

    [Fact]
    public void NextStructure_TouchedLane_IsTheOuterTurret()
    {
        var state = LaneStructureState.From(RiftEvents(), false);
        var mid = MinionLaneMap.SummonersRift.Lanes.Single(lane => lane.Key == "mid");

        var next = MinionWaveForecaster.NextStructure(mid, state, 200);

        Assert.Equal(LaneStructureKind.Outer, next.Kind);
    }

    [Fact]
    public void NextStructure_OwnMidWithOneTurretDown_IsTheInnerTurret()
    {
        var state = LaneStructureState.From(RiftEvents(), false);
        var mid = MinionLaneMap.SummonersRift.Lanes.Single(lane => lane.Key == "mid");

        var next = MinionWaveForecaster.NextStructure(mid, state, 100);

        Assert.Equal(LaneStructureKind.Inner, next.Kind);
    }

    [Fact]
    public void NextStructure_OpenLaneWithFallenInhibitor_IsTheNexus()
    {
        var state = LaneStructureState.From(RiftEvents(), false);
        var bot = MinionLaneMap.SummonersRift.Lanes.Single(lane => lane.Key == "bot");

        var next = MinionWaveForecaster.NextStructure(bot, state, 200);

        Assert.Equal(LaneStructureKind.Nexus, next.Kind);
    }

    [Fact]
    public void Calculate_Deaths_ListTheOwnTeamFirstThenByLevel()
    {
        var deaths = Calculate(LiveMatchFixture.RiftGame())!.Deaths;

        Assert.Equal(10, deaths.Length);
        Assert.DoesNotContain(deaths.Take(5), death => !death.Ally);
        Assert.Equal(1, deaths.Count(death => death.Self));
        Assert.Equal(13, deaths[0].Level);
    }

    [Fact]
    public void Calculate_Deaths_UseTheModesLadderForTheGameTime()
    {
        var deaths = Calculate(LiveMatchFixture.RiftGame())!.Deaths;

        Assert.Equal(DeathTimeRules.SummonersRift.Seconds(13, 846), deaths[0].Seconds, 9);
    }

    [Fact]
    public void Calculate_Deaths_NameARosterKeyForEveryPlayer()
    {
        var deaths = Calculate(LiveMatchFixture.RiftGame())!.Deaths;

        Assert.DoesNotContain(deaths, death => death.Player.Length == 0);
    }

    [Fact]
    public void Calculate_DeathFactor_IsTheModesFactorInPercent()
    {
        var forecast = Calculate(LiveMatchFixture.WaveGame("CLASSIC", 11, 900, "ORDER"));

        Assert.Equal(5, forecast!.DeathFactor, 9);
    }

    [Fact]
    public void DeathCosts_LevelZeroPlayers_AreLeftOut()
    {
        var game = LiveMatchFixture.WithLevel(LiveMatchFixture.WaveGame("CLASSIC", 11, 900, "ORDER"), 0);

        var costs = MinionWaveForecaster.DeathCosts(game, 100);

        Assert.Empty(costs);
    }

    [Fact]
    public void DeathCosts_ModeWithoutATable_ReturnsNothing()
    {
        var costs = MinionWaveForecaster.DeathCosts(LiveMatchFixture.WaveGame("CHERRY", 30, 100, "ORDER"), 100);

        Assert.Empty(costs);
    }

    [Fact]
    public void Calculate_Arena_ReturnsNull()
    {
        var forecast = MinionWaveForecaster.Calculate(LiveMatchFixture.WaveGame("CHERRY", 30, 100, "ORDER"), 0);

        Assert.Null(forecast);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59.6, "1:00")]
    [InlineData(846, "14:06")]
    [InlineData(1080, "18:00")]
    public void Clock_Seconds_FormatsGameTime(double seconds, string expected)
    {
        string clock = MinionWaveForecaster.Clock(seconds);

        Assert.Equal(expected, clock);
    }

    private static MinionWaveForecast? Calculate(LiveMatchSnapshot snapshot) =>
        MinionWaveForecaster.Calculate(snapshot, 0);

    private static LaneWaveForecast LaneOf(LiveMatchSnapshot snapshot, string name) =>
        Calculate(snapshot)!.Lanes.Single(lane => lane.Name == name);

    private static double[] Times(MinionWaveRules rules, double from, int count) =>
        MinionWaveForecaster
            .Spawns(rules, rules.FirstWave)
            .SkipWhile(wave => wave.Time < from)
            .Take(count)
            .Select(wave => wave.Time)
            .ToArray();

    private static WaveSpawn Wave(MinionWaveRules rules, double time) =>
        MinionWaveForecaster.Spawns(rules, rules.FirstWave).First(wave => wave.Time >= time);

    private static IEnumerable<LiveMatchEvent> RiftEvents() =>
        [
            LiveMatchFixture.Structure("TurretKilled", 610, "Turret_T2_R_03_A"),
            LiveMatchFixture.Structure("TurretKilled", 680, "Turret_T2_R_02_A"),
            LiveMatchFixture.Structure("TurretKilled", 742, "Turret_T2_C_07_A"),
            LiveMatchFixture.Structure("InhibKilled", 780, "Barracks_T2_R1"),
            LiveMatchFixture.Structure("TurretKilled", 701, "Turret_T1_C_05_A"),
        ];
}
