using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record AramTeamCapability(string Key, string Name, double Sum, double Threshold, bool Lack);

public sealed record AramTeamPair(int First, int Second, double Lift, double Games);

public sealed record AramTeamSwap(int ChampionId, double Delta, AramTeamCapability[] Fixed, AramTeamCapability[] Added);

public sealed record AramTeamEvaluation(
    int[] Team,
    int[] Missing,
    double BaseWinRate,
    double PairLift,
    double CompositionScore,
    double EstimatedWinRate,
    AramTeamCapability[] Capabilities,
    double AdShare,
    int FrontCount,
    AramTeamPair[] Pairs,
    int PairEdges,
    string Confidence
)
{
    public AramTeamCapability[] Lacks => Capabilities.Where(capability => capability.Lack).ToArray();
}

/// <summary>
/// Team estimate from arammeta's trained data (MIT), ported from its Draft page (site.js
/// evaluateFullTeam, teamComposition, teamCompositionScore, pairLiftBetween). Inputs are the cached
/// statistics index: per-champion "comp", "pairs", "tags" and "wr", plus "team_score" and
/// "recommendation_composition". The model is trained on ARAM: Mayhem games.
/// </summary>
public static class AramTeamScore
{
    // Display order; keys match arammeta "comp" and "lack_thresholds".
    private static readonly (string Key, string Name)[] CapabilityNames =
    [
        ("front", "前排"),
        ("cc", "控制"),
        ("engage", "开团"),
        ("damage", "伤害"),
        ("poke", "消耗"),
        ("wave", "清线"),
        ("sustain", "续航"),
    ];

    private static readonly string[] SumKeys =
    [
        "phys",
        "magic",
        "true",
        "wave",
        "cc",
        "engage",
        "damage",
        "poke",
        "sustain",
        "front",
    ];

    public static AramTeamEvaluation Evaluate(AramMetaIndex index, IReadOnlyList<int> team)
    {
        var champions = index.Champions.ToDictionary(champion => champion.Id);
        int[] ids = team.Where(id => id > 0).ToArray();
        var known = ids.Where(champions.ContainsKey).ToArray();
        var rates = known.Select(id => champions[id].WinRate).OfType<double>().ToArray();
        double baseWr = rates.Length > 0 ? rates.Average() : 0.5;

        double liftSum = 0;
        int pairEdges = 0;
        double minGames = double.PositiveInfinity;
        var pairs = new List<AramTeamPair>();
        for (int i = 0; i < ids.Length; i++)
        {
            for (int j = i + 1; j < ids.Length; j++)
            {
                pairEdges++;
                if (PairLift(index, champions, ids[i], ids[j]) is not { } hit)
                {
                    continue;
                }

                liftSum += hit.Lift;
                pairs.Add(new(ids[i], ids[j], hit.Lift, hit.Games));
                if (hit.Games > 0)
                {
                    minGames = Math.Min(minGames, hit.Games);
                }
            }
        }

        // Missing pair data counts as zero over all C(n,2) edges, as in arammeta.
        double pairSignal = pairEdges > 0 ? liftSum / pairEdges : 0;
        var composition = Composition(index, champions, ids);
        double compositionSignal = CompositionScore(index, composition, ids.Length);
        double pairLift = pairSignal;
        double compositionScore = compositionSignal;
        double estimate = baseWr + pairLift + compositionScore;
        var config = index.Raw["team_score"] as JObject;
        if ((string?)config?["kind"] == "logit_v2")
        {
            double p = Math.Clamp(baseWr, 1e-6, 1 - 1e-6);
            double baseLogit = Math.Log(p / (1 - p));
            double pairWeight = Math.Max(0, Number(config["pair_logit_weight"]));
            double compositionWeight = Math.Max(0, Number(config["composition_logit_weight"]));
            double afterPair = Sigmoid(baseLogit + pairWeight * pairSignal);
            estimate = Sigmoid(baseLogit + pairWeight * pairSignal + compositionWeight * compositionSignal);
            pairLift = afterPair - baseWr;
            compositionScore = estimate - afterPair;
        }

        string confidence =
            pairs.Count >= 8 && minGames >= 40 ? "高"
            : pairs.Count >= 5 && minGames >= 25 ? "中"
            : "低";
        return new(
            ids,
            ids.Where(id => !champions.ContainsKey(id)).ToArray(),
            baseWr,
            pairLift,
            compositionScore,
            Math.Clamp(estimate, 0.35, 0.65), // arammeta's product guardrail
            composition.Capabilities,
            composition.AdShare,
            composition.FrontCount,
            pairs.ToArray(),
            pairEdges,
            confidence
        );
    }

