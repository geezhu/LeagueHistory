using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class OverlayChampionDetailChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run()
    {
        string session="instance:match",id=Guid.NewGuid().ToString("N");long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var request=new JObject{["protocol"]=1,["id"]=id,["session"]=session,["updatedAt"]=now,["slot"]=5,["championId"]=103};
        int[] slots=[1,0,0,0,0,103,0,0,0,0];
        Check(OverlayDetailRequest.Parse(request,session,slots,now)?.ChampionId==103,"Enemy hero can be selected without account identifiers");
        Check(OverlayDetailRequest.Parse(request,"next-match",slots,now)==null,"Previous game requests are rejected");
        Check(OverlayDetailRequest.Parse(request,session,slots,now+30001)==null,"Stale requests cannot start new loads");
        request["slot"]=1;Check(OverlayDetailRequest.Parse(request,session,slots,now)==null,"Empty roster slots cannot request arbitrary heroes");
        request["slot"]=-1;request["championId"]=0;Check(OverlayDetailRequest.Parse(request,session,slots,now)?.Slot==-1,"Returning to roster cancels selection");
        request["id"]="../../other";Check(OverlayDetailRequest.Parse(request,session,slots,now)==null,"Requests only accept opaque identifiers");
        foreach(string mode in new[]{"aram","ranked","urf","nexus_blitz","arena"})
        {
            var raw=JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","Opgg",mode+".json")));
            var build=OpggBuild.Parse(raw,new("global",mode,"all",mode=="ranked"?"mid":"none",103));
            var data=OverlayChampionDetailService.FromOpgg(build);
            var sections=(JArray)data["sections"]!;
            Check(sections.All(s=>(string?)s["tab"]!="augments"),"Other modes never show Mayhem recommendations");
            foreach(var row in sections.SelectMany(s=>(JArray)s["rows"]!).Where(r=>r["later"]!=null))
            {
                var core=((JArray)row["icons"]!).Select(i=>(int)i["id"]!).ToHashSet();
                Check(((JArray)row["later"]!).All(i=>!core.Contains((int)i["id"]!)),"Later equipment excludes the chosen core combination");
            }
            var spells=sections.Single(s=>(string?)s["title"]=="召唤师技能");
            Check(spells["rows"]!.Count()<=2,"Only most-used and highest-win spell highlights are shown");
            Check(!data.ToString().Contains("puuid"),"Details do not carry player identifiers");
        }
        var index=AramMetaIndex.Parse(JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","AramMeta","index.json"))));
        var detail=JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","AramMeta","103.json")));
        var mayhem=OverlayChampionDetailService.FromAramMeta(new(index,detail,true,DateTime.UtcNow),103);
        Check(((JArray)mayhem["sections"]!).Count(s=>(string?)s["tab"]=="augments")==3,"Mayhem includes all three rarities");
        Check(((JArray)mayhem["sections"]!).All(s=>(string?)s["tab"]!="runes"),"Mayhem does not present a real rune recommendation");
        Check(((string)mayhem["source"]!).StartsWith("arammeta"),"Full source name remains visible");
        Check(((JArray)mayhem["sections"]!).Any(s=>(string?)s["tab"]=="builds" && s["rows"]!.Any()),"Mayhem includes equipment recommendations");
        Check(((JArray)OverlayChampionDetailService.FromAramMeta(new(index,null,true,DateTime.UtcNow),103)["sections"]!).All(s=>(string?)s["tab"]=="augments"),"Missing detail does not discard cached augment recommendations");
        CheckBridge().GetAwaiter().GetResult();
        Console.WriteLine("PASS: hero-detail request isolation, both teams, mode-specific recommendations, spell highlights, later equipment and response channel.");
    }
    private static async Task CheckBridge()
    {
        string folder=Path.Combine(Path.GetTempPath(),"League-detail-"+Guid.NewGuid().ToString("N"));
        var bridge=new GameBarBridge(folder);
        try
        {
            bridge.Publish(new("1",450,"ARAM",[new("private-key","测试玩家","private-account",1,103,100,true)]),new Dictionary<string,PlayerChampionTier>(),new Dictionary<int,Image>(),_=>"九尾妖狐",_=>"测试缓存战绩","one","当前对局",true,true,true);
            string snapshot=Path.Combine(folder,"snapshot.json");
            Check(SpinWait.SpinUntil(()=>File.Exists(snapshot),5000),"Snapshot becomes available");
            var source=JObject.Parse(await File.ReadAllTextAsync(snapshot));
            var request=new JObject{["protocol"]=1,["id"]=Guid.NewGuid().ToString("N"),["session"]=source["session"],
                ["updatedAt"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),["slot"]=0,["championId"]=103};
            await File.WriteAllTextAsync(Path.Combine(folder,"detail-request.json"),request.ToString());
            var read=await bridge.ReadDetailRequestAsync();Check(read!=null,"Current selection reaches main process");
            bridge.PublishDetail(read!,new JObject{["state"]="ready",["sections"]=new JArray()});
            bridge.Dispose();await bridge.Completion;
            var result=JObject.Parse(await File.ReadAllTextAsync(Path.Combine(folder,"detail.json")));
            Check((string?)result["requestId"]==read!.Id && (int?)result["championId"]==103,"Reply identifies the requested hero, not a later slot occupant");
            Check(!result.ToString().Contains("private-"),"Private roster keys are absent from detail responses");
            Check(!Directory.EnumerateFiles(folder,"*.tmp").Any(),"Response writes are atomic and clean up temporary files");
        }
        finally{bridge.Dispose();await bridge.Completion;if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
