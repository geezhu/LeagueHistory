namespace League.Controls;

public partial class MatchDetailView
{
    private readonly Dictionary<Control, Rectangle> _originalBounds = new();
    private readonly Dictionary<Control, string> _originalTexts = new();
    private readonly Dictionary<Panel, bool> _secondaryMetrics = new();
    private bool _reflowing;
    private bool _buildingScoreboard;

    private void ReflowScoreboard()
    {
        if (_reflowing || _buildingScoreboard || IsDisposed)
        {
            return;
        }

        _reflowing = true;
        _scoreboard.SuspendLayout();
        try
        {
            int width = Math.Max(800, _scoreboard.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 28);
            bool compact = width < 1120;
            int sectionY = 8;
            foreach (var section in _scoreboard.Controls.OfType<Panel>().Where(p => p.Name != "MatchHighlights"))
            {
                section.Width = width;
                section.Top = sectionY + _scoreboard.AutoScrollPosition.Y;
                bool secondary = _secondaryMetrics.GetValueOrDefault(section);
                ConfigureMetricPage(section, width, compact, secondary);
                ReflowScoreboardHeaders(section, width, compact, secondary);
                ReflowScoreboardRows(section, width, compact, secondary);
                sectionY += section.Height + 10;
            }
            if (_scoreboard.Controls["MatchHighlights"] is Control highlights)
            {
                highlights.SetBounds(12, sectionY + _scoreboard.AutoScrollPosition.Y, width, 86);
            }
        }
        finally
        {
            _scoreboard.ResumeLayout(true);
            _reflowing = false;
        }
    }

    private void ConfigureMetricPage(Panel section, int width, bool compact, bool secondary)
    {
        if (section.Controls["MetricPage"] is not Button pageButton)
        {
            pageButton = new Button
            {
                Name = "MetricPage",
                Width = 28,
                Height = 28,
                Text = "›",
                Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
            };
            ButtonStyles.Ghost(pageButton);
            pageButton.Click += (_, _) => ToggleMetricPage(section);
            section.Controls.Add(pageButton);
        }
        pageButton.Visible = compact;
        pageButton.SetBounds(width - 30, 32, 28, 28);
        UpdateMetricPageText(pageButton, secondary);
    }

    private void UpdateMetricPageText(Button button, bool secondary)
    {
        button.Text = secondary ? "‹" : "›";
        _toolTip.SetToolTip(button, secondary ? "查看 KDA / 经济 / 造成伤害" : "查看参团率 / 补刀 / 承受伤害");
    }

    private static int MetricIndex(int x) =>
        x switch
        {
            592 or 880 => 0,
            686 or 958 => 1,
            780 or 1026 => 2,
            _ => -1,
        };

    private static bool MetricVisible(int x, bool compact, bool secondary) =>
        !compact || (secondary ? x >= 880 : x < 880);

    private Rectangle RestoreScoreboardBounds(Control control)
    {
        if (!_originalBounds.TryGetValue(control, out var bounds))
        {
            _originalBounds[control] = bounds = control.Bounds;
        }
        control.Bounds = bounds;
        return bounds;
    }

    private void ReflowScoreboardHeaders(Panel section, int width, bool compact, bool secondary)
    {
        int metricWidth = (width - 550) / 3;
        foreach (var header in section.Controls.OfType<Button>())
        {
            if (header.Name == "MetricPage")
            {
                continue;
            }
            if (header.Name == "TeamBans")
            {
                header.Left = width - 76;
                continue;
            }

            var bounds = RestoreScoreboardBounds(header);
            header.Visible = !compact || bounds.X < 592 || MetricVisible(bounds.X, compact, secondary);
            if (!compact)
            {
                continue;
            }
            if (bounds.X >= 592)
            {
                header.SetBounds(520 + MetricIndex(bounds.X) * metricWidth, bounds.Y, metricWidth, bounds.Height);
            }
            else if (bounds.X >= 242)
            {
                header.Left = bounds.X - 64;
            }
            else if (bounds.X == 58)
            {
                header.Width = 116;
            }
        }

        int titleWidth = width - (section.Controls.ContainsKey("TeamBans") ? 100 : 24);
        foreach (var title in section.Controls.OfType<Label>())
        {
            title.Width = titleWidth;
        }
        foreach (var title in section.Controls.OfType<TeamSummaryHeader>())
        {
            title.Width = titleWidth;
        }
    }

    private void ReflowScoreboardRows(Panel section, int width, bool compact, bool secondary)
    {
        int rowY = 64;
        int metricWidth = (width - 550) / 3;
        foreach (var row in section.Controls.OfType<Panel>().OrderBy(r => r.Top))
        {
            row.SetBounds(0, rowY, width, 44);
            foreach (Control cell in row.Controls)
            {
                var bounds = RestoreScoreboardBounds(cell);
                if (!_originalTexts.TryGetValue(cell, out var text))
                {
                    _originalTexts[cell] = text = cell.Text;
                }
                cell.Text = text;
                cell.Visible = true;
                if (compact)
                {
                    ReflowCompactScoreboardCell(row, cell, bounds, metricWidth, secondary);
                }
            }
            rowY += row.Height;
        }
        section.Height = rowY;
    }

    private static void ReflowCompactScoreboardCell(
        Panel row,
        Control cell,
        Rectangle bounds,
        int metricWidth,
        bool secondary
    )
    {
        if (cell is LinkLabel)
        {
            cell.Width = Math.Min(bounds.Width, row.Controls.ContainsKey("PlayerRankEmblem") ? 84 : 118);
        }
        else if (cell.Name == "PlayerRankEmblem")
        {
            cell.SetBounds(146, 1, 28, 22);
        }
        else if (cell.Name.StartsWith("Honor_", StringComparison.Ordinal))
        {
            cell.Left = 58 + (bounds.X - 58) / 16 * 14;
        }
        else if (bounds.X >= 242 && bounds.X < 592)
        {
            cell.Left = bounds.X - 64;
        }
        else if (bounds.X >= 592)
        {
            cell.Visible = MetricVisible(bounds.X, true, secondary);
            cell.Left = 520 + MetricIndex(bounds.X) * metricWidth;
            cell.Width = Math.Min(bounds.Width, metricWidth - 4);
        }
        if (bounds.Width == 3 && bounds.X == 0)
        {
            cell.Height = row.Height;
        }
    }

    private void ToggleMetricPage(Panel section)
    {
        if (section.IsDisposed || section.Controls["MetricPage"] is not Button button || !button.Visible)
        {
            return;
        }

        bool secondary = !_secondaryMetrics.GetValueOrDefault(section);
        _secondaryMetrics[section] = secondary;
        // Both pages already occupy their final positions. Only this team's visibility changes.
        var rows = section.Controls.OfType<Panel>().ToArray();
        section.SuspendLayout();
        foreach (var row in rows)
        {
            row.SuspendLayout();
        }

        try
        {
            foreach (
                var control in section
                    .Controls.Cast<Control>()
                    .Concat(rows.SelectMany(row => row.Controls.Cast<Control>()))
            )
            {
                if (!_originalBounds.TryGetValue(control, out var bounds) || bounds.X < 592)
                {
                    continue;
                }

                bool visible = secondary ? bounds.X >= 880 : bounds.X < 880;
                if (control.Visible != visible)
                {
                    control.Visible = visible;
                }
            }
            UpdateMetricPageText(button, secondary);
        }
        finally
        {
            foreach (var row in rows)
            {
                row.ResumeLayout(false);
            }
            section.ResumeLayout(false);
        }
    }
}
