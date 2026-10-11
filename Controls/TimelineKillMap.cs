using League.Models;

namespace League.Controls;

internal sealed class TimelineKillMap : Control
{
    private readonly Bitmap? _map;
    private readonly int _mapId;
    private readonly ToolTip _tip = new();
    private readonly List<(PointF Point, TimelineEvent Event)> _points = new();
    public MatchTimeline? Data { get; set; }
    public long Until { get; set; } = long.MaxValue;

    public TimelineKillMap(int mapId)
    {
        DoubleBuffered = true;
        _mapId = mapId;
        using var stream = typeof(TimelineKillMap).Assembly.GetManifestResourceStream(
            $"League.Assets.Maps.map{mapId}.png"
        );
        if (stream != null)
        {
            using var image = new Bitmap(stream);
            _map = new Bitmap(image);
        }
        MouseMove += (_, e) =>
        {
            var hit = _points.LastOrDefault(p => Math.Abs(p.Point.X - e.X) < 6 && Math.Abs(p.Point.Y - e.Y) < 6);
            string text =
                hit.Event == null
                    ? ""
                    : $"{TimelineView.FormatTime(hit.Event.Timestamp)} {hit.Event.Actor} · {hit.Event.Description}";
            if (_tip.GetToolTip(this) != text)
            {
                _tip.SetToolTip(this, text);
            }
        };
    }

    public static PointF? Normalize(int mapId, int x, int y)
    {
        var bounds = mapId switch
        {
            11 => (-120d, -120d, 14870d, 14980d),
            12 => (-28d, -19d, 12849d, 12858d),
            _ => (0d, 0d, 0d, 0d),
        };
        if (bounds.Item3 == 0 || x < bounds.Item1 || x > bounds.Item3 || y < bounds.Item2 || y > bounds.Item4)
        {
            return null;
        }

        return new PointF(
            (float)((x - bounds.Item1) / (bounds.Item3 - bounds.Item1)),
            1 - (float)((y - bounds.Item2) / (bounds.Item4 - bounds.Item2))
        );
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _points.Clear();
        TextRenderer.DrawText(e.Graphics, "队伍击杀点 · 蓝 / 红为击杀方", Font, new Point(6, 4), ForeColor);
        if (_map == null)
        {
            TextRenderer.DrawText(e.Graphics, "此地图暂不支持击杀点展示", Font, new Point(6, 40), ForeColor);
            return;
        }
        int size = Math.Max(1, Math.Min(Width - 12, Height - 48));
        var area = new Rectangle((Width - size) / 2, 26, size, size);
        e.Graphics.DrawImage(_map, area);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int missing = 0;
        foreach (
            var ev in Data?.Events.Where(v => v.Kind == "CHAMPION_KILL" && v.Timestamp <= Until)
                ?? Enumerable.Empty<TimelineEvent>()
        )
        {
            if (ev.X is not int x || ev.Y is not int y || Normalize(_mapId, x, y) is not PointF point)
            {
                missing++;
                continue;
            }
            var at = new PointF(area.Left + point.X * size, area.Top + point.Y * size);
            using var brush = new SolidBrush(
                ev.Team == 100 ? Color.FromArgb(69, 174, 211)
                : ev.Team == 200 ? Color.FromArgb(220, 99, 110)
                : Color.Gray
            );
            e.Graphics.FillEllipse(brush, at.X - 3, at.Y - 3, 6, 6);
            _points.Add((at, ev));
        }
        TextRenderer.DrawText(
            e.Graphics,
            $"截至所选时间 {_points.Count} 次击杀" + (missing > 0 ? $" · {missing} 条坐标缺失" : ""),
            Font,
            new Point(6, area.Bottom + 3),
            ForeColor
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _map?.Dispose();
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }
}
