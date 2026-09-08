using System.Drawing.Drawing2D;

namespace League.Controls;

public sealed class RankEmblem : Control
{
    private Bitmap? _image;
    private Rectangle _source;
    private string? _tier;

    public RankEmblem()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public void SetTier(string tier)
    {
        if (_tier == tier) return;
        _tier = tier;
        _image?.Dispose(); _image = null;
        using var stream = typeof(RankEmblem).Assembly.GetManifestResourceStream($"League.Assets.Ranked.emblem-{tier.ToLowerInvariant()}.png");
        if (stream != null)
        {
            using var original = new Bitmap(stream);
            _image = new Bitmap(original);
            int left = original.Width, top = original.Height, right = 0, bottom = 0;
            for (int y = 0; y < original.Height; y += 2)
                for (int x = 0; x < original.Width; x += 2)
                    if (original.GetPixel(x,y).A > 20)
                    { left = Math.Min(left,x); top = Math.Min(top,y); right = Math.Max(right,x); bottom = Math.Max(bottom,y); }
            _source = right >= left ? Rectangle.FromLTRB(Math.Max(0,left-2), Math.Max(0,top-2), Math.Min(original.Width,right+3), Math.Min(original.Height,bottom+3)) : Rectangle.Empty;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image == null || _source.IsEmpty || Width <= 0 || Height <= 0) return;
        float scale = Math.Min((float)Width/_source.Width, (float)Height/_source.Height);
        var target = new RectangleF((Width-_source.Width*scale)/2, (Height-_source.Height*scale)/2, _source.Width*scale, _source.Height*scale);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_image,target,_source,GraphicsUnit.Pixel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _image?.Dispose(); _image = null; }
        base.Dispose(disposing);
    }
}
