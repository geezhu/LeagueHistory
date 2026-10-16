using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

/// <summary>
/// The control and survival page: crowd control recognized from ability text (wording taken from the client's
/// champion data), survival items from the scoreboard inventory, and the snapshot fields the widget reads.
/// </summary>
internal static class ControlChecks
{
    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new Exception(message);
        }
    }

    public static void Run()
    {
        Recognition();
        Parsing();
        Survival();
        Slows();
        ChatMessage();
        ChatMessageSplits();
        ChatRequest();
        Task.Run(SpellCacheAsync).GetAwaiter().GetResult();
        Task.Run(BridgeAsync).GetAwaiter().GetResult();
        Console.WriteLine(
            "PASS: control recognition (synonyms, self effects, removal, passive triggers), ranged strong slows, survival items, Game Bar fields and the control chat message."
        );
    }

    private static string[] Effects(string text, bool passive = false) => ChampionControlSkills.Effects(text, passive);

    private static void Recognition()
    {
        Check(
            Effects("蕾欧娜召唤一道太阳光束，对区域内的敌人造成伤害。区域中心的敌人会被晕眩，并且外围的敌人会被减速。")
                .SequenceEqual(["晕眩"]),
            "Stuns count; slows do not"
        );
        Check(
            Effects("锤石投出他的镰刀，晕眩命中的第一个单位并将其拉拽向锤石").SequenceEqual(["晕眩", "拉拽"]),
            "A hook stuns and pulls"
        );
        Check(
            Effects("当用在一名敌人身上时，璐璐会将其变形秒。").SequenceEqual(["变形"]),
            "Polymorphing an enemy counts"
        );
        Check(
            Effects("希瓦娜变形成一头魔龙并向前跳跃来使途经的敌人们因恐惧而逃跑").SequenceEqual(["恐惧"]),
            "A champion transforming itself is not a polymorph"
        );
        Check(
            Effects("塞拉斯可以再次施放，将他自身拉向命中的第一个敌人。").Length == 0,
            "Pulling oneself is not a pull"
        );
        Check(
            Effects("阿利斯塔发出野性的咆哮，移除身上所有控制效果，并且持续时间内所受伤害将减少。").Length == 0
                && Effects("在持续期间免疫晕眩和击飞").Length == 0,
            "Removing or ignoring control applies none"
        );
        Check(
            Effects("阿利斯塔在对敌方英雄造成晕眩或强制位移时，会积攒他的怒吼层数。", passive: true).Length == 0
                && Effects("安妮的下一次伤害类技能就会对目标造成短暂的晕眩效果。", passive: true)
                    .SequenceEqual(["晕眩"]),
            "A passive's trigger is not an effect; its own stun is"
        );
    }

    private static void Parsing()
    {
        var champion = new JObject
        {
            ["passive"] = new JObject
            {
                ["name"] = "荆棘花园",
                ["description"] = "婕拉可通过施放<b>缠绕之根</b>来让种子长成植物。",
            },
            ["spells"] = new JArray(
                new JObject
                {
                    ["spellKey"] = "q",
                    ["name"] = "致命棘刺",
                    ["description"] = "婕拉造成伤害。",
                    ["dynamicDescription"] = "造成@Damage@魔法伤害。",
                },
                new JObject
                {
                    ["spellKey"] = "e",
                    ["name"] = "缠绕之根",
                    ["description"] = "婕拉放出藤蔓，伤害并禁锢沿途的敌人。",
                    ["dynamicDescription"] = "藤蔓禁锢敌人@RootDuration@秒。",
                }
            ),
        };
        var skills = ChampionControlSkills.Parse(champion);
        Check(
            skills.Length == 1
                && skills[0] is { Key: "E", Name: "缠绕之根" }
                && skills[0].Effects.SequenceEqual(["禁锢"])
                && skills[0].Description == "婕拉放出藤蔓，伤害并禁锢沿途的敌人。",
            "Ability names inside descriptions are not effects; keys are upper case: "
                + string.Join(",", skills.Select(s => s.Key + string.Join("/", s.Effects)))
        );
    }

    // Ranks 1-5 of the game's DataValues: ratios and percents, sub-spells, self and attack-speed slows, reach.
    private static JObject SlowBin()
    {
        JObject Spell(double range, params (string Name, double[] Values)[] values) =>
            new()
            {
                ["mSpell"] = new JObject
                {
                    ["castRange"] = new JArray(0, range, range, range, range, range, range),
                    ["DataValues"] = new JArray(
                        values.Select(v => new JObject { ["name"] = v.Name, ["values"] = new JArray(v.Values) })
                    ),
                },
            };
        var bin = new JObject
        {
            ["Characters/Test/CharacterRecords/Root"] = new JObject
            {
                ["spellNames"] = new JArray("TestQ", "TestW", "TestE", "TestR"),
            },
            ["Characters/Test/Spells/TestQAbility/TestQ"] = Spell(
                900,
                ("SlowAmount", [0, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7]),
                ("AttackSlowPercent", [0, 90, 90, 90, 90, 90, 90]),
                ("SlowDuration", [0, 1, 1.5, 2, 2, 2, 2.5]),
                ("SlowDurationMultiplier", [0, 9, 9, 9, 9, 9, 9]),
                ("Duration", [0, 8, 8, 8, 8, 8, 8])
            ),
            ["Characters/Test/Spells/TestWAbility/TestW"] = Spell(
                0,
                ("SlowPercent", [0, 50, 55, 60, 65, 70, 75]),
                ("CloudDuration", [0, 4, 4, 4, 4, 4, 4])
            ),
            ["Characters/Test/Spells/TestEAbility/TestE"] = Spell(1000, ("SlowAmount", [0, 80, 80, 80, 80, 80, 80])),
            ["Characters/Test/Spells/TestRAbility/TestR"] = Spell(
                1200,
                ("SelfSlow", [0, 30, 30, 30, 30, 30, 30]),
                ("WallDuration", [0, 3, 3, 3, 3, 3, 3]),
                ("RecastDuration", [0, 60, 60, 60, 60, 60, 60])
            ),
            ["Characters/Test/Spells/TestRAbility/TestRMissile"] = Spell(
                25000,
                ("MoveSpeedMod", [0, -0.5, -0.6, -0.7, -0.9, -0.9, -0.9]),
                ("DebuffDuration", [0, 1, 1.5, 2, 2, 2, 2])
            ),
        };
        // Q states six ranks in its tooltip data; R states none, so an ultimate's three ranks apply.
        bin["Characters/Test/Spells/TestQAbility/TestQ"]!["mSpell"]!["mClientData"] = JObject.Parse(
            "{\"mTooltipData\":{\"mLists\":{\"LevelUp\":{\"levelCount\":6}}}}"
        );
        return bin;
    }

    private static void Slows()
    {
        JObject Ability(string key, string text) =>
            new()
            {
                ["spellKey"] = key,
                ["name"] = key + " 技能",
                ["description"] = text,
            };
        var champion = new JObject
        {
            ["alias"] = "Test",
            ["spells"] = new JArray(
                Ability("q", "命中敌人并造成减速。"),
                Ability("w", "对周围敌人造成减速。"),
                Ability("e", "造成伤害。"),
                Ability("r", "释放锁链，减速敌人。")
            ),
        };
        var slows = ChampionSlowSkills.Parse(SlowBin(), champion);
        Check(
            slows.Select(s => $"{s.Key} {s.Percent} {s.Range}").SequenceEqual(["Q 70 900", "W 70 0", "R 70 1200"]),
            "Top-rank slows within each ability's ranks and its own reach; attack-speed and self slows do not count: "
                + string.Join(",", slows.Select(s => $"{s.Key} {s.Percent} {s.Range}"))
        );

        // Q's own slow duration wins over its general duration and a multiplier; W has only a zone; R's sub-spell
        // debuff is the slow, its outlasting wall is kept apart and a minute-long recast window is no slow.
        string Timing(SlowSkill s) => $"{s.Key} {s.Duration} {s.Zone}";
        Check(
            slows.Select(Timing).SequenceEqual(["Q 2.5 0", "W 4 0", "R 2 3"]),
            "Slow durations at the top rank: " + string.Join(",", slows.Select(Timing))
        );
        var merged = ChampionSlowSkills.Merge([new("Q", "Q 技能", ["晕眩"], "")], slows, champion);
        Check(
            merged
                .Select(s => $"{s.Key}:{string.Join("/", s.Effects)}:{s.Slow}:{s.SlowDuration}:{s.SlowZone}")
                .SequenceEqual(["Q:晕眩:70:2.5:0", "R::70:2:3"]),
            "Ranged strong slows join the control skills; a self-centered slow waits: "
                + string.Join(",", merged.Select(s => $"{s.Key}:{s.Slow}"))
        );
        SlowLevels(slows, merged[1]);
    }

    // Q has six ranks of 20-70% lasting 1-2.5 s; R has three of 50-70% lasting 1-2 s beside a 3-second wall.
    private static void SlowLevels(SlowSkill[] slows, ControlSkill ultimate)
    {
        Check(
            slows[0]
                .Ranks.Select(r => $"{r.Percent}/{r.Duration}")
                .SequenceEqual(["20/1", "30/1.5", "40/2", "50/2", "60/2", "70/2.5"])
                && slows[2]
                    .Ranks.Select(r => $"{r.Percent}/{r.Duration}/{r.Zone}")
                    .SequenceEqual(["50/1/3", "60/1.5/3", "70/2/3"]),
            "Each rank keeps its own slow and durations: " + string.Join(",", slows[2].Ranks.Select(r => r.ToString()))
        );
        (string Key, int Ranks, int Level, int Expected)[] caps =
        [
            ("Q", 5, 1, 1),
            ("Q", 5, 3, 2),
            ("Q", 5, 8, 4),
            ("Q", 5, 18, 5),
            ("Q", 6, 11, 6),
            ("R", 3, 5, 0),
            ("R", 3, 6, 1),
            ("R", 3, 15, 2),
            ("R", 3, 16, 3),
            ("R", 4, 1, 1),
            ("R", 4, 16, 4),
        ];
        var wrong = caps.Where(c => ChampionSlowSkills.RankAt(c.Key, c.Ranks, c.Level) != c.Expected).ToArray();
        Check(
            wrong.Length == 0,
            "Basic abilities gain a rank every two levels, ultimates at 6/11/16 (or from 1 with four ranks): "
                + string.Join(",", wrong)
        );

        string At(int level) =>
            ChampionSlowSkills.AtLevel(ultimate, level) is { } at ? $"{at.Slow.Percent} {at.Basis}" : "";
        Check(
            At(12) == "60 12 级时该技能最高 2/3 级"
                && At(4) == "50 4 级还不能学（6 级可学），按 1 级显示"
                && At(0) == "70 等级未知，按满级 3/3 级"
                && ChampionSlowSkills.AtLevel(ultimate with { Slow = null }, 12) == null,
            "The slow follows the highest rank the level allows: " + At(12) + "; " + At(4) + "; " + At(0)
        );
    }

    // A cached patch answers without a request; the folder is a temporary one.
    private static async Task SpellCacheAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "league-spells-" + Guid.NewGuid().ToString("N"));
        string cached = Path.Combine(folder, "16.20-r" + ChampionSpellData.RulesVersion);
        Directory.CreateDirectory(cached);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(cached, "nasus.json"),
                "{\"Slows\":[{\"Key\":\"W\",\"Percent\":95,\"Range\":700,\"Duration\":5}],"
                    + "\"Cooldowns\":[{\"Key\":\"W\",\"Name\":\"枯萎\",\"Cooldowns\":[14,13,12,11,10]}]}"
            );
            var facts = await new ChampionSpellData(folder, "16.20").LoadAsync(
                new JObject { ["alias"] = "Nasus" },
                CancellationToken.None
            );
            Check(
                facts.Slows.Single() == new SlowSkill("W", 95, 700) { Duration = 5 }
                    && facts.Cooldowns.Single() is { Key: "W", Cooldowns: [14, 13, 12, 11, 10], Recharge: [] },
                "Cached slows and cooldowns are read for the client's patch"
            );
            Check(
                await new ChampionSpellData(folder, "16.20").LoadAsync(new JObject { ["alias"] = "../x" }, default)
                    == ChampionSpellFacts.None,
                "An alias is never used as a path outside the cache"
            );
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static void Survival()
    {
        var items = SurvivalItems.Find([
            new LiveMatchItem(223157, "中娅沙漏", 1),
            new LiveMatchItem(3157, "中娅沙漏", 1),
            new LiveMatchItem(3111, "水银之靴", 1),
            new LiveMatchItem(3026, "守护天使", 1),
        ]);
        Check(
            items.Select(i => i.Name + ":" + i.Effect).SequenceEqual(["中娅沙漏:凝滞", "守护天使:复活"]),
            "Mode copies count once and boots are not survival items: "
                + string.Join(",", items.Select(i => i.Name + ":" + i.Effect))
        );
        Check(SurvivalItems.Find(null).Length == 0, "No inventory, no survival items");
    }

    // The enemy's E slows 25-45% over five ranks; at level 5 the third rank is the most it can have.
    private static ControlSkill[] EnemyControls() =>
        [
            new("Q", "光之束缚", ["禁锢"], "束缚敌人。"),
            new("E", "透光奇点", [], "")
            {
                Slow = 45,
                Range = 1100,
                SlowDuration = 1.5,
                SlowRanks = [new(25, 1, 0), new(30, 1, 0), new(35, 1.25, 4), new(40, 1.5, 4), new(45, 1.5, 4)],
            },
        ];

    private static readonly LoadingRoster ChatRoster = new(
        "1",
        450,
        "ARAM",
        [
            new("self", "自己#1", "", 1, 143, 100, true),
            new("ally", "队友#2", "", 2, 1, 100, false),
            new("enemy", "对手#3", "", 3, 99, 200, false),
            new("enemy2", "对手#4", "", 4, 7, 200, false),
        ]
    );

    private static string Hero(int id) => "英雄" + id;

    private static OverlayChatRequest ControlRequest(string target) =>
        new("id", "1", target, -1, 0, 0, false, false, "controls");

    // One side's champions with their control abilities; a sealed ultimate is left out as on the page.
    private static void ChatMessage()
    {
        var roster = ChatRoster;
        var controls = new Dictionary<int, ControlSkill[]>
        {
            [99] = EnemyControls(),
            [143] = [new("Q", "缠绕", ["禁锢"], ""), new("R", "大招", ["击飞"], "")],
            [7] = [],
        };
        Check(
            OverlayControlChatMessage.Build(roster, ControlRequest("enemy"), controls, null, Hero).Single()
                == "【控制·对方】英雄99 Q禁锢 E减速；英雄7 无硬控",
            "Enemy controls list each champion with hard control and slows"
        );

        var sealedR = new OverlayLiveState(
            new LiveMatchInsight(
                0,
                new(0, 0, 0, 0, 0),
                new(0, 0, 0, 0, 0),
                new Dictionary<string, LivePlayerInsight>(),
                [],
                ""
            ),
            new JArray(),
            "rev"
        )
        {
            Effects = new Dictionary<string, AugmentEffects>
            {
                ["self"] = AugmentEffects.None with { Cooldown = CooldownOptions.None with { UltimateSealed = true } },
            },
        };
        Check(
            OverlayControlChatMessage.Build(roster, ControlRequest("own"), controls, sealedR, Hero).Single()
                == "【控制·我方】英雄143 Q禁锢；英雄1 读取中",
            "A sealed ultimate is skipped and unread champions say so"
        );
        Check(
            Rejected(() => OverlayControlChatMessage.Build(roster, ControlRequest("single"), controls, null, Hero))
                && Rejected(() =>
                    OverlayControlChatMessage.Build(
                        roster,
                        ControlRequest("enemy"),
                        new Dictionary<int, ControlSkill[]>(),
                        null,
                        Hero
                    )
                ),
            "Nothing is sent for a single hero or before any control skills are read"
        );
    }

    // Five champions with many effects do not fit one chat message.
    private static void ChatMessageSplits()
    {
        var roster = ChatRoster;
        var many = Enumerable
            .Range(0, 5)
            .Select(i => new ControlSkill(
                "Q",
                "",
                ["击飞", "晕眩", "禁锢", "拉拽", "沉默", "压制", "嘲讽", "恐惧", "魅惑", "沉睡"],
                ""
            ))
            .ToArray();
        var full = roster with
        {
            Players = Enumerable
                .Range(0, 5)
                .Select(i => new LoadingPlayer("e" + i, "", "", i, 10 + i, 200, false))
                .Append(roster.Players[0])
                .ToArray(),
        };
        var messages = OverlayControlChatMessage.Build(
            full,
            ControlRequest("enemy"),
            Enumerable.Range(10, 5).ToDictionary(id => id, _ => many),
            null,
            Hero
        );
        Check(
            messages.Count > 1 && messages.All(m => m.Length <= 180 && m.StartsWith("【控制·对方】")),
            "Long lists continue in further messages"
        );
    }

    private static bool Rejected(Func<IReadOnlyList<string>> build)
    {
        try
        {
            build();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    // The widget sends a side; a single hero is refused like the premade message.
    private static void ChatRequest()
    {
        var json = JObject.FromObject(
            new
            {
                protocol = 1,
                id = Guid.NewGuid().ToString(),
                session = "s",
                target = "enemy",
                kind = "controls",
                slot = -1,
                championId = 0,
                updatedAt = 1000,
                dismissed = false,
                cancelled = false,
            }
        );
        Check(
            OverlayChatRequest.Parse(json, "s", new int[10], 2000)?.Kind == "controls",
            "The widget's request parses"
        );
        json["target"] = "single";
        json["slot"] = 0;
        json["championId"] = 5;
        Check(
            OverlayChatRequest.Parse(json, "s", [5, 0, 0, 0, 0, 0, 0, 0, 0, 0], 2000) == null,
            "A single hero is refused"
        );
    }

    private static async Task BridgeAsync()
    {
        var roster = new LoadingRoster(
            "1",
            2400,
            "KIWI",
            [new("self", "自己#1", "", 1, 143, 100, true), new("enemy", "对手#2", "", 2, 99, 200, false)]
        );
        var insight = new LiveMatchInsight(
            60,
            new(0, 0, 0, 0, 0),
            new(0, 0, 0, 0, 0),
            new Dictionary<string, LivePlayerInsight>
            {
                ["enemy"] = new(3250, 0, 0, 0, null, 0) { Items = [new(3157, "中娅沙漏", 1)], Level = 5 },
            },
            [],
            ""
        );
        string folder = Path.Combine(Path.GetTempPath(), "league-control-" + Guid.NewGuid().ToString("N"));
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
                live: new(insight, new JArray(), "rev-1"),
                controls: new Dictionary<int, ControlSkill[]> { [99] = EnemyControls() }
            );
            var snapshot = JObject.Parse(
                await BridgeCheckFiles.ReadAsync(Path.Combine(folder, "snapshot.json"), "Control snapshot is written")
            );
            var players = snapshot["players"]!.OfType<JObject>().ToArray();
            var enemy = players.Single(p => (int?)p["championId"] == 99);
            var self = players.Single(p => (int?)p["championId"] == 143);
            Check(
                (int?)snapshot["controlProtocol"] == 1
                    && (string?)enemy["controls"]![0]!["effects"] == "禁锢"
                    && enemy["controls"]![0]!["range"] == null
                    && (string?)enemy["controls"]![1]!["effects"] == "减速35% 1.25秒"
                    && (int?)enemy["controls"]![1]!["slow"] == 35
                    && (int?)enemy["controls"]![1]!["range"] == 1100
                    && (double?)enemy["controls"]![1]!["slowDuration"] == 1.25
                    && (double?)enemy["controls"]![1]!["slowZone"] == 4
                    && (string?)enemy["controls"]![1]!["slowNote"]
                        == "5 级时该技能最高 3/5 级：减速持续 1.25秒，区域持续 4秒，停留其中可能再次减速"
                    && (string?)enemy["survival"]![0]!["effect"] == "凝滞",
                "The snapshot carries control skills and survival items: " + enemy
            );
            Check(
                self["controls"] == null && self["survival"] == null,
                "Unloaded champion data and missing live rows are left out, so the widget shows them as pending"
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
