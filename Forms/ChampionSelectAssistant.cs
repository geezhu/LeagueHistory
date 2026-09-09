using System.Diagnostics;
using System.Runtime.InteropServices;
using League.Clients;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League;

internal sealed partial class ChampionSelectAssistant : Form
{
    private readonly FormMain _owner;
    private readonly System.Windows.Forms.Timer _timer=new(){Interval=750};
    private readonly OpggClient _opgg=new();
    private readonly AramMetaClient _am;
    private sealed class BufferedFlow : FlowLayoutPanel {public BufferedFlow(){DoubleBuffered=true;}}
    private readonly FlowLayoutPanel _body=new BufferedFlow(){Dock=DockStyle.Fill,AutoScroll=true,AutoScrollMargin=new Size(0,16),FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(12,4,12,4)};
    private readonly Label _status=new(){Dock=DockStyle.Top,Height=26,Padding=new Padding(10,4,10,4),Text="等待选择英雄…"};
    private readonly CheckBox _pin=new(){Text="固定位置",AutoSize=true};
    private readonly Button _apply=new(){Text="应用符文",AutoSize=true,Enabled=false};
    private readonly Button _fold=new(){Text="收起",AutoSize=true};
    private readonly ComboBox _lane=new Controls.DarkFilterComboBox(){Width=95,Margin=new Padding(2)};
    // The assistant opens without activation so hovering must also work while LOL has focus.
    private readonly ToolTip _tips=new(){ShowAlways=true};
    private CancellationTokenSource? _load,_poll;
    private bool _busy,_selecting,_collapsed,_syncing,_applying;
    private int _hero,_queue;
    private string? _position;
    private string _key="";
    private int _pollFailures;
    private OpggRunes? _runes;
    private int _runeChoice;
    protected override bool ShowWithoutActivation=>true;
    public ChampionSelectAssistant(FormMain owner)
    {
        _owner=owner;_am=new(cacheLifetime:()=>TimeSpan.FromMinutes(Math.Clamp(owner.GetAppConfig()?.AramMetaCacheMinutes??1440,1,10080)));
        Text="选人助手";ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;FormBorderStyle=FormBorderStyle.FixedToolWindow;
        DoubleBuffered=true;
        BackColor=Color.FromArgb(8,29,40);ForeColor=Color.FromArgb(215,225,226);Font=new Font("Microsoft YaHei UI",9);ClientSize=new Size(320,590);
        _status.ForeColor=Color.FromArgb(135,162,173);
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=74,Padding=new Padding(10,6,10,4),BackColor=Color.FromArgb(10,32,43)};
        _pin.FlatStyle=FlatStyle.Flat;_pin.Margin=new Padding(2,7,6,0);
        _fold.AutoSize=false;_fold.Size=new Size(66,28);_fold.Margin=new Padding(2);
        toolbar.Controls.AddRange(new Control[]{_pin,_fold,_mode,_region,_tier,_lane});
        toolbar.SetFlowBreak(_mode,true);
        _lane.Items.AddRange(new object[]{"自动分路","上路","打野","中路","下路","辅助"});_lane.SelectedIndex=0;
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=44,Padding=new Padding(8)};
        var guide=new Button{Text="完整攻略",AutoSize=true};
        var retry=new Button{Text="重试",AutoSize=true};footer.Controls.AddRange(new Control[]{guide,_apply,retry});
        Controls.Add(_body);Controls.Add(_status);Controls.Add(toolbar);Controls.Add(footer);
        foreach(var button in toolbar.Controls.OfType<Button>().Concat(footer.Controls.OfType<Button>()))
        {
            button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Color.FromArgb(49,83,95);
            button.BackColor=Color.FromArgb(16,45,57);button.ForeColor=Color.FromArgb(220,230,226);
        }
        _apply.BackColor=Color.FromArgb(0,126,139);
        foreach(var box in new[]{_mode,_region,_tier,_lane})
        {
            box.BackColor=Color.FromArgb(16,45,57);box.ForeColor=ForeColor;
            box.SelectedIndexChanged+=(_,_)=>{if(_syncing)return;SyncFilters();if(_hero>0)_ = LoadHeroAsync();};
        }
        _tips.SetToolTip(_mode,"默认跟随选人模式，也可以手动切换攻略模式。英雄仍自动跟随当前选人。");
        _tips.SetToolTip(_region,"OP.GG 统计地区，与登录账号大区无关。");
        _tips.SetToolTip(_tier,"统计段位；各模式分别保留选择。");
        _fold.Click+=(_,_)=>{_collapsed=!_collapsed;_fold.Text=_collapsed?"展开":"收起";_body.Visible=!_collapsed;_status.Visible=!_collapsed;footer.Visible=!_collapsed;_region.Visible=_tier.Visible=_lane.Visible=!_collapsed;toolbar.Height=_collapsed?44:74;Height=_collapsed?84:630;SyncFilters();};
        guide.Click+=(_,_)=>{if(_hero>0)_owner.OpenAssistantGuide(_hero,SelectedMode=="mayhem"?2400:0,_position,SelectedMode=="mayhem"?null:SelectedQuery);};
        retry.Click+=(_,_)=>{if(_hero>0)_ = LoadHeroAsync();};
        _apply.Click+=async (_,_)=>await ApplyAsync();
        FormClosing+=(_,e)=>{if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;if(!_collapsed)_fold.PerformClick();}};
        _timer.Tick+=async (_,_)=>await TickAsync();_timer.Start();
    }

    internal static Rectangle Placement(Rectangle client,Rectangle screen,Size size)
    {
        int width=Math.Min(size.Width,screen.Width),height=Math.Min(size.Height,screen.Height);
        int x=client.Right+6;
        if(x+width>screen.Right)x=client.Left-width-6;
        return new Rectangle(Math.Clamp(x,screen.Left,screen.Right-width),Math.Clamp(client.Top,screen.Top,screen.Bottom-height),width,height);
    }
    private void PositionWindow()
    {
        IntPtr handle=IntPtr.Zero;
        foreach(var process in Process.GetProcessesByName("LeagueClientUx"))
        {
            using(process){try{if(process.MainWindowHandle!=IntPtr.Zero && IsWindowVisible(process.MainWindowHandle)){handle=process.MainWindowHandle;break;}}catch{}}
        }
        var foreground=GetForegroundWindow();
        GetWindowThreadProcessId(handle,out uint clientProcess);
        GetWindowThreadProcessId(foreground,out uint foregroundProcess);
        bool clientFocused=handle!=IntPtr.Zero && clientProcess!=0 && clientProcess==foregroundProcess;
        bool editorFocused=OwnedForms.Any(form=>form.ContainsFocus || form.Handle==foreground);
        if(!_selecting || handle==IntPtr.Zero || IsIconic(handle) || (!clientFocused && foreground!=Handle && !ContainsFocus && !editorFocused && !_owner.ContainsFocus)){if(Visible)Hide();return;}
        if(GetWindowRect(handle,out var rect) && !_pin.Checked)
        {
            var target=Placement(Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom),Screen.FromHandle(handle).WorkingArea,new Size(Width,_collapsed?84:630));
            if(Bounds!=target)Bounds=target;
        }
        if(!TopMost)TopMost=true;
        if(!Visible)Show();
    }
    private async Task TickAsync()
    {
        if(!(_owner.GetAppConfig()?.EnableChampionSelectAssistant??false)){Reset();return;}
        PositionWindow();if(_busy)return;
        var client=FormMain.Globals.lcuClient?.Client;if(client==null){Reset();return;}
        _busy=true;_poll?.Dispose();_poll=new(TimeSpan.FromSeconds(3));
        try
        {
            var phase=(await client.GetStringAsync("/lol-gameflow/v1/gameflow-phase",_poll.Token)).Trim('"');
            if(phase!="ChampSelect"){Reset();return;}
            var flow=JObject.Parse(await client.GetStringAsync("/lol-gameflow/v1/session",_poll.Token));
            var session=JObject.Parse(await client.GetStringAsync("/lol-champ-select/v1/session",_poll.Token));
            if(IsDisposed)return;
            _pollFailures=0;
            _selecting=true;
            var own=(session["myTeam"] as JArray)?.OfType<JObject>().FirstOrDefault(p=>(int?)p["cellId"]==(int?)session["localPlayerCellId"]);
            int hero=(int?)own?["championId"]??0;
            if(hero<=0)hero=(session["actions"] as JArray??new()).OfType<JArray>().SelectMany(a=>a.OfType<JObject>())
                .Where(a=>(int?)a["actorCellId"]==(int?)session["localPlayerCellId"] && (string?)a["type"]=="pick")
                .Select(a=>(int?)a["championId"]??0).LastOrDefault(id=>id>0);
            if(hero<=0)
            {
                if(_hero!=0){_load?.Cancel();_hero=0;_key="";_runes=null;_apply.Enabled=false;ClearBody();}
                _status.Text="等待选择英雄…";PositionWindow();return;
            }
            int queue=(int?)flow["gameData"]?["queue"]?["id"]??0;
            string? lane=((string?)own?["assignedPosition"])?.ToLowerInvariant() switch {"middle"=>"mid","bottom"=>"adc","utility"=>"support","top"=>"top","jungle"=>"jungle",_=>null};
            string key=$"{hero}:{queue}:{lane}";
            if(key!=_key)
            {
                bool differentHero=hero!=_hero;_hero=hero;_queue=queue;_position=lane;_key=key;
                if(differentHero){_syncing=true;_lane.SelectedIndex=0;_syncing=false;}
                _ = LoadHeroAsync();
            }
            PositionWindow();
        }
        catch(Exception ex)
        {
            if(!IsDisposed && ++_pollFailures>=3)Reset();
            Debug.WriteLine("Assistant poll: "+ex.GetType().Name);
        }
        finally{_busy=false;}
    }
    private void Reset(){_selecting=false;_key="";_hero=0;_load?.Cancel();_runes=null;_apply.Enabled=false;if(Visible)Hide();}
    private void ClearBody()
    {
        foreach(Control control in _body.Controls.Cast<Control>().ToArray())control.Dispose();
    }
    private void Line(string text,bool heading=false)=>_body.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new Size(282,0),Margin=new Padding(0,2,0,2),ForeColor=heading?Color.FromArgb(208,185,130):Color.FromArgb(195,213,219)});
    private async Task LoadHeroAsync()
    {
        _load?.Cancel();_load?.Dispose();var request=_load=new CancellationTokenSource();int hero=_hero,queue=_queue;
        var token=request.Token;_runes=null;_runeChoice=0;_apply.Text="应用符文";_apply.Enabled=false;ClearBody();
        var name=FormMain.Globals.resLoading?.GetChampionById(hero)?.Name??$"英雄 {hero}";
        _status.Text="正在加载推荐…";
        SyncFilters();
        string mode=SelectedMode;
        try
        {
            _ = LoadSkillsAsync(hero,token);
            if(mode=="mayhem")
            {
                var page=await _am.GetAsync(0,false,token);token.ThrowIfCancellationRequested();
                AddMayhemRecommendations(page,hero,token);
                _status.Text=$"arammeta · 海克斯大乱斗 · 版本 {page.Index.Patch}"+(page.UpdateFailed?" · 旧缓存":"");
            }
            else
            {
                string? selected=new string?[]{null,"top","jungle","mid","adc","support"}[_lane.SelectedIndex];
                var query=SelectedQuery;
                if(mode=="ranked" && selected==null)
                {
                    var tiers=await _opgg.GetTiersAsync(query,false,token);
                    query=query with {Position=OpggQuery.PositionForChampion(tiers.Rows,hero,_position)??"mid"};
                }
                var build=await _opgg.GetBuildAsync(query,false,token);token.ThrowIfCancellationRequested();
                _position=query.Position;_runes=build.Runes.FirstOrDefault();
                string laneName=query.Position switch {"top"=>"上路","jungle"=>"打野","mid"=>"中路","adc"=>"下路","support"=>"辅助",_=>""};
                var tier=(int?)(build.Stats["tier_data"]?["tier"]??build.Stats["tier"]);
                AddTierSummary(tier.HasValue?"T"+tier:"—",$"胜率 {((double?)build.Stats["win_rate"]??OpggBuild.Ratio(build.Stats["win"],build.Stats["play"])):P2} · {laneName}");
                if(_runes!=null)
                {
                    Line("推荐符文",true);
                    AddRuneChoices(build.Runes,token);
                }
                var skills=build.Data["skill_masteries"]?.First?["ids"] as JArray;
                Line("核心装备"+(skills!=null?"  ·  加点 "+string.Join(" → ",skills.Values<string>()):""),true);
                AddEquipmentChoices(build,token);
                Line("召唤师技能",true);
                await AddSpellRecommendations(build.Data["summoner_spells"] as JArray??new(),token);
                string modeName=mode switch {"ranked"=>"召唤师峡谷","aram"=>"极地大乱斗","arena"=>"斗魂竞技场","urf"=>"无限火力",_=>"极限闪击"};
                _status.Text=$"OP.GG · {modeName} · {build.Version}";
                _apply.Enabled=_runes!=null && !_applying;
            }
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(!token.IsCancellationRequested && !IsDisposed)_status.Text="加载失败："+ex.Message;}
    }
    private async Task IconRow(IEnumerable<int> ids,string kind,CancellationToken token,Control? parent=null)
    {
        token.ThrowIfCancellationRequested();
        var container=parent??_body;
        int contentWidth=parent?.MaximumSize.Width>0?parent.MaximumSize.Width-parent.Padding.Horizontal:282;
        var row=new FlowLayoutPanel {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MaximumSize=new Size(contentWidth,0),Margin=Padding.Empty};container.Controls.Add(row);
        foreach(int id in ids)
        {
            var resources=FormMain.Globals.resLoading;
            var info=kind switch
            {
                "champion"=>await resources.GetChampionInfoAsync(id),"augment"=>await resources.GetAugmentInfoAsync(id),
                "item"=>await resources.GetItemInfoAsync(id),"spell"=>await resources.GetSpellInfoAsync(id),_=>await resources.GetRuneInfoAsync(id)
            };
            if(token.IsCancellationRequested || row.IsDisposed){info.image?.Dispose();token.ThrowIfCancellationRequested();return;}
            if(info.image==null){row.Controls.Add(new Label{Text=info.name,AutoSize=true,MaximumSize=new Size(Math.Min(260,contentWidth-6),0)});continue;}
            int size=kind=="champion"?52:kind=="rune"?26:32;
            var icon=new PictureBox {Image=info.image,Size=new Size(size,size),SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(2)};
            icon.Disposed+=(_,_)=>icon.Image?.Dispose();_tips.SetToolTip(icon,League.Controls.TooltipText.Format(info.name,info.description));row.Controls.Add(icon);
        }
    }
    private async Task ApplyAsync()
    {
        if(SelectedMode=="mayhem"){await SendMayhemNoticeAsync();return;}
        if(_applying || _runes==null || _hero<=0)return;
        int hero=_hero,choice=_runeChoice+1;var runes=_runes;_applying=true;_apply.Enabled=false;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(_load?.Token??default);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        string map=SelectedMode switch
        {
            "ranked"=>"召唤师峡谷","aram"=>"极地大乱斗","arena"=>"斗魂竞技场",
            "urf"=>"无限火力","nexus_blitz"=>"极限闪击","mayhem"=>"海克斯大乱斗",_=>"召唤师峡谷"
        };
        _status.Text="正在应用并校验符文…";
        try
        {
            var client=FormMain.Globals.lcuClient?.Client??throw new InvalidOperationException("客户端已断开。");
            var session=JObject.Parse(await client.GetStringAsync("/lol-champ-select/v1/session",timeout.Token));
            var own=(session["myTeam"] as JArray)?.OfType<JObject>().FirstOrDefault(p=>(int?)p["cellId"]==(int?)session["localPlayerCellId"]);
            int selectedHero=(int?)own?["championId"]??0;
            if(selectedHero<=0)selectedHero=(session["actions"] as JArray??new()).OfType<JArray>().SelectMany(a=>a.OfType<JObject>())
                .Where(a=>(int?)a["actorCellId"]==(int?)session["localPlayerCellId"] && (string?)a["type"]=="pick")
                .Select(a=>(int?)a["championId"]??0).LastOrDefault(id=>id>0);
            if(selectedHero!=hero)throw new InvalidOperationException("英雄已变化，请等待推荐更新。");
            string champion=FormMain.Globals.resLoading?.GetChampionById(hero)?.Name??hero.ToString();
            string keystone=FormMain.Globals.resLoading?.GetRuneById(runes.Perks[0])?.name??"推荐符文";
            await OpggRuneService.ApplyAsync(client,runes,champion,timeout.Token);
            if(!IsDisposed)_status.Text=$"符文方案 {choice} 已应用。";
            try
            {
                var rates=new List<string>();
                if(runes.WinRate is double win && double.IsFinite(win) && win>=0 && win<=1)rates.Add($"胜率 {win:P2}");
                if(runes.PickRate is double pick && double.IsFinite(pick) && pick>=0 && pick<=1)rates.Add($"使用率 {pick:P2}");
                string statistics=rates.Count>0?"（"+string.Join(" / ",rates)+"）":"";
                bool sent=await AnnounceRunesAsync(client,$"【{map}】{champion} · {keystone}{statistics}符文已配置成功——数据来自 OP.GG",timeout.Token);
                if(!sent && !IsDisposed)_status.Text=$"方案 {choice} 已应用，聊天提示未发送。";
            }
            catch(Exception ex)
            {
                Debug.WriteLine("符文应用后的聊天提示未发送："+ex.GetType().Name);
                if(!IsDisposed)_status.Text=$"方案 {choice} 已应用，聊天提示未发送。";
            }
        }
        catch(Exception ex){if(!IsDisposed){_status.Text=ex is OperationCanceledException?"应用中断或超时，请确认当前符文后重试。":ex.Message;_tips.SetToolTip(_status,_status.Text);}}
        finally{_applying=false;if(!IsDisposed)_apply.Enabled=_runes!=null && _selecting;}
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){_timer.Stop();_timer.Dispose();_load?.Cancel();_poll?.Cancel();_opgg.Dispose();_am.Dispose();_tips.Dispose();}
        base.Dispose(disposing);
    }
    [StructLayout(LayoutKind.Sequential)]private struct RECT{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out RECT rectangle);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
}
