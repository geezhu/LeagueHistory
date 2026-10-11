namespace League.Controls;

internal sealed class DarkFilterComboBox : DarkSelectionComboBox
{
    public DarkFilterComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        ItemHeight = 22;
        BackColor = Color.FromArgb(16, 45, 57);
        ForeColor = Color.FromArgb(215, 225, 226);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        using var background = new SolidBrush(
            Enabled
                ? ((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(23, 82, 95) : BackColor)
                : Color.FromArgb(12, 34, 45)
        );
        e.Graphics.FillRectangle(background, e.Bounds);
        string text = e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text;
        var bounds = Rectangle.Inflate(e.Bounds, -5, 0);
        TextRenderer.DrawText(
            e.Graphics,
            text,
            Font,
            bounds,
            Enabled ? ForeColor : Color.FromArgb(93, 119, 129),
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
        );
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }
}
