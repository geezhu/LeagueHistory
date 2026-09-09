using League.Models;
using Newtonsoft.Json.Linq;
using System.Text;

namespace League.Services;

public static class OpggRuneService
{
    private static readonly SemaphoreSlim Gate = new(1,1);
    public static JObject CreatePayload(OpggRunes runes,string champion)
    {
        if(runes.Primary<=0 || runes.Secondary<=0 || runes.Primary==runes.Secondary || runes.Perks.Length!=9 || runes.Perks.Any(p=>p<=0))
            throw new InvalidDataException("符文方案不完整，无法应用。");
        return new JObject { ["name"]="[League] OP.GG "+champion,["primaryStyleId"]=runes.Primary,["subStyleId"]=runes.Secondary,
            ["selectedPerkIds"]=new JArray(runes.Perks),["current"]=true };
    }
    public static async Task ApplyAsync(HttpClient client,OpggRunes runes,string champion,CancellationToken token)
    {
        var payload=CreatePayload(runes,champion);
        await Gate.WaitAsync(token);
        try
        {
            using var response=await client.GetAsync("/lol-perks/v1/pages",token);
            response.EnsureSuccessStatusCode();
            var pages=JArray.Parse(await response.Content.ReadAsStringAsync(token));
            var own=pages.OfType<JObject>().FirstOrDefault(p=>(bool?)p["isEditable"]==true && ((string?)p["name"])?.StartsWith("[League] OP.GG ",StringComparison.Ordinal)==true);
            using var content=new StringContent(payload.ToString(),Encoding.UTF8,"application/json");
            using var result=own?["id"]?.Value<long>() is long id
                ? await client.PutAsync($"/lol-perks/v1/pages/{id}",content,token)
                : await client.PostAsync("/lol-perks/v1/pages",content,token);
            if(!result.IsSuccessStatusCode) throw new InvalidOperationException("符文应用失败，请确认客户端可编辑符文且有空余符文页。现有个人符文页未被覆盖。");
            long? pageId=(long?)own?["id"];
            if(pageId==null)
            {
                var written=await result.Content.ReadAsStringAsync(token);
                if(!string.IsNullOrWhiteSpace(written))pageId=(long?)JObject.Parse(written)["id"];
            }
            bool Matches(JObject page)=>
                (int?)page["primaryStyleId"]==runes.Primary && (int?)page["subStyleId"]==runes.Secondary &&
                (page["selectedPerkIds"] as JArray??new()).Values<int>().SequenceEqual(runes.Perks);
            var current=JObject.Parse(await client.GetStringAsync("/lol-perks/v1/currentpage",token));
            if(Matches(current))return;
            if(pageId==null)
            {
                var updated=JArray.Parse(await client.GetStringAsync("/lol-perks/v1/pages",token));
                pageId=(long?)updated.OfType<JObject>().FirstOrDefault(p=>(string?)p["name"]==(string?)payload["name"] && Matches(p))?["id"];
            }
            if(pageId is not >0)throw new InvalidOperationException("符文已写入，但未找到可切换的符文页，请在客户端确认。");
            using var selectContent=new StringContent(pageId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),Encoding.UTF8,"application/json");
            using var selected=await client.PutAsync("/lol-perks/v1/currentpage",selectContent,token);
            if(!selected.IsSuccessStatusCode)throw new InvalidOperationException("符文已保存，但切换当前符文页失败，请在客户端手动选择。");
            for(int attempt=0;attempt<3;attempt++)
            {
                current=JObject.Parse(await client.GetStringAsync("/lol-perks/v1/currentpage",token));
                if((long?)current["id"]==pageId && Matches(current))return;
                await Task.Delay(150,token);
            }
            throw new InvalidOperationException("客户端当前符文与推荐方案不一致，尚未确认应用成功，请重试。");
        }
        finally { Gate.Release(); }
    }
}
