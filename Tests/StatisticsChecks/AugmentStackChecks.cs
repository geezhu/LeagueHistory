using System.Drawing;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

/// <summary>
/// Augment stacks rebuilt from the death streak fixture: A dies at 10, 20 (turret), 40, 60, 80 and 90 seconds, assists
/// at 30 and kills at 50 and 70.
/// </summary>
internal static class AugmentStackChecks
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
        var snapshot = LiveMatchSnapshot.Parse(LiveDeathStreakChecks.Fixture());
        string a = snapshot.Players[0].Key;
        var recursion = AugmentStacks.Count(snapshot, a, TrackedAugment.InfiniteRecursion, new(0, 0));
        Check(
            recursion is { Stacks: 3, Takedowns: 3, Deaths: 6, Complete: true }
                && AugmentStacks.AbilityHaste(TrackedAugment.InfiniteRecursion, recursion.Stacks) == 69,
            "Infinite Recursion counts kills and assists and keeps them through deaths: " + recursion
        );
        var shrink = AugmentStacks.Count(snapshot, a, TrackedAugment.ShrinkEngine, new(0, 0));
        Check(shrink.Stacks == 0, "Shrink Engine loses stacks on each death: " + shrink);

        // A correction to 10 stacks at 45 seconds: +1 at 50, death at 60 keeps 11 − 7 = 4, +1 at 70, death at 80
        // keeps 5 − 3 = 2, death at 90 keeps 2 − 1 = 1.
        var corrected = AugmentStacks.Count(snapshot, a, TrackedAugment.ShrinkEngine, new(45, 10));
        Check(
            corrected is { Stacks: 1, Takedowns: 2, Deaths: 3, Complete: true }
                && AugmentStacks.AbilityHaste(TrackedAugment.ShrinkEngine, 4) == 32,
            "A correction is the new start; later events count on top of it: " + corrected
        );
        // 6 stacks at 85 seconds: the death at 90 loses round(3.9) = 4.
        Check(
            AugmentStacks.Count(snapshot, a, TrackedAugment.TankEngine, new(85, 6)).Stacks == 2
                && AugmentStacks.AbilityHaste(TrackedAugment.TankEngine, 5) == 0,
            "Tank Engine stacks the same way and grants no ability haste"
        );

        var hidden = snapshot with { Players = snapshot.Players.Select(p => p with { EventAliases = [] }).ToArray() };
        Check(
            AugmentStacks.Count(hidden, a, TrackedAugment.ShrinkEngine, new(0, 4)) is { Stacks: 4, Complete: false },
            "A player the events cannot identify keeps the manual count"
        );
        var fixture = LiveDeathStreakChecks.Fixture();
        var events = (JArray)fixture["events"]!["Events"]!;
        events.RemoveAt(0);
        var partial = LiveMatchSnapshot.Parse(fixture);
        Check(
            !AugmentStacks.Count(partial, a, TrackedAugment.InfiniteRecursion, new(0, 0)).Complete
                && AugmentStacks.Count(partial, a, TrackedAugment.InfiniteRecursion, new(45, 1)).Complete,
            "Counting from zero needs the game start; counting from a correction does not"
        );
        Selections();
        Task.Run(BridgeAsync).GetAwaiter().GetResult();
        Console.WriteLine(
            "PASS: augment stacks from takedowns and deaths, 65% loss, corrections as new starts, unmatched players "
                + "and the widget's augment list."
        );
    }

    private static JObject SelectionFile(string session, params JObject[] augments) =>
        new()
        {
            ["protocol"] = 1,
            ["session"] = session,
            ["augments"] = new JArray(augments),
        };

    private static JObject Pick(string augment, double time, int stacks, string? player = null)
    {
        var pick = new JObject
        {
            ["augment"] = augment,
            ["time"] = time,
            ["stacks"] = stacks,
        };
        if (player != null)
        {
            pick["player"] = player;
        }

        return pick;
    }

    private static void Selections()
    {
        var parsed = AugmentSelections.Parse(
            SelectionFile("s", Pick("ShrinkEngine", 240.5, 2), Pick("InfiniteRecursion", 0, 0)),
            "s"
        );
        Check(
            parsed is [{ Player: "", Augment: TrackedAugment.ShrinkEngine, Anchor: { Time: 240.5, Stacks: 2 } }, _]
                && AugmentSelections.Parse(SelectionFile("s"), "s") is [],
            "The widget's list is read with each anchor; an empty list clears it"
        );
        var players = AugmentSelections.Parse(
            SelectionFile("s", Pick("TankEngine", 30, 0, "enemy-1"), Pick("TankEngine", 40, 1, "enemy-2")),
            "s"
        );
        Check(
            players is [{ Player: "enemy-1" }, { Player: "enemy-2", Anchor.Stacks: 1 }],
            "Each player may have the same augment"
        );
        JObject[][] invalid =
        [
            [Pick("ShrinkEngine", 10, 1), Pick("ShrinkEngine", 20, 1)],
            [Pick("ShrinkEngine", -1, 0)],
            [Pick("ShrinkEngine", 10, -1)],
            [Pick("ShrinkEngine", 10, AugmentSelections.MaxStacks + 1)],
            [Pick("1", 10, 0)],
            [Pick("Unknown", 10, 0)],
            [Pick("TankEngine", 10, 0, "enemy-1"), Pick("TankEngine", 20, 2, "enemy-1")],
            [new JObject(Pick("TankEngine", 10, 0)) { ["player"] = 5 }],
        ];
        Check(
            invalid.All(augments => AugmentSelections.Parse(SelectionFile("s", augments), "s") == null)
                && AugmentSelections.Parse(SelectionFile("old", Pick("TankEngine", 1, 1)), "s") == null,
            "Duplicates, negative or huge values, unknown augments and another session's list are refused"
        );
    }

    // The snapshot carries the counted stacks with their anchors, and the widget's file comes back for its session.
    private static async Task BridgeAsync()
    {
        var roster = new LoadingRoster("1", 2400, "KIWI", [new("self", "自己#1", "", 1, 143, 100, true)]);
        var insight = new LiveMatchInsight(
            60,
            new(0, 0, 0, 0, 0),
            new(0, 0, 0, 0, 0),
            new Dictionary<string, LivePlayerInsight> { ["self"] = new(3000, 0, 0, 0, null, 0) { Level = 9 } },
            [],
            ""
        );
        var selection = new AugmentSelection("self", TrackedAugment.ShrinkEngine, new(45, 10));
        var live = new OverlayLiveState(insight, new JArray(), "rev-1")
        {
            AugmentStacks = [new(selection, new(1, 2, 3, true, "依据本局击杀事件推算。"))],
            Health = new Dictionary<string, PlayerHealth>
            {
                ["self"] = new("4414 · 物理 9667 · 法术 7680", "实际属性"),
            },
        };
        string folder = Path.Combine(Path.GetTempPath(), "league-augments-" + Guid.NewGuid().ToString("N"));
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
                await BridgeCheckFiles.ReadAsync(Path.Combine(folder, "snapshot.json"), "Augment snapshot is written")
            );
            var stack = snapshot["augmentStacks"]![0]!;
            Check(
                (int?)snapshot["augmentProtocol"] == 2
                    && (string?)stack["player"] == "self"
                    && (string?)snapshot["players"]![0]!["health"] == "4414 · 物理 9667 · 法术 7680"
                    && (string?)snapshot["players"]![0]!["healthDetails"] == "实际属性"
                    && (string?)stack["augment"] == "ShrinkEngine"
                    && (double?)stack["time"] == 45
                    && (int?)stack["start"] == 10
                    && (int?)stack["stacks"] == 1
                    && (string?)stack["bonus"] == "1 层 · 技能急速 +8"
                    && (bool?)stack["approximate"] == true
                    && ((string?)stack["note"] ?? "").StartsWith("起点 10 层，之后参与击杀 2 次、死亡 3 次。"),
                "The snapshot carries the anchor and the counted stacks: " + stack
            );
            string session = (string)snapshot["session"]!;
            await File.WriteAllTextAsync(
                Path.Combine(folder, "augment-stacks.json"),
                SelectionFile(session, Pick("TankEngine", 300, 4, "self")).ToString()
            );
            Check(
                await bridge.ReadAugmentSelectionsAsync()
                    is [{ Player: "self", Augment: TrackedAugment.TankEngine, Anchor.Stacks: 4 }],
                "The widget's list for this session is read back"
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
