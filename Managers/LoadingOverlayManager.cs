using System.Diagnostics;
using System.Runtime.InteropServices;
using League.Clients;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager : IDisposable
{
    private readonly FormMain _owner;
    private readonly PlayerChampionTierService _tiers;
    private readonly LiveMatchClient _live=new();
    private readonly LoadingRosterIdentityService _rosterIdentity=new(()=>FormMain.Globals.lcuClient?.Client);
    private readonly GameBarBridge _gameBar=new();
    private readonly System.Windows.Forms.Timer _poll=new(){Interval=3000},_follow=new(){Interval=300},_paint=new(){Interval=100};
    private readonly CancellationTokenSource _life=new();
    private readonly Dictionary<string,PlayerChampionTier> _ranks=new();
    private readonly HashSet<string> _rankRequests=new();
    private readonly Dictionary<string,long> _rankRetry=new();
    private readonly Dictionary<int,Image> _icons=new();
    private readonly HashSet<int> _iconRequests=new();
    private LoadingOverlayForm? _window;
    private LoadingRoster? _roster;
    private string _phase="",_own="",_match="",_status="等待当前对局数据…";
    private bool _busy,_disposed,_configuredGameBar;
    private bool? _configuredEnabled;
    private bool UseGameBar=>_owner.GetAppConfig()?.LoadingOverlayBackend=="GameBar";
    public string GameBarStatus=>_gameBar.Status;
    private readonly LoadingOverlayDisplayState _display=new();
    private readonly OverlayFullscreenGuard _fullscreen=new();
    private bool _wanted=>_display.Requested;
    private long _lastPhaseRead,_nextWindowSearch;
    private string _visibilityReason="";
    private string _identitySummary="";
    private int _generation;
    private IntPtr _gameWindow;
    public LoadingOverlayManager(FormMain owner)
    {
        _owner=owner;_tiers=new(owner);
        _championDetails=new(()=>TimeSpan.FromMinutes(Math.Clamp(_owner.GetAppConfig()?.AramMetaCacheMinutes??1440,1,10080)));
        _detailPoll.Tick+=(_,_)=>{_=ReadDetailSelection();_=ReadChatCommand();};_detailPoll.Start();
        _poll.Tick+=(_,_)=>{ConfigureBackend();_=PollAsync();};
        _follow.Tick+=(_,_)=>Follow();
        _paint.Tick+=(_,_)=>{_paint.Stop();PaintSnapshot();};
        _poll.Start();ConfigureBackend();
    }
    public void ConfigureBackend()
    {
        bool enabled=_owner.GetAppConfig()?.EnableLoadingOverlay??true;
        if(enabled==_configuredEnabled && UseGameBar==_configuredGameBar)return;
        _configuredEnabled=enabled;
        bool previousGameBar=_configuredGameBar;_configuredGameBar=UseGameBar;
        if(previousGameBar && !UseGameBar){CancelDetail();PublishGameBar(false);}
        if(!enabled){_display.Reset();ClearMatch();_follow.Stop();return;}
        if(UseGameBar){_window?.Hide();_follow.Stop();SchedulePaint();}
    }
    public void RefreshDisplaySettings()
    {
        if(_disposed)return;
        ConfigureBackend();
        if(!(_owner.GetAppConfig()?.EnableLoadingOverlay??true))return;
        ObserveDisplayPhase();
        SchedulePaint();
    }
    private void ObserveDisplayPhase()
    {
        _display.ObservePhase(_phase,_owner.GetAppConfig()?.AutoShowLoadingOverlay??true);
        if(_display.Preview)return;
        if(UseGameBar){_window?.Hide();_follow.Stop();SchedulePaint();return;}
        if(_wanted){_follow.Start();Follow();}
        else{_window?.Hide();_follow.Stop();}
    }
    private void EnsureWindow()
    {
        if(_window!=null)return;
        _window=new LoadingOverlayForm(Champion,Details);_window.Dismissed+=Dismiss;
        PaintSnapshot();
    }
    public void ShowPreview()
    {
        if(_disposed || !(_owner.GetAppConfig()?.EnableLoadingOverlay??true))return;
        _display.RequestPreview();_follow.Stop();EnsureWindow();
        var work=Screen.FromControl(_owner).WorkingArea;
        _window!.Bounds=LoadingOverlayForm.Placement(work,work);
        PaintSnapshot();_window.SetPreviewTitle();_window.Show();
        ReportVisibility("显示测试：已调用窗口显示，不依赖游戏阶段或快捷键。");
    }
    private void ReportVisibility(string reason)
    {
        if(_visibilityReason==reason)return;
        _visibilityReason=reason;ChatSendDiagnostics.Shared.Report("overlay",reason);
    }
    private string Champion(int id)=>FormMain.Globals.resLoading?.GetChampionById(id)?.Name??(id>0?$"英雄 {id}":"英雄待确认");
    private string Details(LoadingPlayer player)
    {
        var cache=_owner._playerCardManager?.GetAllCachedPlayerInfos().Values.FirstOrDefault(p=>
            player.Puuid.Length>0 && p.Player?.Puuid==player.Puuid || player.SummonerId>0 && p.Player?.SummonerId==player.SummonerId);
        if(cache?.Player==null)return "暂无已缓存的玩家战绩；不会因点击卡片而阻塞游戏。";
        string rank=cache.Player.SoloRank??"未定级";
        var wins=cache.WinHistory;
        return $"单双排 {rank} · 灵活组排 {cache.Player.FlexRank??"未定级"} · "+(wins.Count>0?$"近期 {wins.Count} 场，{wins.Count(w=>w)} 胜 / {wins.Count(w=>!w)} 负":"近期战绩未缓存");
    }
    private void Dismiss(){_display.Dismiss();_follow.Stop();_window?.Hide();ReportVisibility("用户收起对局速览，本局不再自动弹出。");}
    private void ClearMatch()
    {
        CancelDetail();
        _status=LoadingRoster.WaitingStatus(_phase);
        _masteries.Clear();
        _identitySummary="";
        if(_roster!=null || _match.Length>0){_generation++;_roster=null;_match="";_ranks.Clear();_rankRequests.Clear();_rankRetry.Clear();}
        _gameWindow=IntPtr.Zero;_nextWindowSearch=0;_fullscreen.Reset();
        if(!_display.Preview){_display.Reset();_follow.Stop();_window?.Hide();}
        else PaintSnapshot();
        if(UseGameBar)PublishGameBar();
    }
    private async Task PollAsync()
    {
        if(_busy || _disposed || !(_owner.GetAppConfig()?.EnableLoadingOverlay??true))return;
        var client=FormMain.Globals.lcuClient?.Client;
        if(client==null){_phase="";ClearMatch();return;}
        _busy=true;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(_life.Token);timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            string phase=(await client.GetStringAsync("/lol-gameflow/v1/gameflow-phase",timeout.Token)).Trim().Trim('"');
            if(_disposed)return;
            _phase=phase;_lastPhaseRead=Environment.TickCount64;
            // Display the shell as soon as the phase is known; roster and statistics may still be loading.
            ObserveDisplayPhase();
            if(!LoadingRoster.CanDisplay(phase) && phase!="ChampSelect"){_own="";ClearMatch();return;}
            var flow=JObject.Parse(await client.GetStringAsync("/lol-gameflow/v1/session",timeout.Token));
            if(_own.Length==0)
            {
                try{_own=(string?)JObject.Parse(await client.GetStringAsync("/lol-summoner/v1/current-summoner",timeout.Token))["puuid"]??"";}catch{ }
            }
            var roster=LoadingRoster.Parse(flow,_own);
            int clientPlayers=roster.Players.Length,livePlayers=0;
            if(roster.Players.Length<10 && LoadingRoster.CanDisplay(phase))
            {
                try
                {
                    var live=await _live.GetAsync(timeout.Token);
                    livePlayers=live.Players.Length;
                    if(live.Players.Length>=roster.Players.Length)
                    {
                        roster=await _rosterIdentity.MergeAsync(roster,flow,_own,live,alias=>FormMain.Globals.resLoading?.Champions.Champions.FirstOrDefault(c=>c.Alias.Equals(alias,StringComparison.OrdinalIgnoreCase))?.Id??0,timeout.Token);
                        int normalized=PlayerChampionTier.NormalizeQueue(roster.Queue,live.Mode);
                        roster=roster with {Queue=normalized};
                        if(flow["gameData"] is not JObject)flow["gameData"]=new JObject();
                        if(flow["gameData"]!["queue"] is not JObject)flow["gameData"]!["queue"]=new JObject();
                        flow["gameData"]!["queue"]!["id"]=normalized;
                    }
                }
                catch { /* Loading can precede Live Client availability. Keep placeholders. */ }
            }
            if(_disposed || timeout.IsCancellationRequested || !(_owner.GetAppConfig()?.EnableLoadingOverlay??true))return;
            string match=roster.GameId.Length>0?roster.GameId:$"{roster.Queue}:"+string.Join("|",roster.Players.Select(p=>p.Key));
            match+="/"+roster.Queue;
            if(_match!=match){CancelDetail();_generation++;_ranks.Clear();_rankRequests.Clear();_rankRetry.Clear();_masteries.Clear();_match=match;}
            _roster=roster;
            string identitySummary=$"lcu={clientPlayers} live={livePlayers} roster={roster.Players.Length} confirmed={roster.Players.Count(p=>LoadingRosterIdentityService.HasPuuid(p.Puuid))} hidden={roster.Players.Count(p=>p.IdentityHidden)}";
            if(identitySummary!=_identitySummary){_identitySummary=identitySummary;RoomLoadDiagnostics.Shared.Report(_generation,"overlay-identities",detail:identitySummary);}
            _status=$"{(phase=="ChampSelect"?"准备进入对局":"当前对局")} · 已读取 {roster.Players.Length} 人 · {DateTime.Now:HH:mm:ss} · 点击卡片查看{(UseGameBar?"英雄详情":"缓存战绩")}";
            foreach(var p in roster.Players.Where(p=>p.ChampionId>0)){_=LoadRank(p,_generation,flow);_=LoadIcon(p.ChampionId);_=LoadMastery(p,_generation);}
            SchedulePaint();
        }
        catch(OperationCanceledException)when(_life.IsCancellationRequested){}
        catch
        {
            if(!_disposed && Environment.TickCount64-_lastPhaseRead>15000){_phase="";ClearMatch();ReportVisibility("客户端阶段读取中断，暂停自动展示。");}
            else if(!_disposed){_status="连接暂时中断，显示本局已缓存信息…";SchedulePaint();}
        }
        finally{_busy=false;if(!_disposed)_poll.Interval=_wanted || UseGameBar || _phase is "ChampSelect" or "GameStart"?3000:10000;}
    }
    private async Task LoadRank(LoadingPlayer player,int generation,JObject flow)
    {
        string key=$"{player.Key}:{player.ChampionId}";
        if(_rankRetry.GetValueOrDefault(key)>Environment.TickCount64 || !_rankRequests.Add(key))return;
        try
        {
            var rank=await _tiers.GetAsync(player.ChampionId,player.Puuid,flow);
            if(_disposed || generation!=_generation || _roster?.Players.Any(p=>p.Key==player.Key && p.ChampionId==player.ChampionId)!=true)return;
            _ranks[player.Key]=rank;SchedulePaint();
        }
        catch
        {
            if(_disposed || generation!=_generation || _roster?.Players.Any(p=>p.Key==player.Key && p.ChampionId==player.ChampionId)!=true)return;
            _rankRequests.Remove(key);_rankRetry[key]=Environment.TickCount64+30000;
            _ranks[player.Key]=new("—","统计暂不可用","后台稍后重试。",_roster?.Queue??0,"none");SchedulePaint();
        }
    }
    private async Task LoadIcon(int id)
    {
        if(_icons.ContainsKey(id) || !_iconRequests.Add(id) || FormMain.Globals.resLoading==null)return;
        try
        {
            var icon=await FormMain.Globals.resLoading.GetChampionIconAsync(id);
            if(_disposed){icon?.Dispose();return;}
            if(icon!=null)_icons[id]=icon;SchedulePaint();
        }
        catch{ }
        finally{_iconRequests.Remove(id);}
    }
    private void SchedulePaint(){if(!_disposed && (_wanted || UseGameBar) && !_paint.Enabled)_paint.Start();}
    private void PublishGameBar(bool? enabled=null)=>_gameBar.Publish(_roster,_ranks,_icons,Champion,Details,_match,
        enabled==false?"对局速览已停止同步。":LoadingRoster.CanDisplay(_phase)?_status:LoadingRoster.WaitingStatus(_phase),
        enabled??(UseGameBar && (_owner.GetAppConfig()?.EnableLoadingOverlay??true)),
        LoadingRoster.CanDisplay(_phase) && Environment.TickCount64-_lastPhaseRead<=15000,_wanted && !_display.Preview,_masteries);
    private void PaintSnapshot()
    {
        if(_disposed)return;
        if(UseGameBar)PublishGameBar();
        if(_window!=null)_window.View.UpdateRoster(_roster,_ranks,_icons,_display.Preview?"显示测试 · 此窗口可正常绘制；游戏内覆盖效果需在加载画面确认。":_status,_masteries);
    }
    private bool GameFocused()
    {
        var foreground=GetForegroundWindow();
        GetWindowThreadProcessId(foreground,out uint front);GetWindowThreadProcessId(_gameWindow,out uint game);
        return game!=0 && (front==game || foreground==_window?.Handle);
    }
    private bool FindGameWindow()
    {
        if(_gameWindow!=IntPtr.Zero && IsWindow(_gameWindow) && IsWindowVisible(_gameWindow))return true;
        if(Environment.TickCount64<_nextWindowSearch)return false;
        _nextWindowSearch=Environment.TickCount64+2000;
        var processes=Process.GetProcessesByName("League of Legends");
        try
        {
            var ids=processes.Select(p=>(uint)p.Id).ToHashSet();_gameWindow=IntPtr.Zero;
            EnumWindows((window,_)=>
            {
                GetWindowThreadProcessId(window,out uint id);
                if(ids.Contains(id) && IsWindowVisible(window) && GetWindowRect(window,out var r) && r.Right-r.Left>=320 && r.Bottom-r.Top>=240){_gameWindow=window;return false;}
                return true;
            },IntPtr.Zero);
        }
        finally{foreach(var p in processes)p.Dispose();}
        return _gameWindow!=IntPtr.Zero;
    }
    private void Follow()
    {
        if(UseGameBar)return;
        if(!_wanted || _display.Preview)return;
        void Suspend(string reason){_window?.Hide();ReportVisibility(reason);}
        if(!LoadingRoster.CanDisplay(_phase) || Environment.TickCount64-_lastPhaseRead>15000){Suspend("等待有效的加载 / 游戏阶段。");return;}
        if(!FindGameWindow()){Suspend("已确认游戏阶段，等待游戏窗口创建。");return;}
        if(IsIconic(_gameWindow) || !GameFocused()){Suspend("游戏未在前台或已最小化，返回游戏后自动恢复。");return;}
        if(_fullscreen.ShouldPause(Environment.TickCount64)){Suspend("检测到独占全屏，已暂停桌面速览以免游戏切到桌面；当前版本尚不支持在独占全屏画面内显示。");return;}
        if(!GetWindowRect(_gameWindow,out var r) || r.Right<=r.Left || r.Bottom<=r.Top){Suspend("暂时无法读取游戏窗口位置。");return;}
        EnsureWindow();
        _window!.SetRosterTitle();
        var bounds=LoadingOverlayForm.Placement(Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom),Screen.FromHandle(_gameWindow).WorkingArea);
        if(_window.Bounds!=bounds)_window.Bounds=bounds;
        if(!_window.Visible){PaintSnapshot();_window.Show();ReportVisibility("已在游戏窗口上方显示对局速览。");}
    }
    public void Dispose()
    {
        if(_disposed)return;CancelDetail();_detailPoll.Dispose();_championDetails.Dispose();if(UseGameBar)PublishGameBar(false);_gameBar.Dispose();_disposed=true;_life.Cancel();_poll.Dispose();_follow.Dispose();_paint.Dispose();_window?.Dispose();_tiers.Dispose();_live.Dispose();
        foreach(var image in _icons.Values)image.Dispose();_icons.Clear();
    }
    private delegate bool EnumWindowProc(IntPtr window,IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool EnumWindows(EnumWindowProc callback,IntPtr parameter);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr window);
}
