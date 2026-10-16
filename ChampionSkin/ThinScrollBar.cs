namespace League.ChampionSkin
{
    /// <summary>
    /// A slim scroll bar painted by its owner view, matching the dark theme: the thumb can be dragged and a click on
    /// the track pages toward it. The owner forwards mouse events and paints it last.
    /// </summary>
    internal sealed class ThinScrollBar(bool vertical)
    {
        private int? _dragStart;
        private int _dragValue;

        public int Value { get; private set; }

        public int Content { get; private set; }

        public int Viewport { get; private set; }

        public int Max => Math.Max(0, Content - Viewport);

        public bool Needed => Content > Viewport;

        public bool Dragging => _dragStart.HasValue;

        public void Update(int content, int viewport)
        {
            Content = Math.Max(0, content);
            Viewport = Math.Max(1, viewport);
            Value = Math.Clamp(Value, 0, Max);
        }

        /// <summary>Moves to the value; returns whether it changed.</summary>
        public bool ScrollTo(int value)
        {
            int next = Math.Clamp(value, 0, Max);
            bool changed = next != Value;
            Value = next;
            return changed;
        }

        public Rectangle Track(Rectangle client, int thickness, int inset) =>
            vertical
                ? new Rectangle(
                    client.Right - thickness - inset,
                    client.Top + inset,
                    thickness,
                    client.Height - 2 * inset
                )
                : new Rectangle(
                    client.Left + inset,
                    client.Bottom - thickness - inset,
                    client.Width - 2 * inset,
                    thickness
                );

        public Rectangle Thumb(Rectangle track)
        {
            int length = vertical ? track.Height : track.Width;
            int size = Math.Clamp(length * Viewport / Math.Max(1, Content), Math.Min(length, 24), length);
            int offset = Max == 0 ? 0 : (int)((long)(length - size) * Value / Max);
            return vertical
                ? new Rectangle(track.Left, track.Top + offset, track.Width, size)
                : new Rectangle(track.Left + offset, track.Top, size, track.Height);
        }

        /// <summary>Starts a drag on the thumb or pages toward the click; returns whether the track was hit.</summary>
        public bool MouseDown(Point point, Rectangle track)
        {
            var hit = Rectangle.Inflate(track, vertical ? 4 : 0, vertical ? 0 : 4);
            if (!Needed || !hit.Contains(point))
            {
                return false;
            }

            var thumb = Thumb(track);
            if (thumb.Contains(point))
            {
                _dragStart = vertical ? point.Y : point.X;
                _dragValue = Value;
            }
            else
            {
                bool before = vertical ? point.Y < thumb.Top : point.X < thumb.Left;
                ScrollTo(Value + (before ? -Viewport : Viewport));
            }

            return true;
        }

        public bool MouseMove(Point point, Rectangle track)
        {
            if (_dragStart is not int start)
            {
                return false;
            }

            var thumb = Thumb(track);
            int free = vertical ? track.Height - thumb.Height : track.Width - thumb.Width;
            int moved = (vertical ? point.Y : point.X) - start;
            return ScrollTo(_dragValue + (free <= 0 ? 0 : (int)((long)moved * Max / free)));
        }

        public void MouseUp() => _dragStart = null;

        public void Paint(Graphics g, Rectangle track, bool active)
        {
            if (!Needed)
            {
                return;
            }

            using var trackBrush = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
            using var thumbBrush = new SolidBrush(
                active || Dragging ? SkinBrowserTheme.Gold : Color.FromArgb(120, 150, 160, 170)
            );
            g.FillRectangle(trackBrush, track);
            g.FillRectangle(thumbBrush, Thumb(track));
        }
    }
}
