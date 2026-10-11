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
                if (section.Controls["MetricPage"] is not Button pageButton)
                {
                    pageButton = new Button
                    {
                        Name = "MetricPage",
                        Width = 28,
                        Height = 28,
                        FlatStyle = FlatStyle.Flat,
                        Text = "\u203a",
                        Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
                        ForeColor = Color.FromArgb(46, 103, 190),
                        Cursor = Cursors.Hand,
                    };
                    pageButton.FlatAppearance.BorderSize = 0;
                    pageButton.Click += (_, _) => ToggleMetricPage(section);
                    section.Controls.Add(pageButton);
                }
                bool secondary = _secondaryMetrics.GetValueOrDefault(section);
                pageButton.Visible = compact;
                pageButton.SetBounds(width - 30, 32, 28, 28);
                pageButton.Text = secondary ? "\u2039" : "\u203a";
                _toolTip.SetToolTip(
                    pageButton,
                    secondary
                        ? "\u67e5\u770b KDA / \u7ecf\u6d4e / \u9020\u6210\u4f24\u5bb3"
                        : "\u67e5\u770b\u53c2\u56e2\u7387 / \u8865\u5200 / \u627f\u53d7\u4f24\u5bb3"
                );
                int metricWidth = (width - 550) / 3;
                int MetricIndex(int x) =>
                    x switch
                    {
                        592 or 880 => 0,
                        686 or 958 => 1,
                        780 or 1026 => 2,
                        _ => -1,
                    };
                bool MetricVisible(int x) => !compact || (secondary ? x >= 880 : x < 880);

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
                    if (!_originalBounds.TryGetValue(header, out var bounds))
                    {
                        _originalBounds[header] = bounds = header.Bounds;
                    }

                    header.Bounds = bounds;
                    if (compact)
                    {
                        header.Visible = bounds.X < 592 || MetricVisible(bounds.X);
                        if (bounds.X >= 592)
                        {
                            header.SetBounds(
                                520 + MetricIndex(bounds.X) * metricWidth,
                                bounds.Y,
                                metricWidth,
                                bounds.Height
                            );
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
                    else
                    {
                        header.Visible = true;
                    }
                }
                foreach (var title in section.Controls.OfType<Label>())
                {
                    title.Width = width - (section.Controls.ContainsKey("TeamBans") ? 100 : 24);
                }

                foreach (var title in section.Controls.OfType<TeamSummaryHeader>())
                {
                    title.Width = width - (section.Controls.ContainsKey("TeamBans") ? 100 : 24);
                }

                int rowY = 64;
                foreach (var row in section.Controls.OfType<Panel>().OrderBy(r => r.Top))
                {
                    row.SetBounds(0, rowY, width, 44);
                    foreach (Control cell in row.Controls)
                    {
                        if (!_originalBounds.TryGetValue(cell, out var bounds))
                        {
                            _originalBounds[cell] = bounds = cell.Bounds;
                        }

                        if (!_originalTexts.TryGetValue(cell, out var text))
                        {
                            _originalTexts[cell] = text = cell.Text;
                        }

                        cell.Bounds = bounds;
                        cell.Text = text;
                        cell.Visible = true;
                        if (!compact)
                        {
                            continue;
                        }

                        if (cell is LinkLabel)
                        {
                            cell.Width = Math.Min(
                                bounds.Width,
                                row.Controls.ContainsKey("PlayerRankEmblem") ? 84 : 118
                            );
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
                            cell.Visible = MetricVisible(bounds.X);
                            cell.Left = 520 + MetricIndex(bounds.X) * metricWidth;
                            cell.Width = Math.Min(bounds.Width, metricWidth - 4);
                        }
                        if (bounds.Width == 3 && bounds.X == 0)
                        {
                            cell.Height = row.Height;
                        }
                    }
                    rowY += row.Height;
                }
                section.Height = rowY;
                sectionY += rowY + 10;
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
            button.Text = secondary ? "‹" : "›";
            _toolTip.SetToolTip(button, secondary ? "查看 KDA / 经济 / 造成伤害" : "查看参团率 / 补刀 / 承受伤害");
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
