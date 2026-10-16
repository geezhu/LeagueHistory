using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The Game Bar composition page: only ARAM-type games, both teams with grades and damage mix, the Mayhem-only
/// estimate and pairs, the chat line for either side, and the request and snapshot plumbing.
/// </summary>
internal static class GameBarCompositionChecks
{
    public static void Run()
    {
        var fixture = JObject.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/AramMeta/team-score.json"))
        );
        var index = AramMetaIndex.Parse((JObject)fixture["index"]!);
        var grades = AramCapabilityGrades.Build(index);
        int[] ids = index.Champions.Select(champion => champion.Id).Take(10).ToArray();
        string Name(int id) => index.Champions.First(champion => champion.Id == id).Name;
        LoadingRoster Roster(int queue, string mode) =>
            new(
                "1",
                queue,
                mode,
                ids.Select((id, i) => new LoadingPlayer("p" + i, "", "", i, id, i < 5 ? 200 : 100, i == 0)).ToArray()
            );

        Check(
            GameBarComposition.Supports(Roster(450, "ARAM"))
                && GameBarComposition.Supports(Roster(2400, "KIWI"))
                && !GameBarComposition.Supports(Roster(420, "CLASSIC")),
            "Only ARAM and ARAM: Mayhem have a composition page"
        );

        var mayhem = GameBarComposition.Build(index, grades, Roster(2400, "KIWI"), Name)!;
        var teams = (JArray)mayhem.Json["teams"]!;
        Check(
            (string?)teams[0]!["title"] == "我方" && (string?)teams[1]!["title"] == "敌方",
            "Own team first, enemies second"
        );
        Check(
            mayhem.Own.Team.SequenceEqual(ids.Take(5)) && mayhem.Enemy.Team.SequenceEqual(ids.Skip(5)),
            "Teams follow the self player's side, not the team number"
        );
        var capabilities = (JArray)teams[0]!["capabilities"]!;
        Check(
            capabilities.Count == 7
                && capabilities.All(c => (double)c["percentile"]! is >= 0 and <= 1)
                && capabilities.All(c => ((bool)c["lack"]! ? "D" : (string)c["grade"]!) == (string?)c["grade"]),
            "Seven capabilities with a percentile and a grade; gaps are D"
        );
        Check(
            ((string)teams[0]!["winRate"]!).StartsWith("估计胜率 ") && teams[0]!["pairs"] is JArray { Count: <= 3 },
            "Mayhem shows the estimate and at most three pairs"
        );
        Check(((string)teams[1]!["damage"]!).Contains("前排"), "Damage mix and front line are described");

        var aram = GameBarComposition.Build(index, grades, Roster(450, "ARAM"), Name)!;
        Check(
            ((JArray)aram.Json["teams"]!).All(t => t["winRate"] == null && t["pairs"] == null),
            "Normal ARAM leaves out the Mayhem-trained estimate"
        );
        Check(
            GameBarComposition.Build(
                index,
                grades,
                new LoadingRoster("1", 450, "ARAM", [new("a", "", "", 1, ids[0], 100, false)]),
                Name
            ) == null,
            "Without a self player there is nothing to compare"
        );

        Chat(mayhem);
        Bridge(mayhem);
        Console.WriteLine("PASS: Game Bar composition page, Mayhem-only estimate, chat line and snapshot.");
    }

    private static OverlayChatRequest Request(string target) =>
        new("id", "s", target, -1, 0, 0, false, false, "composition");

    private static void Chat(GameComposition composition)
    {
        string own = GameBarComposition.ChatMessage(composition, Request("own")).Single();
        string enemy = GameBarComposition.ChatMessage(composition, Request("enemy")).Single();
        Check(
            own.StartsWith("【海克斯大乱斗·我方阵容能力】") && enemy.StartsWith("【海克斯大乱斗·对方阵容能力】"),
            "Each side's line says which team it describes: " + own
        );
        Check(own.Length <= 180 && enemy.Length <= 180, "Lines fit one chat message");

        bool Rejected(Action action)
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

        Check(
            Rejected(() => GameBarComposition.ChatMessage(null, Request("own")))
                && Rejected(() => GameBarComposition.ChatMessage(composition, Request("single"))),
            "Nothing is sent before the analysis is read or for a single hero"
        );

        var json = new JObject
        {
            ["protocol"] = 1,
            ["id"] = Guid.NewGuid().ToString(),
            ["session"] = "s",
            ["target"] = "enemy",
            ["kind"] = "composition",
            ["slot"] = -1,
            ["championId"] = 0,
            ["updatedAt"] = 1000,
            ["dismissed"] = false,
            ["cancelled"] = false,
        };
        Check(OverlayChatRequest.Parse(json, "s", new int[10], 2000)?.Kind == "composition", "The request parses");
    }

    private static void Bridge(GameComposition composition)
    {
        string folder = Path.Combine(Path.GetTempPath(), "league-composition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var bridge = new GameBarBridge(folder);
        try
        {
            var roster = new LoadingRoster("1", 2400, "KIWI", [new("self", "", "", 1, 1, 100, true)]);
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
                composition: composition.Json
            );
            var snapshot = JObject.Parse(
                BridgeCheckFiles
                    .ReadAsync(Path.Combine(folder, "snapshot.json"), "Composition snapshot is written")
                    .GetAwaiter()
                    .GetResult()
            );
            Check(
                (int?)snapshot["compositionProtocol"] == 1
                    && (bool?)snapshot["chatComposition"] == true
                    && (string?)snapshot["composition"]?["revision"] == (string?)composition.Json["revision"],
                "The snapshot carries the composition page"
            );
        }
        finally
        {
            bridge.Dispose();
            bridge.Completion.GetAwaiter().GetResult();
            Directory.Delete(folder, true);
        }
    }
}
