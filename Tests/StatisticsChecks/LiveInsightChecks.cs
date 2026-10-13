using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>Scoreboard insight, survivability advice, build comparison and their Game Bar fields, from a 2999 sample.</summary>
internal static class LiveInsightChecks
{
    private static readonly Dictionary<int, int> Prices = new() { [3071] = 3000, [1001] = 300 };

    public static async Task RunAsync()
    {
        var snapshot = LiveMatchSnapshot.Parse(Sample());
        var roster = new LoadingRoster(
            "game-1",
            2400,
            "KIWI",
            [
                new("r-a1", "A1#1", "", 0, 1, 100, true),
                new("r-a2", "A2#1", "", 0, 2, 100, false),
                new("r-b1", "B1#1", "", 0, 3, 200, false),
                new("r-b2", "B2#1", "", 0, 4, 200, false),
            ]
        );
        CheckSnapshot(snapshot);
        var insight = LiveMatchInsight.Calculate(
            roster,
            snapshot,
            item => Prices.GetValueOrDefault(item.Id, item.Price)
        )!;
        CheckInsight(insight);
        CheckDefense();
        CheckBuild();
        var sections = CheckPanel(insight, snapshot);
        await CheckBridgeAsync(roster, new(insight, sections, "rev-1"));
        Console.WriteLine(
            "PASS: live economy, respawns, fights, defense advice, build comparison and Game Bar fields."
        );
    }

    private static void CheckSnapshot(LiveMatchSnapshot snapshot)
    {
        var dead = snapshot.Players.Single(p => p.Name == "B2#1");
        Check(dead.IsDead && dead.RespawnTimer == 17.5, "Death state and respawn time are read for every player");
        Check(snapshot.Players.Single(p => p.Self).Assists == 1, "Assists are read from the scoreboard");
        Check(
            snapshot.Active?.Stats is { MaxHealth: 1000, Armor: 100, ArmorPenetrationPercent: > 0.19 and < 0.21 },
            "Percent penetration converts 2999's remaining multiplier into a penetration share"
        );
        Check(snapshot.Active!.Runes.SequenceEqual([8010, 9111, 5008]), "Rune ids include keystone, runes and shards");
        var kiwi = (JObject)Sample().DeepClone();
        kiwi["activePlayer"]!["fullRunes"] = new JObject();
        Check(LiveMatchSnapshot.Parse(kiwi).Active!.Runes.Length == 0, "Mayhem's empty rune object yields no runes");
        Check(
            snapshot.Events!.Single(e => e.Name == "ChampionKill" && e.Time == 100).Assisters.SequenceEqual(["A2"]),
            "Kill assisters are kept for participation"
        );
        Check(
            LiveDeathStreakAnalysis.Calculate(snapshot).Count == 4,
            "Added event kinds leave the death-streak rebuild intact"
        );
    }

    private static void CheckInsight(LiveMatchInsight insight)
    {
        Check(
            insight.Own.ItemGold == 3000 && insight.Enemy.ItemGold == 600 && insight.GoldLead == 2400,
            "Item value uses the catalog's total price times the stack count"
        );
        Check(insight.Own.Kills == 4 && insight.Enemy.Kills == 1, "Team kills come from the scoreboard");
        var self = insight.Players["r-a1"];
        Check(
            self.KillParticipation == 0.75 && self.Score == "2/0/1",
            "Participation is (kills + assists) / team kills"
        );
        Check(insight.Players["r-b2"].Respawn == 17.5 && insight.Players["r-a1"].Respawn == 0, "Only the dead respawn");
        Check(
            insight.Fights.Length == 1 && insight.Fights[0] is { OwnKills: 2, EnemyKills: 1 } && insight.FightsWon == 1,
            "Three close kills form one fight; a lone kill later does not"
        );
        Check(insight.Own.Aces == 1 && insight.Enemy.Aces == 0, "Aces are credited to the acing team");
        Check(
            insight.Own.Multikills == 1 && insight.Own.LargestMultikill == 3,
            "A triple kill counts as one multikill run"
        );
        Check(insight.Result == "Win", "The game end result is kept for the summary");
    }

