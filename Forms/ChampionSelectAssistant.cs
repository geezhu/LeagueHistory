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
    private readonly FlowLayoutPanel _body=new BufferedFlow(){Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(12)};
    private readonly Label _status=new(){Dock=DockStyle.Top,Height=34,Padding=new Padding(10,4,10,4),Text="等待选择英雄…"};
    private readonly CheckBox _pin=new(){Text="固定位置",AutoSize=true};
    private readonly Button _apply=new(){Text="应用符文",AutoSize=true,Enabled=false};
    private readonly Button _fold=new(){Text="收起",AutoSize=true};
    private readonly ComboBox _lane=new(){Width=95,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ToolTip _tips=new();
    private CancellationTokenSource? _load,_poll;
    private bool _busy,_selecting,_collapsed,_syncing,_applying;
    private int _hero,_queue;
    private string? _position;
    private string _key="";
    private int _pollFailures;
    private OpggRunes? _runes;
    protected override bool ShowWithoutActivation=>true;
    public ChampionSelectAssistant(FormMain owner)
    {
        _owner=owner;_am=new(cacheLifetime:()=>TimeSpan.FromMinutes(Math.Clamp(owner.GetAppConfig()?.AramMetaCacheMinutes??1440,1,10080)));
        Text="选人助手";ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;FormBorderStyle=FormBorderStyle.FixedToolWindow;
        DoubleBuffered=true;
        BackColor=Color.FromArgb(8,29,40);ForeColor=Color.FromArgb(215,225,226);Font=new Font("Microsoft YaHei UI",9);ClientSize=new Size(320,590);
        _status.ForeColor=Color.FromArgb(135,162,173);
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=38,Padding=new Padding(6)};
        toolbar.Controls.AddRange(new Control[]{_pin,_fold,_lane});
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
        _lane.BackColor=Color.FromArgb(16,45,57);_lane.ForeColor=ForeColor;
        _fold.Click+=(_,_)=>{_collapsed=!_collapsed;_fold.Text=_collapsed?"展开":"收起";_body.Visible=!_collapsed;_status.Visible=!_collapsed;footer.Visible=!_collapsed;Height=_collapsed?78:630;};
        _lane.SelectedIndexChanged+=(_,_)=>{if(!_syncing && _hero>0)_ = LoadHeroAsync();};
        guide.Click+=(_,_)=>{if(_hero>0)_owner.OpenAssistantGuide(_hero,_queue,_position);};
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
        if(!_selecting || handle==IntPtr.Zero || IsIconic(handle) || (!clientFocused && foreground!=Handle && !ContainsFocus && !_owner.ContainsFocus)){if(Visible)Hide();return;}
        if(GetWindowRect(handle,out var rect) && !_pin.Checked)
        {
            var target=Placement(Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom),Screen.FromHandle(handle).WorkingArea,new Size(Width,_collapsed?78:630));
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
    private void Line(string text,bool heading=false)=>_body.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new Size(282,0),Margin=new Padding(0,5,0,5),ForeColor=heading?Color.FromArgb(208,185,130):Color.FromArgb(195,213,219)});
    private async Task LoadHeroAsync()
    {
        _load?.Cancel();_load?.Dispose();var request=_load=new CancellationTokenSource();int hero=_hero,queue=_queue;
        var token=request.Token;_runes=null;_apply.Enabled=false;ClearBody();
        var name=FormMain.Globals.resLoading?.GetChampionById(hero)?.Name??$"英雄 {hero}";
        _status.Text="正在加载推荐…";
        _lane.Visible=queue!=2400 && OpggQuery.ModeForFollowing(queue)=="ranked";
        try
        {
            _ = LoadSkillsAsync(hero,token);
            if(queue==2400)
            {
                // The index already contains augment recommendations; no item-detail request is needed.
                var page=await _am.GetAsync(0,false,token);token.ThrowIfCancellationRequested();
                var champion=page.Index.Champions.FirstOrDefault(c=>c.Id==hero);
                if(champion==null)throw new InvalidDataException("该英雄暂无海克斯推荐。");
                Line($"{champion.Tier} · 胜率 {champion.WinRate:P2}");
                foreach(var (key,title) in new[]{("kPrismatic","棱彩"),("kGold","黄金"),("kSilver","白银")})
                {
                    Line(title+"强化",true);
                    foreach(var entry in (champion.Data["top"]?[key] as JArray??new()).OfType<JObject>().Take(3))
                    {
                        int id=(int?)entry["id"]??0;var meta=page.Index.Augments[id.ToString()];
                        string label=FormMain.Globals.resLoading?.GetAugmentById(id)?.Name??(string?)meta?["name_zh"]??$"强化 {id}";
                        Line($"{label}  ·  {AramMetaIndex.Rate(entry["wr"]):P1}");
                        await IconRow(new[]{id},"augment",token);
                    }
                }
                _status.Text=$"AM · 海克斯大乱斗 · {page.Index.Patch}"+(page.UpdateFailed?" · 旧缓存":"");
            }
            else
            {
                string mode=OpggQuery.ModeForFollowing(queue);
                string? selected=new string?[]{null,"top","jungle","mid","adc","support"}[_lane.SelectedIndex];
                var query=new OpggQuery("global",mode,mode=="ranked"?"emerald_plus":"all",selected??_position??"mid",hero).Normalize();
                if(mode=="ranked" && selected==null)
                {
                    var tiers=await _opgg.GetTiersAsync(query,false,token);
                    query=query with {Position=OpggQuery.PositionForChampion(tiers.Rows,hero,_position)??"mid"};
                }
                var build=await _opgg.GetBuildAsync(query,false,token);token.ThrowIfCancellationRequested();
                _position=query.Position;_runes=build.Runes.FirstOrDefault();
                string laneName=query.Position switch {"top"=>"上路","jungle"=>"打野","mid"=>"中路","adc"=>"下路","support"=>"辅助",_=>""};
                var tier=(int?)(build.Stats["tier_data"]?["tier"]??build.Stats["tier"]);
                Line($"{(tier.HasValue?"T"+tier:"—")} · 胜率 {((double?)build.Stats["win_rate"]??OpggBuild.Ratio(build.Stats["win"],build.Stats["play"])):P2} · {laneName}",true);
                if(_runes!=null)
                {
                    Line("推荐符文",true);
                    await IconRow(_runes.Perks,"rune",token);
                }
                Line("核心装备",true);
                var items=build.Data["core_items"]?.First?["ids"] as JArray;
                await IconRow((items??new JArray()).Values<int>().Take(3),"item",token);
                var skills=build.Data["skill_masteries"]?.First?["ids"] as JArray;
                if(skills!=null)Line("加点："+string.Join(" → ",skills.Values<string>()));
                Line("召唤师技能",true);
                await IconRow((build.Data["summoner_spells"]?.First?["ids"] as JArray??new()).Values<int>(),"spell",token);
                string modeName=mode switch {"ranked"=>"召唤师峡谷","aram"=>"极地大乱斗","arena"=>"斗魂竞技场","urf"=>"无限火力",_=>"极限闪击"};
                _status.Text=$"OP.GG · {modeName} · {build.Version}";
                _apply.Enabled=_runes!=null && !_applying;
            }
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(!token.IsCancellationRequested && !IsDisposed)_status.Text="加载失败："+ex.Message;}
    }
    private async Task IconRow(IEnumerable<int> ids,string kind,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var row=new FlowLayoutPanel {AutoSize=true,MaximumSize=new Size(282,0),Margin=Padding.Empty};_body.Controls.Add(row);
        foreach(int id in ids)
        {
            var resources=FormMain.Globals.resLoading;
            var info=kind switch
            {
                "champion"=>await resources.GetChampionInfoAsync(id),"augment"=>await resources.GetAugmentInfoAsync(id),
                "item"=>await resources.GetItemInfoAsync(id),"spell"=>await resources.GetSpellInfoAsync(id),_=>await resources.GetRuneInfoAsync(id)
            };
            if(token.IsCancellationRequested || row.IsDisposed){info.image?.Dispose();token.ThrowIfCancellationRequested();return;}
            if(info.image==null){row.Controls.Add(new Label{Text=info.name,AutoSize=true,MaximumSize=new Size(260,0)});continue;}
            int size=kind=="champion"?52:kind=="rune"?26:32;
            var icon=new PictureBox {Image=info.image,Size=new Size(size,size),SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(2)};
            icon.Disposed+=(_,_)=>icon.Image?.Dispose();_tips.SetToolTip(icon,League.Controls.TooltipText.Format(info.name,info.description));row.Controls.Add(icon);
        }
    }
    private async Task ApplyAsync()
    {
        if(_applying || _runes==null || _hero<=0)return;
        int hero=_hero;var runes=_runes;_applying=true;_apply.Enabled=false;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var client=FormMain.Globals.lcuClient?.Client??throw new InvalidOperationException("客户端已断开。");
            var session=JObject.Parse(await client.GetStringAsync("/lol-champ-select/v1/session",timeout.Token));
            var own=(session["myTeam"] as JArray)?.OfType<JObject>().FirstOrDefault(p=>(int?)p["cellId"]==(int?)session["localPlayerCellId"]);
            if((int?)own?["championId"]!=hero)throw new InvalidOperationException("英雄已变化，请等待推荐更新。");
            await OpggRuneService.ApplyAsync(client,runes,FormMain.Globals.resLoading?.GetChampionById(hero)?.Name??hero.ToString(),timeout.Token);
            if(!IsDisposed)_status.Text="符文已应用。";
        }
        catch(Exception ex){if(!IsDisposed)_status.Text=ex.Message;}
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
