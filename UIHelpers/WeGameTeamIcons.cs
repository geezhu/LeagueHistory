using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace League.UIHelpers
{
    /// <summary>
    /// The icons of WeGame's team summary line (kills, gold, turrets, inhibitors, Baron, dragons, Rift Herald). They
    /// are masks: only the alpha carries the shape, and they are drawn in a colour of the caller's, the way WeGame
    /// tints them with its text colour. Each mask is twice the size it is shown at, so halving it keeps smooth edges.
    /// </summary>
    internal static class WeGameTeamIcons
    {
        private static readonly ConcurrentDictionary<string, Bitmap?> Masks = new();

        /// <summary>Draws the icon for a summary key ("kda", "gold", "tower", …) centred in bounds; false when none.</summary>
        public static bool Draw(Graphics graphics, string key, Color color, Rectangle bounds)
        {
            var mask = Masks.GetOrAdd(key, Load);
            if (mask == null)
            {
                return false;
            }

            float width = mask.Width / 2f;
            float height = mask.Height / 2f;
            var target = new RectangleF(
                bounds.X + (bounds.Width - width) / 2,
                bounds.Y + (bounds.Height - height) / 2,
                width,
                height
            );
            using var tint = new ImageAttributes();
            // Every pixel takes the colour; the mask's alpha alone decides how much of it shows.
            tint.SetColorMatrix(
                new ColorMatrix([
                    [0, 0, 0, 0, 0],
                    [0, 0, 0, 0, 0],
                    [0, 0, 0, 0, 0],
                    [0, 0, 0, color.A / 255f, 0],
                    [color.R / 255f, color.G / 255f, color.B / 255f, 0, 1],
                ])
            );
            var interpolation = graphics.InterpolationMode;
            var offset = graphics.PixelOffsetMode;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            lock (mask)
            {
                graphics.DrawImage(
                    mask,
                    [target.Location, new PointF(target.Right, target.Top), new PointF(target.Left, target.Bottom)],
                    new RectangleF(0, 0, mask.Width, mask.Height),
                    GraphicsUnit.Pixel,
                    tint
                );
            }
            graphics.InterpolationMode = interpolation;
            graphics.PixelOffsetMode = offset;
            return true;
        }

        // Kept for the process: seven small images shared by every team header.
        private static Bitmap? Load(string key)
        {
            using var stream = typeof(WeGameTeamIcons).Assembly.GetManifestResourceStream(
                $"League.WeGame.team-{key}.png"
            );
            if (stream == null)
            {
                return null;
            }

            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
    }
}
