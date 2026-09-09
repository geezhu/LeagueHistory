using League.Models;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;

namespace League.Services;

public static class OpggItemSetService
{
    private static readonly SemaphoreSlim Gate=new(1,1);
    public static int[] Ids(JObject? row)=>(row?["ids"] as JArray??new()).Values<int>().Where(id=>id>0).ToArray();
    public static JObject[] Options(JObject data,string key)=>(data[key] as JArray??new()).OfType<JObject>()
        .Where(o=>Ids(o).Length>0).OrderByDescending(o=>(double?)o["pick_rate"]??-1).ToArray();
    public static int[] Later(JObject data,JObject core)
    {
        var excluded=Ids(core).ToHashSet();
        return (data["last_items"] as JArray??new()).OfType<JObject>().SelectMany(Ids).Where(id=>!excluded.Contains(id)).Distinct().ToArray();
    }
    public static OpggBuild FromAramMeta(AramMetaPage page,int hero)
    {
        if(page.Detail==null)throw new InvalidDataException("出装详情尚未加载。");
        var data=new JObject();
        foreach(var (source,target) in new[]{("items","core_items"),("singleItems","last_items"),("boots","boots")})
            data[target]=new JArray(AramMetaRecommendation.Read(page,hero,source).Select(r=>r.ToOption()));
        return new OpggBuild(new("global","mayhem","all","",hero),page.Index.Patch,data,new JObject(),Array.Empty<OpggRunes>());
    }
    public static JObject Create(OpggBuild build,string champion,JObject core,JObject? starter,JObject? boots,string source="OP.GG")
    {
        if(source is not ("OP.GG" or "arammeta"))throw new ArgumentException("Unknown equipment source",nameof(source));
        if(Ids(core).Length==0)throw new InvalidDataException("没有可用的核心装备。");
        string scope=$"League:{(source=="OP.GG"?"OPGG":"arammeta")}:{build.Query.ChampionId}:{build.Query.Mode}:{build.Query.Position}";
        string uid=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(scope)).Take(16).ToArray()).ToString();
        var blocks=new JArray();
        void Block(string title,IEnumerable<int> ids)
        {
            var items=new JArray(ids.GroupBy(id=>id).Select(g=>new JObject{["id"]=g.Key.ToString(),["count"]=g.Count()}));
            if(items.Count>0)blocks.Add(new JObject{["type"]=title,["items"]=items});
        }
        Block("出门装",Ids(starter));Block("鞋子",Ids(boots));Block("核心装备",Ids(core));Block("后续可选装备",Later(build.Data,core));
        int map=build.Query.Mode switch{"ranked" or "urf"=>11,"aram" or "mayhem"=>12,"nexus_blitz"=>21,"arena"=>30,_=>0};
        string mode=build.Query.Mode switch{"ranked"=>"峡谷","aram"=>"大乱斗","mayhem"=>"海克斯大乱斗","urf"=>"无限火力","arena"=>"竞技场","nexus_blitz"=>"极限闪击",_=>build.Query.Mode};
        string lane=build.Query.Position switch{"top"=>"上路","jungle"=>"打野","mid"=>"中路","adc"=>"下路","support"=>"辅助",_=>""};
        string title=$"[{source}] {champion}·{mode}"+(build.Query.Mode=="ranked" && lane.Length>0?"·"+lane:"");
        return new JObject{["uid"]=uid,["title"]=title,
            ["mode"]="any",["map"]="any",["type"]="custom",["sortrank"]=0,["startedFrom"]="blank",
            ["associatedChampions"]=new JArray(build.Query.ChampionId),["associatedMaps"]=map>0?new JArray(map):new JArray(),
            ["preferredItemSlots"]=new JArray(),["blocks"]=blocks};
    }
    public static async Task SaveAsync(HttpClient client,JObject itemSet,CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try
        {
            var summoner=JObject.Parse(await client.GetStringAsync("/lol-summoner/v1/current-summoner",token));
            long id=(long?)summoner["summonerId"]??0;
            if(id<=0)throw new InvalidOperationException("无法读取当前账号。");
            string path=$"/lol-item-sets/v1/item-sets/{id}/sets";
            var payload=JObject.Parse(await client.GetStringAsync(path,token));
            if(payload["itemSets"] is not JArray sets)throw new InvalidDataException("物品套装数据不完整，未进行覆盖。");
            var existing=sets.OfType<JObject>().FirstOrDefault(s=>(string?)s["uid"]==(string?)itemSet["uid"]);
            string existingTitle=(string?)existing?["title"]??"";
            bool arammeta=((string?)itemSet["title"]??"").StartsWith("[arammeta] ",StringComparison.Ordinal);
            bool managed=arammeta?existingTitle.StartsWith("[arammeta] ",StringComparison.Ordinal):existingTitle.StartsWith("[League] OP.GG ",StringComparison.Ordinal)||existingTitle.StartsWith("[OP.GG] ",StringComparison.Ordinal);
            if(existing!=null && !managed)throw new InvalidOperationException("同名标识属于其他套装，未进行覆盖。");
            if(existing!=null)existing.Replace(itemSet.DeepClone());else sets.Add(itemSet.DeepClone());
            using var body=new StringContent(payload.ToString(),Encoding.UTF8,"application/json");
            using var response=await client.PutAsync(path,body,token);response.EnsureSuccessStatusCode();
            for(int attempt=0;attempt<3;attempt++)
            {
                var saved=JObject.Parse(await client.GetStringAsync(path,token));
                var result=(saved["itemSets"] as JArray)?.OfType<JObject>().FirstOrDefault(s=>(string?)s["uid"]==(string?)itemSet["uid"]);
                if(result!=null && JToken.DeepEquals(result["blocks"],itemSet["blocks"]) && JToken.DeepEquals(result["associatedChampions"],itemSet["associatedChampions"]) && JToken.DeepEquals(result["associatedMaps"],itemSet["associatedMaps"]))return;
                if(attempt<2)await Task.Delay(150,token);
            }
            throw new InvalidOperationException("未确认物品套装保存成功，请检查客户端。");
        }
        finally{Gate.Release();}
    }
}
