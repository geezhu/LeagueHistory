using System.Drawing;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

/// <summary>
/// Ally summoner spell timers: 2999's spell names, Data Dragon's cooldowns read from the cache, summoner haste, the
/// widget's timer file, the ready alert once per start, and a snapshot where only allies carry a cooldown. No audio
/// is played and nothing is downloaded.
/// </summary>
internal static class SummonerTimerChecks
{
    private static readonly SummonerSpellInfo Flash = new("SummonerFlash", "闪现", 300, "SummonerFlash.png");
    private static readonly SummonerSpellInfo Mark = new("SummonerSnowball", "雪球", 80, "Summoner_Mark.png");

    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new Exception(message);
        }
    }

    public static void Run()
    {
        CheckSpells();
        CheckTimerFile();
        CheckRefusedTimerFiles();
        CheckAlerts();
        Task.Run(CacheAsync).GetAwaiter().GetResult();
        Task.Run(BridgeAsync).GetAwaiter().GetResult();
        Console.WriteLine(
            "PASS: ally summoner timers, cooldowns with haste and the enemy-free snapshot; no audio played."
        );
    }

    private static void CheckSpells()
    {
        var snapshot = LiveMatchSnapshot.Parse(
            JObject.Parse(
                """
                {
                  "gameData": { "gameMode": "ARAM", "mapNumber": 12, "gameTime": 100 },
                  "allPlayers": [
                    {
                      "riotId": "A#1",
                      "team": "ORDER",
                      "summonerSpells": {
                        "summonerSpellOne": { "rawDisplayName": "GeneratedTip_SummonerSpell_SummonerFlash_DisplayName" },
                        "summonerSpellTwo": { "rawDisplayName": "GeneratedTip_SummonerSpell_SummonerSnowball_DisplayName" }
                      }
                    },
                    { "riotId": "B#2", "team": "CHAOS" }
                  ]
                }
                """
            )
        );
        Check(
            snapshot.Players[0].SummonerSpells.SequenceEqual(["SummonerFlash", "SummonerSnowball"])
                && snapshot.Players[1].SummonerSpells.SequenceEqual(["", ""])
                && SummonerSpells.InternalName("SummonerFlash") == "",
            "2999's display-name keys give the internal names; a missing spell is empty"
        );
        LiveMatchItem Item(int id) => new(id, "", 1);
        Check(
            SummonerSpells.Haste([Item(3158)], []) == 10
                && SummonerSpells.Haste([Item(223158), Item(3171)], [8347]) == 48
                && SummonerSpells.Haste([Item(3020)], [8000]) == 0,
            "Ionian Boots, Crimson Lucidity (mode copies included) and Cosmic Insight add summoner haste"
        );
        Check(
            Math.Abs(SummonerSpells.Cooldown(Flash, 18) - 300 / 1.18) < 1e-9
                && SummonerSpells.Cooldown(Mark, 0) == 80
                && SummonerSpells.Cooldown(Flash, -5) == 300,
            "Summoner haste shortens the spell's own cooldown"
        );
        var spells = SummonerSpellData.Parse(
            JObject.Parse(
                """
                {
                  "data": {
                    "SummonerFlash": { "name": "闪现", "cooldown": [300], "image": { "full": "SummonerFlash.png" } },
                    "SummonerSnowball": { "name": "雪球", "cooldown": [80], "image": { "full": "Summoner_Mark.png" } },
                    "SummonerBroken": { "name": "坏数据", "cooldown": ["x"] }
                  }
                }
                """
            )
        );
        Check(
            spells.Count == 2 && spells["SummonerFlash"] == Flash && spells["SummonerSnowball"] == Mark,
            "Data Dragon's list gives each spell's name, cooldown and icon; a malformed entry is skipped"
        );
        Check(
            ControlSettings.Parse(JObject.Parse("""{"protocol":1,"survivalSound":true}"""))?.SummonerSound == true
                && ControlSettings
                    .Parse(JObject.Parse("""{"protocol":1,"survivalSound":true,"summonerSound":false}"""))
                    ?.SummonerSound == false
                && ControlSettings.Parse(JObject.Parse("""{"protocol":1,"survivalSound":true,"summonerSound":1}"""))
                    == null,
            "The ready chime is on unless the widget turns it off; a malformed setting is refused"
        );
    }

    private static JObject TimerFile(string session, params object[] timers) =>
        new()
        {
            ["protocol"] = 1,
            ["session"] = session,
            ["timers"] = JArray.FromObject(timers),
        };

    private static void CheckTimerFile()
    {
        Check(
            SummonerSpells.ParseTimers(
                TimerFile(
                    "s",
                    new
                    {
                        player = "a",
                        slot = 0,
                        time = 61.5,
                    },
                    new
                    {
                        player = "a",
                        slot = 1,
                        time = 90,
                    }
                ),
                "s"
            )
                is [{ Player: "a", Slot: 0, Time: 61.5 }, { Player: "a", Slot: 1, Time: 90 }],
            "The widget's timers for this session are read"
        );
    }

    private static void CheckRefusedTimerFiles()
    {
        Check(
            SummonerSpells.ParseTimers(
                TimerFile(
                    "old",
                    new
                    {
                        player = "a",
                        slot = 0,
                        time = 1,
                    }
                ),
                "s"
            ) == null
                && SummonerSpells.ParseTimers(
                    TimerFile(
                        "s",
                        new
                        {
                            player = "a",
                            slot = 2,
                            time = 1,
                        }
                    ),
                    "s"
                ) == null
                && SummonerSpells.ParseTimers(
                    TimerFile(
                        "s",
                        new
                        {
                            player = "a",
                            slot = 0,
                            time = -1,
                        }
                    ),
                    "s"
                ) == null
                && SummonerSpells.ParseTimers(
                    TimerFile(
                        "s",
                        new
                        {
                            player = "a",
                            slot = 0,
                            time = 1,
                        },
                        new
                        {
                            player = "a",
                            slot = 0,
                            time = 2,
                        }
                    ),
                    "s"
                ) == null
                && SummonerSpells.ParseTimers(
                    TimerFile(
                        "",
                        new
                        {
                            player = "a",
                            slot = 0,
                            time = 1,
                        }
                    ),
                    ""
                ) == null,
            "Another session's file, a third slot, negative times and duplicates are refused"
        );
    }

    private static void CheckAlerts()
    {
        var alerts = new SummonerTimerAlerts();
        SummonerTimerState Timer(string key, double? started) => new(key, new(0, Flash, 300, started));
        Check(
            alerts.Update([Timer("old", 0), Timer("a", 100)], 350).Length == 0,
            "A timer already back when first seen, as after a restart, is quiet"
        );
        Check(alerts.Update([Timer("old", 0), Timer("a", 100)], 399).Length == 0, "A running timer is quiet");
        Check(
            alerts.Update([Timer("old", 0), Timer("a", 100)], 400.2) is [{ Key: "a" }]
                && alerts.Update([Timer("old", 0), Timer("a", 100)], 401).Length == 0,
            "A timer coming back chimes once"
        );
        Check(
            alerts.Update([Timer("a", 500)], 600).Length == 0
                && alerts.Update([Timer("a", 500)], 800) is [{ Key: "a" }],
            "A restarted timer chimes again when it comes back"
        );
        Check(
            alerts.Update([Timer("a", null), new("enemy", new(0, Flash, 0, null))], 900).Length == 0,
            "Cancelled timers and enemies' untimed spells never chime"
        );
    }

    // Both the list and the icons are read from the version's cache folder, so no request is made.
    private static async Task CacheAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "league-summoners-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "16.20.1"));
        try
        {
            await System.IO.File.WriteAllTextAsync(
                Path.Combine(folder, "16.20.1", "summoner.json"),
                """{"data":{"SummonerFlash":{"name":"闪现","cooldown":[300],"image":{"full":"SummonerFlash.png"}}}}"""
            );
            byte[] png = [0x89, 0x50, 0x4e, 0x47];
            await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, "16.20.1", "SummonerFlash.png"), png);
            var data = new SummonerSpellData(folder, "16.20.1");
            var spells = await data.LoadAsync(CancellationToken.None);
            Check(
                spells["SummonerFlash"] == Flash
                    && await data.IconAsync(Flash, CancellationToken.None) == Convert.ToBase64String(png)
                    && await data.IconAsync(Flash with { Image = "../x.png" }, CancellationToken.None) == "",
                "The cached list and icons are read without the network; an icon name with a path is refused"
            );
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static OverlayLiveState SampleLive()
    {
        var insight = new LiveMatchInsight(
            120,
            new(0, 0, 0, 0, 0),
            new(0, 0, 0, 0, 0),
            new Dictionary<string, LivePlayerInsight>
            {
                ["self"] = new(0, 0, 0, 0, null, 0),
                ["enemy"] = new(0, 0, 0, 0, null, 0),
            },
            [],
            ""
        );
        return new OverlayLiveState(insight, new JArray(), "rev-1")
        {
            GameTime = 120,
            GameTimeAt = 1_000,
            Summoners = new Dictionary<string, PlayerSummoner[]>
            {
                ["self"] = [new(0, Flash, 254.2, 100), new(1, Mark, 80, null)],
                ["enemy"] = [new(0, Flash, 0, null)],
            },
            SummonerIcons = new Dictionary<string, string>
            {
                ["SummonerFlash"] = "Zmxhc2g=",
                ["SummonerHeal"] = "aGVhbA==",
            },
        };
    }

    private static void CheckSnapshot(JObject snapshot)
    {
        var players = (JArray)snapshot["players"]!;
        var ally = players.OfType<JObject>().First(p => (string?)p["key"] == "self")["summoners"]!;
        var enemy = (JObject)players.OfType<JObject>().First(p => (string?)p["key"] == "enemy")["summoners"]![0]!;
        Check(
            (bool?)ally[0]!["timed"] == true
                && (double?)ally[0]!["cooldown"] == 254.2
                && (double?)ally[0]!["started"] == 100
                && ally[1]!["started"]!.Type == JTokenType.Null
                && (string?)ally[1]!["spell"] == "SummonerSnowball",
            "An ally's spells carry the cooldown after haste and the start: " + ally
        );
        Check(
            (bool?)enemy["timed"] == false
                && (string?)enemy["name"] == "闪现"
                && enemy["cooldown"] == null
                && enemy["started"] == null,
            "An enemy's spell carries no cooldown or start: " + enemy
        );
        Check(
            ((JObject)snapshot["summonerIcons"]!).Properties().Select(p => p.Name).SequenceEqual(["SummonerFlash"])
                && (double?)snapshot["gameClock"]!["gameTime"] == 120
                && (long?)snapshot["gameClock"]!["clock"] == 1_000,
            "Icons go once for the spells on the cards, with the game clock for the countdowns"
        );
    }

    // Allies carry their cooldown and start; enemies only the spell, so the widget cannot time them.
    private static async Task BridgeAsync()
    {
        var roster = new LoadingRoster(
            "1",
            450,
            "ARAM",
            [new("self", "自己#1", "", 1, 143, 100, true), new("enemy", "敌人#2", "", 2, 22, 200, false)]
        );
        var live = SampleLive();
        string folder = Path.Combine(Path.GetTempPath(), "league-summoner-bridge-" + Guid.NewGuid().ToString("N"));
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
                await BridgeCheckFiles.ReadAsync(Path.Combine(folder, "snapshot.json"), "Summoner snapshot is written")
            );
            CheckSnapshot(snapshot);
            await System.IO.File.WriteAllTextAsync(
                Path.Combine(folder, "summoner-timers.json"),
                new JObject
                {
                    ["protocol"] = 1,
                    ["session"] = (string)snapshot["session"]!,
                    ["timers"] = new JArray(
                        new JObject
                        {
                            ["player"] = "self",
                            ["slot"] = 1,
                            ["time"] = 110,
                        }
                    ),
                }.ToString()
            );
            Check(
                await bridge.ReadSummonerTimersAsync() is [{ Player: "self", Slot: 1, Time: 110 }],
                "The widget's timers for this session are read back"
            );
        }
        finally
        {
            bridge.Dispose();
            await bridge.Completion;
            Directory.Delete(folder, true);
        }
    }
}
