using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record TeamObjective(string Key, string Name, long? Count);

public sealed record TeamSummary(long? Kills, long? Deaths, long? Assists, long? Gold, TeamObjective[] Objectives)
{
    public static TeamSummary From(MatchInfo match, int teamId)
    {
        var players =
            match.AllParticipants?.Where(p => (int?)p["teamId"] == teamId).ToArray() ?? Array.Empty<JObject>();
        static long? Number(JToken? token) =>
            token != null && long.TryParse(token.ToString(), out var n) && n >= 0 ? n : null;
        long? Sum(string key)
        {
            var values = players.Select(p => Number(p[key] ?? p["stats"]?[key])).ToArray();
            return values.Length > 0 && values.All(v => v.HasValue) ? values.Sum(v => v!.Value) : null;
        }
        var raw = match.RawGameData;
        var game = raw?["json"] as JObject ?? raw;
        game = game?["info"] as JObject ?? game;
        var team = (game?["teams"] as JArray)?.OfType<JObject>().FirstOrDefault(t => (int?)t["teamId"] == teamId);
        TeamObjective Objective(string key, string name, string legacy) =>
            new(key, name, Number(team?["objectives"]?[key]?["kills"]) ?? Number(team?[legacy]));
        var objectives = new List<TeamObjective>
        {
            Objective("tower", "摧毁防御塔", "towerKills"),
            Objective("inhibitor", "摧毁召唤水晶", "inhibitorKills"),
        };
        bool aram = (int?)game?["mapId"] == 12 || match.QueueId is "450" or "2400";
        if (!aram)
        {
            objectives.Add(Objective("baron", "击杀纳什男爵", "baronKills"));
            objectives.Add(Objective("dragon", "击杀巨龙", "dragonKills"));
            objectives.Add(Objective("riftHerald", "击杀峡谷先锋", "riftHeraldKills"));
        }
        return new(Sum("kills"), Sum("deaths"), Sum("assists"), Sum("goldEarned"), objectives.ToArray());
    }
}
