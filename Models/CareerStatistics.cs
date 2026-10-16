using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record CareerGame(
    long Id,
    long Created,
    int Queue,
    int Champion,
    bool Win,
    int? Kills,
    int? Deaths,
    int? Assists,
    long? Damage,
    long? Gold,
    int? Penta
);

public sealed class CareerStatistics
{
    public List<CareerGame> Games { get; } = new();
    public int ExcludedRemakes { get; private set; }
    public int Unrecognized { get; private set; }
    public int Wins => Games.Count(g => g.Win);
    public double? Kda =>
        Games.Count > 0 && Games.All(g => g.Kills.HasValue && g.Deaths.HasValue && g.Assists.HasValue)
            ? Games.Sum(g => (long)g.Kills!.Value + g.Assists!.Value)
                / (double)Math.Max(1, Games.Sum(g => (long)g.Deaths!.Value))
            : null;

    public static JArray? Records(JToken? token) =>
        token switch
        {
            JArray array => array,
            JObject obj => obj["games"] as JArray
                ?? (obj["games"] is JObject nested ? nested["games"] as JArray : null),
            _ => null,
        };

    public static CareerStatistics Parse(JArray records, string puuid, bool includeRemakes = false)
    {
        var result = new CareerStatistics();
        var seen = new HashSet<long>();
        foreach (var entry in records.OfType<JObject>())
        {
            var game = entry["json"] as JObject ?? entry;
            game = game["info"] as JObject ?? game;
            if (game["gameId"]?.Type != JTokenType.Integer || game["participants"] is not JArray players)
            {
                result.Unrecognized++;
                continue;
            }
            long id = game["gameId"]!.Value<long>();
            if (id <= 0 || seen.Contains(id))
            {
                continue;
            }

            var identity = (game["participantIdentities"] as JArray)
                ?.OfType<JObject>()
                .FirstOrDefault(p => p["player"]?["puuid"]?.ToString() == puuid);
            var player =
                players.OfType<JObject>().FirstOrDefault(p => p["puuid"]?.ToString() == puuid)
                ?? (
                    identity == null
                        ? null
                        : players
                            .OfType<JObject>()
                            .FirstOrDefault(p => JToken.DeepEquals(p["participantId"], identity["participantId"]))
                )
                ?? (
                    players.Count == 1 && string.IsNullOrWhiteSpace(players[0]?["puuid"]?.ToString())
                        ? players[0] as JObject
                        : null
                );
            var stats = player?["stats"] as JObject ?? player;
            if (stats == null || stats["win"]?.Type != JTokenType.Boolean)
            {
                result.Unrecognized++;
                continue;
            }
            seen.Add(id);
            if (!includeRemakes && MatchResult.IsRemake(game))
            {
                result.ExcludedRemakes++;
                continue;
            }
            int? Number(string key) => stats[key]?.Type == JTokenType.Integer ? stats[key]!.Value<int>() : null;
            long? Long(string key) => stats[key]?.Type == JTokenType.Integer ? stats[key]!.Value<long>() : null;
            result.Games.Add(
                new(
                    id,
                    game["gameCreation"]?.Value<long>() ?? 0,
                    game["queueId"]?.Value<int>() ?? 0,
                    player?["championId"]?.Value<int>() ?? 0,
                    stats["win"]!.Value<bool>(),
                    Number("kills"),
                    Number("deaths"),
                    Number("assists"),
                    Long("totalDamageDealtToChampions"),
                    Long("goldEarned"),
                    Number("pentaKills")
                )
            );
        }
        return result;
    }

    public static string QueueName(int queue) =>
        queue switch
        {
            420 => "单双排",
            440 => "灵活排位",
            450 => "极地大乱斗",
            2400 => "海克斯大乱斗",
            400 or 430 => "匹配",
            1700 => "斗魂竞技场",
            // Custom rooms have queues of their own; their games stay apart from the matchmade modes.
            3270 => "自定义海克斯大乱斗",
            3100 => "自定义峡谷",
            3140 => "训练模式",
            0 => "自定义",
            _ => Parsers.MatchParserSgp.QueueModeName(queue) ?? $"模式 {queue}",
        };
}
