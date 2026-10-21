namespace League.Controls;

/// <summary>
/// A vertical page list for a <see cref="TabControl"/> whose own headers are hidden: one row per page, the selected
/// row marked with the accent bar the main navigation rail uses. Pages added later appear without extra wiring.
/// </summary>
public sealed class SettingsNavigation : Control
{
    private const int RowHeight = 40;
    private const int TopPadding = 16;
    private const int SelectionBarWidth = 3;
    private readonly TabControl _tabs;
    private readonly Font _selectedFont;
    private int _hoverIndex = -1;

    public SettingsNavigation(TabControl tabs)
    {
        _tabs = tabs;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
            true
        );
        BackColor = Theme.Surface;
        ForeColor = Theme.Ink;
        Font = tabs.Font;
        _selectedFont = new Font(Font, FontStyle.Bold);
        Width = 168;
        _tabs.SelectedIndexChanged += OnPagesChanged;
        _tabs.ControlAdded += OnPagesChanged;
        _tabs.ControlRemoved += OnPagesChanged;
    }

    /// <summary>The row bounds of a page, for layout checks and hit testing.</summary>
    public Rectangle RowBounds(int index) => new(0, TopPadding + index * RowHeight, Width - 1, RowHeight);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        for (int i = 0; i < _tabs.TabPages.Count; i++)
        {
            DrawRow(e.Graphics, i);
        }

        using var divider = new Pen(Theme.Divider);
        e.Graphics.DrawLine(divider, Width - 1, 0, Width - 1, Height);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHoverIndex(IndexAt(e.Location));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHoverIndex(-1);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int index = IndexAt(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0)
        {
            _tabs.SelectedIndex = index;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tabs.SelectedIndexChanged -= OnPagesChanged;
            _tabs.ControlAdded -= OnPagesChanged;
            _tabs.ControlRemoved -= OnPagesChanged;
            _selectedFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrawRow(Graphics g, int index)
    {
        var row = RowBounds(index);
        bool selected = index == _tabs.SelectedIndex;
        if (selected || index == _hoverIndex)
        {
            using var fill = new SolidBrush(selected ? Theme.AccentSoft : Theme.Canvas);
            g.FillRectangle(fill, row);
        }

        if (selected)
        {
            using var bar = new SolidBrush(Theme.Accent);
            g.FillRectangle(bar, row.X, row.Y + 8, SelectionBarWidth, row.Height - 16);
        }

        TextRenderer.DrawText(
            g,
            _tabs.TabPages[index].Text,
            selected ? _selectedFont : Font,
            new Rectangle(row.X + 24, row.Y, row.Width - 32, row.Height),
            selected ? Theme.Accent : ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
        );
    }

    private int IndexAt(Point location)
    {
        for (int i = 0; i < _tabs.TabPages.Count; i++)
        {
            if (RowBounds(i).Contains(location))
            {
                return i;
            }
        }

        return -1;
    }

    private void SetHoverIndex(int index)
    {
        if (_hoverIndex == index)
        {
            return;
        }

        _hoverIndex = index;
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    private void OnPagesChanged(object? sender, EventArgs e) => Invalidate();
}
