using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;
using static StatisticsFixtures;

internal static class HistoryDataChecks
{
    public static void Run()
    {
        MatchResults();
        OutcomePaging();
        CareerRecords();
    }

    private static void MatchResults()
    {
        var game = Game();
        var remake = (JObject)game.DeepClone();
        remake["gameId"] = 2;
        remake["participants"]![0]!["stats"]!["gameEndedInEarlySurrender"] = true;
        Check(MatchOutcomeFilter.GetWin(remake, "self") == null, "Remakes excluded from win/loss filters");
        using (
            var remakeInfo = new MatchInfo
            {
                ResultText = "胜利",
                RawGameData = new JObject { ["json"] = new JObject { ["info"] = remake.DeepClone() } },
            }
        )
        {
            Check(
                remakeInfo.ResultText == "重开" && remakeInfo.ResultColor == MatchResult.RemakeColor,
                "Remake overrides win text and color"
            );
        }

        Check(MatchResult.IsRemake(new JObject { ["gameEndedInEarlySurrender"] = true }), "Root remake flag");
        Check(
            MatchResult.IsRemake(null, new[] { new JObject { ["gameEndedInEarlySurrender"] = true } }),
            "Flattened participant remake flag"
        );
        Check(
            !MatchResult.IsRemake(new JObject { ["gameDuration"] = 90, ["gameEndedInSurrender"] = true }),
            "Short matches and ordinary surrenders are not assumed remakes"
        );
        Check(
            MatchOutcomeFilter.GetWin(game, "self") == true && MatchOutcomeFilter.GetWin(game, "other") == null,
            "Outcome filtering must select the viewed player"
        );
        var lost = (JObject)game.DeepClone();
        lost["participants"]![0]!["stats"]!["win"] = false;
        Check(
            MatchOutcomeFilter.GetWin(new JObject { ["json"] = new JObject { ["info"] = lost } }, "self") == false,
            "SGP nested defeat filtering"
        );
        var noResult = (JObject)game.DeepClone();
        ((JObject)noResult["participants"]![0]!["stats"]!).Remove("win");
        Check(MatchOutcomeFilter.GetWin(noResult, "self") == null, "Missing result must not count as defeat");
    }

    private static void OutcomePaging()
    {
        var game = Game();
        var lost = (JObject)game.DeepClone();
        lost["participants"]![0]!["stats"]!["win"] = false;
        var source = new JArray(game, lost, game, lost, game);
        var pager = new OutcomeHistoryPager();
        var offsets = new List<int>();
        Task<JArray> FetchOutcomes(int offset, int count)
        {
            offsets.Add(offset);
            return Task.FromResult(new JArray(source.Skip(offset).Take(count)));
        }
        Check(
            pager.LoadPage("self", true, 1, 2, FetchOutcomes).GetAwaiter().GetResult().Count == 2
                && pager.Scanned == 3
                && !pager.Exhausted,
            "Stop scanning as soon as first result page is full"
        );
        int requests = offsets.Count;
        pager.LoadPage("self", true, 1, 2, FetchOutcomes).GetAwaiter().GetResult();
        Check(offsets.Count == requests, "Previously loaded pages must not fetch again");
        Check(
            pager.LoadPage("self", true, 2, 2, FetchOutcomes).GetAwaiter().GetResult().Count == 1
                && offsets[requests] == 3
                && pager.Exhausted,
            "Continue from cursor and preserve final partial page"
        );
        requests = offsets.Count;
        Check(
            pager.LoadPage("self", false, 1, 2, FetchOutcomes).GetAwaiter().GetResult().Count == 2
                && offsets.Count == requests,
            "Changing outcome reuses scanned records"
        );
    }

    private static void CareerRecords()
    {
        var game = Game();
        var remake = (JObject)game.DeepClone();
        remake["gameId"] = 2;
        remake["participants"]![0]!["stats"]!["gameEndedInEarlySurrender"] = true;
        var wrong = (JObject)game.DeepClone();
        wrong["gameId"] = 3;
        wrong["participants"]![0]!["puuid"] = "other";
        var records = new JArray(game, game.DeepClone(), remake, wrong);
        var stats = CareerStatistics.Parse(records, "self");
        Check(stats.Games.Count == 1 && stats.Kda == 15 && stats.Wins == 1, "Deduplication/zero-death KDA");
        Check(stats.ExcludedRemakes == 1 && stats.Unrecognized == 1, "Remake/player identity filtering");
        var recentWithRemakes = CareerStatistics.Parse(records, "self", includeRemakes: true);
        Check(
            recentWithRemakes.ExcludedRemakes == 0 && recentWithRemakes.Games.Count == stats.Games.Count + 1,
            "Recent activity includes remakes while retaining deduplication"
        );
        var missing = (JObject)game.DeepClone();
        ((JObject)missing["participants"]![0]!["stats"]!).Remove("kills");
        Check(CareerStatistics.Parse(new JArray(missing), "self").Kda == null, "Missing kills must not become zero");
        Check(CareerStatistics.Records(new JObject { ["games"] = "invalid" }) == null, "Unknown record shape");
        var sgp = new JObject { ["json"] = new JObject { ["info"] = game.DeepClone() } };
        Check(CareerStatistics.Parse(new JArray(sgp), "self").Games.Count == 1, "SGP info wrapper");
    }
}
