using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record AramMetaChampion(int Id,string Name,string Alias,string[] Roles,double? WinRate,double? RawWinRate,long? Games,JObject Data)
{
    // arammeta/scripts/tierlist_engine.py applies these cuts to Bayesian WR.
    public string Tier => WinRate switch { >=.55=>"OP",>=.52=>"T1",>=.50=>"T2",>=.48=>"T3",>=.46=>"T4",null=>"—",_=>"T5" };
}
public sealed record AramMetaIndex(string Patch,string Version,AramMetaChampion[] Champions,JObject Augments,JObject Raw)
{
    public static AramMetaIndex Parse(JObject raw)
    {
        if(raw["champs"] is not JObject champs || string.IsNullOrWhiteSpace((string?)raw["detailVersion"]))
            throw new InvalidDataException("arammeta 数据结构不完整。");
        var rows=champs.Properties().Where(p=>int.TryParse(p.Name,out _) && p.Value is JObject).Select(p=>
        {
            var c=(JObject)p.Value;
            return new AramMetaChampion(int.Parse(p.Name),(string?)c["name_zh"]??(string?)c["name"]??p.Name,(string?)c["alias"]??"",
                (c["tags"] as JArray??new()).Values<string>().Where(s=>s!=null).Cast<string>().ToArray(),Rate(c["wr"]),Rate(c["rawWr"]),(long?)c["g"],(JObject)c.DeepClone());
        }).ToArray();
        return new((string?)raw["patch_prefix"]??"未知",(string)raw["detailVersion"]!,rows,(JObject)(raw["augs"]?.DeepClone()??new JObject()),(JObject)raw.DeepClone());
    }
    public static double? Rate(JToken? value) => (double?)value is double n && double.IsFinite(n) && n>=0 && n<=1?n:null;
}
public sealed record AramMetaPage(AramMetaIndex Index,JObject? Detail,bool Cached,DateTime FetchedAt);
