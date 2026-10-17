using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class GameBarBridgeChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    public static void Run()
    {
        HeartbeatDescriptions();
        Task.Run(PublisherLifecycleAsync).GetAwaiter().GetResult();
        Console.WriteLine("Game Bar bridge checks passed.");
    }

    private static void HeartbeatDescriptions()
    {
        long now = 100000;
        Check(
            GameBarBridge.DescribeWidgetState(null, now).Contains("未连接"),
            "A written snapshot alone must not claim the widget is connected"
        );
        var heartbeat = new JObject
        {
            ["updatedAt"] = now - 100,
            ["pinned"] = true,
            ["shortcut"] = "PrtSc",
            ["width"] = 820,
            ["height"] = 560,
        };
        Check(
            GameBarBridge.DescribeWidgetState(heartbeat, now).Contains("已连接并固定"),
            "A fresh rendered widget reports connection"
        );
        Check(
            GameBarBridge.DescribeWidgetState(heartbeat, now + 16000).Contains("未连接"),
            "A closed widget cannot retain connected status"
        );
        heartbeat["width"] = 0;
        Check(
            GameBarBridge.DescribeWidgetState(heartbeat, now).Contains("画面尚未加载"),
            "A zero-size widget is diagnosed separately from missing statistics"
        );
        heartbeat["readError"] = "read failed";
        Check(
            GameBarBridge.DescribeWidgetState(heartbeat, now).Contains("尚未读到有效数据"),
            "Widget-side file failures are visible in settings"
        );
        heartbeat["width"] = 820;
        heartbeat["connected"] = true;
        heartbeat["active"] = false;
        Check(
            GameBarBridge.DescribeWidgetState(heartbeat, now).Contains("等待对局"),
            "A connected widget in the lobby is waiting for a match, not disconnected"
        );
        heartbeat["pinned"] = false;
        Check(
            GameBarBridge.DescribeWidgetState(heartbeat, now).Contains("尚未固定"),
            "Pinning requirement remains visible outside matches"
        );
        heartbeat["pinned"] = true;
        heartbeat["active"] = true;
        Check(
            GameBarBridge.DescribeWidgetState(heartbeat, now).Contains("已连接并固定"),
            "A transient read error retains a connection while the last snapshot remains fresh"
        );
        heartbeat["connected"] = false;
        Check(
            GameBarBridge.DescribeWidgetState(heartbeat, now).Contains("尚未读到有效数据"),
            "A stopped publisher remains disconnected even if the widget itself has a heartbeat"
        );
    }

    private static LoadingRoster CreateRoster()
    {
        return new LoadingRoster(
            "123",
            2400,
            "KIWI",
            [
                new("enemy-key", "Enemy", "private-puuid", 123, 1, 100, false),
                new("self-key", "Self", "secret-puuid", 456, 2, 200, true),
            ]
        );
    }

    private static async Task PublisherLifecycleAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "League-GameBar-" + Guid.NewGuid().ToString("N"));
        var bridge = new GameBarBridge(folder);
        try
        {
            using var icon = new Bitmap(32, 32);
            var latest = await PublishSnapshotsAsync(bridge, folder, icon);
            CheckPlayerSnapshot(latest);
            Check(!Directory.EnumerateFiles(folder, "*.tmp").Any(), "Atomic writer cleans temporary files");
            await DisabledPublisherAsync(folder, latest);
        }
        finally
        {
            bridge.Dispose();
            await bridge.Completion;
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    private static async Task<JObject> PublishSnapshotsAsync(GameBarBridge bridge, string folder, Image icon)
    {
        var roster = CreateRoster();
        var ranks = new Dictionary<string, PlayerChampionTier>
        {
            ["self-key"] = new("T4", "胜 48.5%", "arammeta · 调整后胜率", 2400, "none") { PickRate = "7.1%" },
        };
        var icons = new Dictionary<int, Image> { [2] = icon };
        var mastery = new Dictionary<string, PlayerChampionMastery>
        {
            ["self-key"] = new(2, 12, 186321, null, true),
            ["enemy-key"] = new(1, 5, 2000, null, true),
        };
        var streaks = new Dictionary<string, LiveDeathStreak>
        {
            ["self-key"] = new(3, 5, true, "完整历史"),
            ["enemy-key"] = LiveDeathStreak.Unavailable(),
        };
        var parties = new Dictionary<string, OverlayPartyMarker> { ["self-key"] = new(5, 0xcba2ff) };
        void Publish(bool enabled = true, bool active = true) =>
            bridge.Publish(
                roster,
                ranks,
                icons,
                id => "英雄 " + id,
                _ => "近期 20 场，12 胜",
                "match-1",
                "当前对局",
                enabled,
                active,
                true,
                mastery,
                streaks,
                parties
            );
        Publish();
        string file = Path.Combine(folder, "snapshot.json");
        var first = JObject.Parse(
            await BridgeCheckFiles.ReadAsync(file, "Game Bar snapshot is written asynchronously")
        );
        for (int i = 0; i < 100; i++)
        {
            Publish();
        }

        bridge.Dispose();
        await bridge.Completion;
        var latest = JObject.Parse(await File.ReadAllTextAsync(file));
        Check(
            (int)latest["protocol"]! == 1 && ((string)latest["revision"]!).EndsWith(":101"),
            "Bounded writer keeps the newest snapshot"
        );
        Check(
            (string?)latest["shortcutOwner"] == "main"
                && (string?)latest["shortcut"] == ""
                && latest["toggleSequence"] == null
                && latest["toggleAt"] == null,
            "Older widgets cannot register a fallback shortcut, and no keyboard commands are published"
        );
        Check(
            (string?)latest["command"] == (string?)first["command"],
            "Statistics refresh never overrides a manually hidden widget"
        );
        return latest;
    }

    private static void CheckPlayerSnapshot(JObject latest)
    {
        Check(
            (string?)latest["teams"]![0] == "我方" && (string?)latest["players"]![0]!["name"] == "Self · 自己",
            "Own red team is placed first"
        );
        Check(
            latest["players"]!.Count() == 10 && !latest["players"]![1]!.HasValues,
            "Missing slots stay on the correct team"
        );
        Check((string?)latest["players"]![5]!["name"] == "Enemy", "Enemy remains in the other column");
        Check(
            ((string?)latest["players"]![0]!["mastery"])?.Contains("12级") == true
                && ((string?)latest["players"]![5]!["mastery"])?.Contains("5级") == true,
            "Player mastery is independent on both teams"
        );
        Check(
            (int?)latest["players"]![0]!["currentDeathStreak"] == 3
                && (int?)latest["players"]![0]!["maxDeathStreak"] == 5
                && (bool?)latest["players"]![0]!["deathStreakComplete"] == true
                && ((string?)latest["players"]![0]!["deathStreak"])?.Contains("连死 3") == true,
            "Current/highest death streaks and completeness cross the bridge"
        );
        Check(
            latest["players"]![5]!["currentDeathStreak"]!.Type == JTokenType.Null,
            "Unavailable event data remains unknown instead of becoming zero"
        );
        Check(
            (int?)latest["players"]![0]!["partySize"] == 5
                && (int?)latest["players"]![0]!["partyColor"] == 0xcba2ff
                && (string?)latest["players"]![0]!["party"] == "5人"
                && ((string?)latest["players"]![0]!["partyDetails"])?.Contains("疑似5人组") == true,
            "Group count, accessible color and inference explanation cross the bridge"
        );
        Check(
            (int?)latest["players"]![5]!["partySize"] == 0 && (string?)latest["players"]![5]!["party"] == "",
            "Missing history leaves the player unmarked rather than implying solo queue"
        );
        Check(
            (string?)latest["players"]![0]!["tier"] == "T4"
                && ((string)latest["players"]![0]!["stats"]!).Contains("7.1%"),
            "Tier, win rate and pick rate cross the bridge"
        );
        using var imageBytes = new MemoryStream(Convert.FromBase64String((string)latest["players"]![0]!["icon"]!));
        using var decoded = Image.FromStream(imageBytes);
        Check(decoded.Width == 48, "Cached champion image is sized for the widget");
        Check(
            !latest.ToString().Contains("puuid")
                && !latest.ToString().Contains("private-puuid")
                && !latest.ToString().Contains("secret-puuid"),
            "Account identifiers are not exported"
        );
        Check(
            (double?)latest["waveWidget"]?["opacity"] == LeagueConfig.DefaultGameBarWaveOpacity / 100.0,
            "The mini wave widget opacity defaults to the config default"
        );
    }

    private static async Task DisabledPublisherAsync(string folder, JObject latest)
    {
        using var disabled = new GameBarBridge(folder);
        disabled.Publish(
            CreateRoster(),
            new Dictionary<string, PlayerChampionTier>(),
            new Dictionary<int, Image>(),
            _ => "英雄",
            _ => "战绩",
            "match-1",
            "已关闭",
            false,
            false,
            false,
            waveOpacity: 5
        );
        disabled.Dispose();
        await disabled.Completion;
        var cleared = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "snapshot.json")));
        Check(
            cleared["players"]!.All(row => !row.HasValues) && !(bool)cleared["enabled"]!,
            "Disabling the feature removes old match information"
        );
        Check(
            (string?)cleared["session"] != (string?)latest["session"],
            "Restart gives commands a new instance identity"
        );
        Check(
            (int?)cleared["waveWidget"]?["protocol"] == 1 && (double?)cleared["waveWidget"]?["opacity"] == 0.2,
            "The mini wave widget gets its opacity outside a game, clamped to the minimum"
        );
    }
}
