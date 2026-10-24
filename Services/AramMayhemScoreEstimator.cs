using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>
/// Estimates the score WeGame gives each player of an ARAM: Mayhem game, from the stats every match record carries.
/// The weights and the per-champion offsets were fitted on WeGame's own scores (Assets/Scores/SOURCES.md): each
/// stat is compared with the game's best or average, which is what the scores follow. It is not WeGame's number:
/// about 0.7 points off on average, and the top score falls on the right player about four games in five.
/// </summary>
public sealed class AramMayhemScoreEstimator
{
    private const string ResourceName = "League.Scores.aram-mayhem-score.json";
    private const double Lowest = 0;
    private const double Highest = 20;

    private static readonly string[] RequiredStats =
    [
        "teamId",
        "championId",
        "kills",
        "deaths",
        "goldEarned",
        "totalDamageTaken",
        "totalDamageDealtToChampions",
        "timeCCingOthers",
    ];

    private static readonly Lazy<AramMayhemScoreEstimator> Embedded = new(() =>
    {
        using var stream =
            typeof(AramMayhemScoreEstimator).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("缺少海克斯大乱斗评分模型资源。");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    });

    private readonly HashSet<int> _queues;
    private readonly double _intercept;
    private readonly Weights _weights;
    private readonly Dictionary<int, double> _championOffsets;

    private AramMayhemScoreEstimator(
        HashSet<int> queues,
        double intercept,
        Weights weights,
        Dictionary<int, double> championOffsets
    )
    {
        _queues = queues;
        _intercept = intercept;
        _weights = weights;
        _championOffsets = championOffsets;
    }

    /// <summary>The model the application ships.</summary>
    public static AramMayhemScoreEstimator Shared => Embedded.Value;

    private sealed record Weights(
        double GoldShareOfBest,
        double DeathsZScore,
        double DamageTakenShareOfBest,
        double ChampionDamageShareOfBest,
        double KillsShareOfBest,
        double CrowdControlToEnemyAverage
    );

