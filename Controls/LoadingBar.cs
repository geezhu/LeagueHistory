using System.Drawing.Drawing2D;

namespace League.Controls;

/// <summary>
/// A thin indeterminate loading bar in the accent colour: a short segment sweeps along a light track. It stays a
/// <see cref="ProgressBar"/>, so assistive tools still announce progress, but draws itself instead of the system's
/// green marquee. The sweep runs only while the bar is visible.
/// </summary>
public sealed class LoadingBar : ProgressBar
{
    private const float SegmentShare = 0.3f;
    private const float StepShare = 0.02f;
    private readonly System.Windows.Forms.Timer _sweep = new() { Interval = 30 };
    private float _position;

    public LoadingBar()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
            true
        );
        Style = ProgressBarStyle.Marquee;
        Height = 4;
        _sweep.Tick += (_, _) =>
        {
            _position = (_position + StepShare) % (1 + SegmentShare);
            Invalidate();
        };
    }

    /// <summary>Where the segment's leading edge is, from 0 to 1 + its own width; exposed for layout checks.</summary>
    public float Position => _position;

    public bool Sweeping => _sweep.Enabled;

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateSweep();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateSweep();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(0, 0, Width, Height);
        using (var trackBrush = new SolidBrush(Theme.Divider))
        using (var trackPath = Pill(track))
        {
            g.FillPath(trackBrush, trackPath);
        }

        float segment = Width * SegmentShare;
        float right = Width * _position;
        var bar = RectangleF.FromLTRB(Math.Max(0, right - segment), 0, Math.Min(Width, right), Height);
        if (bar.Width <= 0)
        {
            return;
        }

        using var barBrush = new SolidBrush(Theme.Accent);
        using var barPath = Pill(bar);
        g.FillPath(barBrush, barPath);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sweep.Dispose();
        }

        base.Dispose(disposing);
    }

    private void UpdateSweep() => _sweep.Enabled = IsHandleCreated && Visible;

    private static GraphicsPath Pill(RectangleF bounds)
    {
        float diameter = Math.Min(bounds.Height, bounds.Width);
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 90, 180);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 180);
        path.CloseFigure();
        return path;
    }
}