    private static void CheckDefense()
    {
        var stats = new LiveChampionStats(1000, 100, 0, 0, 0, 0, 0, 0);
        var physical = LiveDefenseAdvice.Calculate(stats, 1);
        Check(
            Math.Abs(physical.PhysicalHealth - 2000) < 1e-9 && Math.Abs(physical.MagicHealth - 1000) < 1e-9,
            "Effective health is health × (1 + resistance / 100)"
        );
        Check(physical.Priority == LiveDefensePriority.Armor, "All-physical enemies favor armor");
        Check(
            LiveDefenseAdvice.Calculate(stats, 0).Priority == LiveDefensePriority.MagicResist,
            "All-magic enemies favor magic resist"
        );
        Check(
            LiveDefenseAdvice.Calculate(stats, null).Priority == LiveDefensePriority.Unknown,
            "Without a damage mix no resistance is recommended"
        );
        var negative = LiveDefenseAdvice.Calculate(new(1200, -50, 0, 0, 0, 0, 0, 0), null);
        Check(Math.Abs(negative.PhysicalHealth - 900) < 1e-9, "Negative armor increases damage taken");
    }

    private static void CheckBuild()
    {
        LiveBuildOption Option(params int[] ids) => new(ids, 0.5, 0.1, 100);
        var advice = LiveBuildAdvice.Choose([Option(1, 2, 3), Option(3071, 4, 5), Option(3071, 6, 7)], [3071, 6]);
        Check(
            advice is { Owned: 2, Next: 7 },
            "The build sharing most owned items is followed to its first missing item"
        );
        Check(LiveBuildAdvice.Choose([Option(1, 2)], [1, 2])!.Next == null, "A completed build has no next item");
        Check(LiveBuildAdvice.Choose([], [1]) == null, "No recommendations produce no advice");
    }

    private static JArray CheckPanel(LiveMatchInsight insight, LiveMatchSnapshot snapshot)
    {
        var stats = snapshot.Active!.Stats!;
        var sections = LivePanelSections.Build(
            new(
                insight,
                insight.Players["r-a1"],
                stats,
                LiveDefenseAdvice.Calculate(stats, 0.7),
                new(new([3071, 3053], 0.55, 0.2, 500), 1, 3053),
                [3071],
                snapshot.Active.Runes,
                "来自游戏内数据。"
            ),
            id => "装备" + id
        );
        string[] titles = sections.Select(s => (string)s["title"]!).ToArray();
        Check(
            titles.SequenceEqual(["本局结束", "战况", "生存与属性", "出装对照", "符文页"])
                && sections.All(s => (string?)s["tab"] == LivePanelSections.Tab),
            "The live tab lists the ending, battle, survivability, build and runes: " + string.Join(",", titles)
        );
        var build = (JObject)sections[3]["rows"]![0]!;
        Check(
            (string)build["title"]! == "下一件：装备3053"
                && (bool)build["icons"]![0]!["owned"]!
                && !(bool)build["icons"]![1]!["owned"]!,
            "The build row names the next item and dims owned ones"
        );
        string battle = sections[1]["rows"]![0]!.ToString();
        Check(
            battle.Contains("领先 2.4k") && battle.Contains("团战 1 胜 0 负"),
            "The battle row shows lead and fights"
        );
        return sections;
    }

