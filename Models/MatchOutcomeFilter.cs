using Newtonsoft.Json.Linq;

namespace League.Models;

public static class MatchOutcomeFilter
{
    public static bool? GetWin(JObject entry, string puuid)
    {
        if (MatchResult.IsRemake(entry)) return null;
        var game = entry["json"] as JObject ?? entry;
        game = game["info"] as JObject ?? game;
        if (game["participants"] is not JArray players) return null;
        var identity = (game["participantIdentities"] as JArray)?.OfType<JObject>()
            .FirstOrDefault(p => p["player"]?["puuid"]?.ToString() == puuid);
        var player = players.OfType<JObject>().FirstOrDefault(p => p["puuid"]?.ToString() == puuid)
            ?? (identity == null ? null : players.OfType<JObject>().FirstOrDefault(p => JToken.DeepEquals(p["participantId"], identity["participantId"])));
        var stats = player?["stats"] as JObject ?? player;
        return stats?["win"]?.Type == JTokenType.Boolean ? stats["win"]!.Value<bool>() : null;
    }
}
