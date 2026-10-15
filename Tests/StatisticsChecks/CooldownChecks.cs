using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

/// <summary>
/// Ability cooldowns from the game's champion data and the active player's cooldowns after ability haste on the live
/// tab. The fixture covers a six-rank ability, a spell the extraction left at the default, charges and a script name
/// in another case.
/// </summary>
internal static class CooldownChecks
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
        var cooldowns = ChampionCooldowns.Parse(Bin(), Champion());
        string Text(SpellCooldown c) =>
            $"{c.Key} {c.Name} {string.Join("/", c.Cooldowns)} {string.Join("/", c.Recharge)} {string.Join("/", c.Charges)}";
        Check(
            cooldowns
                .Select(Text)
                .SequenceEqual([
                    "Q 光之束缚 9/8/7/6/5/4  ",
                    "W 棱光屏障 10/10/10/10/10  ",
                    "E 透光奇点 0.4/0.4/0.4/0.4/0.4 18/17/16/15/14 2/2/2/3/3",
                    "R 终极闪光 80/60/40  ",
                ]),
            "Cooldowns per rank, the default for a missing one, charges and a lower-case script name: "
                + string.Join(" | ", cooldowns.Select(Text))
        );

        var ranks = new Dictionary<string, int>
        {
            ["Q"] = 3,
            ["E"] = 4,
            ["R"] = 1,
        };
        var now = AbilityCooldowns.Describe(cooldowns, ranks, 50);
        Check(
            now[0] is { Rank: 3, Ranks: 6, Base: 7 }
                && Math.Abs(now[0].Current - 7 / 1.5) < 1e-9
                && now[1] is { Rank: 0, Current: 0 }
                && now[2] is { Base: 15, Charges: 3 }
                && Math.Abs(now[3].Current - 80 / 1.5) < 1e-9
                && AbilityCooldowns.Hasted(10, -20) == 10,
            "Haste shortens to 100 / (100 + haste); a charge ability's recharge is its cooldown"
        );

        var active = LiveActivePlayer.Parse(
            JObject.Parse(
                """{"abilities":{"Passive":{},"Q":{"abilityLevel":3},"W":{"abilityLevel":"x"},"E":{"abilityLevel":4}}}"""
            )
        )!;
        Check(
            active.AbilityRanks.Count == 2 && active.AbilityRanks["Q"] == 3 && active.AbilityRanks["E"] == 4,
            "2999's ability levels are read for Q, W, E and R; malformed ones are left out"
        );
        CheckSection(now);
        HasteAndCoverage(cooldowns);
        Task.Run(() => BridgeAsync(cooldowns)).GetAwaiter().GetResult();
        Console.WriteLine(
            "PASS: cooldowns per rank, defaults, charges, ability haste and the live tab's cooldown cards."
        );
    }

    // Others' haste comes from items and added augments; coverage is a slow's duration over the hasted cooldown.
    private static void HasteAndCoverage(SpellCooldown[] cooldowns)
    {
        var recursion = new LiveAugmentStack(
            new("enemy", TrackedAugment.InfiniteRecursion, new(0, 0)),
            new(3, 3, 0, true, "")
        );
        var effects = AugmentEffects.From([recursion], 0);
        var estimate = PlayerHaste.Describe(new ItemStats(0, 0, 0, 15), effects, null);
        var real = PlayerHaste.Describe(ItemStats.None, effects, new(2000, 50, 40, 35, 0, 0, 0, 0));
        Check(
            estimate is { Value: 84, Estimated: true }
                && estimate.Details.Contains("装备 15 + 海克斯 69")
                && real is { Value: 35, Estimated: false },
            "Haste is 2999's for the player and items plus augments for others: " + estimate.Details
        );

        var slow = new SlowAtLevel(new(40, 2, 0), 3, "");
        var coverage = SlowCoverage.Estimate(slow, cooldowns[0], estimate);
        var charged = SlowCoverage.Estimate(slow with { Rank = 5 }, cooldowns[2], null);
        Check(
            coverage != null
                && Math.Abs(coverage.Cooldown - 7 * 100 / 184.0) < 1e-9
                && Math.Abs(coverage.Share - 2 / (7 * 100 / 184.0)) < 1e-9
                && coverage.Note.Contains("估算技能急速 84")
                && charged is { Cooldown: 14, Note: var note }
                && note.StartsWith("每层充能约 14 秒")
                && SlowCoverage.Estimate(slow, null, estimate) == null
                && SlowCoverage.Estimate(slow with { Slow = new(40, 0, 0) }, cooldowns[0], estimate) == null,
            "Coverage uses the rank's cooldown or a charge's return, after haste: " + coverage?.Note
        );
    }

    // Both players at level 7; the enemy's haste is an estimate and the player's ranks come from 2999.
    private static OverlayLiveState BridgeLive()
    {
        var insight = new LiveMatchInsight(
            60,
            new(0, 0, 0, 0, 0),
            new(0, 0, 0, 0, 0),
            new Dictionary<string, LivePlayerInsight>
            {
                ["self"] = new(3000, 0, 0, 0, null, 0) { Level = 7 },
                ["enemy"] = new(3000, 0, 0, 0, null, 0) { Level = 7 },
            },
            [],
            ""
        );
        return new OverlayLiveState(insight, new JArray(), "rev-1")
        {
            Haste = new Dictionary<string, PlayerHaste> { ["enemy"] = new(25, true, "估算") },
            AbilityRanks = new Dictionary<string, int> { ["Q"] = 2, ["E"] = 1 },
        };
    }

    // The snapshot carries the calculator's starting point and the slow's coverage for every player.
    private static async Task BridgeAsync(SpellCooldown[] cooldowns)
    {
        var roster = new LoadingRoster(
            "1",
            2400,
            "KIWI",
            [new("self", "自己#1", "", 1, 99, 100, true), new("enemy", "对手#2", "", 2, 99, 200, false)]
        );
        var live = BridgeLive();
        var slowQ = new ControlSkill("Q", "光之束缚", [], "")
        {
            Slow = 40,
            Range = 900,
            SlowRanks = [new(20, 2, 0), new(25, 2, 0), new(30, 2, 0), new(35, 2, 0), new(40, 2, 0), new(45, 2, 0)],
        };
        string folder = Path.Combine(Path.GetTempPath(), "league-cooldowns-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var bridge = new GameBarBridge(folder);
        try
        {
            bridge.Publish(
                roster,
                new Dictionary<string, PlayerChampionTier>(),
                new Dictionary<int, System.Drawing.Image>(),
                id => "英雄 " + id,
                _ => "",
                "match-1",
                "当前对局",
                true,
                true,
                true,
                live: live,
                controls: new Dictionary<int, ControlSkill[]> { [99] = [slowQ] },
                cooldowns: new Dictionary<int, SpellCooldown[]> { [99] = cooldowns }
            );
            var snapshot = JObject.Parse(
                await BridgeCheckFiles.ReadAsync(Path.Combine(folder, "snapshot.json"), "Cooldown snapshot is written")
            );
            var players = snapshot["players"]!.OfType<JObject>().ToArray();
            var self = players.Single(p => (bool?)p["self"] == true);
            var enemy = players.Single(p => (string?)p["key"] == "enemy");
            string Ranks(JObject row) =>
                string.Join(",", row["calc"]!["abilities"]!.Select(a => $"{a["key"]}{a["rank"]}"));
            // Level 7 allows rank 4 of a basic ability and rank 1 of a three-rank ultimate; Q here has six ranks.
            Check(
                Ranks(self) == "Q2,W0,E1,R0"
                    && Ranks(enemy) == "Q4,W4,E4,R1"
                    && (double?)enemy["calc"]!["haste"] == 25
                    && (double?)self["calc"]!["haste"] == 0
                    && (bool?)self["calc"]!["hasteEstimated"] == true
                    && (bool?)enemy["calc"]!["hasteEstimated"] == true
                    && (string?)enemy["controls"]![0]!["effects"] == "减速35% 2秒 覆盖42%"
                    && Math.Abs(((double?)enemy["controls"]![0]!["slowCoverage"] ?? 0) - 0.417) < 0.001,
                "The player's ranks come from 2999, others' from the level; coverage follows the shown rank: "
                    + Ranks(self)
                    + " / "
                    + Ranks(enemy)
                    + " / "
                    + enemy["controls"]![0]!["effects"]
            );
        }
        finally
        {
            bridge.Dispose();
            await bridge.Completion;
            Directory.Delete(folder, true);
        }
    }

    private static void CheckSection(AbilityCooldown[] now)
    {
        var insight = new LiveMatchInsight(
            60,
            new(0, 0, 0, 0, 0),
            new(0, 0, 0, 0, 0),
            new Dictionary<string, LivePlayerInsight>(),
            [],
            ""
        );
        var stats = new LiveChampionStats(2000, 50, 40, 50, 0, 0, 0, 0);
        var sections = LivePanelSections.Build(
            new LivePanelInput(insight, null, stats, null, null, [], [], "") { Cooldowns = now },
            id => id.ToString()
        );
        var section = sections.OfType<JObject>().Single(s => (string?)s["title"] == "技能冷却");
        var tiles = section["rows"]!.OfType<JObject>().Select(t => $"{t["value"]}|{t["stats"]}").ToArray();
        Check(
            tiles.SequenceEqual([
                "4.7 秒|3/6 级 · 基础 7 秒",
                "未学|",
                "充能 10 秒|4/5 级 · 3 层充能 · 基础 15 秒",
                "53 秒|1/3 级 · 基础 80 秒",
            ]) && ((string?)section["tip"] ?? "").Contains("技能急速 50"),
            "The live tab lists each ability's cooldown after haste: " + string.Join(" ; ", tiles)
        );
    }

    private static JObject Spell(params (string Name, JToken Value)[] fields)
    {
        var spell = new JObject();
        foreach (var (name, value) in fields)
        {
            spell[name] = value;
        }

        return new JObject { ["mSpell"] = spell };
    }

    private static JObject Bin()
    {
        var bin = new JObject
        {
            ["Characters/Test/CharacterRecords/Root"] = new JObject
            {
                ["spellNames"] = new JArray("TestQ", "TestW", "TestE", "Testr"),
            },
            ["Characters/Test/Spells/TestQAbility/TestQ"] = Spell(
                ("cooldownTime", new JArray(0, 9, 8, 7, 6, 5, 4)),
                ("mClientData", JObject.Parse("{\"mTooltipData\":{\"mLists\":{\"LevelUp\":{\"levelCount\":6}}}}"))
            ),
            // The extraction leaves the default out; a sub-spell's own cooldown is not the ability's.
            ["Characters/Test/Spells/TestWAbility/TestW"] = Spell(("mCastTime", 0.25)),
            ["Characters/Test/Spells/TestWAbility/TestWMissile"] = Spell(("cooldownTime", new JArray(0, 12, 12))),
            ["Characters/Test/Spells/TestEAbility/TestE"] = Spell(
                ("cooldownTime", new JArray(0.4000000059604645, 0.4000000059604645, 0.4, 0.4, 0.4, 0.4)),
                ("mAmmoRechargeTime", new JArray(0, 18, 17, 16, 15, 14)),
                ("mMaxAmmo", new JArray(2, 2, 2, 2, 3, 3))
            ),
            ["Characters/Test/Spells/TestRAbility/TestR"] = Spell(("cooldownTime", new JArray(0, 80, 60, 40, 40))),
        };
        return bin;
    }

    private static JObject Champion()
    {
        JObject Ability(string key, string name) => new() { ["spellKey"] = key, ["name"] = name };
        return new JObject
        {
            ["alias"] = "Test",
            ["spells"] = new JArray(
                Ability("q", "光之束缚"),
                Ability("w", "棱光屏障"),
                Ability("e", "透光奇点"),
                Ability("r", "终极闪光")
            ),
        };
    }
}
