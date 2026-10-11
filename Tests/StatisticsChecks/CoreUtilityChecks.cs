using League.Clients;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

internal static class CoreUtilityChecks
{
    public static void Run()
    {
        SearchHistory();
        ClientDiscovery();
        MapNames();
        MetricSelection();
        DisplayedValues();
    }

    private static void SearchHistory()
    {
        string historyFile = Path.Combine(
            Path.GetTempPath(),
            "league-search-" + Guid.NewGuid().ToString("N") + ".json"
        );
        try
        {
            var history = new League.Services.PlayerSearchHistory(historyFile);
            history.Add("玩家#123");
            history.Add("Other#456");
            history.Add(" 玩家#123 ");
            Check(
                history.Items.Count == 2 && history.Items[0] == "玩家#123",
                "Search history deduplicates and promotes"
            );
            Check(
                new League.Services.PlayerSearchHistory(historyFile).Items.SequenceEqual(history.Items),
                "Search history survives reload"
            );
            history.Add("invalid");
            Check(history.Items.Count == 2, "Invalid ID excluded");
            for (int i = 0; i < 25; i++)
            {
                history.Add($"Player{i}#123");
            }

            Check(history.Items.Count == 20 && history.Items[0] == "Player24#123", "Search history bounded");
            history.Remove("player24#123");
            Check(history.Items.Count == 19, "History removal ignores case");
            history.Clear();
            Check(new League.Services.PlayerSearchHistory(historyFile).Items.Count == 0, "History clear persists");
            File.WriteAllText(historyFile, "invalid json");
            Check(
                new League.Services.PlayerSearchHistory(historyFile).Items.Count == 0,
                "Damaged history does not prevent startup"
            );
        }
        finally
        {
            if (File.Exists(historyFile))
            {
                File.Delete(historyFile);
            }
        }
    }

    private static void ClientDiscovery()
    {
        Check(
            LcuProcessDiscovery.TryParse(
                "\"C:\\Program Files\\LeagueClientUx.exe\" \"--app-port=12345\" --remoting-auth-token=\"test-value\"",
                out var connectionPort,
                out var connectionToken
            )
                && connectionPort == "12345"
                && connectionToken == "test-value",
            "Quoted LCU arguments"
        );
        Check(
            !LcuProcessDiscovery.TryParse("client.exe --app-port=70000 --remoting-auth-token=test", out _, out _),
            "Reject invalid port"
        );
        Check(
            !LcuProcessDiscovery.TryParse("client.exe --app-port=12345", out _, out _),
            "Reject missing authentication"
        );
        Check(
            LcuProcessDiscovery.TryReadCommandLine(Environment.ProcessId, out var ownCommandLine, out _)
                && ownCommandLine.Length > 0,
            "Native command line query"
        );
    }

    private static void MapNames()
    {
        Check(MatchMap.GetName(null) == "未知地图", "Missing map must not be inferred from queue");
        Check(
            MatchMap.GetName(JObject.Parse("{\"json\":{\"info\":{\"mapId\":12}}}")) == "随机地图",
            "ARAM ID must not imply a specific bridge"
        );
        Check(MatchMap.GetName(new JObject { ["mapId"] = 11 }) == "召唤师峡谷", "Rift map name");
        foreach (
            var (mutator, name) in new[]
            {
                ("MapSkin_Map12_Bloom", "莲华栈桥"),
                ("MapSkin_HA_Bilgewater", "屠夫之桥"),
                ("MapSkin_HA_Crepe", "进步之桥"),
            }
        )
        {
            var mapData = new JObject
            {
                ["mapId"] = 12,
                ["gameModeMutators"] = new JArray("ARAM", mutator.ToLowerInvariant()),
            };
            Check(
                MatchMap.GetName(new JObject { ["json"] = new JObject { ["info"] = mapData } }) == name,
                "Map variants in nested SGP data"
            );
            mapData["mapId"] = 11;
            Check(MatchMap.GetName(mapData) == "召唤师峡谷", "Bridge overrides apply only to map 12");
        }
        Check(
            MatchMap.GetName(new JObject { ["mapId"] = 12, ["gameModeMutators"] = new JArray() }) == "嚎哭深渊",
            "Explicit base map without variant"
        );
    }

    private static void MetricSelection()
    {
        var metricSelection = new MatchMetricSelection();
        Check(metricSelection.Set(new[] { "a", "b", "c", "d", "e", "f" }, true), "Six chart metrics allowed");
        Check(
            !metricSelection.Set(new[] { "g", "h" }, true)
                && metricSelection.Count == 6
                && !metricSelection.Contains("g"),
            "Over-limit group is atomic"
        );
        Check(
            metricSelection.Set(new[] { "a", "b" }, false) && metricSelection.Set(new[] { "g", "h" }, true),
            "Chart slots reusable after deselection"
        );
        Check(
            metricSelection.Set(new[] { "g", "g" }, true) && metricSelection.Count == 6,
            "Duplicate selection does not consume slots"
        );
        Check(
            metricSelection.SetGroup(new[] { "x", "y" }, true)
                && metricSelection.Count == 2
                && !metricSelection.Contains("g"),
            "Full selection replaced by another group"
        );
        Check(
            metricSelection.SetGroup(new[] { "x", "y" }, false) && metricSelection.Count == 0,
            "Selected group can be deselected"
        );
    }

    private static void DisplayedValues()
    {
        Check(
            MatchMetricCatalog.Number(new JObject(), "goldEarned") == null
                && MatchMetricCatalog.Number(new JObject { ["goldEarned"] = 0 }, "goldEarned") == 0,
            "Chart distinguishes zero and missing"
        );
        Check(
            MatchStatisticsView.Value(new JObject { ["goldEarned"] = 0 }, "goldEarned") == "0",
            "Statistics preserves zero"
        );
        Check(MatchStatisticsView.Value(new JObject(), "goldEarned") == "—", "Statistics preserves missing values");
        Check(
            MatchStatisticsView.Value(JObject.Parse("{\"stats\":{\"kills\":1,\"deaths\":0,\"assists\":2}}"), "kda")
                == "1/0/2",
            "Statistics supports nested stats"
        );
        Check(
            MatchStatisticsView.Value(new JObject { ["firstBloodKill"] = false }, "firstBloodKill") == "○",
            "Statistics preserves false"
        );
        var rankNames = RankedStats.FromJson(
            JObject.Parse(
                """
                {"queueMap":{"RANKED_SOLO_5x5":{"tier":"PLATINUM","division":"II","highestTier":"MASTER","previousSeasonEndTier":"DIAMOND","previousSeasonEndDivision":"II"}}}
                """
            )
        )["单双排"];
        Check(
            rankNames.FormattedTier == "华贵铂金 II"
                && rankNames.HighestTierDisplay == "超凡大师"
                && rankNames.PreviousSeasonDisplay == "璀璨钻石 II",
            "Full Chinese rank names in all profile fields"
        );
    }
}
