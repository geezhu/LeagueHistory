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
        private const int CloseButtonMargin = 5;

        private Dictionary<int, Rectangle> _closeButtonBounds = new Dictionary<int, Rectangle>();

        public ClosableTabControl()
        {
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
                true
            );
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Normal;
            Appearance = TabAppearance.Normal;

            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Normal;
            Padding = new Point(20, 4); // 标签标题内边距，避免文字与叉重叠

            // 立即触发重绘
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            for (int i = 0; i < TabCount; i++)
            {
                if (_closeButtonBounds.TryGetValue(i, out var rect) && rect.Contains(e.Location))
                {
                    var page = TabPages[i];
                    string? pagePuuid = page.Tag as string;

                    if (
                        pagePuuid != null
                        && string.Equals(pagePuuid, Globals.CurrentPuuid, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        MessageBox.Show(
                            "此选项卡为自己数据，不能关闭",
                            "提示",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Asterisk
                        );
                    }
                    else
                    {
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
                    }
                    break;
                }
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(SystemColors.Control);
            _closeButtonBounds.Clear();

            for (int i = 0; i < TabCount; i++)
            {
                PaintTab(e.Graphics, i);
            }

            if (SelectedIndex >= 0 && SelectedIndex < TabPages.Count)
            {
                PaintPageBorder(e.Graphics, TabPages[SelectedIndex].Bounds);
            }
        }

        // 选中页白底、其他页浅灰底，灰色边框；标题右侧留出关闭按钮的位置。
        private void PaintTab(Graphics g, int index)
        {
            Rectangle tabRect = GetTabRect(index);
            using (Brush b = new SolidBrush(index == SelectedIndex ? Color.White : Color.LightGray))
            {
                g.FillRectangle(b, tabRect);
            }

            using (Pen p = new Pen(Color.Gray))
            {
                g.DrawRectangle(p, tabRect);
            }

            Rectangle textRect = new Rectangle(
                tabRect.X + 5,
                tabRect.Y + 4,
                tabRect.Width - CloseButtonSize - 10,
                tabRect.Height - 8
            );
            TextRenderer.DrawText(
                g,
                TabPages[index].Text,
                Font,
                textRect,
                Color.Black,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            );

            PaintCloseButton(g, index, tabRect);
        }

        private void PaintCloseButton(Graphics g, int index, Rectangle tabRect)
        {
            Rectangle closeRect = new Rectangle(
                tabRect.Right - CloseButtonSize - 4,
                tabRect.Top + (tabRect.Height - CloseButtonSize) / 2,
                CloseButtonSize,
                CloseButtonSize
            );
            _closeButtonBounds[index] = closeRect;

            using Pen pen = new Pen(Color.DarkGray, 2);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawLine(pen, closeRect.Left + 3, closeRect.Top + 3, closeRect.Right - 3, closeRect.Bottom - 3);
            g.DrawLine(pen, closeRect.Right - 3, closeRect.Top + 3, closeRect.Left + 3, closeRect.Bottom - 3);
        }

        private static void PaintPageBorder(Graphics g, Rectangle pageBounds)
        {
            using Pen borderPen = new Pen(Color.Gray);
            g.DrawRectangle(borderPen, pageBounds.X - 2, pageBounds.Y - 2, pageBounds.Width + 3, pageBounds.Height + 3);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            base.OnDrawItem(e);

            var tabRect = GetTabRect(e.Index);
            var tabPage = TabPages[e.Index];

            // 绘制标签文字
            TextRenderer.DrawText(
                e.Graphics,
                tabPage.Text,
                Font,
                tabRect,
                tabPage.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            );

            // 绘制关闭按钮
            Rectangle closeRect = GetCloseButtonRect(tabRect);
            e.Graphics.DrawRectangle(Pens.DarkGray, closeRect);
            e.Graphics.DrawLine(
                Pens.Black,
                closeRect.Left + 3,
                closeRect.Top + 3,
                closeRect.Right - 3,
                closeRect.Bottom - 3
            );
            e.Graphics.DrawLine(
                Pens.Black,
                closeRect.Right - 3,
                closeRect.Top + 3,
                closeRect.Left + 3,
                closeRect.Bottom - 3
            );
        }

        private Rectangle GetCloseButtonRect(Rectangle tabRect)
        {
            return new Rectangle(
                tabRect.Right - CloseButtonSize - CloseButtonMargin,
                tabRect.Top + (tabRect.Height - CloseButtonSize) / 2,
                CloseButtonSize,
                CloseButtonSize
            );
        }
    }
}
