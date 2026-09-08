using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public sealed class TimelineView : UserControl
{
    private readonly MatchInfo _match;
    private readonly PlayerStatisticsService _service;
    private readonly TimelineGoldChart _chart = new() { Dock = DockStyle.Top, Height = 218 };
    private readonly TrackBar _time = new() { Dock = DockStyle.Top, Height = 42, TickStyle = TickStyle.None, Enabled = false };
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8,4,0,0) };
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly ListView _events = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None };
    private readonly Button _retry = new() { Text = "重新加载", AutoSize = true, FlatStyle = FlatStyle.Flat };
    private CancellationTokenSource? _request;
    private MatchTimeline? _data;
    private bool _started;

    public TimelineView(MatchInfo match, PlayerStatisticsService? service = null)
    {
        _match = match; _service = service ?? new(() => FormMain.Globals.lcuClient.Client);
        BackColor = Color.White; ForeColor = Color.FromArgb(36,63,98);
        _filter.Items.AddRange(new[] { "全部事件", "击杀", "地图目标" }); _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => ShowEvents();
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        toolbar.Controls.AddRange(new Control[] { _filter, _retry }); _retry.Click += (_, _) => LoadTimeline();
        foreach (var (title,width) in new[] { ("时间",65),("玩家",150),("事件",260),("位置坐标",110) }) _events.Columns.Add(title,width);
        _events.SelectedIndexChanged += (_, _) =>
        {
            if (_data == null || _events.SelectedItems.Count == 0 || _events.SelectedItems[0].Tag is not long stamp) return;
            int index = _data.Frames.FindLastIndex(f => f.Timestamp <= stamp);
            if (index >= 0) _time.Value = Math.Min(index,_time.Maximum);
        };
        _time.ValueChanged += (_, _) => UpdateFrame();
        Controls.Add(_events); Controls.Add(toolbar); Controls.Add(_time); Controls.Add(_chart); Controls.Add(_status);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e); if (!_started) { _started = true; LoadTimeline(); }
    }

    private async void LoadTimeline()
    {
        _request?.Cancel(); _request?.Dispose(); var request = _request = new CancellationTokenSource();
        _retry.Enabled = false; _time.Enabled = false; _events.Items.Clear(); _data = null; _chart.Data = null; _chart.Invalidate();
        _status.Text = "正在加载时间线…";
        try
        {
            var response = await _service.GetAsync($"/lol-match-history/v1/game-timelines/{_match.GameId}", request.Token);
            if (IsDisposed || request.IsCancellationRequested) return;
            if (response.Data == null) { _status.Text = response.Message; return; }
            if (response.Data is Newtonsoft.Json.Linq.JObject root && root["gameId"] != null && root["gameId"]!.Value<long>() != _match.GameId)
            { _status.Text = "返回的时间线与当前比赛不匹配。"; return; }
            _data = MatchTimeline.Parse(response.Data,_match.AllParticipants);
            if (_data.Frames.Count == 0) { _status.Text = "这场比赛暂无时间线数据；仍可查看基础计分板。"; return; }
            _chart.Data = _data; _time.Maximum = _data.Frames.Count-1; _time.Value = 0; _time.Enabled = true;
            ShowEvents(); UpdateFrame();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"时间线解析失败: {ex.GetType().Name}");
            if (!IsDisposed && !request.IsCancellationRequested) _status.Text = "时间线格式暂不支持，请重试。";
        }
        finally { if (!IsDisposed && !request.IsCancellationRequested) _retry.Enabled = true; }
    }

    private void UpdateFrame()
    {
        if (_data == null || _data.Frames.Count == 0) return;
        var frame = _data.Frames[_time.Value];
        _chart.Selected = _time.Value; _chart.Invalidate();
        string Gold(long? value) => value.HasValue ? $"{value.Value:N0}" : "数据缺失";
        _status.Text = $"{FormatTime(frame.Timestamp)}  ·  蓝队总经济 {Gold(frame.Blue)}  ·  红队总经济 {Gold(frame.Red)}\n拖动滑块查看时间点；经济缺失的区间不连线。";
    }

    private void ShowEvents()
    {
        _events.BeginUpdate(); _events.Items.Clear();
        if (_data != null)
            foreach (var ev in _data.Events.Where(e => _filter.SelectedIndex == 0 || (_filter.SelectedIndex == 1 ? e.Kind == "CHAMPION_KILL" : e.Kind != "CHAMPION_KILL")))
                _events.Items.Add(new ListViewItem(new[] { FormatTime(ev.Timestamp),ev.Actor,ev.Description,ev.X.HasValue && ev.Y.HasValue ? $"{ev.X}, {ev.Y}" : "—" }) { Tag = ev.Timestamp });
        _events.EndUpdate();
    }

    public static string FormatTime(long milliseconds) => $"{milliseconds/60000:00}:{milliseconds/1000%60:00}";
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _request?.Cancel(); _request?.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class TimelineGoldChart : Control
{
    public MatchTimeline? Data { get; set; }
    public int Selected { get; set; }
    public TimelineGoldChart() => DoubleBuffered = true;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Data == null || Data.Frames.Count == 0) return;
        var area = new Rectangle(58,26,Math.Max(1,Width-82),Math.Max(1,Height-58));
        long max = Math.Max(1,Data.Frames.Max(f => Math.Max(f.Blue ?? 0,f.Red ?? 0)));
        long duration = Math.Max(1,Data.Frames.Last().Timestamp);
        using var grid = new Pen(Color.FromArgb(231,237,245));
        for (int i=0;i<=4;i++)
        {
            int y = area.Bottom-area.Height*i/4;
            e.Graphics.DrawLine(grid,area.Left,y,area.Right,y);
            TextRenderer.DrawText(e.Graphics,$"{max*i/4/1000d:0.#}k",Font,new Point(5,y-8),ForeColor);
        }
        PointF Point(GoldFrame f,long gold) => new(area.Left+area.Width*f.Timestamp/(float)duration,area.Bottom-area.Height*gold/(float)max);
        void Series(Func<GoldFrame,long?> value,Color color)
        {
            using var pen = new Pen(color,2);
            for (int i=1;i<Data.Frames.Count;i++)
            {
                var a = Data.Frames[i-1]; var b = Data.Frames[i];
                if (Data.FrameInterval is > 0 && b.Timestamp-a.Timestamp > Data.FrameInterval.Value*1.5) continue;
                if (value(a) is long av && value(b) is long bv) e.Graphics.DrawLine(pen,Point(a,av),Point(b,bv));
            }
        }
        Series(f=>f.Blue,Color.FromArgb(46,103,190)); Series(f=>f.Red,Color.FromArgb(191,81,91));
        using var cursor = new Pen(Color.FromArgb(111,123,139)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        float x = Point(Data.Frames[Math.Clamp(Selected,0,Data.Frames.Count-1)],0).X;
        e.Graphics.DrawLine(cursor,x,area.Top,x,area.Bottom);
        TextRenderer.DrawText(e.Graphics,"蓝队",Font,new Point(area.Left,3),Color.FromArgb(46,103,190));
        TextRenderer.DrawText(e.Graphics,"红队",Font,new Point(area.Left+60,3),Color.FromArgb(191,81,91));
        TextRenderer.DrawText(e.Graphics,"00:00",Font,new Point(area.Left,area.Bottom+6),ForeColor);
        TextRenderer.DrawText(e.Graphics,TimelineView.FormatTime(duration),Font,new Point(area.Right-45,area.Bottom+6),ForeColor);
    }
}
