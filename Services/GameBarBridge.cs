using System.Drawing.Imaging;
using System.Threading.Channels;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>Local snapshots and bounded roster commands. No LCU credentials or network endpoints cross the boundary.</summary>
public sealed partial class GameBarBridge : IDisposable
{
    private readonly Channel<JObject> _pending=Channel.CreateBounded<JObject>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropOldest,SingleReader=true});
    private readonly Dictionary<int,string> _images=new();
    private readonly string? _testDirectory;
    private readonly string _instance=Guid.NewGuid().ToString("N");
    private string _lastCommand="",_command="",_directory="";
    private long _revision,_nextDiscovery;
    private bool _disposed;
    public string Status { get; private set; }="请在 Win＋G 中打开并固定「League 对局速览」。";
    public Task Completion { get; }
    public GameBarBridge(string? directory=null){_testDirectory=directory;Completion=Task.WhenAll(Task.Run(WriteLoop),Task.Run(WriteDetailLoop),Task.Run(WriteChatLoop));}

    public void Publish(LoadingRoster? roster,IReadOnlyDictionary<string,PlayerChampionTier> ranks,
        IReadOnlyDictionary<int,Image> icons,Func<int,string> champion,Func<LoadingPlayer,string> details,
        string session,string status,bool enabled,bool active,bool show,IReadOnlyDictionary<string,PlayerChampionMastery>? masteries=null)
    {
        if(_disposed)return;
        string identity=$"{session}|{enabled}|{active}|{show}";
        if(identity!=_lastCommand){_lastCommand=identity;_command=Guid.NewGuid().ToString("N");}
        int own=roster?.Players.FirstOrDefault(p=>p.Self)?.Team??100;
        int[] teams=own==200?[200,100]:[100,200];
        var rows=new JArray();
        foreach(int team in teams)
        {
            var players=enabled && active?roster?.Players.Where(p=>p.Team==team).Take(5).ToArray()??[]:[];
            for(int i=0;i<5;i++)
            {
                var p=players.ElementAtOrDefault(i);
                if(p==null){rows.Add(new JObject());continue;}
                if(!_images.ContainsKey(p.ChampionId) && icons.TryGetValue(p.ChampionId,out var icon))
                {
                    using var small=new Bitmap(icon,new Size(48,48));using var buffer=new MemoryStream();
                    small.Save(buffer,ImageFormat.Png);_images[p.ChampionId]=Convert.ToBase64String(buffer.ToArray());
                }
                var rank=ranks.GetValueOrDefault(p.Key);
                var mastery=masteries?.GetValueOrDefault(p.Key);
                if(mastery?.ChampionId!=p.ChampionId)mastery=null;
                rows.Add(new JObject{
                    ["championId"]=p.ChampionId,["champion"]=champion(p.ChampionId),["name"]=p.Name+(p.Self?" · 自己":""),
                    ["tier"]=rank?.Tier??"—",["color"]=AramMetaView.TierColor(rank?.Tier??"—").ToArgb()&0xffffff,
                    ["stats"]=rank==null?"统计读取中…":rank.Rate+" · 选用率 "+rank.PickRate,
                    ["mastery"]=p.ChampionId>0?mastery?.Text??"成就读取中…":"等待英雄确认",["masteryDetails"]=mastery?.Description??"正在读取该玩家使用此英雄的成就。",
                    ["details"]=champion(p.ChampionId)+" · "+details(p)+"\n"+(rank?.Note??"英雄统计尚未读取。"),
                    ["icon"]=_images.GetValueOrDefault(p.ChampionId)??""});
            }
        }
        _detailScope=new(enabled && active?_instance+":"+session:"",rows.Select(r=>(int?)r["championId"]??0).ToArray());
        _pending.Writer.TryWrite(new JObject{
            ["detailProtocol"]=1,["chatProtocol"]=1,["chatMastery"]=true,["chatTeams"]=roster?.Players.Any(p=>p.Self)??false,["queue"]=roster?.Queue??0,
            ["protocol"]=1,["revision"]=_instance+":"+(++_revision),["command"]=_instance+":"+_command,
            ["session"]=_instance+":"+session,["updatedAt"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["enabled"]=enabled,["active"]=active,["show"]=show,
            // Prevent older widgets from falling back to their built-in keyboard watcher.
            ["shortcutOwner"]="main",["shortcut"]="",
            ["status"]=status,["teams"]=new JArray(teams.Select(t=>roster?.TeamLabel(t)??(t==100?"我方":"对方"))),["players"]=rows});
    }
    private string FindDirectory()
    {
        if(_testDirectory!=null)return _testDirectory;
        if(Environment.TickCount64<_nextDiscovery)return _directory;
        _nextDiscovery=Environment.TickCount64+10000;
        string packages=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages");
        _directory=Directory.Exists(packages)?Directory.EnumerateDirectories(packages,"LeagueHistory.GameBar_*")
            .Select(p=>Path.Combine(p,"LocalState")).FirstOrDefault(Directory.Exists)??"":"";
        return _directory;
    }
    private async Task WriteLoop()
    {
        await foreach(var data in _pending.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                string directory=FindDirectory();
                if(directory.Length==0){Status="Game Bar 组件尚未就绪；安装后按 Win＋G 打开「League 对局速览」。";continue;}
                Directory.CreateDirectory(directory);
                string path=Path.Combine(directory,"snapshot.json"),temporary=Path.Combine(directory,"snapshot-"+_instance+".tmp");
                try
                {
                    await File.WriteAllTextAsync(temporary,data.ToString(Newtonsoft.Json.Formatting.None),new System.Text.UTF8Encoding(false)).ConfigureAwait(false);
                    File.Move(temporary,path,true);
                }
                finally{if(File.Exists(temporary))File.Delete(temporary);}
                Status=DescribeWidgetState(null,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                string heartbeat=Path.Combine(directory,"widget-status.json");
                if(File.Exists(heartbeat) && new FileInfo(heartbeat).Length<8192)
                {
                    var state=JObject.Parse(await File.ReadAllTextAsync(heartbeat).ConfigureAwait(false));
                    Status=DescribeWidgetState(state,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                }
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
            {Status="Game Bar 本机同步暂不可用："+e.Message;}
        }
    }
    public static string DescribeWidgetState(JObject? state,long now)
    {
        long age=now-((long?)state?["updatedAt"]??0);
        if(state==null || age<0 || age>=15000)
            return "数据已准备，Game Bar 组件未连接；请在 Win＋G 中打开「League 对局速览」。";
        string error=(string?)state["error"]??"";
        if(error.Length>0)return error;
        if((bool?)state["connected"]==false || (state["connected"]==null && !string.IsNullOrEmpty((string?)state["readError"])))
            return "组件已启动，但尚未读到有效数据；请确认主程序正在运行。";
        if((double?)state["width"]<=0 || (double?)state["height"]<=0)
            return "组件已连接，但画面尚未加载；请关闭该小组件后重新打开。";
        if((bool?)state["pinned"]!=true)return "Game Bar 已连接但尚未固定 · 在 Win＋G 中查看，固定后可保留在游戏上方。";
        if((bool?)state["active"]==false)return "Game Bar 已连接并固定 · 等待对局 · Win＋G 管理面板。";
        return "Game Bar 已连接并固定 · Win＋G 管理面板。";
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_pending.Writer.TryComplete();_detailPending.Writer.TryComplete();_chatPending.Writer.TryComplete();}
}
