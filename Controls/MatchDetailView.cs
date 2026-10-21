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

    private enum DetailTab
    {
        Scoreboard,
        Statistics,
        Charts,
        Timeline,
    }

    private readonly Dictionary<DetailTab, Button> _tabButtons = new();

    public MatchDetailView(MatchInfo match)
    {
        _match = match;
        DoubleBuffered = true;
        BackColor = Theme.Canvas;
        _scoreboard = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true };
        var actions = CreateNavigation();
        Controls.Add(_scoreboard);
        Controls.Add(CreateMatchHeader(match, actions));
        _scoreboard.SizeChanged += (_, _) => ReflowScoreboard();
    }

    private Button[] CreateNavigation()
    {
        var replay = NavigationButton("查看回放", 76);
        replay.Click += (_, _) => ReplayRequested?.Invoke();
        foreach (
            var (tab, text, width) in new[]
            {
                (DetailTab.Scoreboard, "基础数据", 76),
                (DetailTab.Statistics, "统计", 58),
                (DetailTab.Charts, "图表", 58),
                (DetailTab.Timeline, "时间线", 68),
            }
        )
        {
            var button = NavigationButton(text, width);
            _tabButtons[tab] = button;
            button.Click += (_, _) => SelectTab(tab);
        }
        SelectTab(DetailTab.Scoreboard);
        return new[] { replay }.Concat(_tabButtons.Values).ToArray();
    }

    private static Button NavigationButton(string text, int width) =>
        new()
        {
            Text = text,
            Width = width,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
        };

    private void SelectTab(DetailTab selected)
    {
        EnsureTabContent(selected);
        foreach (var (tab, button) in _tabButtons)
        {
            bool active = tab == selected;
            button.BackColor = active ? Theme.AccentSoftHover : BackColor;
            button.ForeColor = active ? Theme.Accent : Theme.Muted;
            button.FlatAppearance.BorderColor = active ? Theme.Accent : BackColor;
        }
        _scoreboard.Visible = selected == DetailTab.Scoreboard;
        if (_timeline != null)
        {
            _timeline.Visible = selected == DetailTab.Timeline;
        }
        if (_statistics != null)
        {
            _statistics.Visible = selected == DetailTab.Statistics;
        }
        if (_charts != null)
        {
            _charts.Visible = selected == DetailTab.Charts;
        }
    }

    private void EnsureTabContent(DetailTab selected)
    {
        Control? content = null;
        switch (selected)
        {
            case DetailTab.Charts when _charts == null:
                content = _charts = new MatchChartsView(_match);
                break;
            case DetailTab.Statistics when _statistics == null:
                content = _statistics = new MatchStatisticsView(_match);
                break;
            case DetailTab.Timeline when _timeline == null:
                content = _timeline = new TimelineView(_match) { Dock = DockStyle.Fill };
                break;
        }
        if (content != null)
        {
            Controls.Add(content);
            content.BringToFront();
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _ = PrepareAsync();
    }

    public Task PrepareAsync() => IsDisposed ? Task.CompletedTask : _preparation ??= PrepareCoreAsync();

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
        var token = _requestCancellation.Token;
        var layout = new DetailedViewLayoutManager(_toolTip)
        {
            ResolvePlayerProfile = async id =>
            {
                token.ThrowIfCancellationRequested();
                var request = FormMain.Globals.lcuClient.GetGameNameBySummonerId(id, token);
                TrackRequest(request);
                return await request;
            },
        };
        layout.PlayerProfileRequested += id => PlayerProfileRequested?.Invoke(id);
        await CreateDetailedPlayerViews(_scoreboard, _match, layout);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RetireRequests();
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
