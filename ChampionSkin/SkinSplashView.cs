using System.Drawing.Drawing2D;

namespace League.ChampionSkin
{
    /// <summary>
    /// The selected skin's splash art, cropped to fill the area like the client's collection page, with the skin and
    /// champion names over a dark gradient and a mark when it is the automatic skin.
    /// </summary>
    public sealed class SkinSplashView : Control
    {
        private readonly Font _titleFont = new(SkinBrowserTheme.FontName, 18F, FontStyle.Bold);
        private readonly Font _subtitleFont = new(SkinBrowserTheme.FontName, 10F);
        private readonly Font _badgeFont = new(SkinBrowserTheme.FontName, 9.5F, FontStyle.Bold);
        private Image? _image;
        private string _title = "",
            _subtitle = "",
            _status = "";
        private bool _preferred;

        public SkinSplashView()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw,
                true
            );
            BackColor = SkinBrowserTheme.Background;
        }

        /// <summary>The art to show; the caller keeps ownership. Null shows the status text instead.</summary>
        public void Show(Image? image, string title, string subtitle, bool preferred, string status)
        {
            _image = image;
            _title = title;
            _subtitle = subtitle;
            _preferred = preferred;
            _status = status;
            Invalidate();
        }

        public Image? Image => _image;

        private int S(int value) => LogicalToDeviceUnits(value);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var area = ClientRectangle;
            if (area.Width <= 0 || area.Height <= 0)
            {
                return;
            }

            if (_image != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_image, area, Cover(_image.Size, area.Size), GraphicsUnit.Pixel);
            }
            else if (_status.Length > 0)
            {
                TextRenderer.DrawText(
                    g,
                    _status,
                    _subtitleFont,
                    area,
                    SkinBrowserTheme.MutedText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak
                );
            }

            PaintCaption(g, area);
        }

        // The part of the image that fills the area without distortion; splash art keeps its center.
        private static Rectangle Cover(Size image, Size area)
        {
            double scale = Math.Max((double)area.Width / image.Width, (double)area.Height / image.Height);
            int width = (int)Math.Round(area.Width / scale);
            int height = (int)Math.Round(area.Height / scale);
            return new Rectangle((image.Width - width) / 2, (image.Height - height) / 2, width, height);
        }

        private void PaintCaption(Graphics g, Rectangle area)
        {
            if (_title.Length == 0)
            {
                return;
            }

            int height = Math.Min(area.Height, S(120));
            var band = new Rectangle(area.Left, area.Bottom - height, area.Width, height);
            using (
                var shade = new LinearGradientBrush(
                    new Rectangle(band.Left, band.Top - 1, band.Width, band.Height + 1),
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(215, 8, 10, 14),
                    LinearGradientMode.Vertical
                )
            )
            {
                g.FillRectangle(shade, band);
            }

            int left = area.Left + S(22);
            var subtitle = new Rectangle(left, area.Bottom - S(34), area.Width - S(44), S(24));
            var title = new Rectangle(left, subtitle.Top - S(40), area.Width - S(44), S(40));
            TextRenderer.DrawText(
                g,
                _title,
                _titleFont,
                title,
                Color.White,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.Bottom
            );
            TextRenderer.DrawText(
                g,
                _subtitle,
                _subtitleFont,
                subtitle,
                SkinBrowserTheme.MutedText,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
            );
            if (_preferred)
            {
                PaintBadge(g, area);
            }
        }

        private void PaintBadge(Graphics g, Rectangle area)
        {
            const string text = "★ 自动皮肤";
            var size = TextRenderer.MeasureText(g, text, _badgeFont);
            var badge = new Rectangle(area.Right - size.Width - S(34), area.Top + S(16), size.Width + S(16), S(28));
            using var fill = new SolidBrush(Color.FromArgb(200, 17, 21, 26));
            using var border = new Pen(SkinBrowserTheme.Gold, S(1));
            g.FillRectangle(fill, badge);
            g.DrawRectangle(border, badge);
            TextRenderer.DrawText(
                g,
                text,
                _badgeFont,
                badge,
                SkinBrowserTheme.GoldBright,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _titleFont.Dispose();
                _subtitleFont.Dispose();
                _badgeFont.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
