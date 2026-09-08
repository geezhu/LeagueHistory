using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed partial class AramMetaView : UserControl
{
    private readonly AramMetaClient _client;
    private readonly Button _back=new(){Text="←",Width=34,Enabled=false},_forward=new(){Text="→",Width=34,Enabled=false};
    private readonly TextBox _search=new(){Width=170,PlaceholderText="搜索英雄 / ID"};
    private readonly ComboBox _role=new(){Width=90,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly CheckBox _follow=new(){Text="跟随选人",Checked=true,AutoSize=true};
    private readonly Label _status=new(){Dock=DockStyle.Top,Height=44,Padding=new Padding(12,4,8,4)};
    private readonly DataGridView _grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,
        RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor=Color.White,BorderStyle=BorderStyle.None,AutoGenerateColumns=false};
    private readonly FlowLayoutPanel _detail=new(){Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(12),Visible=false};
    private readonly ToolTip _tips=new();
    private readonly System.Windows.Forms.Timer _timer=new(){Interval=2500};
    private CancellationTokenSource? _request,_poll;
    private AramMetaPage? _page;
    private int _champion,_sort=4,_historyIndex=0;
    private bool _descending=true,_syncing,_polling;
    private string _selection="";
    private sealed record Visit(int Champion,string Search,int Role,int Sort,bool Descending,int Scroll);
    private readonly List<Visit> _history=new(){new(0,"",0,4,true,0)};
    private static readonly string[] Roles={"","Fighter","Mage","Assassin","Marksman","Support","Tank"};
    private static string Rate(JToken? n)=>Rate(AramMetaIndex.Rate(n));
    private static string Rate(double? n)=>n.HasValue?$"{n:P2}":"—";
    private static string Name(AramMetaChampion c)=>FormMain.Globals.resLoading?.GetChampionById(c.Id)?.Name??c.Name;
    public AramMetaView():this(new AramMetaClient(),new HttpClient {Timeout=TimeSpan.FromSeconds(12)}){}
    public AramMetaView(Func<TimeSpan> cacheLifetime):this(new AramMetaClient(cacheLifetime:cacheLifetime),new HttpClient {Timeout=TimeSpan.FromSeconds(12)}){}
    internal AramMetaView(AramMetaClient client,HttpClient images)
    {
        _client=client;_imageHttp=images;
        Dock=DockStyle.Fill;Font=new Font("Microsoft YaHei UI",9);BackColor=Color.FromArgb(244,247,251);
        _role.Items.AddRange(new object[]{"全部职业","战士","法师","刺客","射手","辅助","坦克"});_role.SelectedIndex=0;
        var refresh=new Button {Text="刷新",AutoSize=true};
        var home=new Button {Text="英雄榜",AutoSize=true};
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(10)};
        toolbar.Controls.AddRange(new Control[]{_back,_forward,home,_search,_role,refresh,_follow});
        Controls.Add(_detail);Controls.Add(_grid);Controls.Add(_status);Controls.Add(toolbar);
        _tips.SetToolTip(_back,"后退");_tips.SetToolTip(_forward,"前进");
        _tips.SetToolTip(_status,"来源 arammeta；调整后胜率经过来源模型处理，原始胜率为样本胜率。数据不是 OP.GG 统计。");
        _grid.RowTemplate.Height=46;_grid.ColumnHeadersHeight=36;
        _grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(222,242,243);_grid.DefaultCellStyle.SelectionForeColor=Color.FromArgb(30,60,75);
        _grid.Columns.Add(new DataGridViewImageColumn {HeaderText="",Width=46,MinimumWidth=46,FillWeight=40,ImageLayout=DataGridViewImageCellLayout.Zoom,DefaultCellStyle=new(){NullValue=null}});
        foreach(var (text,min) in new[]{("英雄",125),("梯队",55),("原始胜率",85),("调整后胜率",95),("样本量",80)})
            _grid.Columns.Add(new DataGridViewTextBoxColumn {HeaderText=text,MinimumWidth=min,FillWeight=min,SortMode=DataGridViewColumnSortMode.Programmatic});
        _grid.CellClick+=async (_,e)=>{if(e.RowIndex>=0 && _grid.Rows[e.RowIndex].Tag is AramMetaChampion c){_follow.Checked=false;await NavigateAsync(c.Id);}};
        _grid.RowPrePaint+=(_,e)=>
        {
            var row=_grid.Rows[e.RowIndex];
            if(row.Cells[0].Value==null && row.Tag is AramMetaChampion c)_=SetGridImageAsync(row,c,_request?.Token??default);
        };
        _grid.KeyDown+=async (_,e)=>{if(e.KeyCode==Keys.Enter && _grid.CurrentRow?.Tag is AramMetaChampion c){e.SuppressKeyPress=true;_follow.Checked=false;await NavigateAsync(c.Id);}};
        _grid.ColumnHeaderMouseClick+=(_,e)=>{if(e.ColumnIndex==0)return;_follow.Checked=false;_descending=_sort==e.ColumnIndex?!_descending:e.ColumnIndex!=1;_sort=e.ColumnIndex;FillGrid();};
        _search.TextChanged+=(_,_)=>FilterChanged();_role.SelectedIndexChanged+=(_,_)=>FilterChanged();
        home.Click+=async (_,_)=>{_follow.Checked=false;await NavigateAsync(0);};
        _back.Click+=async (_,_)=>await TravelAsync(-1);_forward.Click+=async (_,_)=>await TravelAsync(1);
        refresh.Click+=async (_,_)=>{_history[_historyIndex]=Capture();await LoadAsync(true);};
        _timer.Tick+=async (_,_)=>await PollAsync();_follow.CheckedChanged+=(_,_)=>_selection="";
        _detail.SizeChanged+=(_,_)=>ResizeSections();
    }
    private void FilterChanged(){if(_syncing)return;_follow.Checked=false;if(_champion==0)FillGrid();}
    private Visit Capture()=>new(_champion,_search.Text,_role.SelectedIndex,_sort,_descending,_champion==0?Math.Max(0,_grid.FirstDisplayedScrollingRowIndex):Math.Max(0,-_detail.AutoScrollPosition.Y));
    private void Buttons(){_back.Enabled=_historyIndex>0;_forward.Enabled=_historyIndex<_history.Count-1;}
    private async Task NavigateAsync(int id)
    {
        _history[_historyIndex]=Capture();
        if(id!=_champion)
        {
            _history.RemoveRange(_historyIndex+1,_history.Count-_historyIndex-1);
            _history.Add(Capture() with {Champion=id,Scroll=0});_historyIndex++;
            if(_history.Count>100){_history.RemoveAt(0);_historyIndex--;}
        }
        _champion=id;Buttons();await LoadAsync(false);
    }
    private async Task TravelAsync(int delta)
    {
        int next=_historyIndex+delta;if(next<0 || next>=_history.Count)return;
        _follow.Checked=false;_history[_historyIndex]=Capture();_historyIndex=next;var visit=_history[next];
        _syncing=true;_champion=visit.Champion;_search.Text=visit.Search;_role.SelectedIndex=visit.Role;_sort=visit.Sort;_descending=visit.Descending;_syncing=false;
        Buttons();await LoadAsync(false);
    }
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if(Visible){_timer.Start();_ = LoadAsync(false);}
        else {_timer.Stop();_request?.Cancel();_poll?.Cancel();_selection="";}
    }
    private async Task LoadAsync(bool refresh)
    {
        _request?.Cancel();_request?.Dispose();var request=_request=new();int id=_champion;
        _status.Text="正在加载 arammeta · 海克斯大乱斗…";
        try
        {
            bool previewShown=false;
            string? previewVersion=null;
            var page=await _client.GetAsync(id,refresh,request.Token,preview=>
            {
                if(IsDisposed || request.IsCancellationRequested || _request!=request)return;
                _page=preview;_search.Enabled=false;_role.Enabled=false;
                _grid.Visible=false;_detail.Visible=true;
                RenderDetail(preview,id,request.Token);_detail.BringToFront();
                _detail.AutoScrollPosition=new Point(0,_history[_historyIndex].Scroll);
                previewShown=true;
                previewVersion=preview.Index.Version;
                _status.Text=$"arammeta · 海克斯大乱斗 · 数据版本 {preview.Index.Patch} · 英雄概要与强化推荐已加载，正在获取装备详情…";
            });request.Token.ThrowIfCancellationRequested();
            if(IsDisposed)return;_page=page;
            int detailScroll=previewShown?Math.Max(0,-_detail.AutoScrollPosition.Y):_history[_historyIndex].Scroll;
            _search.Enabled=id==0;_role.Enabled=id==0;
            _grid.Visible=id==0;_detail.Visible=id!=0;
            if(id==0){FillGrid();_grid.BringToFront();}
            else {RenderDetail(page,id,request.Token,previewShown && previewVersion==page.Index.Version);_detail.BringToFront();}
            var visit=_history[_historyIndex];
            if(id==0 && _grid.Rows.Count>0)_grid.FirstDisplayedScrollingRowIndex=Math.Min(visit.Scroll,_grid.Rows.Count-1);
            if(id!=0)_detail.AutoScrollPosition=new Point(0,detailScroll);
            _status.Text=$"arammeta · 海克斯大乱斗 · 数据版本 {page.Index.Patch} · {(page.UpdateFailed?"\u66f4\u65b0\u5931\u8d25\uff0c\u663e\u793a\u65e7\u7f13\u5b58":page.Cached?"缓存":"已更新")} {page.FetchedAt.ToLocalTime():MM-dd HH:mm}\n调整后胜率与原始胜率分别展示；样本不代表全服。";
        }
        catch(OperationCanceledException){if(!request.IsCancellationRequested && !IsDisposed){MarkDetailUnavailable();_status.Text="请求超时，点击刷新重试。";}}
        catch(Exception ex){if(!request.IsCancellationRequested && !IsDisposed){MarkDetailUnavailable();_status.Text="加载失败："+ex.Message+" 点击刷新重试；保留上次内容。";}}
    }
    private void FillGrid()
    {
        if(_page==null)return;
        var rows=_page.Index.Champions.Where(c=>(_role.SelectedIndex==0 || c.Roles.Contains(Roles[_role.SelectedIndex])) &&
            (Name(c)+" "+c.Name+" "+c.Alias+" "+c.Id).Contains(_search.Text.Trim(),StringComparison.OrdinalIgnoreCase));
        if(_sort==1)rows=_descending?rows.OrderByDescending(Name):rows.OrderBy(Name);
        else {double Value(AramMetaChampion c)=>_sort switch {3=>c.RawWinRate??-1,5=>c.Games??-1,_=>c.WinRate??-1};rows=_descending?rows.OrderByDescending(Value):rows.OrderBy(Value);}
        _grid.Rows.Clear();
        foreach(var c in rows)
        {
            int index=_grid.Rows.Add(null,Name(c),c.Tier,Rate(c.RawWinRate),Rate(c.WinRate),c.Games?.ToString("N0")??"—");
            var row=_grid.Rows[index];row.Tag=c;
        }
        foreach(DataGridViewColumn column in _grid.Columns)column.HeaderCell.SortGlyphDirection=column.Index==_sort?(_descending?SortOrder.Descending:SortOrder.Ascending):SortOrder.None;
        _grid.ClearSelection();
    }
    private async Task PollAsync()
    {
        if(_polling || !Visible || !_follow.Checked)return;
        var client=FormMain.Globals.lcuClient?.Client;if(client==null)return;
        _polling=true;_poll?.Dispose();_poll=new(TimeSpan.FromSeconds(5));
        try
        {
            var flow=JObject.Parse(await client.GetStringAsync("/lol-gameflow/v1/session",_poll.Token));
            if((int?)flow["gameData"]?["queue"]?["id"]!=2400){_selection="";return;}
            var session=JObject.Parse(await client.GetStringAsync("/lol-champ-select/v1/session",_poll.Token));
            var own=(session["myTeam"] as JArray)?.OfType<JObject>().FirstOrDefault(p=>(int?)p["cellId"]==(int?)session["localPlayerCellId"]);
            int id=(int?)own?["championId"]??0;
            if(id<=0 || id.ToString()==_selection || !Visible || !_follow.Checked)return;
            _selection=id.ToString();_ = NavigateAsync(id);
        }
        catch(OperationCanceledException){}catch(HttpRequestException){_selection="";}catch(Newtonsoft.Json.JsonException){}
        finally{_polling=false;}
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){_timer.Stop();_timer.Dispose();_request?.Cancel();_poll?.Cancel();_client.Dispose();_tips.Dispose();_imageHttp.Dispose();foreach(var image in _images.Values)image.Dispose();_images.Clear();}
        base.Dispose(disposing);
    }
}
