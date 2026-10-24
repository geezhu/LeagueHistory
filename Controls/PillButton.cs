using System.Drawing.Drawing2D;

namespace League.Controls;

/// <summary>
/// The capsule buttons of WeGame's end-of-game panel in the application's colours: filled with the accent and white
/// text, or outlined on the page. Painted by hand because a flat WinForms button cannot round its corners.
/// </summary>
public sealed class PillButton : Button
{
    private bool _filled;
    private bool _hover;

    public PillButton()
    {
        SetStyle(
            ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
            true
        );
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
    }

    /// <summary>The gold capsule of a primary action; otherwise the grey outline.</summary>
    public bool Filled
    {
        get => _filled;
        set
        {
            _filled = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        Color background = Parent?.BackColor ?? BackColor;
        g.Clear(background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var capsule = Capsule(bounds);
        // A disabled button keeps its shape and fades into the page, as WeGame's sent rows do.
        float fade = Enabled ? 0f : 0.55f;
        Color text;
        if (_filled)
        {
            using var fill = new SolidBrush(
                Blend(_hover && Enabled ? Theme.AccentHover : Theme.Accent, background, fade)
            );
            g.FillPath(fill, capsule);
            text = Blend(Color.White, background, fade / 2);
        }
        else
        {
            using var pen = new Pen(
                Blend(_hover && Enabled ? Theme.Accent : Theme.DividerStrong, background, fade),
                1f
            );
            g.DrawPath(pen, capsule);
            text = Blend(_hover && Enabled ? Theme.Accent : Theme.Ink, background, fade);
        }

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            ClientRectangle,
            text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
        );
    }

    // TextRenderer draws through GDI, which ignores alpha, so faded text is mixed with the background instead.
    private static Color Blend(Color color, Color background, float amount) =>
        Color.FromArgb(
            (int)(color.R + (background.R - color.R) * amount),
            (int)(color.G + (background.G - color.G) * amount),
            (int)(color.B + (background.B - color.B) * amount)
        );

    private static GraphicsPath Capsule(RectangleF bounds)
    {
        float diameter = bounds.Height;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 90, 180);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 180);
        path.CloseFigure();
        return path;
    }
}
