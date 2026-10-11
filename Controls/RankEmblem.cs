using System.Drawing.Drawing2D;

namespace League.Controls;

public sealed class RankEmblem : Control
{
    private Bitmap? _image;
    private Rectangle _source;
    private string? _tier;

    // Bounds of the shipped original PNGs, computed once during development.
    private static readonly Dictionary<string, Rectangle> BoundsByTier = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bronze"] = new(522, 274, 238, 158),
        ["challenger"] = new(479, 218, 324, 243),
        ["diamond"] = new(483, 261, 316, 191),
        ["emerald"] = new(990, 458, 584, 434),
        ["gold"] = new(509, 235, 260, 220),
        ["grandmaster"] = new(483, 240, 315, 225),
        ["iron"] = new(541, 295, 200, 123),
        ["master"] = new(490, 236, 302, 220),
        ["platinum"] = new(1016, 445, 529, 449),
        ["silver"] = new(510, 246, 260, 184),
    };
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Bitmap>> Cache = new();

    private static Bitmap LoadEmblem(string tier, Rectangle source)
    {
        using var stream =
            typeof(RankEmblem).Assembly.GetManifestResourceStream($"League.Assets.Ranked.emblem-{tier}.png")
            ?? throw new InvalidOperationException($"Missing rank emblem: {tier}");
        using var original = new Bitmap(stream);
        float scale = Math.Min(1f, 256f / Math.Max(source.Width, source.Height));
        var bitmap = new Bitmap(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(original, new Rectangle(0, 0, bitmap.Width, bitmap.Height), source, GraphicsUnit.Pixel);
        return bitmap;
    }

    public RankEmblem()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public void SetTier(string tier)
    {
        if (_tier == tier)
            return;
        _tier = tier;
        _image = null;
        string key = tier.ToLowerInvariant();
        if (BoundsByTier.TryGetValue(key, out var bounds))
        {
            _image = Cache.GetOrAdd(key, k => new Lazy<Bitmap>(() => LoadEmblem(k, bounds))).Value;
            _source = new Rectangle(0, 0, _image.Width, _image.Height);
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image == null || _source.IsEmpty || Width <= 0 || Height <= 0)
            return;
        float scale = Math.Min((float)Width / _source.Width, (float)Height / _source.Height);
        var target = new RectangleF(
            (Width - _source.Width * scale) / 2,
            (Height - _source.Height * scale) / 2,
            _source.Width * scale,
            _source.Height * scale
        );
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_image, target, _source, GraphicsUnit.Pixel);
    }

    protected override void Dispose(bool disposing)
    {
        // The ten small cached images are shared for the application lifetime.
        if (disposing)
            _image = null;
        base.Dispose(disposing);
    }
}
