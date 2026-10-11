namespace League.Controls;

internal class DarkSelectionComboBox : ComboBox
{
    private const int WmPaint = 0x000F;
    private const int WmPrint = 0x0317;
    private const int WmPrintClient = 0x0318;

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        // OwnerDraw only paints the item; replace the native light frame and arrow too.
        if (IsDisposed || Width <= 0 || Height <= 0)
        {
            return;
        }

        if (message.Msg == WmPaint)
        {
            using var graphics = Graphics.FromHwnd(Handle);
            DrawFrame(graphics);
        }
        else if ((message.Msg == WmPrint || message.Msg == WmPrintClient) && message.WParam != IntPtr.Zero)
        {
            using var graphics = Graphics.FromHdc(message.WParam);
            DrawFrame(graphics);
        }
    }

    private void DrawFrame(Graphics graphics)
    {
        int arrowWidth = Math.Max(20, SystemInformation.VerticalScrollBarWidth);
        var item = new Rectangle(1, 1, Math.Max(1, Width - arrowWidth - 2), Height - 2);
        OnDrawItem(
            new DrawItemEventArgs(graphics, Font, item, SelectedIndex, DrawItemState.None, ForeColor, BackColor)
        );
        using var fill = new SolidBrush(BackColor);
        graphics.FillRectangle(fill, new Rectangle(Width - arrowWidth - 1, 1, arrowWidth, Height - 2));
        var accent = Enabled && (Focused || DroppedDown) ? Color.FromArgb(0, 160, 174) : Color.FromArgb(42, 72, 83);
        using var border = new Pen(accent);
        graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using var arrow = new Pen(Enabled ? Color.FromArgb(147, 181, 190) : Color.FromArgb(76, 103, 113), 1.5f);
        int x = Width - arrowWidth / 2 - 1,
            y = Height / 2;
        graphics.DrawLines(arrow, new[] { new Point(x - 4, y - 2), new Point(x, y + 2), new Point(x + 4, y - 2) });
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnDropDownClosed(EventArgs e)
    {
        base.OnDropDownClosed(e);
        Invalidate();
    }
}
