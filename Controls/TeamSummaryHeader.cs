using System.Drawing.Drawing2D;
using League.Models;
using League.UIHelpers;

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
        // The containing detail view owns the shared tooltip and its lifetime.
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
            WeGameTeamIcons.Draw(g, icon, Color.FromArgb(122, 139, 155), new Rectangle(x, 9, 14, 14));
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
        {
            Metric(objective.Key, Count(objective.Count), objective.Name + "：" + Count(objective.Count));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var tip = _targets.FirstOrDefault(t => t.Bounds.Contains(e.Location)).Tip;
        if (tip == _currentTip)
        {
            return;
        }

        _currentTip = tip;
        _tips.SetToolTip(this, tip);
    }
}