    private sealed record TeamComposition(
        AramTeamCapability[] Capabilities,
        double AdShare,
        int FrontCount,
        int Mages,
        int Marksmen,
        int LackCount
    );

    private static TeamComposition Composition(
        AramMetaIndex index,
        Dictionary<int, AramMetaChampion> champions,
        int[] ids
    )
    {
        var config = index.Raw["recommendation_composition"] as JObject;
        var thresholds = config?["lack_thresholds"] as JObject;
        var sums = SumKeys.ToDictionary(key => key, _ => 0.0);
        int frontCount = 0,
            mages = 0,
            marksmen = 0;
        foreach (int id in ids)
        {
            if (!champions.TryGetValue(id, out var champion))
            {
                continue;
            }

            var comp = champion.Data["comp"] as JObject;
            foreach (string key in SumKeys)
            {
                sums[key] += Number(comp?[key]);
            }

            if (Number(comp?["front"]) >= 2.0)
            {
                frontCount++;
            }

            mages += champion.Roles.Contains("Mage") ? 1 : 0;
            marksmen += champion.Roles.Contains("Marksman") ? 1 : 0;
        }

        int size = Math.Max(1, ids.Length);
        double thresholdScale = size / 5.0;
        double adDenominator = sums["phys"] + sums["magic"];
        var capabilities = CapabilityNames
            .Select(capability =>
            {
                double threshold = Number(thresholds?[capability.Key]);
                double sum = sums[capability.Key];
                return new AramTeamCapability(
                    capability.Key,
                    capability.Name,
                    sum,
                    threshold * thresholdScale,
                    threshold > 0 && sum < threshold * thresholdScale
                );
            })
            .ToArray();
        return new(
            capabilities,
            adDenominator > 0 ? sums["phys"] / adDenominator : 0.5,
            frontCount,
            mages,
            marksmen,
            capabilities.Count(capability => capability.Lack)
        );
    }

    private static double CompositionScore(AramMetaIndex index, TeamComposition composition, int size)
    {
        if (size == 0)
        {
            return 0;
        }

        var config = index.Raw["recommendation_composition"] as JObject;
        var weights = config?["table_weights"] as JObject;
        var tables = config?["tables"] as JObject;
        double clamp = Number(config?["clamp"]) is var value && value != 0 ? value : 0.05;
        double projection = 5.0 / size;
        string adBin = composition.AdShare switch
        {
            < 0.35 => "<35% AD",
            < 0.45 => "35-45% AD",
            < 0.55 => "45-55% AD",
            < 0.65 => "55-65% AD",
            _ => ">=65% AD",
        };
        string front = CountGroup(composition.FrontCount * projection) + " front";
        bool Lacks(string key) => composition.Capabilities.First(capability => capability.Key == key).Lack;
        double Table(string name, string key) => Number(tables?[name]?[key]);
        double Weight(string name) => Number(weights?[name]);
        double score =
            Weight("ad_front") * Table("ad_front", $"{front}|{adBin}")
            + Weight("poke_front") * Table("poke_front", $"{front}|{(Lacks("poke") ? "poke lack" : "poke ok")}")
            + Weight("wave_engage")
                * Table(
                    "wave_engage",
                    $"{(Lacks("wave") ? "wave lack" : "wave ok")}|{(Lacks("engage") ? "engage lack" : "engage ok")}"
                )
            + Weight("all_lacks") * Table("all_lacks", CountGroup(composition.LackCount * projection))
            + Weight("mage_ad") * Table("mage_ad", $"{CountGroup(composition.Mages * projection)}|{adBin}")
            + Weight("marksman_ad") * Table("marksman_ad", $"{CountGroup(composition.Marksmen * projection)}|{adBin}");
        double sizeWeight = Math.Clamp((size - 1) / 4.0, 0, 1);
        return Math.Clamp(score, -clamp, clamp) * sizeWeight;
    }

