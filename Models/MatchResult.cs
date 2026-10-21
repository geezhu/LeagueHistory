using League.Controls;
using Newtonsoft.Json.Linq;

namespace League.Models;

public static class MatchResult
{
    public static readonly Color RemakeColor = Theme.Muted;

    public static bool IsRemake(JObject? entry, IEnumerable<JObject>? participants = null)
    {
        var game = entry?["json"] as JObject ?? entry;
        game = game?["info"] as JObject ?? game;
        bool Flag(JObject? data) =>
            data?["gameEndedInEarlySurrender"]?.Type == JTokenType.Boolean
            && data["gameEndedInEarlySurrender"]!.Value<bool>();
        bool PlayerFlag(JObject player) => Flag(player) || Flag(player["stats"] as JObject);
        return Flag(game)
            || Flag(game?["stats"] as JObject)
            || (game?["participants"] as JArray)?.OfType<JObject>().Any(PlayerFlag) == true
            || participants?.Any(PlayerFlag) == true;
    }
}
