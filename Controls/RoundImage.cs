using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace League.Controls;

/// <summary>
/// Draws an image inside a circle with a smooth edge. Clipping to an ellipse path is never anti-aliased in GDI+, so
/// the edge came out jagged whatever the smoothing mode; filling the ellipse with a texture brush is. The image is
/// scaled to the circle first, because a texture brush samples a large icon too coarsely when it shrinks it.
/// </summary>
internal static class RoundImage
{
    public static void Draw(Graphics g, Image image, Rectangle bounds, ImageAttributes? attributes = null) =>
        Draw(
            g,
            bounds,
            canvas =>
                canvas.DrawImage(
                    image,
                    new Rectangle(0, 0, bounds.Width, bounds.Height),
                    0,
                    0,
                    image.Width,
                    image.Height,
                    GraphicsUnit.Pixel,
                    attributes
                )
        );

    /// <summary>
    /// Paints anything inside the circle, such as a placeholder made of shapes; <paramref name="paint"/> draws with
    /// the circle's top-left corner at the origin.
    /// </summary>
    public static void Draw(Graphics g, Rectangle bounds, Action<Graphics> paint)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var scaled = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        using (var canvas = Graphics.FromImage(scaled))
        {
            canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
            canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            paint(canvas);
        }

        using var brush = new TextureBrush(scaled, WrapMode.Clamp);
        brush.TranslateTransform(bounds.X, bounds.Y);
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillEllipse(brush, bounds);
        g.SmoothingMode = smoothing;
    }
}
