namespace League.Controls;

internal static class PlayerTeamsLayout
{
    public static void Show(TableLayoutPanel table, Control parent)
    {
        Control surface = table.Parent is Panel panel && panel.Name == "PlayerTeamsSurface" ? panel : table;
        if (surface.Parent != parent) parent.Controls.Add(surface);
        surface.Dock = DockStyle.Fill;
        surface.Visible = true;
        table.Visible = true;
        surface.BringToFront();
    }

    public static void Hide(TableLayoutPanel table)
    {
        if (table.Parent is Panel panel && panel.Name == "PlayerTeamsSurface") panel.Visible = false;
        table.Visible = false;
    }
    public static void Attach(TableLayoutPanel table)
    {
        var parent = table.Parent;
        if (parent == null) return;
        var host = new Panel { Name = "PlayerTeamsSurface", Dock = DockStyle.Fill,
            AutoScroll = true, BackColor = Color.FromArgb(245,247,250) };
        parent.Controls.Add(host);
        host.Controls.Add(table);
        table.Dock = DockStyle.None;
        table.AutoScroll = false;
        table.ColumnCount = 5; table.RowCount = 2;
        table.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
        table.ColumnStyles.Clear(); table.RowStyles.Clear();
        for (int i = 0; i < 5; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,20));
        for (int i = 0; i < 2; i++) table.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        void ResizeGrid()
        {
            if (table.IsDisposed) return;
            var size = new Size(Math.Max(1100,host.ClientSize.Width),Math.Max(600,host.ClientSize.Height));
            if (table.Size != size) table.Size = size;
        }
        host.SizeChanged += (_,_) => ResizeGrid();
        table.ControlAdded += (_,_) => table.Invalidate();
        table.ControlRemoved += (_,_) => table.Invalidate();
        table.CellPaint += (_,e) =>
        {
            if (table.GetControlFromPosition(e.Column,e.Row) != null) return;
            var bounds = Rectangle.Inflate(e.CellBounds,-5,-5);
            using var fill = new SolidBrush(Color.FromArgb(249,251,253));
            using var border = new Pen(Color.FromArgb(226,233,241));
            e.Graphics.FillRectangle(fill,bounds);
            e.Graphics.DrawRectangle(border,bounds);
            TextRenderer.DrawText(e.Graphics,"等待玩家",table.Font,bounds,Color.FromArgb(157,169,183),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        ResizeGrid(); host.BringToFront();
    }
}