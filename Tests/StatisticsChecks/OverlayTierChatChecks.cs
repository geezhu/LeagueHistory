using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class OverlayTierChatChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run()
    {
        const long now=100000;
        int[] slots=[99,25,0,0,0,103,0,0,0,0];
        var data=new JObject{["protocol"]=1,["id"]=Guid.NewGuid().ToString(),["session"]="match",
            ["target"]="own",["slot"]=-1,["championId"]=0,["updatedAt"]=now,["dismissed"]=false,["cancelled"]=false};
        var request=OverlayChatRequest.Parse(data,"match",slots,now)??throw new Exception("Explicit current-match command rejected");
        Check(request.Kind=="tier","Old commands retain tier semantics");
        foreach(var change in new (string Key,JToken Value)[]{("target","all"),("slot",0),("session","old"),("id","bad"),
            ("protocol",2),("updatedAt",now-30001),("updatedAt",now+1),("dismissed","true"),("championId",99),("kind","custom"),("kind",1),("kind",JValue.CreateNull())})
        {
            var bad=(JObject)data.DeepClone();bad[change.Key]=change.Value;
            Check(OverlayChatRequest.Parse(bad,"match",slots,now)==null,"Malformed/stale commands rejected: "+change.Key);
        }
        var single=(JObject)data.DeepClone();single["target"]="single";single["slot"]=5;single["championId"]=103;
        single["kind"]="mastery";
        Check(OverlayChatRequest.Parse(single,"match",slots,now)?.Kind=="mastery","Mastery uses the validated current roster command");
        Check(OverlayChatRequest.Parse(single,"match",slots,now)?.ChampionId==103,"Enemy single selection accepted");
        single["slot"]=2;Check(OverlayChatRequest.Parse(single,"match",slots,now)==null,"Empty slot cannot send");
        var seen=new OverlayChatRequests();
        Check(seen.Take(request) && !seen.Take(request),"The same click never sends twice");
        var other=request with{Id=Guid.NewGuid().ToString("N")};Check(seen.Take(other) && !seen.Take(request),"An intervening command cannot replay an old click");
        var ready=request with{Dismissed=true};var gate=new OverlayChatReady(request);
        Check(gate.Observe(request,now,true)==OverlayChatReadiness.Waiting,"Game foreground alone cannot send while Game Bar is open");
        Check(gate.Observe(ready,now,false)==OverlayChatReadiness.Waiting,"Dismissing Game Bar while another app is foreground cannot send");
        Check(gate.Observe(ready,now,true)==OverlayChatReadiness.Waiting && gate.Observe(ready,now+599,true)==OverlayChatReadiness.Waiting,"Foreground must settle before input");
        Check(gate.Observe(ready,now+600,true)==OverlayChatReadiness.Ready,"A dismissed overlay and stable game permit sending");
        Check(gate.Observe(request,now+610,true)==OverlayChatReadiness.Waiting && gate.Observe(ready,now+800,true)==OverlayChatReadiness.Waiting,"Reopening Game Bar resets readiness");
        Check(gate.Observe(ready with{Cancelled=true},now+900,true)==OverlayChatReadiness.Cancelled,"Explicit cancellation suppresses input");
        Check(gate.Observe(other,now+900,true)==OverlayChatReadiness.Cancelled && gate.Observe(ready with{Target="enemy"},now+900,true)==OverlayChatReadiness.Cancelled,"Changed request or scope cannot repurpose a pending send");
        Check(gate.Observe(ready with{Kind="mastery"},now+900,true)==OverlayChatReadiness.Cancelled,"Changing tier to mastery cancels the pending command");
        Check(gate.Observe(ready,now+20000,true)==OverlayChatReadiness.TimedOut,"A late return to the game never sends stale text");

        var roster=new LoadingRoster("game",2400,"KIWI",[
            new("enemy","敌人","secret",1,103,100,false),new("self","自己","private",2,99,200,true),new("ally","队友","",3,25,200,false)]);
        var ranks=roster.Players.ToDictionary(p=>p.Key,p=>new PlayerChampionTier("T2","display text is not parsed","note",2400,"none")
            {WinRateValue=.527,PickRateValue=.086,Version="16.18"});
        string Hero(int id)=>id switch{99=>"拉克丝",25=>"莫甘娜",_=>"阿狸"};
        string Own()=>string.Join("\n",OverlayTierChatMessage.Build(roster,request,ranks,Hero));
        Check(Own().Contains("我方") && Own().Contains("拉克丝") && Own().Contains("莫甘娜") && !Own().Contains("阿狸"),"Red-side self maps to own team correctly");
        string enemy=string.Join("",OverlayTierChatMessage.Build(roster,request with{Target="enemy"},ranks,Hero));
        Check(enemy.Contains("阿狸") && !enemy.Contains("拉克丝") && enemy.Contains("调整后胜率 52.7%｜样本选取率 8.6%"),"Enemy tier preserves Mayhem definitions and rates");
        var individual=request with{Target="single",Slot=5,ChampionId=103};
        Check(OverlayTierChatMessage.Select(roster,individual).Single().Key=="enemy","Single selection uses exactly the clicked slot");
        var unknown=roster with{Players=roster.Players.Select(p=>p with{Self=false}).ToArray()};
        Check(OverlayTierChatMessage.Select(unknown,request).Length==0,"Unknown self never guesses sides");
        Check(OverlayTierChatMessage.Select(unknown,individual with{Slot=0}).Single().Key=="enemy","A single hero remains selectable without guessing a side");
        Check(OverlayTierChatMessage.Select(roster,individual with{ChampionId=99}).Length==0,"Changed hero is rejected");
        ranks["self"]=ranks["self"] with{Position="mid",ReferencePosition=true,Version="16.18",Queue=420};
        string normal=string.Join("",OverlayTierChatMessage.Build(roster with{Queue=420,Mode="CLASSIC"},request with{Target="single",Slot=0,ChampionId=99},ranks,Hero));
        Check(normal.Contains("召唤师峡谷") && normal.Contains("OP.GG") && normal.Contains("参考中路") && normal.Contains("胜率 52.7%｜登场率 8.6%"),"Rift messages retain the reference lane instead of presenting it as the actual role");
        ranks.Clear();bool rejected=false;
        try{_=Own();}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Unloaded statistics do not send empty placeholder messages");
        CheckMastery(roster,request,Hero);
        Task.Run(CheckBridge).GetAwaiter().GetResult();
        Console.WriteLine("PASS: Game Bar team/single chat, rates, request validation, replay prevention, cancellation, foreground/dismissal gate and timeout. No game input sent.");
    }
    private static void CheckMastery(LoadingRoster roster,OverlayChatRequest request,Func<int,string> hero)
    {
        var masteries=new Dictionary<string,PlayerChampionMastery>{
            ["self"]=new(99,12,186321,null,true),
            ["ally"]=PlayerChampionMastery.Unavailable(25),
            ["enemy"]=new(103,123,3000000000,null,true,true)};
        string Message(OverlayChatRequest command)=>string.Join("\n",OverlayMasteryChatMessage.Build(roster,command,masteries,hero));
        string own=Message(request with{Kind="mastery"});
        Check(own.Contains("我方") && own.Contains("12级 / 186,321点") && own.Contains("莫甘娜 身份未公开") && !own.Contains("阿狸") && !own.Contains("secret"),"Mastery follows players and sides, excluding identities and invented zero values");
        string enemy=Message(request with{Kind="mastery",Target="enemy"});
        Check(enemy.Contains("123级 / 3,000,000,000点（缓存）") && !enemy.Contains("拉克丝"),"Enemy mastery preserves 64-bit points, uncapped levels and stale marker");
        var single=request with{Kind="mastery",Target="single",Slot=5,ChampionId=103};
        Check(Message(single).Contains("阿狸") && !Message(single).Contains("莫甘娜"),"Single mastery sends only the clicked player");
        void Rejected(Action build,string note){bool rejected=false;try{build();}catch(InvalidOperationException){rejected=true;}Check(rejected,note);}
        masteries["enemy"]=masteries["enemy"] with{ChampionId=99};
        Rejected(()=>Message(single),"A previous champion's mastery cannot be sent");
        masteries["enemy"]=new(103,null,null,null,true);
        Check(Message(single).Contains("暂无成就记录") && !Message(single).Contains("0点"),"An absent record is not interpreted as zero points");
        masteries["enemy"]=new(103,0,0,null,true);
        Check(Message(single).Contains("0级 / 0点"),"Confirmed zero remains sendable");
        masteries.Clear();Rejected(()=>Message(request),"Unloaded or unavailable mastery does not send placeholder-only messages");
        var lines=ChampionMasteryChatMessage.Build("海克斯大乱斗","我方",Enumerable.Range(0,5).Select(i=>new ChampionMasteryChatEntry(new string('英',24)+i,new(1,123,3000000000,null,true))));
        Check(lines.Count>1 && lines.All(l=>l.Length<=180) && lines.All(l=>l.StartsWith("【海克斯大乱斗·我方】")),"Long team messages split with context before the game chat limit");
    }
    private static async Task CheckBridge()
    {
        string folder=Path.Combine(Path.GetTempPath(),"League-chat-check-"+Guid.NewGuid().ToString("N"));
        var bridge=new GameBarBridge(folder);
        try
        {
            var roster=new LoadingRoster("1",450,"ARAM",[new("private","Test","secret",1,99,100,true)]);
            void Publish(string match,bool active=true)=>bridge.Publish(roster,new Dictionary<string,PlayerChampionTier>(),new Dictionary<int,Image>(),_=>"拉克丝",_=>"",match,"",true,active,true);
            Publish("one");string snapshot=Path.Combine(folder,"snapshot.json");
            Check(SpinWait.SpinUntil(()=>File.Exists(snapshot),5000),"Snapshot written");
            var source=JObject.Parse(await File.ReadAllTextAsync(snapshot));
            Check((int?)source["chatProtocol"]==1 && (bool?)source["chatTeams"]==true && (bool?)source["chatMastery"]==true,"Send capability, mastery extension and side identity advertised");
            var data=new JObject{["protocol"]=1,["id"]=Guid.NewGuid().ToString("N"),["session"]=source["session"],
                ["target"]="single",["kind"]="mastery",["slot"]=0,["championId"]=99,["updatedAt"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),["dismissed"]=false,["cancelled"]=false};
            string file=Path.Combine(folder,"chat-request.json");await File.WriteAllTextAsync(file,data.ToString());
            var request=await bridge.ReadChatRequestAsync();Check(request!=null && !request.Dismissed && request.Kind=="mastery","Mastery click reaches main process before dismissal");
            data["dismissed"]=true;await File.WriteAllTextAsync(file,data.ToString());
            var ready=await bridge.ReadChatRequestAsync();Check(ready!.Dismissed && ready.SameCommand(request!),"Closing Game Bar updates the same command without a new send");
            bridge.PublishChatStatus(request!,"permission","请以管理员身份启动 League.exe。",true);
            string status=Path.Combine(folder,"chat-status.json");Check(SpinWait.SpinUntil(()=>File.Exists(status),5000),"Send outcome written");
            var response=JObject.Parse(await File.ReadAllTextAsync(status));
            Check((string?)response["requestId"]==request!.Id && (bool?)response["complete"]==true && !response.ToString().Contains("secret"),"Outcome is scoped and excludes private player identifiers");
            Publish("two");Check(await bridge.ReadChatRequestAsync()==null,"A previous match cannot trigger sending");
            Publish("one",false);Check(await bridge.ReadChatRequestAsync()==null,"Inactive snapshot rejects requests");
            bridge.Dispose();await bridge.Completion;
            Check(!Directory.EnumerateFiles(folder,"*.tmp").Any(),"Atomic command responses leave no temporary files");
        }
        finally{bridge.Dispose();await bridge.Completion;if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