    /// <summary>Reads a model file in the format of Assets/Scores/aram-mayhem-score.json.</summary>
    public static AramMayhemScoreEstimator Parse(string json)
    {
        var root = JObject.Parse(json);
        var weights = root["weights"] as JObject ?? throw new InvalidDataException("评分模型缺少权重。");
        double Weight(string name) =>
            weights[name]?.Value<double>() ?? throw new InvalidDataException($"评分模型缺少权重 {name}。");
        return new(
            (root["queues"] as JArray ?? []).Select(q => q.Value<int>()).ToHashSet(),
            root["intercept"]?.Value<double>() ?? 0,
            new Weights(
                Weight("goldShareOfBest"),
                Weight("deathsZScore"),
                Weight("damageTakenShareOfBest"),
                Weight("championDamageShareOfBest"),
                Weight("killsShareOfBest"),
                Weight("crowdControlToEnemyAverage")
            ),
            (root["championOffsets"] as JObject ?? [])
                .Properties()
                .ToDictionary(
                    p => int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture),
                    p => p.Value.Value<double>()
                )
        );
    }

    public bool Supports(int queueId) => _queues.Contains(queueId);

    /// <summary>
    /// WeGame names the best score of the winning team MVP and the best of the losing team SVP (every one of 111 and
    /// 108 sampled games). With the estimates in their place the pick matches WeGame's in about 86% of games.
    /// </summary>
    public static MatchHonour Honour(IReadOnlyList<JObject> participants, IReadOnlyList<double> estimates, int index)
    {
        if (participants[index]["win"]?.Type != JTokenType.Boolean)
        {
            return MatchHonour.None;
        }

        bool win = participants[index]["win"]!.Value<bool>();
        // Ties go to the first listed player, so a team never shows two.
        int best = Enumerable
            .Range(0, participants.Count)
            .Where(i =>
                participants[i]["win"]?.Type == JTokenType.Boolean && participants[i]["win"]!.Value<bool>() == win
            )
            .OrderByDescending(i => estimates[i])
            .First();
        return best != index ? MatchHonour.None
            : win ? MatchHonour.Mvp
            : MatchHonour.Svp;
    }

    /// <summary>
    /// The estimate and honour of the match's own player (its CurrPuuid); null outside ARAM: Mayhem or when the
    /// player is not among the participants.
    /// </summary>
    public OwnMatchScore? Own(MatchInfo match)
    {
        if (
            string.IsNullOrEmpty(match.CurrPuuid)
            || match.AllParticipants == null
            || Estimate(match) is not double[] estimates
        )
        {
            return null;
        }

        int index = match.AllParticipants.FindIndex(p => (string?)p["puuid"] == match.CurrPuuid);
        return index < 0 ? null : new(estimates[index], Honour(match.AllParticipants, estimates, index));
    }

    /// <summary>The estimate and honour of one participant of a game; null when the game cannot be estimated.</summary>
    public OwnMatchScore? Score(IReadOnlyList<JObject> participants, int queueId, int index) =>
        index >= 0 && index < participants.Count && Estimate(participants, queueId) is double[] estimates
            ? new(estimates[index], Honour(participants, estimates, index))
            : null;

    /// <summary>The estimates for a match record, in the order of its participants; its queue reads "q_2400".</summary>
    public double[]? Estimate(MatchInfo match) =>
        match.AllParticipants != null && int.TryParse(match.QueueId?.Replace("q_", ""), out int queue)
            ? Estimate(match.AllParticipants, queue)
            : null;

    /// <summary>The champion's own adjustment; 0 for a champion the sample never saw.</summary>
    public double ChampionOffset(int championId) => _championOffsets.GetValueOrDefault(championId);

    /// <summary>
    /// One estimate per participant, in the order given; null when the queue is not modelled or any participant
    /// lacks one of the stats, since every term compares a player with the whole game.
    /// </summary>
    public double[]? Estimate(IReadOnlyList<JObject> participants, int queueId)
    {
        if (
            !Supports(queueId)
            || participants.Count == 0
            || participants.Any(p => RequiredStats.Any(key => p[key]?.Type != JTokenType.Integer))
        )
        {
            return null;
        }

        double Stat(JObject p, string key) => p[key]!.Value<double>();
        double ShareOfBest(JObject p, string key)
        {
            double best = participants.Max(other => Stat(other, key));
            return best > 0 ? Stat(p, key) / best : 0;
        }

        double meanDeaths = participants.Average(p => Stat(p, "deaths"));
        double deathsSpread = Math.Sqrt(participants.Average(p => Math.Pow(Stat(p, "deaths") - meanDeaths, 2)));
        return participants
            .Select(p =>
            {
                var enemies = participants.Where(other => Stat(other, "teamId") != Stat(p, "teamId")).ToArray();
                double enemyControl = enemies.Length > 0 ? enemies.Average(e => Stat(e, "timeCCingOthers")) : 0;
                double score =
                    _intercept
                    + _weights.GoldShareOfBest * ShareOfBest(p, "goldEarned")
                    + _weights.DeathsZScore * (deathsSpread > 0 ? (Stat(p, "deaths") - meanDeaths) / deathsSpread : 0)
                    + _weights.DamageTakenShareOfBest * ShareOfBest(p, "totalDamageTaken")
                    + _weights.ChampionDamageShareOfBest * ShareOfBest(p, "totalDamageDealtToChampions")
                    + _weights.KillsShareOfBest * ShareOfBest(p, "kills")
                    + _weights.CrowdControlToEnemyAverage
                        * (enemyControl > 0 ? Stat(p, "timeCCingOthers") / enemyControl : 0)
                    + ChampionOffset((int)Stat(p, "championId"));
                return Math.Clamp(score, Lowest, Highest);
            })
            .ToArray();
    }
}
