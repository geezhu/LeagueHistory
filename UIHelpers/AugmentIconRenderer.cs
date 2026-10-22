using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace League.UIHelpers
{
    internal static class AugmentIconRenderer
    {
        // The client supplies monochrome masks; rarity determines the display tint. This is only the fallback for an
        // augment without its full-colour art, so it follows the art: a transparent glyph without a dark plate or a
        // frame, in rarity colours deep enough to read on the light pages as well as the dark windows.
        // Always return an independent image, leaving the cached source untouched.
        public static Bitmap Render(Image source, string? rarity)
        {
            string? tier = rarity?.Trim().ToLowerInvariant();
            Color? tint = tier switch
            {
                "ksilver" => Color.FromArgb(112, 128, 144),
                "kgold" => Color.FromArgb(190, 140, 50),
                "kprismatic" => Color.FromArgb(156, 96, 214),
                _ => null,
            };
            if (!tint.HasValue)
            {
                return source is Bitmap bitmap ? (Bitmap)bitmap.Clone() : new Bitmap(source);
            }

            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            try
            {
                using var graphics = Graphics.FromImage(result);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                Color color = tint.Value;
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(
                    new ColorMatrix(
                        new[]
                        {
                            new[] { color.R / 255f, 0f, 0f, 0f, 0f },
                            new[] { 0f, color.G / 255f, 0f, 0f, 0f },
                            new[] { 0f, 0f, color.B / 255f, 0f, 0f },
                            new[] { 0f, 0f, 0f, 1f, 0f },
                            new[] { 0f, 0f, 0f, 0f, 1f },
                        }
                    )
                );

                // The source already includes glyph padding; do not shrink it again.
                var bounds = new Rectangle(0, 0, source.Width, source.Height);
                graphics.DrawImage(source, bounds, 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
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
