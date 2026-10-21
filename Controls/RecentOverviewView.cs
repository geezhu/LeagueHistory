using System.Drawing.Drawing2D;
using League.Models;

namespace League.Controls;

internal sealed class RecentOverviewView : Control
{
    private RecentOverview _data = RecentOverview.Build(Array.Empty<CareerGame>());
    private string _status = "正在加载最近 50 场…";
    private readonly ToolTip _tip = new();
    private readonly List<(Rectangle bounds, string text)> _hits = new();

    public RecentOverviewView()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        ForeColor = Theme.Heading;
        Font = new Font("Microsoft YaHei UI", 9f);
        Height = 170;
        SizeChanged += (_, _) =>
        {
            Height = Width < 540 ? 294 : 170;
            Invalidate();
        };
        MouseMove += (_, e) =>
        {
            var text = _hits.FirstOrDefault(h => h.bounds.Contains(e.Location)).text ?? _status;
            if (_tip.GetToolTip(this) != text)
            {
                _tip.SetToolTip(this, text);
            }
        };
    }

    public void SetGames(CareerGame[] games, string status)
    {
        _data = RecentOverview.Build(games);
        _status = status;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _hits.Clear();
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        TextRenderer.DrawText(
            graphics,
            "数据总览 · " + _status,
            Font,
            new Rectangle(16, 8, Width - 32, 25),
            ForeColor,
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine
        );
        int columns = Width < 540 ? 3 : 5;
        float cell = Math.Max(1, (Width - 24) / (float)columns);
        using var valueFont = new Font(Font.FontFamily, 16, FontStyle.Bold);
        for (int i = 0; i < 5; i++)
        {
            int size = (int)Math.Min(108, cell - 10);
            if (size < 35)
            {
                continue;
            }
            int x = 12 + (int)((i % columns) * cell + (cell - size) / 2);
            int y = 46 + (i / columns) * 124;
            DrawOverviewMetric(graphics, i, new Rectangle(x, y, size, size), valueFont);
        }
    }

    private void DrawOverviewMetric(Graphics g, int i, Rectangle rect, Font valueFont)
    {
        int x = rect.X,
            y = rect.Y,
            size = rect.Width;
        var color =
            i == 0
                ? (_data.UsesAramKda ? Color.FromArgb(132, 99, 190) : Color.FromArgb(181, 139, 43))
                : Color.FromArgb(31, 157, 145);
        double? rate = i == 0 ? null : _data.Modes[i - 1].Rate;
        DrawGauge(g, rect, color, i == 0 ? 344 : (float)(344 * (rate ?? 0)));
        string title = i == 0 ? "KDA" : _data.Modes[i - 1].Name;
        string value =
            i == 0 ? _data.Kda?.ToString("0.00") ?? "—"
            : rate.HasValue ? $"{rate:P0}"
            : "—";
        void Text(string text, Font font, int offset, int height) =>
            TextRenderer.DrawText(
                g,
                text,
                font,
                new Rectangle(x + 2, y + offset, size - 4, height),
                ForeColor,
                TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.SingleLine
            );
        Text(title, Font, size / 5, 24);
        Text(value, valueFont, size / 2 - 9, 30);
        if (i == 0)
        {
            var help = new Rectangle(x + size / 2 - 8, y + size * 3 / 4, 16, 16);
            using var helpPen = new Pen(color, 1);
            g.DrawEllipse(helpPen, help);
            using var helpFont = new Font(Font.FontFamily, 8f, FontStyle.Bold);
            TextRenderer.DrawText(
                g,
                "?",
                helpFont,
                help,
                color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
            );
        }
        else
        {
            Text($"{_data.Modes[i - 1].Games} 场", Font, size * 3 / 4, 21);
        }

        _hits.Add((rect, MetricTooltip(i, title)));
    }

    private static void DrawGauge(Graphics g, Rectangle rect, Color color, float sweep)
    {
        int x = rect.X,
            y = rect.Y,
            size = rect.Width;
        using var track = new Pen(Color.FromArgb(229, 237, 242), 2);
        g.DrawEllipse(track, rect);
        using var pen = new Pen(color, 2);
        g.DrawArc(pen, rect, -82, sweep);
        using var diamond = new SolidBrush(color);
        g.FillPolygon(
            diamond,
            new[]
            {
                new Point(x + size / 2, y - 5),
                new Point(x + size / 2 + 5, y),
                new Point(x + size / 2, y + 5),
                new Point(x + size / 2 - 5, y),
            }
        );
    }

    private string MetricTooltip(int i, string title)
    {
        string kdaSource = _data.UsesAramKda
            ? "无匹配/排位场次，使用极地大乱斗和海克斯大乱斗（紫色）"
            : "优先使用匹配、单双排和灵活排位（金色）";
        return i == 0
            ? $"{kdaSource}\n最近 50 场中参与计算：{_data.KdaGames} 场\n(总击杀+总助攻) / max(1,总死亡) × 3\n总死亡为 0 时按 1 计算；无场次或数据缺失显示 —"
            : $"{title}：{_data.Modes[i - 1].Wins} 胜 / {_data.Modes[i - 1].Games} 场\n统计范围：{_status}，含重开";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}
