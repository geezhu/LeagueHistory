using System.Drawing.Drawing2D;
using League.Controls;
using static League.FormMain;

namespace League.Controls
{
    public class ClosableTabControl : TabControl
    {
        // 新增字段：记录“不可关闭的Tab”
        public TabPage? ProtectedTab { get; private set; }

        private const int CloseButtonSize = 12;
        private const int CloseButtonInset = 4;
        private const int TextInset = 10;
        private const int AccentHeight = 2;

        // A person glyph in front of the account's own tab, which has no close button.
        private const string OwnTabGlyph = "";

        private readonly Dictionary<int, Rectangle> _closeButtonBounds = new Dictionary<int, Rectangle>();
        private int _hoverTab = -1;
        private bool _hoverClose;

        public ClosableTabControl()
        {
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
                true
            );
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Normal;
            Appearance = TabAppearance.Normal;
            Padding = new Point(20, 4); // 标签标题内边距，避免文字与叉重叠

            // 立即触发重绘
            Invalidate();
        }

        // The account's own tab stays open, so it is drawn without a close button and a click there closes nothing.
        private static bool IsClosable(TabPage page) =>
            !(
                page.Tag is string puuid
                && string.Equals(puuid, Globals.CurrentPuuid, StringComparison.OrdinalIgnoreCase)
            );

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            for (int i = 0; i < TabCount; i++)
            {
                if (_closeButtonBounds.TryGetValue(i, out var rect) && rect.Contains(e.Location))
                {
                    var page = TabPages[i];
                    if (!IsClosable(page))
                    {
                        break;
                    }

                    // 查找父级控件中的 MatchTabContent
                    var parent = this.Parent;
                    while (parent != null && !(parent is MatchTabContent))
                    {
                        parent = parent.Parent;
                    }

                    if (parent is MatchTabContent matchTabContent)
                    {
                        // 调用公开的关闭方法
                        matchTabContent.CloseTab(page);
                    }
                    else
                    {
                        // 回退方案：直接移除
                        TabPages.Remove(page);
                        page.Dispose();
                    }
                    break;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int tab = Enumerable.Range(0, TabCount).FirstOrDefault(i => GetTabRect(i).Contains(e.Location), -1);
            bool close = tab >= 0 && _closeButtonBounds.TryGetValue(tab, out var rect) && rect.Contains(e.Location);
            if (tab != _hoverTab || close != _hoverClose)
            {
                _hoverTab = tab;
                _hoverClose = close;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverTab >= 0)
            {
                _hoverTab = -1;
                _hoverClose = false;
                Invalidate();
            }
        }

        // 最后防线：不允许 Tab 完全清空（至少留一个）
        protected override void OnControlRemoved(ControlEventArgs e)
        {
            base.OnControlRemoved(e);

            if (TabCount == 0)
            {
                MessageBox.Show("必须至少保留自己的数据页", "提示", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                // 如果你能拿到自己 Tab 的引用，可以在这里加回去：
                // 但因为没有引用，这里只能提示，实际不加回
            }
        }

        // The strip takes the page canvas colour and ends in one divider; the pages sit under it without a frame.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Theme.Canvas);
            _closeButtonBounds.Clear();
            if (TabCount == 0)
            {
                return;
            }

            int stripBottom = Enumerable.Range(0, TabCount).Max(i => GetTabRect(i).Bottom);
            using (var divider = new SolidBrush(Theme.Divider))
            {
                e.Graphics.FillRectangle(divider, 0, stripBottom, Width, 1);
            }

            for (int i = 0; i < TabCount; i++)
            {
                PaintTab(e.Graphics, i, stripBottom);
            }
        }

        // The selected tab is a white tab with an accent line under it; the others are plain text that lightens on
        // hover. Only a closable tab has a close button, at the right edge.
        private void PaintTab(Graphics g, int index, int stripBottom)
        {
            var page = TabPages[index];
            bool selected = index == SelectedIndex;
            var tabRect = GetTabRect(index);
            var face = new Rectangle(tabRect.X, tabRect.Y, tabRect.Width, stripBottom - tabRect.Y);
            if (selected || index == _hoverTab)
            {
                using var fill = new SolidBrush(selected ? Theme.Surface : Theme.ControlFill);
                g.FillRectangle(fill, face);
            }
            if (selected)
            {
                using var accent = new SolidBrush(Theme.Accent);
                g.FillRectangle(accent, face.X, stripBottom - AccentHeight, face.Width, AccentHeight);
            }

            bool closable = IsClosable(page);
            int textLeft = face.X + TextInset;
            if (!closable)
            {
                using var iconFont = new Font(Theme.IconFontName, 9f);
                var icon = new Rectangle(textLeft - 2, face.Y, 16, face.Height);
                TextRenderer.DrawText(
                    g,
                    OwnTabGlyph,
                    iconFont,
                    icon,
                    selected ? Theme.Accent : Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                );
                textLeft = icon.Right + 2;
            }

            int textRight = closable ? face.Right - CloseButtonSize - CloseButtonInset - 2 : face.Right - 4;
            TextRenderer.DrawText(
                g,
                page.Text,
                Font,
                new Rectangle(textLeft, face.Y, Math.Max(1, textRight - textLeft), face.Height),
                selected ? Theme.Heading : Theme.Muted,
                TextFormatFlags.Left
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine
                    | TextFormatFlags.EndEllipsis
            );

            if (closable)
            {
                PaintCloseButton(g, index, tabRect);
            }
        }

        private void PaintCloseButton(Graphics g, int index, Rectangle tabRect)
        {
            Rectangle closeRect = new Rectangle(
                tabRect.Right - CloseButtonSize - CloseButtonInset,
                tabRect.Top + (tabRect.Height - CloseButtonSize) / 2,
                CloseButtonSize,
                CloseButtonSize
            );
            _closeButtonBounds[index] = closeRect;

            bool hover = index == _hoverTab && _hoverClose;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (hover)
            {
                using var halo = new SolidBrush(Theme.DividerStrong);
                var circle = Rectangle.Inflate(closeRect, 3, 3);
                g.FillEllipse(halo, circle);
            }

            using Pen pen = new Pen(hover ? Theme.Ink : Theme.Muted, 1.5f);
            g.DrawLine(pen, closeRect.Left + 3, closeRect.Top + 3, closeRect.Right - 3, closeRect.Bottom - 3);
            g.DrawLine(pen, closeRect.Right - 3, closeRect.Top + 3, closeRect.Left + 3, closeRect.Bottom - 3);
        }
    }
}
