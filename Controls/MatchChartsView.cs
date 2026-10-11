using System.Drawing.Drawing2D;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public sealed class MatchChartsView : UserControl
{
    private readonly MatchMetricSelection _selection = new();
    private readonly List<(CheckBox Box, MatchMetricCatalog.Metric Metric)> _items = new();
    private readonly List<(CheckBox Box, string[] Keys)> _groups = new();
    private readonly ToolTip _selectionTip = new();
    private readonly ComparisonChart _chart;
    private bool _updating;

    public MatchChartsView(MatchInfo match)
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(36, 63, 98);
        var players = (match.AllParticipants ?? new()).OrderBy(p => (int?)p["teamId"] ?? 0).ToArray();
        _chart = new ComparisonChart(players, match.CurrPuuid, match.CurrSummonerId) { Dock = DockStyle.Fill };
        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 215,
            BackColor = Color.FromArgb(244, 247, 251),
        };
        var choices = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        foreach (var category in MatchChartMetricCatalog.Categories)
        {
            var metrics = category
                .Metrics.Where(m => players.Any(p => MatchMetricCatalog.Number(p, m.Key).HasValue))
                .ToArray();
            if (metrics.Length == 0)
                continue;
            var group = new CheckBox
            {
                Text = category.Title,
                Width = 190,
                Height = 28,
                AutoCheck = false,
                Margin = new Padding(8, 8, 0, 0),
            };
            var keys = metrics.Select(m => m.Key).ToArray();
            _groups.Add((group, keys));
            choices.Controls.Add(group);
            group.Click += (_, _) => UpdateSelection(keys, !keys.All(_selection.Contains), true);
            foreach (var metric in metrics)
            {
                var box = new CheckBox
                {
                    Text = metric.Title,
                    Width = 174,
                    Height = 26,
                    Margin = new Padding(20, 0, 0, 0),
                    AutoCheck = false,
                };
                _items.Add((box, metric));
                choices.Controls.Add(box);
                box.Click += (_, _) => UpdateSelection(new[] { metric.Key }, !_selection.Contains(metric.Key));
            }
        }
        sidebar.Controls.Add(choices);
        Controls.Add(_chart);
        Controls.Add(sidebar);
        var first = _items.FirstOrDefault(i => i.Metric.Key == "totalDamageDealtToChampions");
        if (first.Metric != null)
            _selection.Set(new[] { first.Metric.Key }, true);
        else if (_items.Count > 0)
            _selection.Set(new[] { _items[0].Metric.Key }, true);
        RefreshSelection();
    }

    private void UpdateSelection(string[] keys, bool selected, bool group = false)
    {
        if (_updating)
            return;
        if (!(group ? _selection.SetGroup(keys, selected) : _selection.Set(keys, selected)))
        {
            _selectionTip.Show("最多选择 6 个小项，请先取消部分小项", this, 8, 8, 2500);
            return;
        }
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        _updating = true;
        foreach (var (box, metric) in _items)
        {
            box.Checked = _selection.Contains(metric.Key);
            box.Enabled = box.Checked || _selection.Count < MatchMetricSelection.Limit;
        }
        foreach (var (box, keys) in _groups)
        {
            int count = keys.Count(_selection.Contains);
            box.CheckState =
                count == 0 ? CheckState.Unchecked
                : count == keys.Length ? CheckState.Checked
                : CheckState.Indeterminate;
        }
        _selectionTip.Hide(this);
        _chart.SetMetrics(_items.Where(i => _selection.Contains(i.Metric.Key)).Select(i => i.Metric).ToArray());
        _updating = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _selectionTip.Dispose();
        base.Dispose(disposing);
    }

    private sealed class ComparisonChart : ScrollableControl
    {
        private readonly JObject[] _players;
        private readonly bool[] _self;
        private readonly Image?[] _images;
        private readonly ToolTip _tip = new() { InitialDelay = 100, AutoPopDelay = 15000 };
        private readonly List<(Rectangle Bounds, string Text)> _hits = new();
        private MatchMetricCatalog.Metric[] _metrics = Array.Empty<MatchMetricCatalog.Metric>();
        private bool _loaded;
        private string? _hover;
        private const int RowHeight = 76;
        private const int PlotTop = 30;

        public ComparisonChart(JObject[] players, string? puuid, string? summonerId)
        {
            _players = players;
            _images = new Image?[players.Length];
            _self = players
                .Select(p =>
                    (!string.IsNullOrEmpty(puuid) && (string?)p["puuid"] == puuid)
                    || (!string.IsNullOrEmpty(summonerId) && p["summonerId"]?.ToString() == summonerId)
                )
                .ToArray();
            DoubleBuffered = true;
            AutoScroll = true;
            ResizeRedraw = true;
            BackColor = Color.White;
            AutoScrollMinSize = new Size(340, PlotTop + players.Length * RowHeight);
            MouseMove += (_, e) =>
            {
                var text = _hits.FirstOrDefault(h => h.Bounds.Contains(e.Location)).Text;
                if (_hover != text)
                {
                    _hover = text;
                    _tip.SetToolTip(this, text);
                }
            };
            MouseLeave += (_, _) =>
            {
                _tip.SetToolTip(this, null);
                _hover = null;
            };
            Scroll += (_, _) =>
            {
                _tip.SetToolTip(this, null);
                _hover = null;
                Invalidate();
            };
        }

        public void SetMetrics(MatchMetricCatalog.Metric[] metrics)
        {
            _metrics = metrics;
            _tip.SetToolTip(this, null);
            _hover = null;
            _hits.Clear();
            Invalidate();
        }

        protected override async void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible || _loaded)
                return;
            _loaded = true;
            for (int i = 0; i < _players.Length; i++)
            {
                if (IsDisposed)
                    return;
                try
                {
                    var image = await FormMain.Globals.resLoading.GetChampionIconAsync(
                        (int?)_players[i]["championId"] ?? 0
                    );
                    if (IsDisposed)
                    {
                        image?.Dispose();
                        return;
                    }
                    _images[i] = image;
                    Invalidate();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Chart portrait: {ex.GetType().Name}");
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _hits.Clear();
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_metrics.Length == 0)
            {
                TextRenderer.DrawText(
                    g,
                    "请在左侧选择统计小项",
                    Font,
                    ClientRectangle,
                    ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                );
                return;
            }
            int sx = AutoScrollPosition.X,
                sy = AutoScrollPosition.Y;
            int width = Math.Max(340, ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            int left = 66 + sx,
                plotWidth = width - 92;
            double max = Math.Max(
                1,
                _players
                    .SelectMany(p => _metrics.Select(m => (double)(MatchMetricCatalog.Number(p, m.Key) ?? 0)))
                    .DefaultIfEmpty(0)
                    .Max()
            );
            double step = Math.Pow(10, Math.Floor(Math.Log10(max)));
            double ceiling = Math.Ceiling(max / step) * step;
            // Fixed rows keep selection changes from rearranging the viewport.
            int top = PlotTop + sy;
            using var grid = new Pen(Color.FromArgb(226, 234, 243));
            for (int tick = 0; tick <= 4; tick++)
            {
                int x = left + (int)(plotWidth * tick / 4.0);
                g.DrawLine(grid, x, top - 5, x, top + _players.Length * RowHeight);
                TextRenderer.DrawText(
                    g,
                    (ceiling * tick / 4).ToString("N0"),
                    Font,
                    new Rectangle(x - 30, top - 23, 60, 18),
                    ForeColor,
                    TextFormatFlags.HorizontalCenter
                );
            }
            for (int i = 0; i < _players.Length; i++)
            {
                int y = top + i * RowHeight;
                if (y + RowHeight < 0 || y > ClientSize.Height)
                    continue;
                bool blue = (int?)_players[i]["teamId"] == 100;
                if (_self[i])
                {
                    using var bg = new SolidBrush(Color.FromArgb(239, 245, 254));
                    g.FillRectangle(bg, 0, y, width, RowHeight - 2);
                }
                var portrait = new Rectangle(16 + sx, y + 18, 34, 34);
                if (_images[i] is Image image)
                {
                    var state = g.Save();
                    using var path = new GraphicsPath();
                    path.AddEllipse(portrait);
                    g.SetClip(path);
                    g.DrawImage(image, portrait);
                    g.Restore(state);
                }
                else
                    TextRenderer.DrawText(
                        g,
                        (i + 1).ToString(),
                        Font,
                        portrait,
                        ForeColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    );
                using var ring = new Pen(blue ? Color.FromArgb(0, 161, 183) : Color.FromArgb(215, 72, 96), 2);
                g.DrawEllipse(ring, portrait);
                string player =
                    _players[i]["riotIdGameName"]?.ToString()
                    ?? _players[i]["summonerName"]?.ToString()
                    ?? $"玩家 {i + 1}";
                _hits.Add((portrait, player));
                for (int m = 0; m < _metrics.Length; m++)
                {
                    var metric = _metrics[m];
                    long? value = MatchMetricCatalog.Number(_players[i], metric.Key);
                    var lane = new Rectangle(left, y + 5 + m * 10, plotWidth, 9);
                    _hits.Add(
                        (
                            lane,
                            $"{player}\n{metric.Title}：{(value.HasValue ? value.Value.ToString("N0") : "—（未提供）")}"
                        )
                    );
                    if (value.HasValue)
                    {
                        int length = (int)(plotWidth * Math.Max(0, value.Value) / ceiling);
                        using var brush = new SolidBrush(
                            Color.FromArgb(
                                255 - m * 27,
                                blue ? Color.FromArgb(0, 161, 183) : Color.FromArgb(215, 72, 96)
                            )
                        );
                        if (length > 0)
                            g.FillRectangle(brush, lane.X, lane.Y, length, 6);
                    }
                    else
                        TextRenderer.DrawText(g, "—", Font, new Rectangle(lane.X, lane.Y - 4, 20, 16), Color.Gray);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tip.Dispose();
                foreach (var image in _images)
                    image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
