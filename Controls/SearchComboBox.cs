namespace League.Controls;

// Retains the native editable dropdown and autocomplete, with a quiet border/arrow.
public sealed class SearchComboBox : ComboBox
{
    public SearchComboBox() { FlatStyle = FlatStyle.Flat; }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x000F && IsHandleCreated)
        {
            using var graphics = CreateGraphics();
            PaintChrome(graphics);
        }
        else if (m.Msg == 0x0318 && m.WParam != IntPtr.Zero)
        {
            using var graphics = Graphics.FromHdc(m.WParam);
            PaintChrome(graphics);
        }
    }
    private void PaintChrome(Graphics graphics)
    {
        if (Width < 24 || Height < 8) return;
        using var border = new Pen(BackColor,2);
        graphics.DrawRectangle(border,1,1,Width-2,Height-2);
        int arrowWidth = SystemInformation.VerticalScrollBarWidth;
        using var background = new SolidBrush(BackColor);
        graphics.FillRectangle(background,Width-arrowWidth-1,0,arrowWidth+1,Height);
        using var arrow = new Pen(Enabled ? Color.FromArgb(111,123,139) : SystemColors.GrayText,1.2f);
        int x = Width-arrowWidth/2-1, y = Height/2;
        graphics.DrawLines(arrow,new[] { new Point(x-3,y-1),new Point(x,y+2),new Point(x+3,y-1) });
    }
}
