using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class OverlayVoiceChatChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run()
    {
        const long now=100000;
        var data=new JObject{["protocol"]=1,["id"]=Guid.NewGuid().ToString("N"),["session"]="game",
            ["target"]="text",["kind"]="voice",["slot"]=-1,["championId"]=0,["updatedAt"]=now,
            ["dismissed"]=false,["cancelled"]=false,["confirmed"]=true,["text"]="  等队友到齐再开团。  "};
        var request=OverlayChatRequest.Parse(data,"game",[],now)??throw new Exception("Confirmed text rejected");
        Check(request.IsVoice && request.Text=="等队友到齐再开团。" && OverlayVoiceChatText.Build(request).Single()==request.Text,
            "Confirmed edited text sends exactly one message without relying on hero statistics or account identity");
        foreach(var change in new (string Key,JToken Value)[]{("confirmed",false),("confirmed","true"),("confirmed",JValue.CreateNull()),
            ("text",JValue.CreateNull()),("text",1),("text"," "),("text","第一行\n第二行"),("text","文字\r文字"),("text","文字\t文字"),
            ("text","文字\0文字"),("text","文字\u2028文字"),("text","文字\u2029文字"),("text"," /ff"),
            ("text",new string('字',181)),("text","尾部\ud800"),("text","孤立\udc00"),
            ("target","own"),("slot",0),("championId",99),("session","old"),("updatedAt",now-30001),("updatedAt",now+1)})
        {
            var invalid=(JObject)data.DeepClone();invalid[change.Key]=change.Value;
            Check(OverlayChatRequest.Parse(invalid,"game",[],now)==null,"Invalid/unconfirmed/multi-line/old voice input rejected: "+change.Key);
        }
        var missing=(JObject)data.DeepClone();missing.Remove("confirmed");
        Check(OverlayChatRequest.Parse(missing,"game",[],now)==null,"A draft is not a send command");
        data["text"]=new string('字',178)+"😀";
        Check(OverlayChatRequest.Parse(data,"game",[],now)?.Text.Length==180,"Full surrogate pair at the chat limit remains valid");
        data["kind"]="tier";data["target"]="own";
        Check(OverlayChatRequest.Parse(data,"game",[],now)==null,"Statistics cannot carry arbitrary text");
        var ready=request with{Dismissed=true};var gate=new OverlayChatReady(request);
        Check(gate.Observe(request,now,true)==OverlayChatReadiness.Waiting,"Confirmation while Game Bar is open cannot input");
        Check(gate.Observe(ready,now,false)==OverlayChatReadiness.Waiting,"Confirmation never types into another foreground application");
        Check(gate.Observe(ready,now,true)==OverlayChatReadiness.Waiting && gate.Observe(ready,now+600,true)==OverlayChatReadiness.Ready,
            "Voice reuses the stable foreground and dismissed-overlay gate");
        Check(gate.Observe(ready with{Text="被替换的文字"},now+601,true)==OverlayChatReadiness.Cancelled,"Editing after confirmation invalidates the send");
        Check(gate.Observe(ready with{Confirmed=false},now+601,true)==OverlayChatReadiness.Cancelled,"Revoked confirmation cancels the send");
        Check(gate.Observe(ready with{Cancelled=true},now+601,true)==OverlayChatReadiness.Cancelled,"Explicit cancel suppresses voice input");
        Check(gate.Observe(ready,now+20000,true)==OverlayChatReadiness.TimedOut,"Late return cannot send stale voice text");
        var seen=new OverlayChatRequests();Check(seen.Take(request) && !seen.Take(ready),"Dismissal cannot replay the same confirmed click");
        Task.Run(CheckBridge).GetAwaiter().GetResult();
        Console.WriteLine("PASS: confirmed voice text, length/Unicode validation, edited payload binding, replay prevention, cancellation, timeout and local bridge. No microphone or game input used.");
    }
    private static async Task CheckBridge()
    {
        string folder=Path.Combine(Path.GetTempPath(),"League-voice-check-"+Guid.NewGuid().ToString("N"));
        var bridge=new GameBarBridge(folder);
        try
        {
            void Publish(string session,bool active=true)=>bridge.Publish(null,new Dictionary<string,PlayerChampionTier>(),new Dictionary<int,Image>(),_=>"",_=>"",session,"",true,active,true);
            Publish("one");string snapshot=Path.Combine(folder,"snapshot.json");
            Check(SpinWait.SpinUntil(()=>File.Exists(snapshot),5000),"Voice capability snapshot published");
            var source=JObject.Parse(await File.ReadAllTextAsync(snapshot));
            Check((bool?)source["chatVoice"]==true && (bool?)source["chatTeams"]==false,"Voice works independently of an identified self or loaded hero statistics");
            var data=new JObject{["protocol"]=1,["id"]=Guid.NewGuid().ToString("N"),["session"]=source["session"],["target"]="text",
                ["kind"]="voice",["slot"]=-1,["championId"]=0,["updatedAt"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["dismissed"]=false,["cancelled"]=false,["confirmed"]=true,["text"]="等队友到齐再开团。"};
            string file=Path.Combine(folder,"chat-request.json");await File.WriteAllTextAsync(file,data.ToString());
            var request=await bridge.ReadChatRequestAsync();Check(request?.Text=="等队友到齐再开团。","The confirmed text crosses the bridge unchanged");
            data["dismissed"]=true;await File.WriteAllTextAsync(file,data.ToString());
            var dismissed=await bridge.ReadChatRequestAsync();Check(dismissed!.Dismissed && dismissed.SameCommand(request!),"Leaving Game Bar preserves exactly the confirmed payload");
            bridge.PublishChatStatus(request!,"cancelled","未发送：请求已取消。",true);
            string status=Path.Combine(folder,"chat-status.json");Check(SpinWait.SpinUntil(()=>File.Exists(status),5000),"Voice send response published");
            Check(!File.ReadAllText(status).Contains(request!.Text),"Status and diagnostics do not echo a user's draft");
            Publish("two");Check(await bridge.ReadChatRequestAsync()==null,"Match change invalidates a voice request");
            Publish("one",false);Check(await bridge.ReadChatRequestAsync()==null,"Inactive connection cannot accept voice sends");
        }
        finally{bridge.Dispose();await bridge.Completion;if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
