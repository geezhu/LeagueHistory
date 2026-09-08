using League.Models;
using League.UIState;

namespace League.Controls;

public partial class MatchDetailView : UserControl
{
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 30000, InitialDelay = 100, ShowAlways = true };
    private readonly Panel _scoreboard;
    private readonly MatchInfo _match;
    private bool _started;
    private TimelineView? _timeline;
    public event Action<string>? PlayerProfileRequested;
    public event Action? ReplayRequested;

    public MatchDetailView(MatchInfo match)
    {
        _match = match;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(244,247,251);
        var summary = new Label { Dock = DockStyle.Top, Height = 68, Padding = new Padding(12,8,0,0),
            ForeColor = Color.FromArgb(36,63,98), Font = new Font("Microsoft YaHei UI",10f),
            Text = $"{match.Mode} · {match.DurationText} · {match.GameTime}\n比赛 {match.GameId} · 点击列标题排序" };
        var replay = new Button { Text = "查看回放", Dock = DockStyle.Top, Height = 28, FlatStyle = FlatStyle.Flat };
        replay.Click += (_, _) => ReplayRequested?.Invoke();
        _scoreboard = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true };
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        var basic = new Button { Text = "基础数据", AutoSize = true, FlatStyle = FlatStyle.Flat };
        var timeline = new Button { Text = "时间线", AutoSize = true, FlatStyle = FlatStyle.Flat };
        basic.Click += (_, _) => { if (_timeline != null) _timeline.Visible = false; _scoreboard.Visible = true; };
        timeline.Click += (_, _) =>
        {
            if (_timeline == null) { _timeline = new TimelineView(_match) { Dock = DockStyle.Fill }; Controls.Add(_timeline); _timeline.BringToFront(); }
            _scoreboard.Visible = false; _timeline.Visible = true;
        };
        tabs.Controls.AddRange(new Control[] { basic,timeline });
        Controls.Add(_scoreboard); Controls.Add(tabs); Controls.Add(replay); Controls.Add(summary);
        _scoreboard.SizeChanged += (_, _) => ReflowScoreboard();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_started) return;
        _started = true;
        if (_match.AllParticipants == null || _match.AllParticipants.Count == 0)
        {
            _scoreboard.Controls.Add(new Label { Text = "这场比赛暂无可展示的详细数据。", AutoSize = true }); return;
        }
        var layout = new DetailedViewLayoutManager(_toolTip)
        {
            ResolvePlayerProfile = async id => await FormMain.Globals.lcuClient.GetGameNameBySummonerId(id)
        };
        layout.PlayerProfileRequested += id => PlayerProfileRequested?.Invoke(id);
        CreateDetailedPlayerViews(_scoreboard, _match, layout);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _toolTip.Dispose(); DisposeImagesInControls(Controls); }
        base.Dispose(disposing);
    }
}
