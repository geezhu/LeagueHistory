using System.Drawing.Drawing2D;
using League.Models;

namespace League.Controls;

internal sealed class TimelineGoldChart : Control
{
    private readonly record struct Plot(Rectangle Area, double Maximum, long Duration)
    {
        public float Zero => Area.Top + Area.Height / 2f;

        public PointF Point(GoldFrame frame, double gold) =>
            new(
                Area.Left + Area.Width * frame.Timestamp / (float)Duration,
                Zero - Area.Height * (float)(gold / Maximum) / 2
            );
    }

    public MatchTimeline? Data { get; set; }
    public int Selected { get; set; }

    public TimelineGoldChart() => DoubleBuffered = true;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Data == null || Data.Frames.Count == 0)
        {
            return;
        }
        var plot = new Plot(
            new Rectangle(58, 26, Math.Max(1, Width - 82), Math.Max(1, Height - 58)),
            Math.Max(100, Math.Ceiling(Data.Frames.Max(f => Math.Abs(Data.GoldDifference(f) ?? 0)) / 100) * 100),
            Math.Max(1, Data.Frames.Last().Timestamp)
        );
        DrawScale(e.Graphics, plot);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        DrawTrend(e.Graphics, plot, Data);
        DrawSelection(e.Graphics, plot, Data);
    }

    private void DrawScale(Graphics graphics, Plot plot)
    {
        using var grid = new Pen(Color.FromArgb(231, 237, 245));
        for (int i = 0; i <= 4; i++)
        {
            int y = plot.Area.Bottom - plot.Area.Height * i / 4;
            graphics.DrawLine(grid, plot.Area.Left, y, plot.Area.Right, y);
            TextRenderer.DrawText(
                graphics,
                $"{plot.Maximum * (i - 2) / 2 / 1000d:0.#}k",
                Font,
                new Point(5, y - 8),
                ForeColor
            );
        }
    }

    private static void DrawTrend(Graphics graphics, Plot plot, MatchTimeline data)
    {
        for (int i = 1; i < data.Frames.Count; i++)
        {
            var a = data.Frames[i - 1];
            var b = data.Frames[i];
            if (data.FrameInterval is > 0 && b.Timestamp - a.Timestamp > data.FrameInterval.Value * 1.5)
            {
                continue;
            }
            if (data.GoldDifference(a) is not double av || data.GoldDifference(b) is not double bv)
            {
                continue;
            }
            var ap = plot.Point(a, av);
            var bp = plot.Point(b, bv);
            if (av * bv < 0)
            {
                var crossing = new PointF(
                    ap.X + (bp.X - ap.X) * (float)(Math.Abs(av) / (Math.Abs(av) + Math.Abs(bv))),
                    plot.Zero
                );
                DrawSegment(graphics, plot.Zero, ap, crossing, av > 0);
                DrawSegment(graphics, plot.Zero, crossing, bp, bv > 0);
            }
            else
            {
                DrawSegment(graphics, plot.Zero, ap, bp, av + bv >= 0);
            }
        }
    }

    private static void DrawSegment(Graphics graphics, float zero, PointF a, PointF b, bool positive)
    {
        var color = positive ? Color.FromArgb(46, 153, 190) : Theme.Loss;
        using var fill = new SolidBrush(Color.FromArgb(65, color));
        using var pen = new Pen(color, 2);
        graphics.FillPolygon(fill, new[] { new PointF(a.X, zero), a, b, new PointF(b.X, zero) });
        graphics.DrawLine(pen, a, b);
    }

    private void DrawSelection(Graphics graphics, Plot plot, MatchTimeline data)
    {
        using var cursor = new Pen(Theme.Muted) { DashStyle = DashStyle.Dash };
        float x = plot.Point(data.Frames[Math.Clamp(Selected, 0, data.Frames.Count - 1)], 0).X;
        graphics.DrawLine(cursor, x, plot.Area.Top, x, plot.Area.Bottom);
        TextRenderer.DrawText(graphics, "人均经济差 · 蓝正 / 红负", Font, new Point(plot.Area.Left, 3), ForeColor);
        TextRenderer.DrawText(graphics, "00:00", Font, new Point(plot.Area.Left, plot.Area.Bottom + 6), ForeColor);
        TextRenderer.DrawText(
            graphics,
            TimelineView.FormatTime(plot.Duration),
            Font,
            new Point(plot.Area.Right - 45, plot.Area.Bottom + 6),
            ForeColor
        );
    }
}
