using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace League.UIHelpers
{
    internal static class AugmentIconRenderer
    {
        // The client supplies monochrome masks; rarity determines the display tint.
        // Always return an independent image, leaving the cached source untouched.
        public static Bitmap Render(Image source, string? rarity)
        {
            string? tier = rarity?.Trim().ToLowerInvariant();
            Color? tint = tier switch
            {
                "ksilver" => Color.FromArgb(185, 202, 211),
                "kgold" => Color.FromArgb(231, 201, 143),
                "kprismatic" => Color.FromArgb(204, 153, 235),
                _ => null
            };
            if (!tint.HasValue) return source is Bitmap bitmap ? (Bitmap)bitmap.Clone() : new Bitmap(source);

            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            try
            {
                using var graphics = Graphics.FromImage(result);
                graphics.Clear(Color.FromArgb(15, 25, 34));
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                Color color = tint.Value;
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(new ColorMatrix(new[]
                {
                    new[] { color.R / 255f, 0f, 0f, 0f, 0f },
                    new[] { 0f, color.G / 255f, 0f, 0f, 0f },
                    new[] { 0f, 0f, color.B / 255f, 0f, 0f },
                    new[] { 0f, 0f, 0f, 1f, 0f },
                    new[] { 0f, 0f, 0f, 0f, 1f }
                }));

                // The source already includes glyph padding; do not shrink it again.
                var bounds = new Rectangle(0, 0, source.Width, source.Height);
                graphics.DrawImage(source, bounds, 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
                // One logical pixel at the list's 24px display size.
                float stroke = Math.Min(source.Width, source.Height) / 24f;
                void Frame(Color frameColor, float inset)
                {
                    using var brush = new SolidBrush(frameColor);
                    float width = result.Width - inset * 2, height = result.Height - inset * 2;
                    if (width <= 0 || height <= 0) return;
                    graphics.FillRectangle(brush, inset, inset, width, stroke);
                    graphics.FillRectangle(brush, inset, result.Height - inset - stroke, width, stroke);
                    graphics.FillRectangle(brush, inset, inset, stroke, height);
                    graphics.FillRectangle(brush, result.Width - inset - stroke, inset, stroke, height);
                }
                if (tier == "kprismatic")
                {
                    Frame(Color.FromArgb(76, 46, 93), stroke);
                    Frame(Color.FromArgb(193, 129, 220), 0);
                }
                else
                {
                    Frame(Color.FromArgb(150, color), 0);
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }
    }
}
