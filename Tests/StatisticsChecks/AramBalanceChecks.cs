using League.Clients;
using Newtonsoft.Json.Linq;
using System.Net;

internal static class AramBalanceChecks
{
    private sealed class Handler(Func<HttpResponseMessage> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)=>Task.FromResult(send());}
    public static async Task RunAsync()
    {
        var row=JObject.Parse("{\"champion\":{\"name\":\"Nunu & Willump\"},\"aramDamageDealt\":1.1,\"aramDamageTaken\":0.9,\"aramHealing\":1,\"aramShielding\":1,\"aramTenacity\":1,\"aramAbilityHaste\":10,\"aramAttackSpeed\":1.025,\"aramEnergyRegen\":1}");
        var data=new JObject{["result"]=new JObject{["data"]=new JObject{["allAramModifiersJson"]=new JObject{["nodes"]=new JArray(row)},["patchVersionJson"]=new JObject{["patchVersion"]="test"}}}};
        data["result"]!["data"]!["aramModifiersJson"]=JObject.Parse("{\"timestamp\":\"2026-09-03T10:13:06.697856+00:00\"}");
        var nodes=AramBalanceClient.Nodes(data);
        nodes.Insert(0,new JObject{["champion"]=JValue.CreateNull()});
        nodes.Insert(0,new JObject{["champion"]="invalid"});
        nodes.Insert(0,new JObject{["champion"]=new JObject{["name"]=new JObject()}});
        string text=AramBalanceClient.Describe(data,"Nunu");
        if(!text.Contains("造成伤害 +10%") || !text.Contains("受到伤害 -10%") || !text.Contains("技能急速 +10\n") || !text.Contains("+2.5%"))throw new Exception("Balance alias matching and numeric units");
        if(!AramBalanceClient.Describe(data,"Unknown").Contains("暂未匹配"))throw new Exception("Unknown heroes cannot imply no adjustments");
        string file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+"-balance.json");int calls=0;
        try
        {
            using(var client=new AramBalanceClient(new Handler(()=>{calls++;return new(HttpStatusCode.OK){Content=new StringContent(data.ToString())};}),file))
            {
                await Task.WhenAll(Enumerable.Range(0,5).Select(_=>client.GetAsync("Nunu",TimeSpan.FromDays(1),default)));
                if(calls!=1 || !File.Exists(file))throw new Exception("Balance requests must coalesce and persist");
            }
            using(var client=new AramBalanceClient(new Handler(()=>{calls++;throw new HttpRequestException();}),file))
            {
                await client.GetAsync("Nunu",TimeSpan.FromDays(1),default);
                if(calls!=1)throw new Exception("Fresh disk cache must survive restart offline");
                text=await client.GetAsync("Nunu",TimeSpan.Zero,default);
                if(calls!=2 || !text.Contains("旧缓存"))throw new Exception("Changed expiry refreshes and preserves stale data on failure");
                await client.GetAsync("Nunu",TimeSpan.Zero,default);
                if(calls!=2)throw new Exception("Failed refreshes must back off");
                using var cancel=new CancellationTokenSource();cancel.Cancel();
                try{await client.GetAsync("Nunu",TimeSpan.Zero,cancel.Token);throw new Exception("Cancelled request completed");}catch(OperationCanceledException){}
            }
        }
        finally{File.Delete(file);}
        Console.WriteLine("PASS: balance units, alias mapping, persistent cache, expiry, failure backoff and cancellation.");
    }
}
