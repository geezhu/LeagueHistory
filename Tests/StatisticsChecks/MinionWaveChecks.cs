using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>Wave schedule, composition, bounty growth, structure events and uncontested arrival times.</summary>
internal static class MinionWaveChecks
{
    public static void Run()
    {
        CheckSchedule();
        CheckComposition();
        CheckGold();
        CheckStructures();
        CheckArrivals();
        CheckSnapshot();
        CheckDeathTimes();
        Console.WriteLine(
            "PASS: minion wave schedule, cannon phases, bounty growth, structure names, arrivals and death timers."
        );
    }

    private static double[] Times(MinionWaveRules rules, double from, int count) =>
        MinionWaveForecaster
            .Spawns(rules, rules.FirstWave)
            .SkipWhile(s => s.Time < from)
            .Take(count)
            .Select(s => s.Time)
            .ToArray();

    private static void CheckSchedule()
    {
        var rift = MinionWaveRules.SummonersRift;
        Check(Times(rift, 0, 3).SequenceEqual([30.0, 60, 90]), "Summoner's Rift waves start at 0:30 every 30 seconds");
        Check(
            Times(rift, 810, 3).SequenceEqual([810.0, 840, 865]),
            "14:00 switches to 25 seconds: 13:30, 14:00, 14:25"
        );
        Check(Times(rift, 1765, 3).SequenceEqual([1765.0, 1790, 1810]), "30:00 pulls in the wave: 29:25, 29:50, 30:10");
        var abyss = MinionWaveRules.HowlingAbyss;
        Check(Times(abyss, 0, 3).SequenceEqual([50.0, 75, 100]), "Howling Abyss waves start at 0:50 every 25 seconds");
        Check(MinionWaveRules.For("KIWI", 12) == abyss, "ARAM: Mayhem uses the Howling Abyss rules");
        Check(MinionWaveRules.For("ARAM", 12) == abyss, "ARAM uses the Howling Abyss rules");
        Check(MinionWaveRules.For("URF", 11) == MinionWaveRules.UltraRapidFire, "URF has its own rules");
        Check(MinionWaveRules.For("CHERRY", 30) == null, "Arena has no lane minions");
        Check(MinionWaveRules.For("TUTORIAL", 11) == null, "Unknown modes are not guessed");
    }

    private static WaveSpawn Wave(MinionWaveRules rules, double time) =>
        MinionWaveForecaster.Spawns(rules, rules.FirstWave).First(s => s.Time >= time);

    private static void CheckComposition()
    {
        var rift = MinionWaveRules.SummonersRift;
        var early = MinionWaveForecaster.Spawns(rift, 30).Take(6).Select(s => s.Siege).ToArray();
        Check(early.SequenceEqual([0, 0, 1, 0, 0, 1]), "Cannon every third wave from wave 3");
        Check(Wave(rift, 840) is { Number: 28, Siege: 0, Melee: 3 }, "The 14:00 wave has no cannon");
        Check(Wave(rift, 865) is { Number: 29, Siege: 1, Melee: 2 }, "From 14:00 cannon waves lose a melee minion");
        Check(Wave(rift, 1500).Siege == 1 && Wave(rift, 1525).Siege == 1, "From 25:00 every wave has a cannon");
        Check(Wave(rift, 1810) is { Caster: 2, Melee: 2 }, "From 30:00 waves lose a caster");
        var abyss = MinionWaveRules.HowlingAbyss;
        Check(Wave(abyss, 100) is { Number: 3, Siege: 1 }, "Howling Abyss cannon on wave 3");
        var late = MinionWaveForecaster.Spawns(abyss, 50).Where(s => s.Time > 645).Take(4).Select(s => s.Siege);
        Check(late.Sum() == 2, "From 10:45 every other Howling Abyss wave has a cannon");
    }

    private static void CheckGold()
    {
        var rift = MinionWaveRules.SummonersRift;
        // League Wiki V26.01 averages: cannon 50 at 0:30, 59 at 14:00, 66 at 25:00, 69 at 30:00.
        foreach (var (time, gold) in new[] { (30.0, 50.0), (840, 59), (1500, 66), (1800, 69) })
        {
            Check(rift.SiegeGold.At(rift.Upgrades(time)) == gold, $"Cannon bounty at {time}s is {gold}");
        }
        Check(rift.MeleeGold.At(rift.Upgrades(1800)) == 20, "Rift melee bounty does not grow");
        var abyss = MinionWaveRules.HowlingAbyss;
        Check(abyss.MeleeGold.At(1000) == 32, "Melee growth stops at its cap");
        Check(rift.Speed.At(599) == 350 && rift.Speed.At(600) == 375 && rift.Speed.At(5000) == 450, "Speed steps");
    }

