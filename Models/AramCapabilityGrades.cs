using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>
/// Grades each capability total against random teams of the same size drawn from the statistics index; ARAM hands
/// out champions at random, so uniform draws approximate real teams. A total below its gap threshold is D, the rest
/// rank by percentile from C to S+. Only the threshold enters arammeta's estimate: grades place a team among other
/// teams and are not a proven win rate effect.
/// </summary>
public sealed class AramCapabilityGrades
{
    private const int Samples = 20000;

    // A fixed seed keeps the same roster on the same grades across renders and restarts.
    private const int Seed = 1;

    private const int MaxTeamSize = 5;

    // Lower percentile bound of each grade, best first.
    private static readonly (double From, string Grade)[] Bands =
    [
        (0.98, "S+"),
        (0.95, "S"),
        (0.90, "S-"),
        (0.80, "A+"),
        (0.70, "A"),
        (0.60, "A-"),
        (0.50, "B+"),
        (0.40, "B"),
        (0.30, "B-"),
        (0, "C"),
    ];

    // Sorted sample totals per capability key, indexed by team size minus one.
    private readonly Dictionary<string, double[][]> _totals;

    private AramCapabilityGrades(Dictionary<string, double[][]> totals)
    {
        _totals = totals;
    }

    public const string Gap = "D";

    public static AramCapabilityGrades Build(AramMetaIndex index)
    {
        string[] keys = AramTeamScore.CapabilityKeys.ToArray();
        double[][] values = index
            .Champions.Select(champion => champion.Data["comp"] as JObject)
            .OfType<JObject>()
            .Select(comp => keys.Select(key => AramTeamScore.Number(comp[key])).ToArray())
            .ToArray();
        var totals = keys.ToDictionary(key => key, _ => new double[MaxTeamSize][]);
        if (values.Length == 0)
        {
            return new(totals);
        }

        var random = new Random(Seed);
        int[] order = Enumerable.Range(0, values.Length).ToArray();
        for (int size = 1; size <= MaxTeamSize; size++)
        {
            int picks = Math.Min(size, values.Length);
            double[][] sums = keys.Select(_ => new double[Samples]).ToArray();
            for (int sample = 0; sample < Samples; sample++)
            {
                // A partial Fisher–Yates shuffle draws distinct champions, as a team holds each champion once.
                for (int pick = 0; pick < picks; pick++)
                {
                    int swap = random.Next(pick, order.Length);
                    (order[pick], order[swap]) = (order[swap], order[pick]);
                    for (int key = 0; key < keys.Length; key++)
                    {
                        sums[key][sample] += values[order[pick]][key];
                    }
                }
            }

            for (int key = 0; key < keys.Length; key++)
            {
                Array.Sort(sums[key]);
                totals[keys[key]][size - 1] = sums[key];
            }
        }

        return new(totals);
    }

    /// <summary>The share of random teams of this size whose total is below the capability's total.</summary>
    public double Percentile(AramTeamCapability capability, int size)
    {
        if (!_totals.TryGetValue(capability.Key, out var bySize) || bySize[Size(size) - 1] is not { } sorted)
        {
            return 0;
        }

        int below = LowerBound(sorted, capability.Sum);
        return (double)below / sorted.Length;
    }

    public string Grade(AramTeamCapability capability, int size)
    {
        if (capability.Lack)
        {
            return Gap;
        }

        double percentile = Percentile(capability, size);
        return Bands.First(band => percentile >= band.From).Grade;
    }

    private static int Size(int size) => Math.Clamp(size, 1, MaxTeamSize);

    private static int LowerBound(double[] sorted, double value)
    {
        int low = 0,
            high = sorted.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (sorted[middle] < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
