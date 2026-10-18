using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Support;

/// <summary>
/// arammeta's own draft calculation, captured from the site: one statistics index of ten champions and seven team
/// evaluations whose expected numbers arammeta's site.js (evaluateFullTeam and teamComposition) produced. Copied
/// from Tests/StatisticsChecks/Fixtures/AramMeta/team-score.json; see the README beside it.
/// </summary>
internal static class AramTeamScoreFixture
{
    private static readonly JObject Raw = JObject.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "AramMeta", "team-score.json"))
    );

    public static AramMetaIndex Index { get; } = AramMetaIndex.Parse((JObject)Raw["index"]!);

    /// <summary>One expected row: the team, then the numbers arammeta produced for it.</summary>
    public static TheoryData<int[], double, double, double, double, double, int, string[]> Cases
    {
        get
        {
            var cases = new TheoryData<int[], double, double, double, double, double, int, string[]>();
            foreach (var row in Raw["expected"]!.Cast<JObject>())
            {
                cases.Add(
                    row["team"]!.Values<int>().ToArray(),
                    (double)row["baseWr"]!,
                    (double)row["pairLift"]!,
                    (double)row["compositionScore"]!,
                    (double)row["estWr"]!,
                    (double)row["adShare"]!,
                    (int)row["pairN"]!,
                    row["lacks"]!.Values<string>().Where(lack => lack != null).Cast<string>().ToArray()
                );
            }

            return cases;
        }
    }
}
