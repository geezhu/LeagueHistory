using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

/// <summary>
/// The augment page's records summed per player: the catalog's fixed, manual and takedown augments, their choices,
/// hand-entered ultimate haste, and how the sum reaches health, haste, live cooldowns, slow coverage and the snapshot.
/// </summary>
internal static class CooldownOptionChecks
{
    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new Exception(message);
        }
    }

    private static LiveAugmentStack Record(TrackedAugment augment, int stacks = 0, string choice = "") =>
        new(new("p", augment, new(0, stacks)) { Choice = choice }, new(stacks, 0, 0, true, ""));

    public static void Run()
    {
        var cooldown = AugmentEffects.From(
            [
                Record(TrackedAugment.BreadAndButter),
                Record(TrackedAugment.PursuitOfHaste, 2, "W"),
                Record(TrackedAugment.UltBot),
                Record(TrackedAugment.UltimateAwakening),
            ],
            30
        );
        var options = cooldown.Cooldown;
        Check(
            options.Haste("Q", 40, estimated: false) == 100
                && options.Haste("W", 40, estimated: false) == 160
                && options.Haste("E", 40, estimated: false) == 0
                && options.Haste("R", 40, estimated: false) == (40 + 60) * 1.5
                && options.Haste("R", 40, estimated: true) == (140 + 60) * 1.5
                && options.Summary() == "面包和黄油、急速之追求 W +160、大招工具人、大招急速 60",
            "Recorded augments become cooldown rules; Ultimate Awakening adds 30 to the hand-entered 30: "
                + options.Summary()
        );
        Effects();
        Parsing();
        Applied(options);
        Task.Run(BridgeAsync).GetAwaiter().GetResult();
        Console.WriteLine(
            "PASS: augment records summed into health, haste and cooldown rules, choices, ultimate haste and the snapshot."
        );
    }

    // Health multiplies, flat health adds before it, Glass Cannon caps after it; haste and resistances add up.
    private static void Effects()
    {
        var effects = AugmentEffects.From(
            [
                Record(TrackedAugment.Goliath),
                Record(TrackedAugment.TankEngine, 2),
                Record(TrackedAugment.CelestialBody),
                Record(TrackedAugment.SoulEater, 5, "later"),
                Record(TrackedAugment.StayResolute, 12, "magic"),
                Record(TrackedAugment.Recursion),
                Record(TrackedAugment.MadScientist, 0, "haste"),
                Record(TrackedAugment.GlassCannon),
            ],
            0
        );
        Check(
            effects.Health == 1500 + 100 + 300
                && Math.Abs(effects.HealthMultiplier - 1.35 * 1.1) < 1e-9
                && effects is { GlassCannon: true, Armor: 0, MagicResist: 12, AbilityHaste: 130 }
                && effects.Summary.Contains("吞噬灵魂（5 层 · 生命 +400）")
                && effects.Summary.Contains("保持坚定（魔抗 +12）")
                && effects.Summary.Contains("科学狂人（变小 · 技能急速 +70）"),
            "Each augment adds its own effect: " + effects.Summary
        );
        var stats = new ChampionBaseStats(1000, 0, 30, 0, 30, 0);
        var estimate = HealthEstimate.Estimate(stats, 1, ItemStats.None, effects);
        Check(
            Math.Abs(estimate.Health - (1000 + 1900) * 1.35 * 1.1 * 0.7) < 1e-9 && estimate.MagicResist == 42,
            "The estimate takes flat health, then the increases, then Glass Cannon's cap: " + estimate
        );
        var bigger = AugmentEffects.From([Record(TrackedAugment.MadScientist), Record(TrackedAugment.BackToBasics)], 0);
        Check(
            Math.Abs(bigger.HealthMultiplier - 1.2) < 1e-9 && bigger.AbilityHaste == 70,
            "Mad Scientist's first form is the larger one; Back to Basics adds 70 haste"
        );
        var haste = PlayerHaste.Describe(new ItemStats(0, 0, 0, 15), effects, null);
        Check(
            haste is { Value: 145, Estimated: true } && haste.Details.Contains("装备 15 + 海克斯 130"),
            "Others' haste adds the recorded augments: " + haste.Details
        );
        Purist();
        Completed();
    }

    // Purist - Caster turns bonus attack speed, items' and the level's, into haste: 0.65 per 1%.
    private static void Purist()
    {
        var items = ItemStats.Sum([
            ItemStats.Parse("<stats><attention>40</attention>攻击力<br><attention>25%</attention>攻击速度</stats>"),
            ItemStats.Parse("<stats><attention>30%</attention>攻击速度<br><attention>45</attention>移动速度</stats>"),
        ]);
        var champion = ChampionBaseStats.ParseAll(
            JObject.Parse(
                """{"data":{"Jinx":{"key":"222","stats":{"hp":630,"hpperlevel":105,"armor":26,"armorperlevel":4.7,"spellblock":30,"spellblockperlevel":1.3,"attackspeedperlevel":1}}}}"""
            )
        )[222];
        double growth = ChampionBaseStats.Grown(0, champion.AttackSpeedPerLevel, 11);
        var purist = AugmentEffects.From([Record(TrackedAugment.PuristCaster)], 0);
        var haste = PlayerHaste.Describe(items, purist, null, items.AttackSpeed + growth);
        var plain = PlayerHaste.Describe(items, AugmentEffects.None, null, items.AttackSpeed + growth);
        Check(
            items.AttackSpeed == 55
                && Math.Abs(growth - 10 * (0.7025 + 0.0175 * 10)) < 1e-9
                && Math.Abs(haste.Value - (55 + growth) * 0.65) < 1e-9
                && haste.Details.Contains("纯粹主义者附加攻速")
                && plain.Value == 0
                && purist.Cooldown.Multiplier == 0.9,
            "Purist turns items' and the level's attack speed into haste and cuts cooldowns by 10%: " + haste.Details
        );
    }

    // Draw Your Sword scales with attack range, Stay Resolute adds 20 of both when not first, Back to Basics seals R.
    private static void Completed()
    {
        var champion = ChampionBaseStats.ParseAll(
            JObject.Parse(
                """{"data":{"Ezreal":{"key":"81","stats":{"hp":600,"hpperlevel":102,"armor":24,"armorperlevel":4.7,"spellblock":30,"spellblockperlevel":1.3,"attackrange":550}}}}"""
            )
        )[81];
        var sword = AugmentEffects.From([Record(TrackedAugment.DrawYourSword)], 0, champion.AttackRange);
        Check(
            champion.AttackRange == 550
                && Math.Abs(sword.HealthMultiplier - 1.312) < 1e-9
                && Math.Abs(AugmentEffects.DrawYourSwordShare(650) - 0.48) < 1e-9
                && Math.Abs(AugmentEffects.DrawYourSwordShare(0) - 0.24) < 1e-9
                && sword.Summary.Contains("生命 +31%（攻击距离 550）"),
            "Draw Your Sword gains 10% per 15 range from 500, doubling at 650: " + sword.Summary
        );
        var resolute = AugmentEffects.From([Record(TrackedAugment.StayResolute, 12, "magic-later")], 0);
        Check(
            resolute is { Armor: 20, MagicResist: 32 } && resolute.Summary.Contains("魔抗 +12，另加护甲和魔抗各 20"),
            "Stay Resolute's hits go to the chosen resistance and not being first adds 20 of both: " + resolute.Summary
        );
        var basics = AugmentEffects.From([Record(TrackedAugment.BackToBasics)], 0).Cooldown;
        var cooldowns = AbilityCooldowns.Describe(
            [new("Q", "Q", [8, 7, 6, 5, 4]), new("R", "R", [100, 80, 60])],
            new Dictionary<string, int> { ["Q"] = 1, ["R"] = 1 },
            0,
            basics
        );
        Check(
            basics.UltimateSealed
                && cooldowns[1] is { Sealed: true, Current: 0 }
                && !cooldowns[0].Sealed
                && basics.Summary().Contains("大招已封印"),
            "Back to Basics seals the ultimate: no cooldown for it"
        );
    }

    private static JObject Row(string augment, string? choice = null)
    {
        var row = new JObject
        {
            ["player"] = "p",
            ["augment"] = augment,
            ["time"] = 0,
            ["stacks"] = 1,
        };
        if (choice != null)
        {
            row["choice"] = choice;
        }

        return row;
    }

    private static JObject File(string key, params JObject[] rows) =>
        new()
        {
            ["protocol"] = 1,
            ["session"] = "s",
            [key] = new JArray(rows),
        };

    private static void Parsing()
    {
        Check(
            AugmentSelections.Parse(File("augments", Row("MadScientist", "haste"), Row("Goliath")), "s")
                is [{ Choice: "haste" }, { Augment: TrackedAugment.Goliath, Choice: "" }]
                && AugmentSelections.Parse(File("augments", Row("MadScientist", "tiny")), "s") == null
                && AugmentSelections.Parse(File("augments", Row("Goliath", "haste")), "s") == null,
            "Choices are kept when the augment offers them and refused otherwise"
        );
        JObject Haste(string player, double value) => new() { ["player"] = player, ["ultimateHaste"] = value };
        Check(
            CooldownOptions.ParseUltimateHaste(File("players", Haste("a", 30), Haste("b", 0)), "s") is { Count: 2 } read
                && read["a"] == 30
                && CooldownOptions.ParseUltimateHaste(File("players", Haste("a", -1)), "s") == null
                && CooldownOptions.ParseUltimateHaste(File("players", Haste("a", 1), Haste("a", 2)), "s") == null
                && CooldownOptions.ParseUltimateHaste(File("players", Haste("a", 1)), "old") == null,
            "Hand-entered ultimate haste is read per player; negative, duplicate and other sessions' values are refused"
        );
    }

    private static void Applied(CooldownOptions options)
    {
        SpellCooldown[] spells =
        [
            new("Q", "Q", [10, 10, 10, 10, 10]),
            new("W", "W", [12, 12, 12, 12, 12]),
            new("R", "R", [100, 80, 60]),
        ];
        var ranks = new Dictionary<string, int>
        {
            ["Q"] = 1,
            ["W"] = 1,
            ["R"] = 1,
        };
        var now = AbilityCooldowns.Describe(spells, ranks, 40, options);
        Check(
            now[0].Current == 5 && now[1].Current == 12 * 100 / 260.0 && now[2].Current == 100 * 100 / 250.0,
            "The live tab's cooldowns follow the records: " + string.Join(",", now.Select(c => c.Current))
        );
        var coverage = SlowCoverage.Estimate(
            new SlowAtLevel(new(40, 2, 0), 1, ""),
            spells[0],
            new PlayerHaste(40, false, ""),
            options
        )!;
        Check(
            coverage.Cooldown == 5 && coverage.Note.Contains("面包和黄油"),
            "Coverage uses the same haste: " + coverage.Note
        );
        var insight = new LiveMatchInsight(
            60,
            new(0, 0, 0, 0, 0),
            new(0, 0, 0, 0, 0),
            new Dictionary<string, LivePlayerInsight>(),
            [],
            ""
        );
        var sections = LivePanelSections.Build(
            new LivePanelInput(insight, null, null, null, null, [], [], "")
            {
                Cooldowns = now,
                CooldownOptions = options,
            },
            id => id.ToString()
        );
        Check(
            sections
                .OfType<JObject>()
                .Any(s => ((string?)s["tip"] ?? "").Contains("已计入「海克斯」页的记录：面包和黄油")),
            "The live tab says which recorded augments it used"
        );
    }

    // The player with Mad Scientist recorded as the smaller form, Ult Bot and 30 hand-entered ultimate haste.
    private static OverlayLiveState BridgeLive()
    {
        var insight = new LiveMatchInsight(
            60,
            new(0, 0, 0, 0, 0),
            new(0, 0, 0, 0, 0),
            new Dictionary<string, LivePlayerInsight> { ["self"] = new(3000, 0, 0, 0, null, 0) { Level = 6 } },
            [],
            ""
        );
        var scientist = new LiveAugmentStack(
            new("self", TrackedAugment.MadScientist, new(0, 0)) { Choice = "haste" },
            new(0, 0, 0, true, "固定加成。")
        );
        return new OverlayLiveState(insight, new JArray(), "rev-1")
        {
            AugmentStacks = [scientist],
            Effects = new Dictionary<string, AugmentEffects>
            {
                ["self"] = AugmentEffects.From(
                    [Record(TrackedAugment.UltBot), Record(TrackedAugment.BackToBasics)],
                    30
                ),
            },
            UltimateHaste = new Dictionary<string, double> { ["self"] = 30 },
        };
    }

    // The snapshot carries the catalog, each record's choice and bonus, the hand-entered ultimate haste and the
    // cooldown rules for the calculator; the widget's ultimate haste file is read back for the session.
    private static async Task BridgeAsync()
    {
        var roster = new LoadingRoster("1", 2400, "KIWI", [new("self", "自己#1", "", 1, 99, 100, true)]);
        var live = BridgeLive();
        string folder = Path.Combine(Path.GetTempPath(), "league-augment-records-" + Guid.NewGuid().ToString("N"));
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
                cooldowns: new Dictionary<int, SpellCooldown[]> { [99] = [new("R", "R", [100, 80, 60])] },
                controls: new Dictionary<int, ControlSkill[]>
                {
                    [99] = [new("Q", "Q", ["禁锢"], ""), new("R", "R", ["晕眩"], "")],
                }
            );
            var snapshot = JObject.Parse(
                await BridgeCheckFiles.ReadAsync(Path.Combine(folder, "snapshot.json"), "Records snapshot is written")
            );
            var stack = snapshot["augmentStacks"]![0]!;
            var echo = snapshot["players"]![0]!["calc"]!["options"]!;
            Check(
                (int?)snapshot["augmentProtocol"] == 2
                    && snapshot["augmentCatalog"]!.Count() == AugmentCatalog.All.Length
                    && (string?)snapshot["augmentCatalog"]![18]!["choices"]![1]!["label"] == "变小"
                    && ((string?)snapshot["augmentCatalog"]![2]!["pinyin"] ?? "").Contains("tkyq")
                    && (string?)snapshot["augmentCatalog"]![2]!["tags"]![0] == "生命"
                    && AugmentCatalog.All.All(info => AugmentCatalog.Pinyin(info.Augment).Length > 0)
                    && (double?)snapshot["ultimateHaste"]!["self"] == 30
                    && (string?)stack["kind"] == "Fixed"
                    && (string?)stack["choice"] == "haste"
                    && (string?)stack["bonus"] == "变小 · 技能急速 +70"
                    && (bool?)echo["ultBot"] == true
                    && (double?)echo["ultimateHaste"] == 30
                    && (string?)echo["summary"] == "大招工具人、大招急速 30、大招已封印（回归基本功）"
                    && (bool?)echo["ultimateSealed"] == true
                    && snapshot["players"]![0]!["controls"]!.Select(c => (string?)c["key"]).SequenceEqual(["Q"]),
                "The snapshot carries the catalog, the records and the calculator's rules: " + stack + echo
            );
            await System.IO.File.WriteAllTextAsync(
                Path.Combine(folder, "ultimate-haste.json"),
                new JObject
                {
                    ["protocol"] = 1,
                    ["session"] = (string)snapshot["session"]!,
                    ["players"] = new JArray(new JObject { ["player"] = "self", ["ultimateHaste"] = 15 }),
                }.ToString()
            );
            Check(
                (await bridge.ReadUltimateHasteAsync())?["self"] == 15,
                "The widget's ultimate haste for this session is read back"
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