    private static async Task CheckBridgeAsync(LoadingRoster roster, OverlayLiveState live)
    {
        string folder = Path.Combine(Path.GetTempPath(), "league-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var bridge = new GameBarBridge(folder);
        try
        {
            bridge.Publish(
                roster,
                new Dictionary<string, PlayerChampionTier>(),
                new Dictionary<int, Image>(),
                id => "英雄 " + id,
                _ => "",
                "match-1",
                "当前对局",
                true,
                true,
                true,
                live: live
            );
            var snapshot = JObject.Parse(
                await BridgeCheckFiles.ReadAsync(Path.Combine(folder, "snapshot.json"), "Live snapshot is written")
            );
            var players = (JArray)snapshot["players"]!;
            var self = players.OfType<JObject>().Single(p => (int?)p["championId"] == 1);
            var dead = players.OfType<JObject>().Single(p => (int?)p["championId"] == 4);
            Check(
                (bool)self["self"]! && (string)self["liveStats"]! == "参团 75% · 装备 3.0k",
                "Cards carry participation and item value, not the KDA the scoreboard shows"
            );
            Check((double)dead["respawn"]! == 17.5, "Dead players carry the respawn time for the widget countdown");
            Check(
                ((string)snapshot["teamSummaries"]![0]!).Contains("领先 2.4k")
                    && (string?)snapshot["liveRevision"] == "rev-1"
                    && ((JArray)snapshot["liveSections"]!).Count == live.Sections.Count,
                "Team summaries and the live panel are published"
            );
            await CheckRespawnMarksAsync(bridge, folder, snapshot);
        }
        finally
        {
            bridge.Dispose();
            await bridge.Completion;
            Directory.Delete(folder, true);
        }
    }

    // The widget marks slots; the bridge keeps only those still showing the marked champion.
    private static async Task CheckRespawnMarksAsync(GameBarBridge bridge, string folder, JObject snapshot)
    {
        var players = (JArray)snapshot["players"]!;
        int slot = players.Select((p, i) => (p, i)).Single(row => (int?)row.p["championId"] == 4).i;
        var marks = new JObject
        {
            ["protocol"] = 1,
            ["session"] = snapshot["session"],
            ["slots"] = new JArray(slot, 0),
            ["heroes"] = new JArray(4, 999),
            ["sound"] = true,
            ["test"] = 0,
        };
        await File.WriteAllTextAsync(Path.Combine(folder, "respawn-marks.json"), marks.ToString());
        var read = await bridge.ReadRespawnMarksAsync();
        Check(read != null && read.Slots.SequenceEqual([slot]), "A slot whose champion changed is not marked");
        Check(GameBarBridge.Slots(null).Length == 10, "The widget always has ten slots");
    }

    private static JObject Sample() =>
        new()
        {
            ["activePlayer"] = ActivePlayer(),
            ["allPlayers"] = Players(),
            ["events"] = new JObject { ["Events"] = Events() },
            ["gameData"] = new JObject
            {
                ["gameMode"] = "KIWI",
                ["gameTime"] = 610.0,
                ["mapNumber"] = 12,
            },
        };

    private static JObject ActivePlayer() =>
        new()
        {
            ["riotId"] = "A1#1",
            ["summonerName"] = "A1#1",
            ["championStats"] = new JObject
            {
                ["maxHealth"] = 1000.0,
                ["armor"] = 100.0,
                ["magicResist"] = 0.0,
                ["abilityHaste"] = 20.0,
                ["physicalLethality"] = 10.0,
                ["armorPenetrationPercent"] = 0.8,
                ["magicPenetrationFlat"] = 5.0,
                ["magicPenetrationPercent"] = 1.0,
            },
            ["fullRunes"] = new JObject
            {
                ["generalRunes"] = new JArray(new JObject { ["id"] = 8010 }, new JObject { ["id"] = 9111 }),
                ["statRunes"] = new JArray(new JObject { ["id"] = 5008 }),
            },
        };

    private static JArray Players()
    {
        JObject Player(string name, string team, int kills, int deaths, int assists, JArray items, bool dead = false) =>
            new()
            {
                ["riotId"] = name + "#1",
                ["riotIdGameName"] = name,
                ["summonerName"] = name + "#1",
                ["team"] = team,
                ["level"] = 10,
                ["isDead"] = dead,
                ["respawnTimer"] = dead ? 17.5 : 0,
                ["rawChampionName"] = "game_character_displayname_Ziggs",
                ["championName"] = name,
                ["items"] = items,
                ["scores"] = new JObject
                {
                    ["kills"] = kills,
                    ["deaths"] = deaths,
                    ["assists"] = assists,
                },
            };
        JObject Item(int id, int count, int price) =>
            new()
            {
                ["itemID"] = id,
                ["count"] = count,
                ["price"] = price,
                ["displayName"] = "装备",
            };
        return new JArray(
            Player("A1", "ORDER", 2, 0, 1, new JArray(Item(3071, 1, 200))),
            Player("A2", "ORDER", 2, 1, 0, new JArray()),
            Player("B1", "CHAOS", 1, 1, 0, new JArray(Item(1001, 2, 300))),
            Player("B2", "CHAOS", 0, 2, 0, new JArray(), true)
        );
    }

    private static JArray Events()
    {
        JObject Event(int id, string name, double time, JObject? extra = null)
        {
            var row = new JObject
            {
                ["EventID"] = id,
                ["EventName"] = name,
                ["EventTime"] = time,
            };
            row.Merge(extra ?? new JObject());
            return row;
        }
        JObject Kill(string killer, string victim, params string[] assisters) =>
            new()
            {
                ["KillerName"] = killer,
                ["VictimName"] = victim,
                ["Assisters"] = new JArray(assisters),
            };

        return new JArray(
            Event(0, "GameStart", 0.02),
            Event(1, "ChampionKill", 100, Kill("A1", "B1", "A2")),
            Event(2, "ChampionKill", 104, Kill("A2", "B2")),
            Event(3, "Multikill", 104, new JObject { ["KillerName"] = "A1", ["KillStreak"] = 2 }),
            Event(4, "Multikill", 105, new JObject { ["KillerName"] = "A1", ["KillStreak"] = 3 }),
            Event(5, "Ace", 105, new JObject { ["Acer"] = "A2", ["AcingTeam"] = "ORDER" }),
            Event(6, "ChampionKill", 109, Kill("B1", "A2")),
            Event(7, "ChampionKill", 300, Kill("A1", "B2")),
            Event(8, "GameEnd", 600, new JObject { ["Result"] = "Win" })
        );
    }
}
