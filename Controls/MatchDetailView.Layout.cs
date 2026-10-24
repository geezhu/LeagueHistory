using League.Services;
using League.UIState;

namespace League.Controls;

public partial class MatchDetailView
{
    private readonly Dictionary<Control, Rectangle> _originalBounds = new();
    private readonly Dictionary<Control, string> _originalTexts = new();
    private readonly Dictionary<Panel, bool> _secondaryMetrics = new();
    private bool _reflowing;
    private bool _buildingScoreboard;
    private bool? _hasScoreColumn;

    /// <summary>ARAM: Mayhem boards add the estimated score column right of the metrics.</summary>
    private bool HasScoreColumn => _hasScoreColumn ??= AramMayhemScoreEstimator.Shared.Estimate(_match) != null;

    // A compact board needs every pixel for its three metrics, so the score column narrows there.
    private int ScoreReserve(bool compact) =>
        !HasScoreColumn ? 0
        : compact ? DetailedViewLayoutManager.CompactScoreColumnReserve
        : DetailedViewLayoutManager.ScoreColumnReserve;

    /// <summary>The width that fits every column; narrower boards page their metrics in threes.</summary>
    private int WideBoardWidth => 1120 + ScoreReserve(compact: false);

    private static bool IsScoreColumn(Control control) => control.Name is "ScoreEstimate" or "ScoreHeader";

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
            bool compact = width < WideBoardWidth;
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
        // Left of the score column, which stays put while the metrics page.
        pageButton.SetBounds(width - 30 - ScoreReserve(compact), 32, 28, 28);
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

    /// <summary>
    /// Where a compact page puts its first, second or third metric within the space left of the page button. KDA
    /// ("15 / 7 / 50") is the widest figure on either page, so the first column takes two fifths.
    /// </summary>
    private static (int Left, int Width) MetricSlot(int index, int available)
    {
        int first = available * 2 / 5;
        int other = (available - first) / 2;
        return index == 0 ? (520, first) : (520 + first + (index - 1) * other, other);
    }

    private int MetricSpace(int width) => width - 550 - ScoreReserve(compact: true);

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
        int available = MetricSpace(width);
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
            if (IsScoreColumn(header))
            {
                header.Visible = true;
                PlaceScoreColumn(header, width, compact, bounds);
                continue;
            }
            header.Visible = !compact || bounds.X < 592 || MetricVisible(bounds.X, compact, secondary);
            if (!compact)
            {
                continue;
            }
            if (bounds.X >= 592)
            {
                var (left, slot) = MetricSlot(MetricIndex(bounds.X), available);
                header.SetBounds(left, bounds.Y, slot, bounds.Height);
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
        int available = MetricSpace(width);
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
                if (IsScoreColumn(cell))
                {
                    PlaceScoreColumn(cell, width, compact, bounds);
                }
                else if (compact)
                {
                    ReflowCompactScoreboardCell(row, cell, bounds, available, secondary);
                }
            }
            rowY += row.Height;
        }
        section.Height = rowY;
    }

    // A wide board keeps the column where it was built; a compact one pins its narrower form to the right edge.
    private void PlaceScoreColumn(Control control, int width, bool compact, Rectangle bounds)
    {
        if (compact)
        {
            int reserve = ScoreReserve(compact: true);
            control.SetBounds(width - reserve + 2, bounds.Y, reserve - 4, bounds.Height);
        }
    }

    private static void ReflowCompactScoreboardCell(
        Panel row,
        Control cell,
        Rectangle bounds,
        int available,
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
            var (left, slot) = MetricSlot(MetricIndex(bounds.X), available);
            cell.Left = left;
            cell.Width = Math.Min(bounds.Width, slot - 4);
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
                if (!_originalBounds.TryGetValue(control, out var bounds) || bounds.X < 592 || IsScoreColumn(control))
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
