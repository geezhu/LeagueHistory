using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace League.Controls
{
    /// <summary>
    /// The main window's navigation rail: page icons down a dark strip on the left, with the selected
    /// page marked by an accent bar and the others dimmed until hovered.
    /// </summary>
    public class ImageTabControl : TabControl
    {
        private const int IconInset = 7;
        private const int SelectionBarWidth = 3;
        private const float IdleIconOpacity = 0.55f;
        private int _hoverIndex = -1;

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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Theme.Canvas);
            using (var rail = new SolidBrush(Theme.Rail))
            {
                e.Graphics.FillRectangle(rail, RailBounds());
            }

            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            for (int i = 0; i < TabPages.Count; i++)
            {
                DrawTab(e.Graphics, i);
            }
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
            bool selected = SelectedIndex == index;
            if (selected || index == _hoverIndex)
            {
                using var fill = new SolidBrush(selected ? Theme.RailSelected : Theme.RailHover);
                g.FillRectangle(fill, tab.X, tab.Y, RailBounds().Width, tab.Height);
            }

            if (selected)
            {
                using var bar = new SolidBrush(Theme.Accent);
                g.FillRectangle(bar, 0, tab.Y + 8, SelectionBarWidth, tab.Height - 16);
            }

            if (TabPages[index].Tag is Image icon)
            {
                var area = Rectangle.Inflate(tab, -IconInset, -IconInset);
                DrawIcon(g, icon, Fit(icon.Size, area), selected || index == _hoverIndex ? 1f : IdleIconOpacity);
            }
        }

        private static void DrawIcon(Graphics g, Image icon, Rectangle bounds, float opacity)
        {
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });
            g.DrawImage(icon, bounds, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attributes);
        }

        private static Rectangle Fit(Size image, Rectangle area)
        {
            float ratio = Math.Min((float)area.Width / image.Width, (float)area.Height / image.Height);
            int width = (int)(image.Width * ratio);
            int height = (int)(image.Height * ratio);
            return new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
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