    /// <summary>
    /// Replaces the user's champion with each bench champion. Mayhem orders by the estimate change;
    /// normal ARAM, where the Mayhem-trained estimate is not shown, orders by gaps fixed minus gaps added.
    /// </summary>
    public static AramTeamSwap[] Swaps(
        AramMetaIndex index,
        AramTeamEvaluation current,
        int self,
        IEnumerable<int> bench,
        bool byWinRate
    )
    {
        var before = current.Lacks.Select(lack => lack.Key).ToHashSet();
        return bench
            .Where(id => id > 0 && id != self)
            .Distinct()
            .Select(id =>
            {
                var after = Evaluate(index, current.Team.Select(member => member == self ? id : member).ToArray());
                var now = after.Lacks.Select(lack => lack.Key).ToHashSet();
                return new AramTeamSwap(
                    id,
                    after.EstimatedWinRate - current.EstimatedWinRate,
                    current.Lacks.Where(lack => !now.Contains(lack.Key)).ToArray(),
                    after.Lacks.Where(lack => !before.Contains(lack.Key)).ToArray()
                );
            })
            .OrderByDescending(swap => byWinRate ? swap.Delta : swap.Fixed.Length - swap.Added.Length)
            .ThenByDescending(swap => swap.Delta)
            .ToArray();
    }

    // Each champion stores only ~24 pair rows, so A→B and B→A may each be missing; use what exists.
    private static (double Lift, double Games)? PairLift(
        AramMetaIndex index,
        Dictionary<int, AramMetaChampion> champions,
        int first,
        int second
    )
    {
        var ab = PairEntry(champions, first, second);
        var ba = PairEntry(champions, second, first);
        if (ab == null && ba == null)
        {
            return null;
        }

        double prior = Math.Max(0, Number(index.Raw["team_score"]?["pair_prior_games"]));
        double Adjusted(JObject row)
        {
            double games = Math.Max(0, Number(row["g"]));
            return games + prior > 0 ? Number(row["lift"]) * games / (games + prior) : 0;
        }
        if (ab != null && ba != null)
        {
            double ga = Number(ab["g"]),
                gb = Number(ba["g"]);
            return (
                (Adjusted(ab) + Adjusted(ba)) / 2,
                ga != 0 && gb != 0 ? Math.Min(ga, gb)
                : ga != 0 ? ga
                : gb
            );
        }

        var single = ab ?? ba!;
        return (Adjusted(single), Number(single["g"]));
    }

    private static JObject? PairEntry(Dictionary<int, AramMetaChampion> champions, int from, int to) =>
        champions.TryGetValue(from, out var champion)
            ? (champion.Data["pairs"] as JArray)?.OfType<JObject>().FirstOrDefault(row => (int?)row["id"] == to)
            : null;

    private static string CountGroup(double projected) =>
        projected < 0.5 ? "0"
        : projected < 1.5 ? "1"
        : "2+";

    private static double Sigmoid(double x) => 1 / (1 + Math.Exp(-x));

    // Mirrors JavaScript Number(x || 0): missing or non-numeric values count as zero.
    private static double Number(JToken? token) =>
        token?.Type is JTokenType.Float or JTokenType.Integer && double.IsFinite((double)token) ? (double)token : 0;
}
