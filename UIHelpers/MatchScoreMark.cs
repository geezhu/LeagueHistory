using League.Controls;
using League.Models;

namespace League.UIHelpers
{
    /// <summary>
    /// The mark a history row puts in the bottom right corner of its champion icon, where WeGame puts its own: the
    /// MVP or SVP badge when the player earned one, otherwise the estimated score on a plate of the same size.
    /// </summary>
    internal static class MatchScoreMark
    {
        /// <summary>WeGame's badge size; a smaller icon gets it scaled down to its own width.</summary>
        public static readonly Size BadgeSize = new(28, 12);

        /// <summary>Where the mark goes for an icon: right and bottom aligned, at most the icon's width.</summary>
        public static Rectangle Bounds(Rectangle icon)
        {
            int width = Math.Min(BadgeSize.Width, icon.Width);
            int height = (int)Math.Round(width * (double)BadgeSize.Height / BadgeSize.Width);
            return new Rectangle(icon.Right - width, icon.Bottom - height, width, height);
        }

        public static void Paint(Graphics g, Rectangle bounds, OwnMatchScore score)
        {
            if (WeGameHonorIcons.Badge(score.Honour) is Bitmap badge)
            {
                g.DrawImage(badge, bounds);
                return;
            }

            using var plate = new SolidBrush(Theme.Ink);
            g.FillRectangle(plate, bounds);
            // Sized from the plate in pixels; DrawString rather than TextRenderer, since some rows paint through a
            // DPI scale transform.
            using var font = Theme.NumberFont(Math.Max(6f, bounds.Height - 2f), strong: true, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString($"{score.Estimate:0.0}", font, Brushes.White, bounds, format);
        }

        public static string Hint(OwnMatchScore score) =>
            $"估算评分 {score.Estimate:0.0}\n"
            + (
                score.Honour == MatchHonour.None
                    ? "按 WeGame 海克斯大乱斗评分拟合的估算，不是 WeGame 的数据；平均相差约 0.7 分。"
                    : WeGameHonorIcons.BadgeHint(score.Honour)
            );
    }
}
