using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace League.Controls
{
    /// <summary>
    /// The main window's navigation rail: a light strip of page icons on the left, matching the settings page list. The
    /// selected page gets the accent bar and a tinted row; icons are glyphs from the Windows icon font
    /// (<see cref="RailIcon"/>), with an image tag still drawn for pages that carry one.
    /// </summary>
    public class ImageTabControl : TabControl
    {
        private const int SelectionBarWidth = 3;
        private const int ImageInset = 9;
        private const float IdleImageOpacity = 0.6f;

        // Windows 11 ships Segoe Fluent Icons; Windows 10 has the older MDL2 face with the same code points.
        private static readonly Lazy<string> IconFamily = new(() =>
        {
            using var installed = new InstalledFontCollection();
            return installed.Families.Any(family => family.Name == "Segoe Fluent Icons")
                ? "Segoe Fluent Icons"
                : "Segoe MDL2 Assets";
        });

        private readonly Font _glyphFont = new(IconFamily.Value, 15f);
        private readonly Font _labelFont = new(Theme.FontName, 8f);
        private int _hoverIndex = -1;
        private bool _showLabels;

        public ImageTabControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                true
            );

            Alignment = TabAlignment.Left;
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(50, 50);
        }

        /// <summary>Draws each <see cref="RailIcon"/>'s short label under its glyph; the rows grow to fit.</summary>
        public bool ShowLabels
        {
            get => _showLabels;
            set
            {
                _showLabels = value;
                // Left-aligned tabs swap the axes: Width is the row height and Height the rail width.
                ItemSize = value ? new Size(64, 62) : new Size(50, 50);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Theme.Canvas);
            var rail = RailBounds();
            using (var surface = new SolidBrush(Theme.Surface))
            {
                e.Graphics.FillRectangle(surface, rail);
            }

            for (int i = 0; i < TabPages.Count; i++)
            {
                DrawTab(e.Graphics, i);
            }

            using var divider = new Pen(Theme.Divider);
            e.Graphics.DrawLine(divider, rail.Right - 1, 0, rail.Right - 1, Height);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetHoverIndex(TabIndexAt(e.Location));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHoverIndex(-1);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            // OnPaint draws the whole rail.
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _glyphFont.Dispose();
                _labelFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private Rectangle RailBounds()
        {
            if (TabPages.Count == 0)
            {
                return new Rectangle(0, 0, ItemSize.Height, Height);
            }

            // The rail runs the full height and covers the gap between the tabs and the page.
            int right = DisplayRectangle.Left - 1;
            return new Rectangle(0, 0, Math.Max(right, GetTabRect(0).Right), Height);
        }

        private void DrawTab(Graphics g, int index)
        {
            Rectangle tab = GetTabRect(index);
            var row = new Rectangle(0, tab.Y, RailBounds().Width - 1, tab.Height);
            bool selected = SelectedIndex == index;
            bool hover = index == _hoverIndex;
            if (selected || hover)
            {
                using var fill = new SolidBrush(selected ? Theme.AccentSoft : Theme.Canvas);
                g.FillRectangle(fill, row);
            }

            if (selected)
            {
                using var bar = new SolidBrush(Theme.Accent);
                g.FillRectangle(bar, 0, row.Y + 10, SelectionBarWidth, row.Height - 20);
            }

            Color ink =
                selected ? Theme.Accent
                : hover ? Theme.Ink
                : Theme.Muted;
            switch (TabPages[index].Tag)
            {
                case RailIcon icon:
                    DrawGlyph(g, icon, row, ink);
                    break;
                case Image image:
                    DrawImage(g, image, row, selected || hover ? 1f : IdleImageOpacity);
                    break;
            }
        }

        private void DrawGlyph(Graphics g, RailIcon icon, Rectangle row, Color ink)
        {
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            if (!_showLabels)
            {
                TextRenderer.DrawText(g, icon.Glyph, _glyphFont, row, ink, flags);
                return;
            }

            TextRenderer.DrawText(
                g,
                icon.Glyph,
                _glyphFont,
                new Rectangle(row.X, row.Y + 9, row.Width, 26),
                ink,
                flags
            );
            TextRenderer.DrawText(
                g,
                icon.Label,
                _labelFont,
                new Rectangle(row.X, row.Y + 37, row.Width, 18),
                ink,
                flags | TextFormatFlags.SingleLine
            );
        }

        private static void DrawImage(Graphics g, Image image, Rectangle row, float opacity)
        {
            var area = Rectangle.Inflate(row, -ImageInset, -ImageInset);
            float ratio = Math.Min((float)area.Width / image.Width, (float)area.Height / image.Height);
            int width = (int)(image.Width * ratio);
            int height = (int)(image.Height * ratio);
            var bounds = new Rectangle(
                area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2,
                width,
                height
            );
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, bounds, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }

        private int TabIndexAt(Point location)
        {
            for (int i = 0; i < TabPages.Count; i++)
            {
                if (GetTabRect(i).Contains(location))
                {
                    return i;
                }
            }

            return -1;
        }

        private void SetHoverIndex(int index)
        {
            if (_hoverIndex == index)
            {
                return;
            }

            _hoverIndex = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate(RailBounds());
        }
    }
}
