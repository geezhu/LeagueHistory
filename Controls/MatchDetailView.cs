using League.Models;
using League.UIState;

namespace League.Controls;

public partial class MatchDetailView : UserControl
{
    private readonly ToolTip _toolTip = new()
    {
        AutoPopDelay = 30000,
        InitialDelay = 100,
        ShowAlways = true,
    };
    private readonly Panel _scoreboard;
    private readonly MatchInfo _match;
    private bool _started;
    private Task? _preparation;
    private TimelineView? _timeline;
    private MatchStatisticsView? _statistics;
    private MatchChartsView? _charts;
    public event Action<string>? PlayerProfileRequested;
    public event Action? ReplayRequested;

    public MatchDetailView(MatchInfo match)
    {
        _match = match;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(244, 247, 251);
        var replay = new Button
        {
            Text = "查看回放",
            Width = 76,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
        };
        replay.Click += (_, _) => ReplayRequested?.Invoke();
        _scoreboard = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true };
        var basic = new Button
        {
            Text = "基础数据",
            Width = 76,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
        };
        var timeline = new Button
        {
            Text = "时间线",
            Width = 68,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
        };
        var statistics = new Button
        {
            Text = "统计",
            Width = 58,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
        };
        var charts = new Button
        {
            Text = "图表",
            Width = 58,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
        };
        void SelectTab(Button selected)
        {
            foreach (var button in new[] { basic, statistics, charts, timeline })
            {
                bool active = button == selected;
                button.BackColor = active ? Color.FromArgb(225, 237, 253) : BackColor;
                button.ForeColor = active ? Color.FromArgb(46, 103, 190) : Color.FromArgb(111, 123, 139);
                button.FlatAppearance.BorderColor = active ? Color.FromArgb(46, 103, 190) : BackColor;
            }
            _scoreboard.Visible = selected == basic;
            if (_timeline != null)
            {
                _timeline.Visible = selected == timeline;
            }

            if (_statistics != null)
            {
                _statistics.Visible = selected == statistics;
            }

            if (_charts != null)
            {
                _charts.Visible = selected == charts;
            }
        }
        basic.Click += (_, _) => SelectTab(basic);
        charts.Click += (_, _) =>
        {
            if (_charts == null)
            {
                _charts = new MatchChartsView(_match);
                Controls.Add(_charts);
                _charts.BringToFront();
            }
            SelectTab(charts);
        };
        statistics.Click += (_, _) =>
        {
            if (_statistics == null)
            {
                _statistics = new MatchStatisticsView(_match);
                Controls.Add(_statistics);
                _statistics.BringToFront();
            }
            SelectTab(statistics);
        };
        timeline.Click += (_, _) =>
        {
            if (_timeline == null)
            {
                _timeline = new TimelineView(_match) { Dock = DockStyle.Fill };
                Controls.Add(_timeline);
                _timeline.BringToFront();
            }
            _scoreboard.Visible = false;
            _timeline.Visible = true;
            SelectTab(timeline);
        };
        SelectTab(basic);
        Controls.Add(_scoreboard);
        Controls.Add(CreateMatchHeader(match, replay, basic, statistics, charts, timeline));
        _scoreboard.SizeChanged += (_, _) => ReflowScoreboard();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _ = PrepareAsync();
    }

    public Task PrepareAsync() => _preparation ??= PrepareCoreAsync();

    private async Task PrepareCoreAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (_match.AllParticipants == null || _match.AllParticipants.Count == 0)
        {
            _scoreboard.Controls.Add(new Label { Text = "这场比赛暂无可展示的详细数据。", AutoSize = true });
            return;
        }
        var layout = new DetailedViewLayoutManager(_toolTip)
        {
            ResolvePlayerProfile = async id => await FormMain.Globals.lcuClient.GetGameNameBySummonerId(id),
        };
        layout.PlayerProfileRequested += id => PlayerProfileRequested?.Invoke(id);
        await CreateDetailedPlayerViews(_scoreboard, _match, layout);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rankRequests.Cancel();
            _rankRequests.Dispose();
            foreach (var popup in _banPopups.ToArray())
            {
                foreach (var host in popup.Items.OfType<ToolStripControlHost>())
                {
                    DisposeImagesInControls(host.Control.Controls);
                }

                popup.Dispose();
            }
            _banPopups.Clear();
            _toolTip.Dispose();
            DisposeImagesInControls(Controls);
        }
        base.Dispose(disposing);
    }
}
