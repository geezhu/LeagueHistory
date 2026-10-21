using System.Drawing.Drawing2D;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed class MatchComparisonChart : ScrollableControl
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

    public MatchComparisonChart(JObject[] players, string? puuid, string? summonerId)
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
        {
            return;
        }

        _loaded = true;
        for (int i = 0; i < _players.Length; i++)
        {
            if (IsDisposed)
            {
                return;
            }

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
                AppLog.Shared.Warn(LogCategory.Ui, "Chart portrait", ex);
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _hits.Clear();
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (_metrics.Length == 0)
        {
            TextRenderer.DrawText(
                graphics,
                "请在左侧选择统计小项",
                Font,
                ClientRectangle,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
            return;
        }
        int width = Math.Max(340, ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        var plot = new Rectangle(
            66 + AutoScrollPosition.X,
            PlotTop + AutoScrollPosition.Y,
            width - 92,
            _players.Length * RowHeight
        );
        double ceiling = MetricCeiling();
        DrawScale(graphics, plot, ceiling);
        for (int i = 0; i < _players.Length; i++)
        {
            DrawPlayer(graphics, i, plot, width, ceiling);
        }
    }

    private double MetricCeiling()
    {
        double maximum = Math.Max(
            1,
            _players
                .SelectMany(p => _metrics.Select(m => (double)(MatchMetricCatalog.Number(p, m.Key) ?? 0)))
                .DefaultIfEmpty(0)
                .Max()
        );
        double step = Math.Pow(10, Math.Floor(Math.Log10(maximum)));
        return Math.Ceiling(maximum / step) * step;
    }

    private void DrawScale(Graphics graphics, Rectangle plot, double ceiling)
    {
        using var grid = new Pen(Color.FromArgb(226, 234, 243));
        for (int tick = 0; tick <= 4; tick++)
        {
            int x = plot.Left + (int)(plot.Width * tick / 4.0);
            graphics.DrawLine(grid, x, plot.Top - 5, x, plot.Bottom);
            TextRenderer.DrawText(
                graphics,
                (ceiling * tick / 4).ToString("N0"),
                Font,
                new Rectangle(x - 30, plot.Top - 23, 60, 18),
                ForeColor,
                TextFormatFlags.HorizontalCenter
            );
        }
    }

    private void DrawPlayer(Graphics graphics, int index, Rectangle plot, int width, double ceiling)
    {
        // Fixed rows keep selection changes from rearranging the viewport.
        int y = plot.Top + index * RowHeight;
        if (y + RowHeight < 0 || y > ClientSize.Height)
        {
            return;
        }
        Color color = (int?)_players[index]["teamId"] == 100 ? Theme.BlueTeam : Theme.RedTeam;
        if (_self[index])
        {
            using var background = new SolidBrush(Color.FromArgb(239, 245, 254));
            graphics.FillRectangle(background, 0, y, width, RowHeight - 2);
        }
        string name = DrawPortrait(graphics, index, y, color);
        DrawMetricLanes(graphics, index, name, plot, y, color, ceiling);
    }

    private string DrawPortrait(Graphics graphics, int index, int y, Color color)
    {
        var portrait = new Rectangle(16 + AutoScrollPosition.X, y + 18, 34, 34);
        if (_images[index] is Image image)
        {
            var state = graphics.Save();
            using var path = new GraphicsPath();
            path.AddEllipse(portrait);
            graphics.SetClip(path);
            graphics.DrawImage(image, portrait);
            graphics.Restore(state);
        }
        else
        {
            TextRenderer.DrawText(
                graphics,
                (index + 1).ToString(),
                Font,
                portrait,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
        }
        using var ring = new Pen(color, 2);
        graphics.DrawEllipse(ring, portrait);
        string name =
            _players[index]["riotIdGameName"]?.ToString()
            ?? _players[index]["summonerName"]?.ToString()
            ?? $"玩家 {index + 1}";
        _hits.Add((portrait, name));
        return name;
    }

    private void DrawMetricLanes(
        Graphics graphics,
        int index,
        string name,
        Rectangle plot,
        int y,
        Color color,
        double ceiling
    )
    {
        for (int m = 0; m < _metrics.Length; m++)
        {
            var metric = _metrics[m];
            long? value = MatchMetricCatalog.Number(_players[index], metric.Key);
            var lane = new Rectangle(plot.Left, y + 5 + m * 10, plot.Width, 9);
            _hits.Add(
                (lane, $"{name}\n{metric.Title}：{(value.HasValue ? value.Value.ToString("N0") : "—（未提供）")}")
            );
            if (value.HasValue)
            {
                int length = (int)(plot.Width * Math.Max(0, value.Value) / ceiling);
                using var brush = new SolidBrush(Color.FromArgb(255 - m * 27, color));
                if (length > 0)
                {
                    graphics.FillRectangle(brush, lane.X, lane.Y, length, 6);
                }
            }
            else
            {
                TextRenderer.DrawText(graphics, "—", Font, new Rectangle(lane.X, lane.Y - 4, 20, 16), Color.Gray);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
            foreach (var image in _images)
            {
                image?.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