    private static void CheckStructures()
    {
        Check(LaneStructureState.ParseTurret("Turret_T2_L_03_A", false) == (200, "top", false), "Letter top turret");
        Check(LaneStructureState.ParseTurret("Turret_T1_C_07_A", false) == (100, "bot", false), "Bot inhibitor turret");
        Check(LaneStructureState.ParseTurret("Turret_T1_C_01_A", false) == (100, "mid", true), "Letter nexus turret");
        Check(LaneStructureState.ParseTurret("Turret_T2_L0_P3_1", false) == (200, "bot", false), "Digit bot turret");
        Check(LaneStructureState.ParseTurret("Turret_T1_L1_P4_1", false) == (100, "mid", true), "Digit nexus turret");
        Check(LaneStructureState.ParseTurret("Turret_T1_R_02_A", true) == (100, "mid", false), "Single lane maps");
        Check(LaneStructureState.ParseTurret("Obelisk_T1", false) == null, "Unknown names are not guessed");

        var events = OverlayDetailPreview.SampleWaves().Events!.Append(Event("TurretKilled", 800, "Odd_Name"));
        var state = LaneStructureState.From(events, false);
        Check(state.TurretsDown(200, "bot") == 3 && state.InhibitorDownSince(200, "bot") == 780, "Bot lane fallen");
        Check(state.TurretsDown(100, "mid") == 1 && state.InhibitorsDown(200) == 1, "Mid outer turret fallen");
        Check(state.Unknown.SequenceEqual(["Odd_Name"]), "Unknown structures are reported");
        var restored = LaneStructureState.From(events.Append(Event("InhibRespawned", 1080, "Barracks_T2_R1")), false);
        Check(restored.InhibitorDownSince(200, "bot") == null, "A respawned inhibitor stands again");

        var bot = MinionLaneMap.SummonersRift.Lanes.Single(l => l.Key == "bot");
        Check(MinionWaveForecaster.NextStructure(bot, state, 200).Kind == LaneStructureKind.Nexus, "Then nexus");
        var mid = MinionLaneMap.SummonersRift.Lanes.Single(l => l.Key == "mid");
        Check(MinionWaveForecaster.NextStructure(mid, state, 100).Kind == LaneStructureKind.Inner, "Then inner");
        Check(MinionWaveForecaster.NextStructure(mid, state, 200).Kind == LaneStructureKind.Outer, "Outer first");
    }

    private static LiveMatchEvent Event(string name, double time, string structure) =>
        new(null, name, time, "", "") { Structure = structure };

    private static LiveMatchSnapshot Game(string mode, int map, double time, string team, params LiveMatchEvent[] e) =>
        new(mode, map, time, [new("self|0", "我", "Ahri", "阿狸", team, true, 1, null)]) { Events = e };

    private static void CheckArrivals()
    {
        var forecast = MinionWaveForecaster.Calculate(Game("CLASSIC", 11, 31, "ORDER"), 0)!;
        var mid = forecast.Lanes.Single(l => l.Name == "中路");
        // Wave 2 spawns at 1:00; 7804 units to the red outer turret at 350 speed is about 22.3 seconds.
        Check(Math.Abs(mid.Ally.From - (60 + 7804 / 350.0)) < 0.01 && mid.Ally.Target == "敌方一塔", "Mid arrival");
        Check(Math.Abs(mid.Ally.To - mid.Ally.From - 5 * 0.8) < 0.01, "Six minions arrive over four seconds");
        Check(Math.Abs(mid.Enemy.From - (60 + 7791 / 350.0)) < 0.01 && mid.Enemy.Target == "我方一塔", "Enemy wave");
        var top = forecast.Lanes.Single(l => l.Name == "上路");
        Check(top.Ally.From < 60 + 13138 / 350.0, "The side-lane bonus speeds up early side waves");

        var red = MinionWaveForecaster.Calculate(Game("CLASSIC", 11, 31, "CHAOS"), 0)!;
        Check(red.Lanes.Single(l => l.Name == "中路").Ally.Target == "敌方一塔", "Red sees its own wave as ally");

        var abyss = MinionWaveForecaster.Calculate(Game("KIWI", 12, 10, "ORDER", Event("MinionsSpawning", 49, "")), 0)!;
        Check(abyss.Spawns[0].Time == 49 && abyss.Lanes.Length == 1, "The first-wave event calibrates the schedule");
        Check(abyss.Lanes[0].Ally.Target == "敌方一塔", "Howling Abyss has one lane");
        var far = MinionWaveForecaster.Calculate(Game("ARAM", 12, 10, "ORDER", Event("MinionsSpawning", 5, "")), 0)!;
        Check(far.Spawns[0].Time == 50, "A first-wave event far from the rule is ignored");

        var sample = MinionWaveForecaster.Calculate(OverlayDetailPreview.SampleWaves(), 0)!;
        var lane = sample.Lanes.Single(l => l.Name == "下路");
        Check(lane.Structures.Contains("敌方水晶被破") && lane.Structures.Contains("18:00 重生"), "Inhibitor note");
        Check(sample.Notes.Any(n => n.Contains("每 25 秒一波")), "The current interval is described");
    }

