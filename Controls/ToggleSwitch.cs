using System.Drawing.Drawing2D;

namespace League.Controls;

/// <summary>
/// Paints a text-less <see cref="CheckBox"/> as an on/off switch. The control stays a check box, so clicks, the space
/// key, data binding and accessibility keep working; only its look changes.
/// </summary>
public static class ToggleSwitch
{
    public static readonly Size SwitchSize = new(40, 22);

    public static void Apply(CheckBox check)
    {
        check.AutoSize = false;
        check.Size = SwitchSize;
        check.Cursor = Cursors.Hand;
        check.Paint -= PaintSwitch;
        check.Paint += PaintSwitch;
    }

    private static void PaintSwitch(object? sender, PaintEventArgs e)
    {
        if (sender is not CheckBox check)
        {
            return;
        }

        var g = e.Graphics;
        g.Clear(check.Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(1, (check.Height - 20) / 2f, 38, 20);
        Color fill =
            !check.Enabled ? Theme.Divider
            : check.Checked ? Theme.Accent
            : Theme.DividerStrong;
        using (var path = Pill(track))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        float knob = track.Height - 6;
        float x = check.Checked ? track.Right - knob - 3 : track.X + 3;
        using (var white = new SolidBrush(Color.White))
        {
            g.FillEllipse(white, x, track.Y + 3, knob, knob);
        }

        if (check.Focused)
        {
            using var focus = new Pen(Theme.Accent, 1) { DashStyle = DashStyle.Dot };
            using var ring = Pill(RectangleF.Inflate(track, 1, 1));
            g.DrawPath(focus, ring);
        }
    }

    private static GraphicsPath Pill(RectangleF bounds)
    {
        float diameter = bounds.Height;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 90, 180);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 180);
        path.CloseFigure();
        return path;
    }
}
