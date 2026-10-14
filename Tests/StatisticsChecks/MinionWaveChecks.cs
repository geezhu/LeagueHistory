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
        CheckChat();
        CheckProgress();
        Console.WriteLine(
            "PASS: minion wave schedule, cannon phases, bounty growth, structure names, arrivals, death timers, team chat lines and lane progress."
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
        // Waves spawn at the nexus: 1717 + 560 units behind the blue inhibitor, 1710 + 560 behind the red one.
        Check(
            Math.Abs(mid.Ally.From - (60 + (7804 + 1717 + 560) / 350.0)) < 0.01 && mid.Ally.Target == "敌方一塔",
            "Mid arrival"
        );
        Check(Math.Abs(mid.Ally.To - mid.Ally.From - 5 * 0.8) < 0.01, "Six minions arrive over four seconds");
        Check(
            Math.Abs(mid.Enemy.From - (60 + (7791 + 1710 + 560) / 350.0)) < 0.01 && mid.Enemy.Target == "我方一塔",
            "Enemy wave"
        );
        var top = forecast.Lanes.Single(l => l.Name == "上路");
        Check(top.Ally.From < 60 + (13138 + 1735 + 560) / 350.0, "The side-lane bonus speeds up early side waves");

        var red = MinionWaveForecaster.Calculate(Game("CLASSIC", 11, 31, "CHAOS"), 0)!;
        Check(red.Lanes.Single(l => l.Name == "中路").Ally.Target == "敌方一塔", "Red sees its own wave as ally");

        var abyss = MinionWaveForecaster.Calculate(Game("KIWI", 12, 10, "ORDER", Event("MinionsSpawning", 49, "")), 0)!;
        Check(abyss.Spawns[0].Time == 49 && abyss.Lanes.Length == 1, "The first-wave event calibrates the schedule");
        Check(abyss.Lanes[0].Ally.Target == "敌方一塔", "Howling Abyss has one lane");
        var far = MinionWaveForecaster.Calculate(Game("ARAM", 12, 10, "ORDER", Event("MinionsSpawning", 5, "")), 0)!;
        Check(far.Spawns[0].Time == 50, "A first-wave event far from the rule is ignored");

        var sample = MinionWaveForecaster.Calculate(OverlayDetailPreview.SampleWaves(), 0)!;
        var lane = sample.Lanes.Single(l => l.Name == "下路");
        Check(lane.Structures.Contains("敌方小水晶被破") && lane.Structures.Contains("18:00 重生"), "Inhibitor note");
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

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void CheckChat()
    {
        var sample = MinionWaveForecaster.Calculate(OverlayDetailPreview.SampleWaves(), 100000)!;
        var spawn = MinionWaveChatMessage.Build(sample, "spawn", 100000).Single();
        Check(spawn.StartsWith("兵线：0:19 后出第 29 波（炮车波）") && spawn.Contains("每 25 秒一波"), "Spawn line");
        var later = MinionWaveChatMessage.Build(sample, "spawn", 105000).Single();
        Check(later.StartsWith("兵线：0:14 后"), "The countdown is taken at send time");
        Check(
            MinionWaveChatMessage
                .Build(sample, "super", 100000)
                .SequenceEqual(["超级兵：下路 敌方小水晶被破 · 我方每波 +1 超级兵 · 约 18:00 重生"]),
            "Super minion line names the lane"
        );
        var deaths = MinionWaveChatMessage.Build(sample, "deaths", 100000);
        Check(
            deaths.Count == 2 && deaths[0].StartsWith("现在阵亡需等待（时间加成 +4%） 敌方：样例英雄 10 13级42秒"),
            "Enemies first"
        );
        Check(Throws(() => MinionWaveChatMessage.Build(sample, "deaths", 116000)), "Stale data is not sent");
        Check(Throws(() => MinionWaveChatMessage.Build(null, "spawn", 0)), "No forecast, no message");
        var quiet = MinionWaveForecaster.Calculate(Game("CLASSIC", 11, 31, "ORDER"), 0)!;
        Check(Throws(() => MinionWaveChatMessage.Build(quiet, "super", 0)), "No fallen inhibitor, no super line");

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        JObject Request(string kind, string target) =>
            new()
            {
                ["protocol"] = 1,
                ["id"] = Guid.NewGuid().ToString("N"),
                ["session"] = "s",
                ["target"] = target,
                ["kind"] = kind,
                ["slot"] = -1,
                ["championId"] = 0,
                ["updatedAt"] = now,
                ["dismissed"] = false,
                ["cancelled"] = false,
            };
        var parsed = OverlayChatRequest.Parse(Request("waves", "deaths"), "s", new int[10], now);
        Check(parsed is { Kind: "waves", Target: "deaths", IsRosterFree: true }, "Wave requests parse");
        Check(OverlayChatRequest.Parse(Request("waves", "own"), "s", new int[10], now) == null, "Wave targets only");
        Check(OverlayChatRequest.Parse(Request("tier", "spawn"), "s", new int[10], now) == null, "Tier targets only");
    }

    private static void CheckProgress()
    {
        var sample = MinionWaveForecaster.Calculate(OverlayDetailPreview.SampleWaves(), 0)!;
        Check(sample.Current is { Number: 28, Time: 840 }, "The latest spawned wave is the current one");
        var mid = sample.Lanes.Single(l => l.Name == "中路").Progress!;
        Check(mid.Markers.Length == 12, "Mid lane has twelve structures with the nexus turrets and nexus");
        Check(
            mid.Waves.Select(w => w.Number).SequenceEqual([27, 28, 29, 30, 31, 32])
                && mid.Waves[1] is { Speed: 375, Bonus: 0, Cannon: false }
                && mid.Waves[2].Cannon,
            "Two walking waves and four upcoming ones, with speed and cannon flags"
        );
        var ownOuter = mid.Markers.Single(m => m.Label == "我方一塔");
        Check(
            !ownOuter.Alive && Math.Abs(ownOuter.At - (4113 + 1717 + 560) / 16451.0) < 1e-4,
            "Own outer turret fallen, measured on the nexus-to-nexus bar"
        );
        Check(
            mid.Markers.First().Label == "我方大水晶"
                && mid.Markers.Last().Label == "敌方大水晶"
                && mid.Markers[1].Label == "我方门牙塔（剩 2 座）"
                && mid.Markers[2].Label == "我方小水晶",
            "The bar runs nexus to nexus with nexus turrets and inhibitors inside"
        );
        Check(
            Math.Abs(mid.Length - 16451) < 1 && mid.Spawn == 0 && mid.EnemySpawn == 1,
            "Waves spawn at the nexus at both ends of the bar"
        );
        Check(mid.Hint.Contains("我方半场"), "Losing a turret suggests the lane sits on the own half");
        Check(sample.Lanes.Single(l => l.Name == "下路").Progress!.Hint.Contains("敌方高地"), "Fallen enemy inhibitor");
        Check(sample.Lanes.Single(l => l.Name == "上路").Progress!.Hint.Contains("无法"), "No structures, no guess");

        var red = MinionWaveForecaster.Calculate(Game("CLASSIC", 11, 75, "CHAOS"), 0)!;
        var redMid = red.Lanes.Single(l => l.Name == "中路").Progress!;
        Check(
            redMid.Markers[2].Label == "我方小水晶"
                && Math.Abs(redMid.Markers[2].At - (1710 + 560) / 16451.0) < 1e-4
                && Math.Abs(redMid.Markers[3].At - (1710 + 560 + 651) / 16451.0) < 1e-4,
            "Red sees its own base on the left"
        );
        Check(
            red.Lanes.Single(l => l.Name == "上路").Progress!.Waves.All(w => w.Number < 2 || w.Bonus > 0)
                && redMid.Waves.All(w => w.Bonus == 0),
            "Early side-lane waves carry their bonus, mid waves none"
        );
        var json = (JObject)sample.ToJson()["lanes"]![1]!["progress"]!;
        Check(
            ((JArray)json["markers"]!).Count == 12
                && (string?)json["markers"]?[0]?["kind"] == "base"
                && (string?)json["markers"]?[1]?["kind"] == "nexus"
                && (string?)json["markers"]?[2]?["kind"] == "inhibitor"
                && (string?)json["markers"]?[3]?["kind"] == "turret"
                && (double?)json["enemySpawn"] == 1
                && (string?)json["hint"] == mid.Hint
                && (int?)json["waves"]?[2]?["wave"] == 29
                && (bool?)json["waves"]?[2]?["cannon"] == true,
            "Progress travels to the widget"
        );
        Check((int?)sample.ToJson()["current"]?["wave"] == 28, "The current wave travels to the widget");
        var spawns = (JArray)sample.ToJson()["spawns"]!;
        Check(
            (int?)spawns[0]!["gold"] == 141
                && (int?)spawns[0]!["xp"] == 292
                && (int?)spawns[1]!["gold"] == 102
                && (int?)spawns[1]!["xp"] == 279,
            "Each upcoming wave carries its gold and experience"
        );
        Check(
            MinionWaveForecaster.Calculate(Game("ARAM", 12, 10, "ORDER"), 0)!.Current == null,
            "Before the first wave"
        );
    }
}
