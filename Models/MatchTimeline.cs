using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record GoldFrame(long Timestamp, long? Blue, long? Red);

public sealed record TimelineEvent(
    long Timestamp,
    string Kind,
    string Actor,
    string Description,
    int? X,
    int? Y,
    int? Team = null
);

public sealed class MatchTimeline
{
    public List<GoldFrame> Frames { get; } = new();
    public List<TimelineEvent> Events { get; } = new();
    public long? FrameInterval { get; private set; }
    public int BluePlayers { get; private set; }
    public int RedPlayers { get; private set; }

    public double? GoldDifference(GoldFrame frame) =>
        frame.Blue.HasValue && frame.Red.HasValue && BluePlayers > 0 && RedPlayers > 0
            ? frame.Blue.Value / (double)BluePlayers - frame.Red.Value / (double)RedPlayers
            : null;

    public static MatchTimeline Parse(JToken data, IReadOnlyList<JObject> participants)
    {
        var result = new MatchTimeline();
        if (data is not JObject)
        {
            return result;
        }

        var root = data["json"] as JObject ?? (JObject)data;
        root = root["info"] as JObject ?? root;
        result.FrameInterval =
            root["frameInterval"]?.Type == JTokenType.Integer ? root["frameInterval"]!.Value<long>() : null;
        var frames = root["frames"] as JArray;
        if (frames == null)
        {
            return result;
        }

        var players = participants
            .Where(p => p["participantId"]?.Type == JTokenType.Integer)
            .GroupBy(p => p["participantId"]!.Value<int>())
            .ToDictionary(g => g.Key, g => g.First());
        bool completeMapping =
            participants.Count > 0
            && players.Count == participants.Count
            && participants.All(p => p["teamId"]?.Type == JTokenType.Integer);
        result.BluePlayers = players.Values.Count(p => (int?)p["teamId"] == 100);
        result.RedPlayers = players.Values.Count(p => (int?)p["teamId"] == 200);
        string Player(int id) =>
            id == 0 ? "环境"
            : players.TryGetValue(id, out var player)
                ? player["riotIdGameName"]?.ToString() ?? player["summonerName"]?.ToString() ?? $"玩家 {id}"
            : $"玩家 {id}";
        foreach (
            var frame in frames
                .OfType<JObject>()
                .Where(f => f["timestamp"]?.Type == JTokenType.Integer)
                .OrderBy(f => (long)f["timestamp"]!)
        )
        {
            long time = (long)frame["timestamp"]!;
            if (time < 0)
            {
                continue;
            }

            var values = frame["participantFrames"] as JObject;
            long? Total(int team)
            {
                var ids = players.Where(p => p.Value["teamId"]?.Value<int>() == team).Select(p => p.Key).ToArray();
                if (
                    !completeMapping
                    || ids.Length == 0
                    || values == null
                    || ids.Any(id => values[id.ToString()]?["totalGold"]?.Type != JTokenType.Integer)
                )
                {
                    return null;
                }

                return ids.Sum(id => values[id.ToString()]!["totalGold"]!.Value<long>());
            }
            result.Frames.Add(new(time, Total(100), Total(200)));
            foreach (var ev in (frame["events"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string kind = ev["type"]?.ToString() ?? ev["eventType"]?.ToString() ?? "";
                if (kind is not ("CHAMPION_KILL" or "ELITE_MONSTER_KILL" or "BUILDING_KILL"))
                {
                    continue;
                }

                long stamp = ev["timestamp"]?.Value<long>() ?? time;
                int killer = ev["killerId"]?.Value<int>() ?? 0;
                string description = kind switch
                {
                    "CHAMPION_KILL" => "击杀 " + Player(ev["victimId"]?.Value<int>() ?? 0),
                    "ELITE_MONSTER_KILL" => "击杀 " + Monster(ev["monsterType"]?.ToString()),
                    _ => ev["buildingType"]?.ToString() == "INHIBITOR_BUILDING" ? "摧毁水晶" : "摧毁防御塔",
                };
                result.Events.Add(
                    new(
                        stamp,
                        kind,
                        Player(killer),
                        description,
                        ev["position"]?["x"]?.Value<int>(),
                        ev["position"]?["y"]?.Value<int>(),
                        players.TryGetValue(killer, out var killerPlayer) ? (int?)killerPlayer["teamId"] : null
                    )
                );
            }
        }
        result.Events.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return result;
    }

    private static string Monster(string? type) =>
        type switch
        {
            "DRAGON" => "巨龙",
            "BARON_NASHOR" => "纳什男爵",
            "RIFTHERALD" => "峡谷先锋",
            "HORDE" => "虚空巢虫",
            _ => "史诗野怪",
        };
}
