using System.Net;
using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;

internal static class LoadingRosterIdentityChecks
{
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> run):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>run(request,token); }
    private static HttpResponseMessage Profile(string puuid,long id,string game,string tag="01")=>new(HttpStatusCode.OK)
    {Content=new StringContent(new JObject{["puuid"]=puuid,["summonerId"]=id,["gameName"]=game,["tagLine"]=tag}.ToString())};
    private static LiveMatchSnapshot Live(params string[] names)=>new("ARAM",12,1,names.Select((name,i)=>
        new LiveMatchPlayer("live:"+i,name,"Lux","拉克丝",i==0?"ORDER":"CHAOS",i==0,1,[])).ToArray());
    public static async Task Run()
    {
        int calls=0;
        using var client=new HttpClient(new Handler((request,_)=>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/lol-summoner/v1/summoners/1"=>Profile("a",1,"玩家甲"),
                "/lol-summoner/v1/summoners/2"=>Profile("b",2,"玩家乙"),
                "/lol-summoner/v2/summoners/puuid/c"=>Profile("c",3,"玩家丙"),
                _=>throw new Exception("Unexpected public identity lookup")
            });
        })){BaseAddress=new Uri("https://test.invalid/")};
        var service=new LoadingRosterIdentityService(()=>client);
        var roster=new LoadingRoster("game",450,"ARAM",[new("a","旧名称甲","a",1,99,100,true),new("b","旧名称乙","b",2,99,200,false)]);
        var flow=new JObject{["gameData"]=new JObject{["playerChampionSelections"]=new JArray(new JObject{["puuid"]="c",["championId"]=99})}};
        var live=Live("玩家甲#01","玩家乙#01","玩家丙#01");
        var merged=await service.MergeAsync(roster,flow,"a",live,_=>99,CancellationToken.None);
        Check(merged.Players.Select(p=>p.Puuid).SequenceEqual(new[]{"a","b","c"}) && merged.Players[2].Team==200,"Legacy names and selection-only public players recover exact identities even with the same champion");
        await service.MergeAsync(roster,flow,"a",live,_=>99,CancellationToken.None);
        Check(calls==3,"Repeated overlay polls reuse public identity metadata");
        var hiddenFlow=new JObject{["gameData"]=new JObject{["playerChampionSelections"]=new JArray(new JObject{["puuid"]="hidden",["championId"]=99})}};
        merged=await service.MergeAsync(roster,hiddenFlow,"a",Live("玩家甲#01","玩家乙#01","#"),_=>99,CancellationToken.None);
        Check(calls==3 && merged.Players[0].Puuid=="a" && merged.Players[1].Puuid=="b" && merged.Players[2] is{Puuid:"",IdentityHidden:true,Name:"隐藏玩家"},"One hidden player does not discard other identities or trigger hidden-profile lookups");
        merged=await service.MergeAsync(roster,new(),"a",Live("玩家甲#01","不匹配的玩家#01"),_=>99,CancellationToken.None);
        Check(merged.Players[1].Puuid=="" && !merged.Players[1].IdentityHidden,"A missing name match is pending identity, not hidden and never inferred from the champion");

        var zeroFlow=new JObject{["gameData"]=new JObject{["teamOne"]=new JArray(new JObject{["puuid"]=Guid.Empty.ToString(),["summonerId"]=1}),
            ["playerChampionSelections"]=new JArray(new JObject{["summonerId"]=1,["puuid"]="a",["championId"]=99})}};
        var parsed=LoadingRoster.Parse(zeroFlow,"a");
        Check(parsed.Players[0].Puuid=="a" && parsed.Players[0].ChampionId==99 && parsed.Players[0].Self,"Zero PUUID can recover from a confirmed summoner-ID join");

        using var ambiguousClient=new HttpClient(new Handler((request,_)=>Task.FromResult(Profile(request.RequestUri!.AbsolutePath.EndsWith("1")?"a":"b",request.RequestUri.AbsolutePath.EndsWith("1")?1:2,"同名")))){BaseAddress=client.BaseAddress};
        merged=await new LoadingRosterIdentityService(()=>ambiguousClient).MergeAsync(roster,new(),"a",Live("同名#01","同名#01"),_=>99,CancellationToken.None);
        Check(merged.Players.All(p=>p.Puuid==""),"Ambiguous full names cannot assign mastery to the wrong player");
        int failures=0;
        using var badClient=new HttpClient(new Handler((_,_)=>{failures++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));})){BaseAddress=client.BaseAddress};
        var failedService=new LoadingRosterIdentityService(()=>badClient);
        var one=roster with{Players=[roster.Players[0]]};
        await failedService.MergeAsync(one,new(),"a",Live("玩家甲#01"),_=>99,CancellationToken.None);
        await failedService.MergeAsync(one,new(),"a",Live("玩家甲#01"),_=>99,CancellationToken.None);
        Check(failures==1,"Failed metadata requests back off instead of repeating every three seconds");

        int delayedCalls=0;var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var delayedClient=new HttpClient(new Handler(async(_,token)=>{Interlocked.Increment(ref delayedCalls);started.TrySetResult();await release.Task.WaitAsync(token);return Profile("a",1,"玩家甲");})){BaseAddress=client.BaseAddress};
        HttpClient current=delayedClient;var delayed=new LoadingRosterIdentityService(()=>current);
        using var cancellation=new CancellationTokenSource();
        var cancelled=delayed.MergeAsync(one,new(),"a",Live("玩家甲#01"),_=>99,cancellation.Token);
        var continuing=delayed.MergeAsync(one,new(),"a",Live("玩家甲#01"),_=>99,CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));cancellation.Cancel();
        try{await cancelled;throw new Exception("Cancelled merge completed");}catch(OperationCanceledException){}
        release.TrySetResult();merged=await continuing;
        Check(delayedCalls==1 && merged.Players[0].Puuid=="a","Cancelling one view preserves a coalesced metadata lookup");
        current=client;
        merged=await delayed.MergeAsync(one,new(),"a",Live("玩家甲#01"),_=>99,CancellationToken.None);
        Check(merged.Players[0].Puuid=="a" && calls==4,"Client reconnection refreshes identity metadata rather than using the previous client's cache");
        var page=new ChampionMasteryPage(null,null,"尚未取得玩家身份，等待名单匹配。");
        Check(page.ForChampion(99).Text=="成就待确认" && PlayerChampionMastery.Unavailable(99).Text=="成就未公开","Pending identity and explicitly hidden identity have distinct labels");
        Console.WriteLine("PASS: LCU/Live identity recovery, same-champion players, hidden slots, selection-only public players, ambiguity, cache, backoff and cancellation.");
    }
}
