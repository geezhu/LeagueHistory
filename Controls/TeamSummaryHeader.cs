using System.Drawing.Drawing2D;
using League.Models;

namespace League.Controls;

internal sealed class TeamSummaryHeader : Control
{
    private readonly TeamSummary _summary;
    private readonly string _title;
    private readonly ToolTip _tips;
    private readonly List<(Rectangle Bounds, string Tip)> _targets = new();
    private string? _currentTip;

    public TeamSummaryHeader(string title, Color color, TeamSummary summary, ToolTip tips)
    {
        _title = title;
        _summary = summary;
        _tips = tips;
        ForeColor = color;
        DoubleBuffered = true;
        Height = 32;
        Font = new Font("Microsoft YaHei UI", 9f);
        AccessibleName = title + "队伍统计";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        _targets.Clear();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int x = 0;
        void Text(string value, Color color)
        {
            int width = TextRenderer.MeasureText(value, Font).Width;
            TextRenderer.DrawText(
                g,
                value,
                Font,
                new Rectangle(x, 0, width, Height),
                color,
                TextFormatFlags.VerticalCenter
            );
            x += width + 10;
        }
        void Metric(string icon, string value, string tip)
        {
            int start = x;
            using var pen = new Pen(Color.FromArgb(122, 139, 155), 1.5f);
            DrawIcon(g, pen, icon, new Rectangle(x, 9, 14, 14));
            x += 17;
            Text(value, Color.FromArgb(87, 106, 126));
            _targets.Add((new Rectangle(start, 0, x - start, Height), tip));
        }
        static string Count(long? value) => value?.ToString() ?? "—";
        Text(_title, ForeColor);
        Metric(
            "kda",
            $"{Count(_summary.Kills)} / {Count(_summary.Deaths)} / {Count(_summary.Assists)}",
            "全队击杀 / 死亡 / 助攻；缺失数据以 — 表示"
        );
        Metric(
            "gold",
            _summary.Gold is long gold ? $"{gold / 1000d:0.##}k" : "—",
            $"全队累计获得金币：{Count(_summary.Gold)}"
        );
        foreach (var objective in _summary.Objectives)
            Metric(objective.Key, Count(objective.Count), objective.Name + "：" + Count(objective.Count));
    }

    private static void DrawIcon(Graphics g, Pen p, string kind, Rectangle r)
    {
        int x = r.X,
            y = r.Y;
        if (kind == "gold")
        {
            g.DrawEllipse(p, r);
            g.DrawEllipse(p, x + 4, y + 2, 6, 10);
        }
        else if (kind == "kda")
        {
            g.DrawLine(p, x, y, x + 14, y + 14);
            g.DrawLine(p, x + 14, y, x, y + 14);
            g.DrawLine(p, x, y + 10, x + 4, y + 14);
            g.DrawLine(p, x + 10, y + 14, x + 14, y + 10);
        }
        else if (kind == "tower")
        {
            g.DrawLines(
                p,
                new[]
                {
                    new Point(x, y),
                    new Point(x, y + 5),
                    new Point(x + 3, y + 5),
                    new Point(x + 3, y + 13),
                    new Point(x + 11, y + 13),
                    new Point(x + 11, y + 5),
                    new Point(x + 14, y + 5),
                    new Point(x + 14, y),
                }
            );
            g.DrawLine(p, x + 7, y, x + 7, y + 4);
        }
        else if (kind == "inhibitor")
            g.DrawPolygon(
                p,
                new[]
                {
                    new Point(x + 7, y),
                    new Point(x + 13, y + 7),
                    new Point(x + 7, y + 14),
                    new Point(x + 1, y + 7),
                }
            );
        else
        {
            g.DrawPolygon(
                p,
                new[]
                {
                    new Point(x, y),
                    new Point(x + 4, y + 4),
                    new Point(x + 7, y + 2),
                    new Point(x + 10, y + 4),
                    new Point(x + 14, y),
                    new Point(x + 12, y + 10),
                    new Point(x + 7, y + 14),
                    new Point(x + 2, y + 10),
                }
            );
            if (kind == "dragon")
                g.DrawLine(p, x, y + 7, x + 14, y + 7);
            else if (kind == "riftHerald")
                g.DrawEllipse(p, x + 5, y + 6, 4, 4);
            else
            {
                g.DrawLine(p, x + 4, y + 7, x + 5, y + 9);
                g.DrawLine(p, x + 9, y + 7, x + 10, y + 9);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var tip = _targets.FirstOrDefault(t => t.Bounds.Contains(e.Location)).Tip;
        if (tip == _currentTip)
            return;
        _currentTip = tip;
        _tips.SetToolTip(this, tip);
    }
}
