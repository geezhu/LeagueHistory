using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record AramMetaRecommendation(int[] Ids,string Name,string Description,double? WinRate,double? PickRate,long? Games,JObject Raw)
{
    public static AramMetaRecommendation[] Read(AramMetaPage page,int hero,string category)
    {
        bool augment=category.StartsWith("k",StringComparison.Ordinal);
        var source=augment?page.Index.Champions.FirstOrDefault(c=>c.Id==hero)?.Data["top"]?[category]:page.Detail?[category]?["top"];
        return (source as JArray??new()).OfType<JObject>().Select(row=>
        {
            int id=(int?)row["id"]??0;var meta=augment?page.Index.Augments[id.ToString()]:row;
            int[] ids=augment?new[]{id}:(row["items"] as JArray??new()).OfType<JObject>().SelectMany(i=>Enumerable.Repeat((int?)i["id"]??0,Math.Clamp((int?)i["ic"]??1,1,10))).Where(i=>i>0).ToArray();
            return new AramMetaRecommendation(ids,(string?)meta?["name_zh"]??(string?)meta?["name_en"]??$"推荐 {id}",(string?)meta?["desc_zh"]??(string?)meta?["desc_en"]??"",AramMetaIndex.Rate(row["wr"]),AramMetaIndex.Rate(row["pick"]),(long?)row["g"],row);
        }).Where(r=>r.Ids.Length>0 && r.Ids.All(i=>i>0)).ToArray();
    }
    public static IEnumerable<AramMetaRecommendation> Sort(IEnumerable<AramMetaRecommendation> rows,int sort)=>sort switch
    {1=>rows.OrderByDescending(r=>r.WinRate??-1),2=>rows.OrderByDescending(r=>r.PickRate??-1),_=>rows};
    public static (string Title,AramMetaRecommendation? Entry)[] SpellHighlights(IEnumerable<AramMetaRecommendation> rows)
    {
        var valid=rows.Where(r=>r.Ids.Length==2).ToArray();
        return new[]{("使用率最高",valid.Where(r=>r.PickRate.HasValue).OrderByDescending(r=>r.PickRate).ThenByDescending(r=>r.Games).FirstOrDefault()),
            ("胜率最高",valid.Where(r=>r.WinRate.HasValue).OrderByDescending(r=>r.WinRate).ThenByDescending(r=>r.Games).FirstOrDefault())};
    }
    public JObject ToOption()=>new(){["ids"]=new JArray(Ids),["play"]=Games,["win_rate"]=WinRate,["win"]=Games.HasValue && WinRate.HasValue?Games.Value*WinRate.Value:null,["pick_rate"]=PickRate};
}
