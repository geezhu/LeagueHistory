using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// A client session that still carries players from an earlier game: the user on both teams, the live game showing
/// fewer players. The roster keeps one entry per account and marks the conflict, the main window keeps only the live
/// game's champions, and nothing throws on a repeated key. Also checks that the regression run uses its own data
/// folder.
/// </summary>
internal static class StaleSessionChecks
{
    private const string Self = "0f8fad5b-d9cb-469f-a165-70867728950e";

    public static void Run()
    {
        var flow = Flow();
        Roster(flow);
        MainWindowTeams((JObject)flow["gameData"]!);
        DataFolder();
        Console.WriteLine(
            "PASS: stale game sessions, one entry per account, live-game filtering and test data isolation."
        );
    }

    // The session seen in a solo Mayhem game: the user alone on team one, and on team two again among four leftovers.
    private static JObject Flow()
    {
        JObject Player(string puuid, int champion, long id) =>
            new()
            {
                ["puuid"] = puuid,
                ["championId"] = champion,
                ["summonerId"] = id,
            };
        return new JObject
        {
            ["gameData"] = new JObject
            {
                ["gameId"] = "1",
                ["queue"] = new JObject { ["id"] = 3270, ["gameMode"] = "KIWI" },
                ["teamOne"] = new JArray(Player(Self, 120, 9)),
                ["teamTwo"] = new JArray(
                    Player(Guid.NewGuid().ToString(), 53, 1),
                    Player(Guid.NewGuid().ToString(), 22, 2),
                    Player(Self, 4, 9),
                    Player(Guid.NewGuid().ToString(), 223, 3),
                    Player(Guid.NewGuid().ToString(), 63, 4)
                ),
                ["playerChampionSelections"] = new JArray(new JObject { ["puuid"] = Self, ["championId"] = 120 }),
            },
        };
    }

    private static void Roster(JObject flow)
    {
        var roster = LoadingRoster.Parse(flow, Self);
        var self = roster.Players.Where(p => p.Self).ToArray();
        Check(roster.Conflicted && roster.Players.Length == 5, "The repeated account is kept once and flagged");
        Check(
            self.Length == 1 && self[0].ChampionId == 120 && self[0].Team == 100,
            "The entry matching the pick stays"
        );

        // The poll builds the death streaks from this roster; a repeated key used to throw and stall every poll.
        var duplicated = roster with
        {
            Players = [.. roster.Players, roster.Players[0]],
        };
        Check(LiveDeathStreakAnalysis.ForRoster(duplicated, null).Count == 5, "Death streaks tolerate a repeated key");

        var clean = new JObject
        {
            ["gameData"] = new JObject
            {
                ["teamOne"] = new JArray(new JObject { ["puuid"] = Self, ["championId"] = 1 }),
                ["teamTwo"] = new JArray(new JObject { ["puuid"] = Guid.NewGuid().ToString(), ["championId"] = 2 }),
            },
        };
        Check(!LoadingRoster.Parse(clean, Self).Conflicted, "An ordinary session is not flagged");
    }

    private static void MainWindowTeams(JObject gameData)
    {
        var kept = GameSessionTeams.KeepLive(gameData, new HashSet<int> { 120 });
        Check(
            ((JArray)kept["teamOne"]!).Single()["championId"]!.Value<int>() == 120
                && ((JArray)kept["teamTwo"]!).Count == 0
                && ((JArray)kept["playerChampionSelections"]!).Count == 1,
            "Only players whose champion is in the live game stay"
        );
        Check(((JArray)gameData["teamTwo"]!).Count == 5, "The original session is not changed");
    }

    private static void DataFolder()
    {
        string root = Environment.GetEnvironmentVariable(AppDataPaths.RootVariable) ?? "";
        Check(
            root.Length > 0
                && AppDataPaths.LocalData == root
                && LogRetention.LogsDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && League.Managers.LeagueConfigManager.ConfigPath.StartsWith(root, StringComparison.OrdinalIgnoreCase),
            "Checks write settings and logs under their own folder, not the user's"
        );
    }
}
