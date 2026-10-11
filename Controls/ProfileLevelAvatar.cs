using System.Drawing.Drawing2D;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public sealed class ProfileLevelAvatar : PictureBox
{
    private string _level = "—";
    private double? _progress;
    public string Level
    {
        get => _level;
        set
        {
            _level = value;
            Invalidate();
        }
    }

    public ProfileLevelAvatar()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint,
            true
        );
    }

    public void SetExperience(JObject profile)
    {
        var current = profile["xpSinceLastLevel"] ?? profile["expPoints"];
        var required = profile["xpUntilNextLevel"] ?? profile["expToNextLevel"];
        _progress =
            current?.Type == JTokenType.Integer
            && required?.Type == JTokenType.Integer
            && (double)required > 0
            && (double)current >= 0
                ? Math.Clamp((double)current / (double)required, 0, 1)
                : null;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        float size = Math.Min(ClientSize.Width, ClientSize.Height);
        if (size < 16)
        {
            return;
        }

        var ring = new RectangleF(4, 4, size - 8, size - 8);
        using var track = new Pen(Color.FromArgb(207, 219, 234), 5)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var progress = new Pen(Color.FromArgb(0, 160, 174), 5)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawArc(track, ring, 120, 300);
        if (_progress is > 0)
        {
            g.DrawArc(progress, ring, 120, (float)(_progress.Value * 300));
        }

        var portrait = new RectangleF(size * .15f, size * .15f, size * .7f, size * .7f);
        using var clip = new GraphicsPath();
        clip.AddEllipse(portrait);
        var state = g.Save();
        g.SetClip(clip);
        if (Image != null)
        {
            g.DrawImage(Image, portrait);
        }

        g.Restore(state);
        using var font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
        using var ink = new SolidBrush(Color.FromArgb(0, 143, 161));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(Level, font, ink, new RectangleF(0, size * .86f, size, size * .14f), format);
    }
}
