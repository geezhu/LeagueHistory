using System.Reflection;
using System.Runtime.CompilerServices;
using League.Models;
using League.Parsers;
using League.Services;
using Newtonsoft.Json.Linq;

/// <summary>
/// The Game Bar record tab and record message: seat matching against the cached histories, the sections, the tag
/// rule list against upstream's tag rules, and the F9/F11 line format. Nothing is sent and no request is made.
/// </summary>
internal static class OverlayRecordChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new Exception(message);
        }
    }

    public static void Run()
    {
        var roster = Roster();
        var self = Info("self-puuid", 1, [true], "");
        var ally = Info("ally-puuid", 2, [true, true, false, false, true], "玻璃心、爱发问号", remakeAt: 2);
        var enemy = Info("enemy-puuid", 3, [false, true], "");
        PlayerMatchInfo[] cached = [self, ally, enemy];
        Matching(roster, cached);
        Sections(roster, ally);
        TagRules();
        Message(roster, cached, ally);
        Check(
            OverlayChatRequest.Parse(Request("record", "own"), "match", new int[10], 100000)?.Kind == "record",
            "The record message is an accepted Game Bar request"
        );
        Check(
            OverlayChatRequest.Parse(Request("party", "enemy"), "match", new int[10], 100000)?.Kind == "party"
                && OverlayChatRequest.Parse(Request("party", "single", 0, 103), "match", Heroes(), 100000) == null,
            "Premades are sent per side; a single hero has no group"
        );
        Console.WriteLine(
            "PASS: Game Bar record tab and message: seat matching, results and streaks, tag rules, F9/F11 line format and send settings."
        );
    }

    private static void Matching(LoadingRoster roster, PlayerMatchInfo[] cached)
    {
        var ally = roster.Players[1];
        Check(
            OverlayPlayerRecords.Find(roster, ally, cached)?.Player.Puuid == "ally-puuid",
            "A seat finds its history"
        );
        Check(
            OverlayPlayerRecords.Find(roster, ally, [.. cached, Info("ally-puuid", 2, [false], "")]) == null,
            "Two cached histories for one account show neither"
        );
        Check(OverlayPlayerRecords.Find(roster, roster.Players[2], cached) == null, "A hidden player has no history");
        var hidden = OverlayRecordSections.Build(roster.Players[2], null, Hero);
        Check(
            (string?)hidden[0]["rows"]![0]!["title"] == "隐藏玩家" && (string?)hidden[0]["tab"] == "record",
            "A hidden player gets a notice on the record tab"
        );
        var missing = OverlayRecordSections.Build(roster.Players[3], null, Hero);
        Check(
            ((string?)missing[0]["rows"]![0]!["description"])!.Contains("重试"),
            "A player the main window has not loaded yet is explained, with a retry"
        );
    }

    private static void Sections(LoadingRoster roster, PlayerMatchInfo ally)
    {
        var sections = OverlayRecordSections.Build(roster.Players[1], ally, Hero);
        Check(
            sections.Count == 3 && sections.All(s => (string?)s["tab"] == "record"),
            "Summary, behavior and games share the record tab"
        );
        Check(
            sections.Select(s => (string?)s["layout"]).SequenceEqual(["tiles", "chips", "games"]),
            "The record tab is drawn as tiles, chips and game cards"
        );
        var tiles = (JArray)sections[0]["rows"]!;
        string Tile(int i, string field) => (string?)tiles[i][field] ?? "";
        // Newest first: win, win, remake, loss, win. The remake counts in the rate as F9/F11 do, not in the streak.
        Check(
            Tile(0, "value") == "60%"
                && Tile(0, "stats") == "3 胜 1 负 1 重开"
                && Tile(1, "value") == "2 连胜"
                && Tile(1, "tone") == "good"
                && Tile(2, "value") == "5.0/3.0/7.0"
                && Tile(3, "value") == "黄金 II"
                && Tile(3, "stats") == "45 胜点"
                && Tile(4, "value") == "未知",
            "Rate, streak, KDA and ranks: " + tiles
        );
        Check(
            sections[0]["form"]!
                .Select(f => (string?)f["result"])
                .SequenceEqual(["win", "win", "remake", "loss", "win"]),
            "The form strip shows every game, newest first"
        );
        var chips = (JArray)sections[1]["rows"]!;
        var signals = (JArray)sections[1]["signals"]!;
        Check(
            chips.Select(c => (string?)c["title"]).SequenceEqual(["玻璃心", "爱发问号"])
                && (string?)chips[0]["tone"] == "bad"
                && (string?)chips[1]["tone"] == "warn"
                && ((string?)chips[0]["description"])!.StartsWith("四种信号")
                && (string?)signals[0]["value"] == "40%"
                && (string?)signals[0]["tone"] == "bad"
                && (string?)signals[1]["tone"] == "warn"
                && (string?)signals[4]["tone"] == "",
            "Tags as chips with their rules, signals colored by their thresholds: " + chips + signals
        );
        var games = (JArray)sections[2]["rows"]!;
        Check(
            games.Count == 5
                && (string?)games[0]["title"] == "阿狸"
                && (string?)games[0]["result"] == "胜"
                && (string?)games[0]["value"] == "5/3/7"
                && (string?)games[0]["stats"] == "21 分钟 · 10-10"
                && ((string?)games[0]["description"])!.Contains("大乱斗")
                && (string?)games[2]["tone"] == "remake"
                && (string?)games[0]["image"] == "",
            "Game cards name the champion, result, KDA, mode, length and date: " + games[0]
        );
    }

    // Every tag BehaviorTags can produce must have a rule text, and no rule may be unreachable.
    private static void TagRules()
    {
        PlayerBehavior[] behaviors =
        [
            new()
            {
                SurrenderRate = 50,
                AvgMissingPings = 10,
                AvgVisionPings = 10,
                AvgGetBackPings = 10,
                AvgDangerPings = 10,
            },
            new()
            {
                SurrenderRate = 30,
                AvgMissingPings = 2.4,
                AvgVisionPings = 2.4,
                AvgGetBackPings = 2.4,
                AvgDangerPings = 2.4,
            },
        ];
        var produced = behaviors
            .SelectMany(b => OverlayRecordSections.Tags(new() { BehaviorSummary = BehaviorTags.Summary(b) }))
            .ToHashSet();
        Check(
            produced.SetEquals(OverlayRecordSections.TagRules.Keys),
            "Tag rules match upstream: " + string.Join("、", produced)
        );
    }

    private static void Message(LoadingRoster roster, PlayerMatchInfo[] cached, PlayerMatchInfo ally)
    {
        var request = OverlayChatRequest.Parse(Request("record", "own"), "match", new int[10], 100000)!;
        var lines = OverlayRecordChatMessage.Build(roster, request, cached, Hero, false, false);
        var builder = (ChatMessageBuilder)RuntimeHelpers.GetUninitializedObject(typeof(ChatMessageBuilder));
        string upstream = (string)
            typeof(ChatMessageBuilder).GetMethod("BuildSimpleLine", Private)!.Invoke(builder, [ally, false, null])!;
        Check(lines.Count == 4 && lines[1] == upstream, "Each player gets the F9/F11 line: " + lines[1]);
        Check(lines[2] == "阿狸 暂无战绩" && lines[0].StartsWith("自己 近1场"), "Hidden players use the hero name");
        var hidden = OverlayRecordChatMessage.Build(roster, request, cached, Hero, true, true);
        Check(
            hidden.Count == 3 && hidden[0].StartsWith("阿狸 近5场 胜率 [60%]"),
            "Hiding yourself and champion names follow the send settings"
        );
        var single = OverlayChatRequest.Parse(Request("record", "single", 0, 103), "match", Heroes(), 100000)!;
        Check(
            OverlayRecordChatMessage.Build(roster, single, cached, Hero, true, false).Single().StartsWith("自己 "),
            "Picking your own hero sends it even when hiding yourself"
        );
        Check(
            Throws(() => OverlayRecordChatMessage.Build(roster, request, [], Hero, false, false)),
            "Nothing is sent before any history is loaded"
        );
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

    private static string Hero(int id) => id == 103 ? "阿狸" : "英雄 " + id;

    private static int[] Heroes() => [103, 103, 103, 103, 0, 103, 0, 0, 0, 0];

    private static LoadingRoster Roster() =>
        new(
            "1",
            450,
            "ARAM",
            [
                new("self-puuid", "自己#1", "self-puuid", 1, 103, 100, true),
                new("ally-puuid", "队友#2", "ally-puuid", 2, 103, 100, false),
                new("100:2", "隐藏玩家", "", 0, 103, 100, false, true),
                new("later-puuid", "稍后#3", "later-puuid", 4, 103, 100, false),
                new("enemy-puuid", "对手#4", "enemy-puuid", 3, 103, 200, false),
            ]
        );

    private static PlayerMatchInfo Info(string puuid, long id, bool[] wins, string summary, int remakeAt = -1)
    {
        var info = new PlayerMatchInfo
        {
            Player = new PlayerInfo
            {
                Puuid = puuid,
                SummonerId = id,
                GameName = puuid == "ally-puuid" ? "队友" : "自己",
                SoloRank = "黄金 II(45)",
                FlexRank = "未知",
                IsPublic = "公开",
            },
            Behavior = new PlayerBehavior
            {
                SurrenderRate = 40,
                AvgMissingPings = 3.2,
                AvgGetBackPings = 4.1,
                BehaviorSummary = summary,
            },
        };
        for (int i = 0; i < wins.Length; i++)
        {
            info.WinHistory.Add(wins[i] && i != remakeAt);
            info.RecentMatches.Add(
                new MatchStat
                {
                    Kills = 5,
                    Deaths = 3,
                    Assists = 7,
                    GameDurationMinutes = 21,
                }
            );
            var item = new ListViewItem
            {
                ImageKey = "Ahri",
                ForeColor = i == remakeAt ? MatchResult.RemakeColor : Color.Green,
                Tag = new MatchMetadata
                {
                    GameData = new JObject
                    {
                        ["participants"] = new JArray(new JObject { ["puuid"] = puuid, ["championId"] = 103 }),
                    },
                },
            };
            item.SubItems.AddRange(["大乱斗", "5/3/7", $"2026-10-1{i}"]);
            info.MatchItems.Add(item);
        }

        return info;
    }

    private static JObject Request(string kind, string target, int slot = -1, int hero = 0) =>
        new()
        {
            ["protocol"] = 1,
            ["id"] = Guid.NewGuid().ToString(),
            ["session"] = "match",
            ["target"] = target,
            ["kind"] = kind,
            ["slot"] = slot,
            ["championId"] = hero,
            ["updatedAt"] = 99000,
            ["dismissed"] = false,
            ["cancelled"] = false,
        };
}
