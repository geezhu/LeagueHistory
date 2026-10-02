using League.Models;
using Newtonsoft.Json.Linq;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    private readonly Button _rosterButton=new(){Text="可选英雄",Dock=DockStyle.Right,Width=112,FlatStyle=FlatStyle.Flat,Enabled=false};
    private ChampionSelectRoster? _roster;
    private int _rosterQueue;
    private Form? _rosterWindow;
    private Action? _rosterChanged;
    private void UpdateRoster(JObject session,int queue)
    {
        if(_rosterQueue!=queue)_rosterWindow?.Close();_rosterQueue=queue;
        bool supported=queue is 450 or 2400 || (bool?)session["benchEnabled"]==true || SelectedMode is "aram" or "mayhem";
        if(!supported){ClearRoster();_tips.SetToolTip(_rosterButton,"当前模式没有大乱斗英雄总览");return;}
        var next=ChampionSelectRoster.Parse(session);
        bool changed=next.Signature!=_roster?.Signature;
        _roster=next;
        _rosterButton.Enabled=true;
        _tips.SetToolTip(_rosterButton,"查看我方阵容与交换池的英雄梯队");
        _rosterButton.Text=$"可选英雄 · {next.Team.Count(t=>t.ChampionId>0)+next.Bench.Length}";
        if(changed)_rosterChanged?.Invoke();
    }
    private void ClearRoster(){_roster=null;_rosterButton.Enabled=false;_rosterButton.Text="可选英雄";_tips.SetToolTip(_rosterButton,"等待客户端提供大乱斗选人数据");_rosterWindow?.Close();}
    private void ShowRoster()
    {
        if(_roster==null)return;
        if(_rosterWindow is {IsDisposed:false}){_rosterWindow.Activate();return;}
        int queue=_queue;bool mayhem=queue==2400 || SelectedMode=="mayhem";
        var query=(SelectedQuery with{Mode="aram",ChampionId=0}).Normalize();
        var window=new Form{Text="当前可选英雄 · "+(mayhem?"海克斯大乱斗":"大乱斗"),ClientSize=new Size(760,550),BackColor=BackColor,ForeColor=ForeColor,Font=Font,StartPosition=FormStartPosition.Manual,ShowInTaskbar=false};_rosterWindow=window;
        var lifetime=new CancellationTokenSource();CancellationTokenSource? painting=null;
        var masteryRefresh=new List<Func<Task>>();
        var masteryTimer=new System.Windows.Forms.Timer{Interval=10000};
        masteryTimer.Tick+=(_,_)=>{foreach(var update in masteryRefresh.ToArray())_=update();};masteryTimer.Start();
        window.Disposed+=(_,_)=>masteryTimer.Dispose();
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=42,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(0,42),Padding=new Padding(10,5,10,5)};
        var sort=new Controls.DarkFilterComboBox{Width=125};sort.Items.AddRange(new object[]{"梯队优先","胜率优先"});sort.SelectedIndex=0;
        var retry=new Button{Text="重试数据",AutoSize=true,FlatStyle=FlatStyle.Flat};toolbar.Controls.AddRange(new Control[]{sort,retry});
        var analysisButton=new Button{Text="阵容分析",AutoSize=true,FlatStyle=FlatStyle.Flat};toolbar.Controls.Add(analysisButton);
        var sendTier=new Button{Text="发送梯队 ▾",AutoSize=true,FlatStyle=FlatStyle.Flat,Enabled=false};toolbar.Controls.Add(sendTier);
        var sendMenu=new ContextMenuStrip();
        var sendAll=sendMenu.Items.Add("发送全部梯队");
        var sendTeam=sendMenu.Items.Add("发送我方梯队");var sendBench=sendMenu.Items.Add("发送交换池梯队");
        sendTier.Click+=(_,_)=>{sendBench.Enabled=_roster?.Bench.Length>0;sendMenu.Show(sendTier,new Point(0,sendTier.Height));};
        window.Disposed+=(_,_)=>sendMenu.Dispose();
        var status=new Label{Dock=DockStyle.Top,Height=42,Padding=new Padding(10,3,10,3),Text="正在读取英雄梯队…",ForeColor=Color.FromArgb(135,162,173)};
        void FitStatus()=>status.Height=Math.Max(42,TextRenderer.MeasureText(status.Text,Font,new Size(Math.Max(200,window.ClientSize.Width-20),0),TextFormatFlags.WordBreak).Height+8);
        window.SizeChanged+=(_,_)=>FitStatus();status.TextChanged+=(_,_)=>FitStatus();
        var content=new BufferedFlow{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(10)};
        window.Controls.Add(content);window.Controls.Add(status);window.Controls.Add(toolbar);
        var analysis=new Controls.ChampionCompositionView(id=>{var hero=FormMain.Globals.resLoading.GetChampionById(id);return(hero?.Name??$"英雄 {id}",hero?.Alias??"");},
            ()=>TimeSpan.FromMinutes(Math.Clamp(_owner.GetAppConfig()?.ChampionAnalysisCacheMinutes??1440,1,10080))){Dock=DockStyle.Fill,Visible=false};
        window.Controls.Add(analysis);analysis.BringToFront();
        analysisButton.Click+=(_,_)=>{analysis.Visible=!analysis.Visible;content.Visible=!analysis.Visible;status.Visible=!analysis.Visible;sort.Visible=!analysis.Visible;retry.Visible=!analysis.Visible;analysisButton.Text=analysis.Visible?"返回英雄梯队":"阵容分析";if(analysis.Visible && _roster!=null)analysis.SetRoster(_roster);};
        var ranks=new Dictionary<int,(string Tier,double? Win,double? Pick)>();bool loading=false,sending=false;string loadedVersion="";
        async Task SendTiers(bool? selection)
        {
            if(sending || loading || _roster==null || ranks.Count==0)return;
            var messages=new List<string>();
            foreach(bool bench in selection.HasValue?new[]{selection.Value}:new[]{false,true})
            {
                var entries=bench?_roster.Bench.Select(id=>(Id:id,Title:Name(id))):_roster.Team.Where(e=>e.ChampionId>0).Select(e=>(Id:e.ChampionId,Title:Name(e.ChampionId)+(e.Preview?"（预选）":"")));
                var rows=entries.OrderBy(e=>Rank(ranks.GetValueOrDefault(e.Id).Tier??"—"))
                    .Select(e=>{var rank=ranks.GetValueOrDefault(e.Id);return new Services.ChampionTierChatEntry(e.Title,rank.Tier??"暂无梯队",rank.Win,rank.Pick);});
                messages.AddRange(Services.ChampionTierChatMessage.Build(mayhem?"海克斯大乱斗":"大乱斗",mayhem?"arammeta":"OP.GG",loadedVersion,bench?"交换池":"我方",rows,mayhem));
            }
            if(messages.Count==0){status.Text="当前没有可发送的英雄。";return;}
            sending=true;sendTier.Enabled=false;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var client=FormMain.Globals.lcuClient?.Client??throw new InvalidOperationException("客户端未连接。");
                int sent=0;
                foreach(string message in messages)
                {
                    if(!await AnnounceRunesAsync(client,message,timeout.Token))break;
                    sent++;
                }
                if(!window.IsDisposed){status.Visible=true;status.Text=sent==messages.Count?"英雄梯队已发送到选人聊天。":$"发送未完成（{sent}/{messages.Count}），请确认仍在选人阶段。";}
            }
            catch(OperationCanceledException){if(!window.IsDisposed && !lifetime.IsCancellationRequested){status.Visible=true;status.Text="发送超时，请检查选人聊天。";}}
            catch(Exception){if(!window.IsDisposed){status.Visible=true;status.Text="梯队发送失败，请检查客户端连接后重试。";}}
            finally{sending=false;if(!window.IsDisposed)sendTier.Enabled=!loading && ranks.Count>0;}
        }
        sendAll.Click+=(_,_)=>_=SendTiers(null);sendTeam.Click+=(_,_)=>_=SendTiers(false);sendBench.Click+=(_,_)=>_=SendTiers(true);
        int Rank(string tier)=>tier=="OP"?0:int.TryParse(tier.TrimStart('T'),out int n)?n:99;
        string Name(int id)=>FormMain.Globals.resLoading.GetChampionById(id)?.Name??$"英雄 {id}";
        void Render()
        {
            if(window.IsDisposed || _roster==null)return;
            if(queue!=_queue){window.Close();return;}
            painting?.Cancel();painting?.Dispose();painting=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=painting.Token;
            masteryRefresh.Clear();
            content.SuspendLayout();foreach(Control c in content.Controls.Cast<Control>().ToArray())c.Dispose();
            void Group(string title,IEnumerable<ChampionSelectRosterEntry> entries,string empty)
            {
                content.Controls.Add(new Label{Text=title,AutoSize=true,ForeColor=Color.FromArgb(208,185,130),Margin=new Padding(0,8,0,6)});
                var grid=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MaximumSize=new Size(Math.Max(260,content.ClientSize.Width-36),0),Margin=Padding.Empty};content.Controls.Add(grid);
                var ordered=sort.SelectedIndex==0?entries.OrderBy(e=>Rank(ranks.GetValueOrDefault(e.ChampionId).Tier??"—")).ThenByDescending(e=>ranks.GetValueOrDefault(e.ChampionId).Win):entries.OrderByDescending(e=>ranks.GetValueOrDefault(e.ChampionId).Win).ThenBy(e=>Rank(ranks.GetValueOrDefault(e.ChampionId).Tier??"—"));
                foreach(var entry in ordered)
                {
                    var card=new Panel{Size=new Size(132,136),Margin=new Padding(0,0,6,6),BackColor=Color.FromArgb(16,45,57)};grid.Controls.Add(card);
                    int id=entry.ChampionId;var rank=ranks.GetValueOrDefault(id);string tier=rank.Tier??"—";
                    var icon=new PictureBox{Location=new Point(7,7),Size=new Size(40,40),SizeMode=PictureBoxSizeMode.Zoom};card.Controls.Add(icon);icon.Disposed+=(_,_)=>icon.Image?.Dispose();
                    card.Controls.Add(new Label{Text=tier,Location=new Point(56,9),Size=new Size(66,22),ForeColor=League.Controls.AramMetaView.TierColor(tier)});
                    card.Controls.Add(new Label{Text=rank.Win.HasValue?$"{rank.Win:P1}":"暂无统计",Location=new Point(56,32),Size=new Size(72,20)});
                    card.Controls.Add(new Label{Text=id>0?Name(id):"等待选择",AutoEllipsis=true,Location=new Point(7,57),Size=new Size(118,22)});
                    card.Controls.Add(new Label{Text=entry.CellId<0?"交换池":(entry.IsSelf?"自己":"队友 "+(entry.CellId+1))+(entry.Preview?" · 预选":""),Location=new Point(7,84),Size=new Size(118,22),ForeColor=Color.FromArgb(135,162,173)});
                    if(id>0)
                    {
                        var achievement=new Label{Text="成就读取中…",AutoEllipsis=true,Location=new Point(7,107),Size=new Size(118,22),ForeColor=Color.FromArgb(164,188,199)};card.Controls.Add(achievement);
                        string puuid=entry.CellId<0 ? _owner.GetCurrentPuuid()??"" : entry.Puuid;
                        if(entry.IsSelf && (string.IsNullOrWhiteSpace(puuid) || puuid==Guid.Empty.ToString()))puuid=_owner.GetCurrentPuuid()??"";
                        bool masteryBusy=false;
                        async Task Mastery()
                        {
                            if(masteryBusy || achievement.IsDisposed || token.IsCancellationRequested)return;
                            masteryBusy=true;
                            try
                            {
                                var page=await _masteryService.GetAsync(puuid,token);
                                if(achievement.IsDisposed || token.IsCancellationRequested)return;
                                var value=page.ForChampion(id);achievement.Text=value.Text;
                                _tips.SetToolTip(achievement,(entry.CellId<0?"自己使用此英雄的成就\n":"")+value.Description);
                            }
                            catch(OperationCanceledException){}
                            catch(Exception){if(!achievement.IsDisposed && !token.IsCancellationRequested)achievement.Text="成就暂不可用";}
                            finally{masteryBusy=false;}
                        }
                        masteryRefresh.Add(Mastery);
                        _=Mastery();
                        async Task Image(){try{var image=await FormMain.Globals.resLoading.GetChampionIconAsync(id);if(icon.IsDisposed || token.IsCancellationRequested){image?.Dispose();return;}icon.Image=image;}catch{}}
                        _=Image();
                        void Clickable(Control c){c.Cursor=Cursors.Hand;c.Click+=(_,_)=>{if(token.IsCancellationRequested)return;_owner.OpenAssistantGuide(id,mayhem?2400:0,"none",mayhem?null:query with{ChampionId=id});};foreach(Control child in c.Controls)Clickable(child);}Clickable(card);
                    }
                }
                if(grid.Controls.Count==0)grid.Controls.Add(new Label{Text=empty,AutoSize=true});
            }
            Group("我方阵容",_roster.Team,"尚未读取到我方阵容");
            Group("交换池",_roster.Bench.Select(id=>new ChampionSelectRosterEntry(id,-1,false,false)),_roster.BenchEnabled?"交换池当前为空":"客户端未提供交换池");
            content.ResumeLayout();
        }
        async Task Load()
        {
            if(loading)return;loading=true;retry.Enabled=false;sendTier.Enabled=false;
            try
            {
                string version;
                if(mayhem){var page=await _am.GetAsync(0,false,lifetime.Token);version=page.Index.Patch+(page.UpdateFailed?" · 旧缓存":"");foreach(var r in page.Index.Champions)ranks[r.Id]=(r.Tier,r.WinRate,page.Index.PickRate(r));}
                else{var page=await _opgg.GetTiersAsync(query,false,lifetime.Token);version=page.Version;foreach(var r in page.Rows)ranks[r.ChampionId]=(r.Tier.HasValue?"T"+r.Tier:"—",r.WinRate,r.PickRate);}
                if(window.IsDisposed)return;
                loadedVersion=version;
                status.Text=$"{(mayhem?"arammeta · 调整后胜率":"OP.GG · 胜率")} · {version}\n点击英雄查看攻略；队友英雄仅供查看，助手继续跟随自己的选人。";Render();
            }
            catch(OperationCanceledException){}
            catch(Exception ex){if(!window.IsDisposed){status.Text="梯队数据暂不可用，可重试；仍显示客户端提供的英雄。";_tips.SetToolTip(status,ex.Message);System.Diagnostics.Debug.WriteLine(ex);}}
            finally{loading=false;if(!window.IsDisposed){retry.Enabled=true;sendTier.Enabled=!sending && ranks.Count>0;}}
        }
        _rosterChanged=()=>{Render();if(analysis.Visible && _roster!=null)analysis.SetRoster(_roster);};sort.SelectedIndexChanged+=(_,_)=>Render();retry.Click+=(_,_)=>_=Load();
        content.SizeChanged+=(_,_)=>{foreach(var grid in content.Controls.OfType<FlowLayoutPanel>())grid.MaximumSize=new Size(Math.Max(260,content.ClientSize.Width-36),0);};
        window.FormClosed+=(_,_)=>{lifetime.Cancel();painting?.Cancel();painting?.Dispose();lifetime.Dispose();_rosterChanged=null;_rosterWindow=null;window.Dispose();};
        window.Shown+=(_,_)=>{PositionEquipmentEditor(window,new Size(340,360));Render();_=Load();};window.Show(this);
    }
}
