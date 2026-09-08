using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record OpggQuery(string Region, string Mode, string Tier, string Position, int ChampionId)
{
    public static string? ModeForQueue(int queue) => queue switch {
        400 or 420 or 430 or 440 or 480 or 490 => "ranked", 450 => "aram", 1700 or 1710 => "arena",
        900 or 1900 => "urf", 1200 or 1300 => "nexus_blitz", _ => null };
    public static string ModeForFollowing(int queue) => ModeForQueue(queue) ?? "ranked";
    public static string? PositionForChampion(IEnumerable<OpggTier> rows,int championId,string? assignedPosition)
    {
        var available=rows.Where(r=>r.ChampionId==championId && new[]{"top","jungle","mid","adc","support"}.Contains(r.Position.ToLowerInvariant())).ToArray();
        var assigned=available.FirstOrDefault(r=>string.Equals(r.Position,assignedPosition,StringComparison.OrdinalIgnoreCase));
        return (assigned??available.OrderByDescending(r=>r.PickRate??-1).ThenBy(r=>r.Rank??int.MaxValue).FirstOrDefault())?.Position.ToLowerInvariant();
    }
    public OpggQuery Normalize() => this with { Position = Mode == "ranked" ? Position.ToLowerInvariant() : "none", Tier = Mode == "arena" ? "all" : Tier };
}
public sealed record OpggRunes(int Primary, int Secondary, int[] Perks, long? Plays, double? WinRate)
{
    public double? PickRate { get; init; }
}
public sealed record OpggTier(int ChampionId, string Position, double? WinRate, double? PickRate, double? BanRate, int? Tier, int? Rank)
{
    public int[] Counters { get; init; } = Array.Empty<int>();
}
public sealed record OpggBuild(OpggQuery Query, string Version, JObject Data, JObject Stats, OpggRunes[] Runes)
{
    public static double? Ratio(JToken? numerator,JToken? denominator) => (double?)denominator > 0 ? (double?)numerator / (double?)denominator : null;
    public static OpggBuild Parse(JObject raw,OpggQuery query)
    {
        if (raw["data"] is not JObject data || data["summary"] is not JObject summary || (int?)summary["id"] != query.ChampionId)
            throw new InvalidDataException("该英雄在当前筛选条件下暂无攻略。");
        var stats = query.Mode == "ranked"
            ? (summary["positions"] as JArray)?.OfType<JObject>().FirstOrDefault(p => string.Equals((string?)p["name"],query.Position,StringComparison.OrdinalIgnoreCase))?["stats"] as JObject
            : summary["average_stats"] as JObject;
        if (stats == null) throw new InvalidDataException("该英雄在当前分路暂无统计，请更换分路。");
        var runes = (data["runes"] as JArray ?? new()).OfType<JObject>().Select(r=>new OpggRunes(
            (int?)r["primary_page_id"] ?? 0,(int?)r["secondary_page_id"] ?? 0,
            new[]{"primary_rune_ids","secondary_rune_ids","stat_mod_ids"}.SelectMany(k=>(r[k] as JArray ?? new()).Values<int>()).ToArray(),
            (long?)r["play"],Ratio(r["win"],r["play"])) { PickRate=(double?)r["pick_rate"] }).Where(r=>r.Primary>0 && r.Secondary>0 && r.Primary!=r.Secondary && r.Perks.Length==9 && r.Perks.All(id=>id>0)).ToArray();
        return new(query,(string?)raw["meta"]?["version"] ?? "未知",(JObject)data.DeepClone(),(JObject)stats.DeepClone(),runes);
    }
    public static OpggTier[] ParseTiers(JObject raw,string mode)
    {
        if (raw["data"] is not JArray list) throw new InvalidDataException("暂无英雄梯队数据。");
        var results = new List<OpggTier>();
        foreach(var champion in list.OfType<JObject>())
        {
            var positions = mode == "ranked" ? (champion["positions"] as JArray ?? new()).OfType<JObject>()
                : new[]{new JObject { ["name"]="none",["stats"]=champion["average_stats"]?.DeepClone() }};
            foreach(var position in positions)
            {
                if(position["stats"] is not JObject stats) continue;
                results.Add(new((int?)champion["id"] ?? 0,(string?)position["name"] ?? "none",
                    (double?)stats["win_rate"] ?? Ratio(stats["win"],stats["play"]),(double?)stats["pick_rate"],(double?)stats["ban_rate"],
                    (int?)(stats["tier_data"]?["tier"] ?? stats["tier"]),(int?)(stats["tier_data"]?["rank"] ?? stats["rank"])) {
                        Counters=(position["counters"] as JArray ?? new()).OfType<JObject>().Take(3).Select(c=>(int?)c["champion_id"]??0).Where(id=>id>0).ToArray()
                    });
            }
        }
        return results.Where(r=>r.ChampionId>0).OrderBy(r=>r.Rank ?? int.MaxValue).ToArray();
    }
}
