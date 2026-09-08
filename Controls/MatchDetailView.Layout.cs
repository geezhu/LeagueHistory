namespace League.Controls;

public partial class MatchDetailView
{
    private readonly Dictionary<Control, Rectangle> _originalBounds = new();
    private readonly Dictionary<Control, string> _originalTexts = new();
    private bool _reflowing;
    private bool _buildingScoreboard;

    private void ReflowScoreboard()
    {
        if (_reflowing || _buildingScoreboard || IsDisposed) return;
        _reflowing = true;
        _scoreboard.SuspendLayout();
        try
        {
            int width = Math.Max(680, _scoreboard.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 28);
            bool compact = width < 1120;
            int MapX(int x) => x switch { 592 => 250, 686 => 350, 780 => 440, 880 => 530, 958 => 610, 1026 => 680, _ => x };
            int sectionY = 8;
            foreach (var section in _scoreboard.Controls.OfType<Panel>().Where(p => p.Name != "MatchHighlights"))
            {
                section.Width = width;
                section.Top = sectionY + _scoreboard.AutoScrollPosition.Y;
                foreach (var header in section.Controls.OfType<Button>())
                {
                    if (!_originalBounds.TryGetValue(header, out var bounds)) _originalBounds[header] = bounds = header.Bounds;
                    header.Visible = !compact || (bounds.X != 242 && bounds.X != 436 && (bounds.X != 1026 || width >= 780));
                    header.Left = compact ? MapX(bounds.X) : bounds.X;
                }
                foreach (var title in section.Controls.OfType<Label>()) title.Width = width - 24;
                int rowY = 64;
                foreach (var row in section.Controls.OfType<Panel>().OrderBy(r => r.Top))
                {
                    row.SetBounds(0,rowY,width,compact ? 76 : 44);
                    foreach (Control cell in row.Controls)
                    {
                        if (!_originalBounds.TryGetValue(cell,out var bounds)) _originalBounds[cell] = bounds = cell.Bounds;
                        if (!_originalTexts.TryGetValue(cell,out var text)) _originalTexts[cell] = text = cell.Text;
                        cell.Bounds = bounds;
                        cell.Text = text;
                        cell.Visible = true;
                        if (!compact) continue;
                        if (bounds.X >= 242 && bounds.X < 592)
                            cell.Location = new Point(bounds.X - 184, bounds.Y + 30);
                        else if (bounds.X >= 592)
                        {
                            cell.Left = MapX(bounds.X);
                            if (bounds.X == 1026 && width < 780)
                            {
                                cell.SetBounds(530,44,140,26);
                                cell.Text = "承伤 " + text;
                            }
                        }
                        if (bounds.Width == 3 && bounds.X == 0) cell.Height = row.Height;
                    }
                    rowY += row.Height;
                }
                section.Height = rowY;
                sectionY += rowY + 10;
            }
            if (_scoreboard.Controls["MatchHighlights"] is Control highlights)
                highlights.SetBounds(12,sectionY + _scoreboard.AutoScrollPosition.Y,width,86);
        }
        finally { _scoreboard.ResumeLayout(true); _reflowing = false; }
    }
}
