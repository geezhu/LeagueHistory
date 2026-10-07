using System.Reflection;
using League.Controls;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

internal static class LoadingOverlayChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run()
    {
        var previous=SynchronizationContext.Current;
        try { RunCore(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    private static void RunCore()
    {
        CheckFullscreenPause();
        foreach(var oldKey in new[]{"F6","F8","F10","PrtSc"})
        {
            var defaults=Newtonsoft.Json.JsonConvert.DeserializeObject<LeagueConfig>($"{{\"LoadingOverlayHotkey\":\"{oldKey}\"}}")!;
            Check(defaults.AutoShowLoadingOverlay && JObject.FromObject(defaults)["LoadingOverlayHotkey"]==null,"Legacy overlay bindings are ignored without selecting another key");
        }
        var chosen=new LeagueConfig{AutoShowLoadingOverlay=false};
        var saved=Newtonsoft.Json.JsonConvert.DeserializeObject<LeagueConfig>(Newtonsoft.Json.JsonConvert.SerializeObject(chosen))!;
        Check(!saved.AutoShowLoadingOverlay,"Automatic display preference survives settings reload");
        var display=new LoadingOverlayDisplayState();
        display.ObservePhase("ChampSelect",true);
        Check(!display.Requested,"Champion select does not open loading overlay");
        display.ObservePhase("GameStart",true);
        Check(display.Requested,"Automatic display starts before roster or statistics exist");
        display.ObservePhase("InProgress",true);
        Check(display.Requested,"Loading to game transition retains display request");
        display.Dismiss();display.ObservePhase("InProgress",true);
        Check(!display.Requested && display.Suppressed,"Manual dismiss persists for the current match");
        display.Request();Check(display.Requested,"Explicit display request can reopen after manual dismiss");
        display.ObservePhase("EndOfGame",true);Check(!display.Requested,"Game end clears automatic display");
        display.ObservePhase("GameStart",true);Check(display.Requested,"Next match auto-displays again");
        display.Reset();display.ObservePhase("InProgress",false);Check(!display.Requested,"Automatic display can be disabled");
        display.RequestPreview();display.ObservePhase("Lobby",true);Check(display.Preview && display.Requested,"Desktop preview works outside a match");
        display.Reset();Check(!display.Requested && !display.Preview,"Feature shutdown also hides preview");
        var flow=new JObject{["gameData"]=new JObject{["gameId"]="123",["queue"]=new JObject{["id"]=450},
            ["teamOne"]=new JArray(new JObject{["puuid"]="a",["summonerId"]=1,["gameName"]="玩家甲",["tagLine"]="01"}),
            ["teamTwo"]=new JArray(new JObject{["puuid"]="b",["championId"]=25}),
            ["playerChampionSelections"]=new JArray(new JObject{["puuid"]="a",["championId"]=99},new JObject{["puuid"]="unknown",["championId"]=111})}};
        var roster=LoadingRoster.Parse(flow,"a");
        Check(roster.Players.Length==2 && roster.Players[0].ChampionId==99,"Selections join through confirmed identity only");
        Check(roster.Players[0].Name=="玩家甲#01" && roster.Players[0].Self,"Name and self identity preserved");
        Check(roster.TeamLabel(100)=="我方" && roster.TeamLabel(200)=="对方","Own/enemy labels use confirmed identity");
        Check(LoadingRoster.Parse(flow,"").TeamLabel(100)=="蓝方","Unknown self is not assumed to be blue");
        Check(new[]{"GameStart","InProgress","Reconnect"}.All(LoadingRoster.CanDisplay),"Loading, game and reconnect are eligible");
        Check(new[]{"ChampSelect","Lobby","EndOfGame","None","Unknown"}.All(p=>!LoadingRoster.CanDisplay(p)),"Unrelated phases cannot show overlay");
        Check(LoadingRoster.WaitingStatus("Lobby").Contains("暂无进行中的对局") && LoadingRoster.WaitingStatus("EndOfGame").Contains("本局已结束"),"Lobby and game end are distinguished from connection failures");
        Check(LoadingRoster.WaitingStatus("ChampSelect").Contains("选人中") && LoadingRoster.WaitingStatus("").Contains("等待连接英雄联盟客户端"),"Missing client and champion selection have distinct waiting states");
        var live=new LiveMatchSnapshot("ARAM",12,0,[new("live-a","玩家甲#01","Lux","拉克丝","ORDER",true,1,[]),new("live-b","不同玩家","Morgana","莫甘娜","CHAOS",false,1,[])]);
        var merged=roster.FromLive(live,p=>p.ChampionAlias=="Lux"?99:25);
        Check(merged.Players[0].Puuid=="a" && merged.Players[1].Puuid=="","Live merge never guesses identity from champion");
        var type=typeof(League.FormMain).Assembly.GetType("League.LoadingOverlayForm")!;
        using var form=(Form)Activator.CreateInstance(type,new object[]{(Func<int,string>)(id=>id==99?"光辉女郎":"堕落天使"),(Func<LoadingPlayer,string>)(_=>"单双排 钻石 · 近期 20 场 12 胜 8 负")})!;
        var props=(CreateParams)type.GetProperty("CreateParams",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!;
        Check((props.ExStyle&0x08000000)!=0 && (bool)type.GetProperty("ShowWithoutActivation",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(form)!,"Overlay does not activate on show");
        var message=Message.Create(IntPtr.Zero,0x21,IntPtr.Zero,IntPtr.Zero);object[] args=[message];
        type.GetMethod("WndProc",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,args);
        Check(((Message)args[0]).Result==new IntPtr(3),"Mouse interaction does not steal keyboard focus");
        var placement=(Rectangle)type.GetMethod("Placement")!.Invoke(null,new object[]{new Rectangle(-1700,50,1280,720),new Rectangle(-1920,0,1920,1040)})!;
        Check(placement.Left>=-1920 && placement.Right<=0,"Overlay placement supports negative monitor coordinates");
        var players=Enumerable.Range(0,10).Select(i=>new LoadingPlayer("p"+i,"玩家 "+(i+1),"p"+i,i+1,i%2==0?99:25,i<5?100:200,i==0)).ToArray();
        var fixture=new LoadingRoster("test",450,"ARAM",players);
        var ranks=players.ToDictionary(p=>p.Key,p=>new PlayerChampionTier("T2","胜 52.8%","OP.GG · 普通大乱斗 · 全球 / 全段位 · 示例",450,"none"){PickRate="7.5%"});
        var view=(LoadingOverlayView)type.GetProperty("View")!.GetValue(form)!;
        using var icon=new Bitmap(48,48);using(var graphics=Graphics.FromImage(icon))graphics.Clear(Color.Teal);
        var icons=new Dictionary<int,Image>{[99]=icon,[25]=icon};
        form.ClientSize=new Size(1020,670);form.Show();
        view.UpdateRoster(fixture,ranks,icons,"数据已缓存 · 点击查看详情");Application.DoEvents();
        IEnumerable<Control> All(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(All(c)));
        Check(All(form).OfType<Label>().Any(c=>c.Text=="对局速览 · 双方英雄"),"Overlay title contains no removed shortcut hint");
        Check(All(view).Count(c=>c.Tag is LoadingPlayer)==10,"Both teams show ten player cards");
        using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(AppContext.BaseDirectory,"loading-overlay.png"));
        var watch=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<100;i++)view.UpdateRoster(fixture,ranks,icons,"已缓存");
        Check(watch.Elapsed<TimeSpan.FromSeconds(5),"Cached updates stay bounded");
        view.UpdateRoster(null,new Dictionary<string,PlayerChampionTier>(),new Dictionary<int,Image>(),"等待新对局");
        Check(!All(view).Any(c=>c.Tag is LoadingPlayer),"Clearing snapshot removes all previous players");
        type.GetMethod("SetPreviewTitle")!.Invoke(form,null);
        Check(All(form).OfType<Label>().Any(c=>c.Text.Contains("显示测试")),"Desktop preview is identified as a test");
        Check(form.Visible,"Preview shell can be shown without roster data");
        form.Close();
        CheckBackendSwitching();
        Console.WriteLine($"PASS: automatic display before data, manual close, reentry, saved config, preview, parsing, no-activation flags, ten cards and 100 updates ({watch.ElapsedMilliseconds} ms).");
    }

    private static void CheckBackendSwitching()
    {
        using var owner=new League.FormMain();
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        using var chat=(IDisposable)typeof(League.FormMain).GetField("_hotkeyManager",flags)!.GetValue(owner)!;
        var config=new LeagueConfig();
        typeof(League.FormMain).GetField("_appConfig",flags)!.SetValue(owner,config);
        var manager=typeof(League.FormMain).GetField("_loadingOverlay",flags)!.GetValue(owner)!;
        var type=manager.GetType();
        Check(typeof(League.FormMain).Assembly.GetType("League.Managers.OverlayHotkeyWindow")==null,"Overlay keyboard receiver is no longer shipped");
        foreach(var backend in new[]{"GameBar","Desktop","GameBar"})
        {
            config.LoadingOverlayBackend=backend;
            type.GetMethod("ConfigureBackend")!.Invoke(manager,null);
            Check((bool)type.GetField("_configuredGameBar",flags)!.GetValue(manager)! == (backend=="GameBar"),"Backend selection still changes without a keyboard receiver");
            Check(type.GetField("_window",flags)!.GetValue(manager)==null,"Backend configuration does not create a desktop overlay window");
        }
        config.EnableLoadingOverlay=false;
        type.GetMethod("ConfigureBackend")!.Invoke(manager,null);
        Check((bool?)type.GetField("_configuredEnabled",flags)!.GetValue(manager)==false,"Disabling overlay is still observed without shortcut configuration");
    }

    private static void CheckFullscreenPause()
    {
        var type=typeof(League.FormMain).Assembly.GetType("League.Managers.OverlayFullscreenGuard")!;
        int? state=3;int reads=0;
        var guard=Activator.CreateInstance(type,new object[]{(Func<int?>)(()=>{reads++;return state;})})!;
        var pause=type.GetMethod("ShouldPause")!.CreateDelegate<Func<long,bool,bool>>(guard);
        Check(pause(0,false),"Exclusive fullscreen suspends desktop overlay presentation");
        state=2;
        for(int i=1;i<1000;i++)Check(pause(i,false),"Fullscreen state is cached between checks");
        Check(reads==1,"Following the game does not query fullscreen state on every tick");
        Check(!pause(1000,false),"Borderless/fullscreen-window busy state resumes overlay presentation");
        state=3;Check(pause(1100,true),"An explicit request can recheck a new fullscreen transition immediately");
        type.GetMethod("Reset")!.Invoke(guard,null);state=5;
        Check(!pause(1150,false),"New match/request clears cached exclusive mode");
        state=null;Check(!pause(2200,false),"Unavailable mode is not misreported as confirmed exclusive fullscreen");
        Console.WriteLine("PASS: exclusive fullscreen pause, borderless resume, immediate recheck and throttled display-mode reads.");
    }
}
