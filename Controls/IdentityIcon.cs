using System.Drawing.Drawing2D;

namespace League.Controls;

internal sealed class IdentityIcon : Label
{
    public bool IsCopy { get; init; }

    public IdentityIcon()
    {
        AutoSize = false;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float size = Math.Min(Math.Min(Width - 6, Height - 6), Font.Height * .68f);
        if (size <= 0) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float x = (Width - size) / 2, y = (Height - size) / 2;
        using var pen = new Pen(ForeColor, Math.Max(1.2f, size / 11)) { LineJoin = LineJoin.Round };
        if (IsCopy)
        {
            g.DrawRectangle(pen, x + size * .3f, y + size * .3f, size * .6f, size * .65f);
            g.DrawLines(pen, new[] { new PointF(x + size * .15f,y + size * .75f), new PointF(x,y + size * .75f),
                new PointF(x,y), new PointF(x + size * .6f,y), new PointF(x + size * .6f,y + size * .15f) });
        }
        else
        {
            g.DrawArc(pen, x + size * .25f, y, size * .5f, size * .65f, 180, 180);
            g.DrawRectangle(pen, x + size * .1f, y + size * .35f, size * .8f, size * .6f);
            g.DrawLine(pen, x + size * .5f, y + size * .55f, x + size * .5f, y + size * .75f);
        }
        if (Focused && IsCopy) ControlPaint.DrawFocusRectangle(g, ClientRectangle);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsCopy && e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
