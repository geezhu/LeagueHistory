using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class GameBarBridgeChecks
{
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static void Run()
    {
        long now=100000;
        Check(GameBarBridge.DescribeWidgetState(null,now).Contains("未连接"),"A written snapshot alone must not claim the widget is connected");
        var heartbeat=new JObject{["updatedAt"]=now-100,["pinned"]=true,["shortcut"]="PrtSc",["width"]=820,["height"]=560};
        Check(GameBarBridge.DescribeWidgetState(heartbeat,now).Contains("已连接并固定"),"A fresh rendered widget reports connection");
        Check(GameBarBridge.DescribeWidgetState(heartbeat,now+16000).Contains("未连接"),"A closed widget cannot retain connected status");
        heartbeat["width"]=0;
        Check(GameBarBridge.DescribeWidgetState(heartbeat,now).Contains("画面尚未加载"),"A zero-size widget is diagnosed separately from missing statistics");
        heartbeat["readError"]="read failed";
        Check(GameBarBridge.DescribeWidgetState(heartbeat,now).Contains("尚未读到有效数据"),"Widget-side file failures are visible in settings");
        heartbeat["width"]=820;heartbeat["connected"]=true;heartbeat["active"]=false;
        Check(GameBarBridge.DescribeWidgetState(heartbeat,now).Contains("等待对局"),"A connected widget in the lobby is waiting for a match, not disconnected");
        heartbeat["pinned"]=false;
        Check(GameBarBridge.DescribeWidgetState(heartbeat,now).Contains("尚未固定"),"Pinning requirement remains visible outside matches");
        heartbeat["pinned"]=true;
        heartbeat["active"]=true;
        Check(GameBarBridge.DescribeWidgetState(heartbeat,now).Contains("已连接并固定"),"A transient read error retains a connection while the last snapshot remains fresh");
        heartbeat["connected"]=false;
        Check(GameBarBridge.DescribeWidgetState(heartbeat,now).Contains("尚未读到有效数据"),"A stopped publisher remains disconnected even if the widget itself has a heartbeat");
        string folder=Path.Combine(Path.GetTempPath(),"League-GameBar-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var icon=new Bitmap(32,32);
            var roster=new LoadingRoster("123",2400,"KIWI",[
                new("enemy-key","Enemy","private-puuid",123,1,100,false),
                new("self-key","Self","secret-puuid",456,2,200,true)]);
            var ranks=new Dictionary<string,PlayerChampionTier>{["self-key"]=new("T4","胜 48.5%","arammeta · 调整后胜率",2400,"none"){PickRate="7.1%"}};
            var icons=new Dictionary<int,Image>{[2]=icon};
            var bridge=new GameBarBridge(folder);
            void Publish(bool enabled=true,bool active=true)=>bridge.Publish(roster,ranks,icons,id=>"英雄 "+id,_=>"近期 20 场，12 胜","match-1","当前对局",enabled,active,true);
            Publish();
            string file=Path.Combine(folder,"snapshot.json");
            Check(SpinWait.SpinUntil(()=>File.Exists(file),5000),"Game Bar snapshot is written asynchronously");
            var first=JObject.Parse(File.ReadAllText(file));
            for(int i=0;i<100;i++)Publish();
            bridge.Dispose();bridge.Completion.GetAwaiter().GetResult();
            var latest=JObject.Parse(File.ReadAllText(file));
            Check((int)latest["protocol"]! ==1 && ((string)latest["revision"]!).EndsWith(":101"),"Bounded writer keeps the newest snapshot");
            Check((string?)latest["shortcutOwner"]=="main" && (string?)latest["shortcut"]=="" && latest["toggleSequence"]==null && latest["toggleAt"]==null,"Older widgets cannot register a fallback shortcut, and no keyboard commands are published");
            Check((string?)latest["command"]==(string?)first["command"],"Statistics refresh never overrides a manually hidden widget");
            Check((string?)latest["teams"]![0]=="我方" && (string?)latest["players"]![0]!["name"]=="Self · 自己","Own red team is placed first");
            Check(latest["players"]!.Count()==10 && !latest["players"]![1]!.HasValues,"Missing slots stay on the correct team");
            Check((string?)latest["players"]![5]!["name"]=="Enemy","Enemy remains in the other column");
            Check((string?)latest["players"]![0]!["tier"]=="T4" && ((string)latest["players"]![0]!["stats"]!).Contains("7.1%"),"Tier, win rate and pick rate cross the bridge");
            using var imageBytes=new MemoryStream(Convert.FromBase64String((string)latest["players"]![0]!["icon"]!));
            using var decoded=Image.FromStream(imageBytes);
            Check(decoded.Width==48,"Cached champion image is sized for the widget");
            Check(!latest.ToString().Contains("puuid") && !latest.ToString().Contains("private-puuid") && !latest.ToString().Contains("secret-puuid"),"Account identifiers are not exported");
            Check(!Directory.EnumerateFiles(folder,"*.tmp").Any(),"Atomic writer cleans temporary files");
            using var disabled=new GameBarBridge(folder);
            disabled.Publish(roster,ranks,icons,id=>"英雄",_=>"战绩","match-1","已关闭",false,false,false);
            disabled.Dispose();disabled.Completion.GetAwaiter().GetResult();
            var cleared=JObject.Parse(File.ReadAllText(file));
            Check(cleared["players"]!.All(row=>!row.HasValues) && !(bool)cleared["enabled"]!,"Disabling the feature removes old match information");
            Check((string?)cleared["session"]!=(string?)latest["session"],"Restart gives commands a new instance identity");
            Console.WriteLine("Game Bar bridge checks passed.");
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
