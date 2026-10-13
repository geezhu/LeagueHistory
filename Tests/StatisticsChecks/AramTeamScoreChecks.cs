using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// Expected values in Fixtures/AramMeta/team-score.json were computed by arammeta's own site.js functions
/// (evaluateFullTeam and teamComposition) on a ten-champion slice of real statistics.
/// </summary>
internal static class AramTeamScoreChecks
{
    public static void Run()
    {
        var fixture = JObject.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/AramMeta/team-score.json"))
        );
        var index = AramMetaIndex.Parse((JObject)fixture["index"]!);
        foreach (JObject expected in fixture["expected"]!.Cast<JObject>())
        {
            int[] team = expected["team"]!.Values<int>().ToArray();
            var result = AramTeamScore.Evaluate(index, team);
            string label = string.Join(",", team);
            Close(result.BaseWinRate, expected["baseWr"]!, "champion base rate", label);
            Close(result.PairLift, expected["pairLift"]!, "pair lift", label);
            Close(result.CompositionScore, expected["compositionScore"]!, "composition score", label);
            Close(result.EstimatedWinRate, expected["estWr"]!, "estimated win rate", label);
            Close(result.AdShare, expected["adShare"]!, "physical damage share", label);
            Check(result.Pairs.Length == (int)expected["pairN"]!, $"Known pair count matches arammeta for {label}");
            Check(
                result
                    .Lacks.Select(lack => lack.Key)
                    .Order()
                    .SequenceEqual(expected["lacks"]!.Values<string>().Order()),
                $"Capability gaps match arammeta for {label}"
            );
        }

        var partial = AramTeamScore.Evaluate(index, [(int)fixture["expected"]![4]!["team"]![0]!, 99999]);
        Check(partial.Missing.SequenceEqual([99999]), "Unknown champions are reported instead of counted as zero");
        Console.WriteLine("PASS: team estimate, synergy, composition and gaps match arammeta's Draft calculation.");
    }

    private static void Close(double actual, JToken expected, string name, string team) =>
        Check(Math.Abs(actual - (double)expected) < 1e-9, $"{name} matches arammeta for {team}: {actual}");
}
