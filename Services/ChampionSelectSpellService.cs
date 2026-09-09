using Newtonsoft.Json.Linq;
using System.Text;

namespace League.Services;

internal static class ChampionSelectSpellService
{
    internal static async Task ApplyAsync(HttpClient client,int[] spells,int hero,CancellationToken token)
    {
        if(spells.Length!=2 || spells.Any(id=>id<=0) || spells[0]==spells[1])throw new InvalidDataException("召唤师技能组合无效。");
        async Task<JObject> Own()
        {
            var session=JObject.Parse(await client.GetStringAsync("/lol-champ-select/v1/session",token));
            var own=(session["myTeam"] as JArray)?.OfType<JObject>().FirstOrDefault(p=>(int?)p["cellId"]==(int?)session["localPlayerCellId"])
                ??throw new InvalidOperationException("当前不在选人房间。");
            int selected=(int?)own["championId"]??0;
            if(selected<=0)selected=(session["actions"] as JArray??new()).OfType<JArray>().SelectMany(a=>a.OfType<JObject>())
                .Where(a=>(int?)a["actorCellId"]==(int?)session["localPlayerCellId"] && (string?)a["type"]=="pick")
                .Select(a=>(int?)a["championId"]??0).LastOrDefault(id=>id>0);
            if(selected!=hero)throw new InvalidOperationException("英雄已变化，请等待推荐更新。");
            return own;
        }
        var current=await Own();
        int first=spells[0],second=spells[1];
        // Retain an existing shared spell's slot, especially the user's Flash key.
        if((int?)current["spell1Id"]==second || (int?)current["spell2Id"]==first)(first,second)=(second,first);
        using var body=new StringContent(new JObject{["spell1Id"]=first,["spell2Id"]=second}.ToString(),Encoding.UTF8,"application/json");
        using var response=await client.PatchAsync("/lol-champ-select/v1/session/my-selection",body,token);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("客户端未接受召唤师技能配置，请检查当前模式或是否已结束选人。");
        for(int attempt=0;attempt<3;attempt++)
        {
            var applied=await Own();
            if((int?)applied["spell1Id"]==first && (int?)applied["spell2Id"]==second)return;
            if(attempt<2)await Task.Delay(150,token);
        }
        throw new InvalidOperationException("未确认召唤师技能生效，请检查客户端。");
    }
}
