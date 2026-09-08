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
        }
        finally { Gate.Release(); }
    }
}
