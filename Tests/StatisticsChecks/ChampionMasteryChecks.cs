using System.Net;
using System.Reflection;
using League;
using League.Clients;
using League.Controls;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class ChampionMasteryChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private const string Sample="""
        [{"championId":103,"championLevel":12,"championPoints":186321,"lastPlayTime":1700000000000},
         {"championId":1,"championLevel":123,"championPoints":3000000000},
         {"championId":103,"championLevel":2,"championPoints":100},
         {"championId":2,"championLevel":0,"championPoints":0}, {"championId":4}]
        """;
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>action(request,token);}
    private static HttpResponseMessage Response(string data=Sample,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=new StringContent(data)};
    private static HttpClient Client(Handler handler)=>new(handler){BaseAddress=new Uri("https://test.invalid/")};
    public static async Task RunAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),"League-Mastery-"+Guid.NewGuid().ToString("N"));
        try
        {
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls=0;
            using var client=Client(new Handler(async(req,token)=>{Interlocked.Increment(ref calls);started.TrySetResult();await release.Task.WaitAsync(token);return Response();}));
            string scope="platform-A|viewer";
            var cache=new ChampionMasteryService(()=>client,()=>scope,root);
            using var cancellation=new CancellationTokenSource();
            var cancelled=cache.GetAsync("player",cancellation.Token);
            var shared=cache.GetAsync("player",CancellationToken.None);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));cancellation.Cancel();
            try{await cancelled;throw new Exception("Cancelled waiter unexpectedly succeeded");}catch(OperationCanceledException){}
            release.SetResult();var page=await shared;
            Check(calls==1 && page.Data?.Count==3,"Concurrent views share one complete list; cancelling a view preserves the other");
            var fox=page.ForChampion(103);
            Check(fox.Level==12 && fox.Points==186321 && fox.Text.Contains("18.6万") && fox.Description.Contains("186,321"),"Selected champion, compact points, exact tooltip and duplicate handling");
            Check(page.ForChampion(1).Level==123 && page.ForChampion(1).Points==3000000000,"Unlimited mastery levels and 64-bit points");
            Check(page.ForChampion(2).Points==0 && page.ForChampion(3).Available && page.ForChampion(3).Points==null,"Zero points differ from no achievement record");
            await cache.GetAsync("player",CancellationToken.None);Check(calls==1,"Changing selected champion reuses the player's list");
            page.Data![0]!["championPoints"]=0;
            Check((await cache.GetAsync("player",CancellationToken.None)).ForChampion(103).Points==186321,"Caller mutations cannot corrupt the shared cache");
            var diskCache=new ChampionMasteryService(()=>client,()=>scope,root);
            Check((await diskCache.GetAsync("player",CancellationToken.None)).ForChampion(103).Points==186321 && calls==1,"A restarted service reads persistent data without a request");
            Check(Directory.GetFiles(root,"*.json").Length==1 && !File.ReadAllText(Directory.GetFiles(root,"*.json")[0]).Contains("player"),"Disk contains normalized statistics, no account or authorization fields");
            scope="platform-B|viewer";await cache.GetAsync("player",CancellationToken.None);Check(calls==2,"Platforms do not share player caches");
            cache.Invalidate("player");await cache.GetAsync("player",CancellationToken.None,true);Check(calls==3,"Post-game invalidation forces a new list");
            int failedCalls=0;bool denied=false;
            var refreshStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var refreshRelease=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var offline=Client(new Handler(async(_,token)=>{int n=Interlocked.Increment(ref failedCalls);if(n==1)return Response();refreshStarted.TrySetResult();await refreshRelease.Task.WaitAsync(token);return Response("",denied?HttpStatusCode.Forbidden:HttpStatusCode.ServiceUnavailable);}));
            var staleCache=new ChampionMasteryService(()=>offline,()=>"fresh",Path.Combine(root,"stale"));
            await staleCache.GetAsync("one",CancellationToken.None);staleCache.CacheLifetime=TimeSpan.Zero;
            var stale=await staleCache.GetAsync("one",CancellationToken.None);
            await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(stale.Stale && stale.ForChampion(103).Points==186321,"Expired cache is returned without waiting for the refresh");
            var pending=staleCache.GetAsync("one",CancellationToken.None,true);
            refreshRelease.SetResult();
            var failed=await pending;
            Check(failed.Stale && failed.Data!=null && failed.Message.Length>0,"Transient errors preserve old statistics with an explicit stale status");
            await staleCache.GetAsync("one",CancellationToken.None);Check(failedCalls==2,"Failed refreshes back off instead of repeating at each poll");
            denied=true;
            var forbidden=await staleCache.GetAsync("one",CancellationToken.None,true);
            Check(forbidden.Data==null && forbidden.Message.Contains("不允许") && Directory.GetFiles(Path.Combine(root,"stale"),"*.json").Length==0,"A privacy denial discards prior statistics and disk cache");
            int before=failedCalls;
            Check((await staleCache.GetAsync(Guid.Empty.ToString(),CancellationToken.None)).Data==null && failedCalls==before,"Hidden identity must not trigger a query");
            Check(!ChampionMasteryService.TryNormalize(JObject.Parse("{\"errorCode\":\"NOT_FOUND\"}"),out _) && !ChampionMasteryService.TryNormalize(JArray.Parse("[{\"championId\":4}]"),out _) && ChampionMasteryService.TryNormalize(new JArray(),out _),"Error and malformed responses differ from a valid empty list");
            Console.WriteLine("PASS: mastery identity, coalescing, cancellation, persistence, expiry, invalidation, privacy and failure backoff.");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    public static void Render()
    {
        using var owner=new FormMain();
        var type=typeof(FormMain).Assembly.GetType("League.ChampionSelectAssistant")!;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var assistant=(Form)Activator.CreateInstance(type,new object[]{owner})!;
        ((System.Windows.Forms.Timer)type.GetField("_timer",flags)!.GetValue(assistant)!).Stop();
        string root=Path.Combine(Path.GetTempPath(),"League-Mastery-UI-"+Guid.NewGuid().ToString("N"));
        using var client=Client(new Handler((req,_)=>Task.FromResult(Response(req.RequestUri!.AbsolutePath.Contains("second")?"[{\"championId\":103,\"championLevel\":5,\"championPoints\":2000}]":Sample))));
        try
        {
            type.GetField("_masteryService",flags)!.SetValue(assistant,new ChampionMasteryService(()=>client,directory:root));
            type.GetField("_selecting",flags)!.SetValue(assistant,true);
            type.GetField("_queue",flags)!.SetValue(assistant,450);
            type.GetField("_hero",flags)!.SetValue(assistant,103);type.GetField("_masteryPuuid",flags)!.SetValue(assistant,"first");
            var add=type.GetMethod("AddMasterySummary",flags)!;
            add.Invoke(assistant,null);
            var label=(Label)type.GetField("_masteryLabel",flags)!.GetValue(assistant)!;
            Pump(()=>label.Text.Contains("18.6万"));
            Check(label.Text.Contains("12级"),"Assistant shows mastery of the selected player and hero");
            type.GetMethod("AddTierSummary",flags)!.Invoke(assistant,new object[]{"T2","胜率 50.4%"});
            var body=(FlowLayoutPanel)type.GetField("_body",flags)!.GetValue(assistant)!;
            Check(body.Controls.GetChildIndex(label.Parent!)>body.Controls.OfType<FlowLayoutPanel>().Select(c=>body.Controls.GetChildIndex(c)).Max(),"Achievement stays below tier without replacing its tooltip or balance row");
            var send=(Button)type.GetField("_sendMastery",flags)!.GetValue(assistant)!;
            Check(send.Enabled && send.Text=="发送熟练度","Assistant has a send button beside loaded mastery");
            string? bodySent=null;int posts=0,serverHero=103;bool changeBeforePost=false;string phase="ChampSelect";
            using var chatClient=Client(new Handler(async(req,token)=>
            {
                switch(req.RequestUri!.AbsolutePath)
                {
                    case "/lol-champ-select/v1/session":return Response(new JObject{["localPlayerCellId"]=1,["myTeam"]=new JArray(new JObject{["cellId"]=1,["championId"]=serverHero,["puuid"]="first"})}.ToString());
                    case "/lol-gameflow/v1/gameflow-phase":return Response("\""+phase+"\"");
                    case "/lol-chat/v1/conversations":
                        if(changeBeforePost)type.GetField("_hero",flags)!.SetValue(assistant,1);
                        return Response("[{\"id\":\"room/one\",\"type\":\"championSelect\"}]");
                    default:
                        Check(req.Method==HttpMethod.Post && req.RequestUri.AbsolutePath.Contains("/messages"),"Only the selected champion chat room receives messages");
                        bodySent=await req.Content!.ReadAsStringAsync(token);posts++;return Response("{}");
                }
            }));
            void Send(){var task=(Task)type.GetMethod("SendMasteryAsync",flags)!.Invoke(assistant,new object[]{chatClient})!;Pump(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
            Send();Check(posts==1 && bodySent!.Contains("186,321") && bodySent.Contains("英雄熟练度") && bodySent.Contains("大乱斗"),"Assistant sends exact mastery points and map to champion-select chat");
            Send();Check(posts==2,"A later explicit click sends again instead of reporting a cached success");
            serverHero=1;Send();Check(posts==2,"A newer server champion selection suppresses stale mastery");serverHero=103;
            phase="InProgress";Send();Check(posts==2,"Assistant never sends to champion chat after the game starts");phase="ChampSelect";
            changeBeforePost=true;Send();Check(posts==2,"Changing hero during room lookup cancels the send before POST");
            type.GetField("_hero",flags)!.SetValue(assistant,103);
            type.GetField("_hero",flags)!.SetValue(assistant,1);type.GetMethod("ClearBody",flags)!.Invoke(assistant,null);add.Invoke(assistant,null);
            label=(Label)type.GetField("_masteryLabel",flags)!.GetValue(assistant)!;
            Pump(()=>label.Text.Contains("123级"));
            type.GetField("_hero",flags)!.SetValue(assistant,0);
            using var view=new LoadingOverlayView(id=>"英雄 "+id,_=>"已缓存战绩"){Size=new Size(850,700)};
            var page=new ChampionMasteryPage(JArray.Parse(Sample),DateTimeOffset.UtcNow);
            var second=new PlayerChampionMastery(103,5,2000,null,true);
            var roster=new LoadingRoster("game",450,"ARAM",[new("first","玩家甲","first",1,103,100,true),new("second","玩家乙","second",2,103,200,false)]);
            var masteries=new Dictionary<string,PlayerChampionMastery>{["first"]=page.ForChampion(103),["second"]=second};
            var ranks=new Dictionary<string,PlayerChampionTier>{["first"]=new("T2","胜率 50.4%","来源",450,"none")};
            view.UpdateRoster(roster,ranks,new Dictionary<int,Image>(),"成就布局验证",masteries);
            IEnumerable<Control> All(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(All(c)));
            Check(All(view).Any(c=>c.Text.Contains("18.6万")) && All(view).Any(c=>c.Text.Contains("5级")),"Same champion on both sides retains each player's mastery");
            using var image=new Bitmap(view.Width,view.Height);view.DrawToBitmap(image,new Rectangle(Point.Empty,view.Size));image.Save(Path.Combine(AppContext.BaseDirectory,"champion-mastery-overlay.png"));
            Console.WriteLine("PASS: assistant mastery, champion switching and both-team achievement cards.");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static void Pump(Func<bool> condition)
    {
        var limit=DateTime.UtcNow.AddSeconds(5);
        while(!condition() && DateTime.UtcNow<limit){Application.DoEvents();Thread.Sleep(5);}
        Check(condition(),"UI achievement update timed out");
    }
}