    private static void CheckSnapshot()
    {
        var data = new JObject
        {
            ["allPlayers"] = new JArray(new JObject { ["riotId"] = "我#1", ["team"] = "ORDER" }),
            ["activePlayer"] = new JObject { ["riotId"] = "我#1" },
            ["gameData"] = new JObject
            {
                ["gameMode"] = "CLASSIC",
                ["mapNumber"] = 11,
                ["gameTime"] = 700.5,
            },
            ["events"] = new JObject
            {
                ["Events"] = new JArray(
                    new JObject { ["EventName"] = "MinionsSpawning", ["EventTime"] = 30.0 },
                    new JObject
                    {
                        ["EventName"] = "TurretKilled",
                        ["EventTime"] = 612.3,
                        ["TurretKilled"] = "Turret_T2_L_03_A",
                        ["KillerName"] = "我",
                    }
                ),
            },
        };
        var snapshot = LiveMatchSnapshot.Parse(data);
        Check(snapshot.Events?.Single(e => e.Name == "TurretKilled").Structure == "Turret_T2_L_03_A", "Structure kept");
        var json = MinionWaveForecaster.Calculate(snapshot, 1234)!.ToJson();
        Check(
            (long?)json["clock"] == 1234 && (double?)json["gameTime"] == 700.5,
            "Clock and game time travel together"
        );
        var top = ((JArray)json["lanes"]!).Single(l => (string?)l["name"] == "上路");
        Check((string?)top["ally"]?["target"] == "敌方二塔", "Destroyed turrets move the target");
        Check(((JArray)json["spawns"]!).Count == 4, "Several spawns survive a missed poll");
    }

    private static void CheckDeathTimes()
    {
        var rift = DeathTimeRules.SummonersRift;
        Check(rift.Factor(599) == 0 && Math.Abs(rift.Factor(900) - 0.05) < 1e-9, "Rift factor reaches 5% at 15:00");
        Check(Math.Abs(rift.Factor(1800) - 0.1775) < 1e-9, "Rift factor reaches 17.75% at 30:00");
        Check(Math.Abs(rift.Seconds(18, 1800) - 52.5 * 1.1775) < 1e-9, "Level 18 at 30:00");
        Check(rift.Seconds(18, 3600) == 52.5 * 1.5 && rift.Seconds(30, 0) == 52.5, "Cap and level clamp");
        var abyss = DeathTimeRules.HowlingAbyss;
        Check(abyss.Seconds(18, 1200) == 40 && abyss.Seconds(1, 0) == 9, "Howling Abyss levels before 24:00");
        Check(Math.Abs(abyss.Seconds(18, 1740) - 45) < 1e-9 && abyss.Seconds(18, 2400) == 50, "2.5% a minute, 25% cap");
        Check(DeathTimeRules.For("KIWI", 12) == abyss && DeathTimeRules.For("CHERRY", 30) == null, "Mode tables");
        var swift = DeathTimeRules.Swiftplay;
        Check(swift.Factor(1500) == 0 && Math.Abs(swift.Factor(1530) - 0.1) < 1e-9, "Swiftplay grows after 25:00");

        var deaths = MinionWaveForecaster.Calculate(OverlayDetailPreview.SampleWaves(), 0)!.Deaths;
        Check(deaths.Length == 10 && deaths.Take(5).All(d => d.Ally) && deaths.Count(d => d.Self) == 1, "Own first");
        Check(deaths[0].Level == 13 && deaths[0].Seconds == rift.Seconds(13, 846), "Highest level first");
    }
}
