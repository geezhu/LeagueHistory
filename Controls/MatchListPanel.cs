using System.Diagnostics;
using System.Text.RegularExpressions;
using League.Models;
using League.Services;

namespace League.Controls
{
    public class MatchListPanel : Panel, IDisposable
    {
        private ToolTip _tooltip = new ToolTip();

        private string _lastTooltip = "";

        private MatchInfo _matchInfo;

        private Rectangle _detailsRect;

        private Rectangle _replayRect;

        private bool _disposed;

        // 存储图片引用的列表
        private List<Image> _matchImages = new List<Image>();

        public MatchInfo MatchInfo
        {
            get { return _matchInfo; }
            set
            {
                _matchInfo = value;
                Invalidate();
            }
        }

        public event Action<MatchInfo> DetailsClicked;

        public event Action<MatchInfo> ReplayClicked;

        public event Action<string> PlayerIconClicked;
        public bool Compact { get; set; }
        public bool SelectedMatch { get; set; }

        public void RequestReplay() => ReplayClicked?.Invoke(MatchInfo);

        public MatchListPanel(MatchInfo match)
        {
            _matchInfo = match;
            SetStyle(
                ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer,
                value: true
            );
            BorderStyle = BorderStyle.None;
            BackColor = Color.White;
            DoubleBuffered = true;
            Size = new Size(1000, (int)(112 * DeviceDpi / 96f));
            Margin = new Padding(0, 0, 0, 10);
            _tooltip = new ToolTip
            {
                InitialDelay = 100,
                ReshowDelay = 100,
                AutoPopDelay = 15000,
                ShowAlways = true,
            };
        }

        // 添加图片到跟踪列表
        public void TrackImage(Image image)
        {
            if (image != null)
            {
                _matchImages.Add(image);
            }
        }

        // 清理所有图片资源
        public void DisposeImages()
        {
            foreach (var image in _matchImages)
            {
                try
                {
                    if (image != null)
                    {
                        // 直接尝试释放，不检查 IsDisposed
                        image.Dispose();
                    }
                }
                catch { }
            }
            _matchImages.Clear();
        }

        // 清理所有资源
        public void DisposeAllResources()
        {
            DisposeImages();

            // 清理控件内的图片
            var pictureBoxes = this.Controls.OfType<PictureBox>().ToList();
            foreach (var pb in pictureBoxes)
            {
                if (pb.Image != null)
                {
                    pb.Image.Dispose();
                    pb.Image = null;
                }
            }

            // 清理 MatchInfo
            if (MatchInfo != null)
            {
                MatchInfo.RawGameData = null;
                MatchInfo.AllParticipants = null;
                MatchInfo = null;
            }

            // 解绑事件
            this.DetailsClicked = null;
            this.ReplayClicked = null;
        }

        // 🔥 新增：深度清理方法
        public void DeepClean()
        {
            try
            {
                // 1. 清理图片资源
                DisposeImages();

                // 2. 清理MatchInfo中的资源
                if (_matchInfo != null)
                {
                    _matchInfo.Dispose();
                    _matchInfo = null;
                }

                // 3. 清理控件内的图片
                var pictureBoxes = this.Controls.OfType<PictureBox>().ToList();
                foreach (var pb in pictureBoxes)
                {
                    if (pb.Image != null)
                    {
                        try
                        {
                            pb.Image.Dispose();
                        }
                        catch { }
                        pb.Image = null;
                    }
                }

                // 4. 解绑事件
                this.DetailsClicked = null;
                this.ReplayClicked = null;
                this.PlayerIconClicked = null;

                // 5. 清理ToolTip
                if (_tooltip != null)
                {
                    _tooltip.Dispose();
                    _tooltip = null;
                }

                // 6. 清理子控件
                this.Controls.Clear();

                // 7. 清理引用
                _lastTooltip = null;
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Ui, "[MatchListPanel.DeepClean异常]", ex);
            }
        }

        // 🔥 增强 Dispose 方法
        public new void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                // 先深度清理
                DeepClean();

                // 清理事件
                DetailsClicked = null;
                ReplayClicked = null;
                PlayerIconClicked = null;

                // 清理基类资源
                base.Dispose();
                _tooltip?.Dispose();
                _matchInfo?.Dispose();

                GC.SuppressFinalize(this);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        private readonly List<(Rectangle Bounds, string Text)> _imageHints = new();
        private readonly List<Tuple<Rectangle, PlayerInfo>> _playerRegions = new();
        private static readonly Color Ink = Color.FromArgb(36, 48, 65);
        private static readonly Color Muted = Color.FromArgb(111, 123, 139);
        private static readonly Color Accent = Color.FromArgb(46, 103, 190);

        private Rectangle ScreenRect(int x, int y, int width, int height)
        {
            float scale = DeviceDpi / 96f;
            return Rectangle.Round(new RectangleF(x * scale, y * scale, width * scale, height * scale));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _imageHints.Clear();
            _playerRegions.Clear();
            _detailsRect = _replayRect = Rectangle.Empty;
            if (MatchInfo == null)
            {
                return;
            }

            if (Compact)
            {
                PaintCompact(e.Graphics);
                return;
            }

            PaintFull(e.Graphics);
        }

        private void PaintCompact(Graphics g)
        {
            var color = MatchInfo.ResultColor;
            g.Clear(SelectedMatch ? Color.FromArgb(232, 241, 253) : Color.White);
            using var stripe = new SolidBrush(color);
            if (SelectedMatch)
            {
                g.FillRectangle(stripe, 0, 0, 3, Height);
            }

            if (MatchInfo.HeroIcon != null)
            {
                g.DrawImage(MatchInfo.HeroIcon, new Rectangle(6, 11, 40, 40));
            }

            void Draw(string text, int x, int y, int width, Color ink) =>
                TextRenderer.DrawText(
                    g,
                    text,
                    Font,
                    new Rectangle(x, y, Math.Max(1, width), 20),
                    ink,
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                );
            var resultArea = new Rectangle(Math.Max(0, Width - 100), 0, 100, Height);
            using var resultBackground = new System.Drawing.Drawing2D.LinearGradientBrush(
                resultArea,
                Color.FromArgb(0, color),
                Color.FromArgb(32, color),
                0f
            );
            g.FillRectangle(resultBackground, resultArea);
            Draw(MatchInfo.Mode, 50, 9, Width - 112, Color.FromArgb(36, 48, 65));
            string date = DateTime.TryParse(MatchInfo.GameTime, out var played)
                ? played.ToString("MM-dd")
                : MatchInfo.GameTime;
            Draw(date, 50, 31, Width - 112, Color.FromArgb(111, 123, 139));
            using var resultFont = new Font(Font.FontFamily, 12f, FontStyle.Bold);
            TextRenderer.DrawText(
                g,
                MatchInfo.ResultText,
                resultFont,
                new Rectangle(Width - 58, 0, 56, Height),
                color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
            );
            _detailsRect = ClientRectangle;
        }

        private void PaintFull(Graphics g)
        {
            var state = g.Save();
            try
            {
                float scale = DeviceDpi / 96f;
                g.ScaleTransform(scale, scale);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                float width = Width / scale;
                float height = Height / scale;
                g.Clear(Color.White);
                using var border = new Pen(Color.FromArgb(225, 231, 239));
                g.DrawRectangle(border, 0, 0, width - 1, height - 1);
                using var resultBrush = new SolidBrush(MatchInfo.ResultColor);
                g.FillRectangle(resultBrush, 0, 0, 4, height);
                using var canvas = new RowCanvas(this, g);

                PaintIdentity(canvas);
                PaintItems(canvas);
                bool augments = PaintSpellsAndRunes(canvas);
                bool showTeams = width >= 980;
                PaintStatistics(canvas, width, augments, showTeams);
                if (showTeams)
                {
                    PaintTeams(canvas, width);
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        private void PaintIdentity(RowCanvas canvas)
        {
            canvas.Text(MatchInfo.ResultText, canvas.Strong, MatchInfo.ResultColor, 16, 18, 64);
            canvas.Text(MatchInfo.DurationText, canvas.Small, Muted, 16, 47, 65);
            canvas.Text($"#{MatchInfo.Index:00}", canvas.Small, Muted, 16, 78, 58);
            canvas.Icon(MatchInfo.HeroIcon, 88, 18, 52, MatchInfo.ChampionName);
            canvas.Text(MatchInfo.ChampionName, canvas.Strong, Ink, 150, 16, 120);
            canvas.Text(MatchInfo.Mode, canvas.Normal, Muted, 150, 43, 120);
            canvas.Text(MatchInfo.GameTime, canvas.Small, Muted, 88, 80, 184);
        }

        private void PaintItems(RowCanvas canvas)
        {
            canvas.Text(
                $"{MatchInfo.Kills} / {MatchInfo.Deaths} / {MatchInfo.Assists}",
                canvas.Headline,
                Ink,
                282,
                13,
                194,
                34
            );
            for (int i = 0; i < 6; i++)
            {
                var item = MatchInfo.Items?.ElementAtOrDefault(i);
                canvas.Icon(
                    item?.Icon,
                    282 + i * 31,
                    62,
                    28,
                    item == null ? null : item.Name + "\n" + StripHtmlTags(item.Description)
                );
            }
        }

        /// <summary>
        /// 召唤师技能、符文或海克斯强化，以及详情和回放入口；返回本局是否显示海克斯强化。
        /// </summary>
        private bool PaintSpellsAndRunes(RowCanvas canvas)
        {
            for (int i = 0; i < 2; i++)
            {
                canvas.Icon(
                    MatchInfo.SummonerSpells?.ElementAtOrDefault(i),
                    488 + i * 28,
                    18,
                    24,
                    MatchInfo.SpellNames?.ElementAtOrDefault(i)
                        + "\n"
                        + StripHtmlTags(MatchInfo.SpellDescriptions?.ElementAtOrDefault(i))
                );
            }

            bool augments = MatchInfo.Mode?.Contains("海克斯") == true || MatchInfo.QueueId == "q_2400";
            for (int i = 0; i < 6; i++)
            {
                var rune =
                    augments ? MatchInfo.Augments?.ElementAtOrDefault(i)
                    : i < 4 ? MatchInfo.PrimaryRunes?.ElementAtOrDefault(i)
                    : MatchInfo.SecondaryRunes?.ElementAtOrDefault(i - 4);
                canvas.Icon(
                    rune?.Icon,
                    552 + i * (augments ? 27 : 23),
                    18,
                    augments ? 24 : 21,
                    rune == null ? null : rune.name + "\n" + StripHtmlTags(rune.longDesc ?? rune.Description),
                    dark: true
                );
            }
            canvas.Text(augments ? "海克斯强化" : "技能与符文", canvas.Small, Muted, 488, 45, 190);
            _detailsRect = ScreenRect(488, 72, 88, 26);
            _replayRect = ScreenRect(582, 72, 90, 26);
            canvas.Text("查看详情 →", canvas.Normal, Accent, 488, 73, 90);
            canvas.Text(MatchInfo.IsReplayDownloaded ? "播放回放" : "下载回放", canvas.Normal, Accent, 582, 73, 90);
            return augments;
        }

        private void PaintStatistics(RowCanvas canvas, float width, bool augments, bool showTeams)
        {
            int statsX = augments ? 728 : 704;
            int statsWidth = showTeams ? (int)width - 158 - statsX : (int)width - 16 - statsX;
            canvas.Text("伤害  " + MatchInfo.DamageText, canvas.Small, Ink, statsX, 18, statsWidth);
            canvas.Text("经济  " + MatchInfo.GoldText, canvas.Small, Muted, statsX, 44, statsWidth);
            canvas.Text("参团  " + MatchInfo.KPPercentage, canvas.Small, Muted, statsX, 70, statsWidth);
        }

        private void PaintTeams(RowCanvas canvas, float width)
        {
            int teamX = (int)width - 148;
            for (int row = 0; row < 2; row++)
            {
                var team = row == 0 ? MatchInfo.RedTeam : MatchInfo.BlueTeam;
                if (team == null)
                {
                    continue;
                }

                for (int i = 0; i < Math.Min(5, team.Count); i++)
                {
                    var player = team[i];
                    if (player == null)
                    {
                        continue;
                    }

                    int x = teamX + i * 27,
                        y = 18 + row * 34;
                    canvas.Icon(player.Avatar, x, y, 24, player.FullName);
                    _playerRegions.Add(Tuple.Create(ScreenRect(x, y, 24, 24), player));
                    if (player.IsSelf)
                    {
                        using var selfPen = new Pen(Accent, 2);
                        canvas.Graphics.DrawRectangle(selfPen, x, y, 24, 24);
                    }
                }
            }
            canvas.Text("双方阵容", canvas.Small, Muted, teamX, 82, 140);
        }

        /// <summary>
        /// 完整战绩行的绘制资源：布局用 96-DPI 像素表示，坐标变换统一负责缩放；图标提示登记到所属行。
        /// </summary>
        private sealed class RowCanvas : IDisposable
        {
            private readonly MatchListPanel _owner;
            private readonly StringFormat _format = new()
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };

            public RowCanvas(MatchListPanel owner, Graphics graphics)
            {
                _owner = owner;
                Graphics = graphics;
            }

            public Graphics Graphics { get; }
            public Font Normal { get; } = new("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
            public Font Strong { get; } = new("Microsoft YaHei UI", 13.33f, FontStyle.Bold, GraphicsUnit.Pixel);
            public Font Headline { get; } = new("Microsoft YaHei UI", 21.33f, FontStyle.Bold, GraphicsUnit.Pixel);
            public Font Small { get; } = new("Microsoft YaHei UI", 10.67f, FontStyle.Regular, GraphicsUnit.Pixel);

            public void Text(string? text, Font font, Color color, int x, int y, int w, int h = 24)
            {
                using var brush = new SolidBrush(color);
                Graphics.DrawString(text ?? "", font, brush, new RectangleF(x, y, w, h), _format);
            }

            public void Icon(Image? image, int x, int y, int size, string? hint, bool dark = false)
            {
                using var fill = new SolidBrush(dark ? Color.FromArgb(40, 49, 63) : Color.FromArgb(239, 243, 248));
                Graphics.FillRectangle(fill, x, y, size, size);
                if (image != null)
                {
                    Graphics.DrawImage(image, x, y, size, size);
                }

                if (!string.IsNullOrWhiteSpace(hint))
                {
                    _owner._imageHints.Add((_owner.ScreenRect(x, y, size, size), hint));
                }
            }

            public void Dispose()
            {
                _format.Dispose();
                Normal.Dispose();
                Strong.Dispose();
                Headline.Dispose();
                Small.Dispose();
            }
        }

        private IEnumerable<Tuple<Rectangle, PlayerInfo>> GetPlayerIconRegions() => _playerRegions;

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_replayRect.Contains(e.Location))
            {
                // 已过期则不再响应点击
                if (MatchInfo?.IsReplayExpired == true)
                    return;

                ReplayClicked?.Invoke(MatchInfo);
            }
            else if (_detailsRect.Contains(e.Location))
            {
                DetailsClicked?.Invoke(MatchInfo);
            }
            else
            {
                if (MatchInfo == null)
                {
                    return;
                }
                foreach (Tuple<Rectangle, PlayerInfo> tuple in GetPlayerIconRegions())
                {
                    if (!tuple.Item1.Contains(e.Location) || tuple.Item2 == null)
                    {
                        continue;
                    }
                    try
                    {
                        if (tuple.Item2.IsSelf)
                        {
                            _tooltip.Show("这是当前玩家", this, e.Location, 1500);
                        }
                        else
                        {
                            PlayerIconClicked?.Invoke(tuple.Item2.SummonerId.ToString());
                        }
                        break;
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string? text =
                _replayRect.Contains(e.Location) ? "查看本场比赛的回放"
                : _detailsRect.Contains(e.Location) ? "查看本场比赛的详细数据"
                : _imageHints.FirstOrDefault(h => h.Bounds.Contains(e.Location)).Text;
            Cursor =
                _replayRect.Contains(e.Location)
                || _detailsRect.Contains(e.Location)
                || _playerRegions.Any(p => p.Item1.Contains(e.Location))
                    ? Cursors.Hand
                    : Cursors.Default;
            if (text != _lastTooltip)
            {
                _tooltip.SetToolTip(this, text);
                _lastTooltip = text;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _tooltip.SetToolTip(this, null);
            _lastTooltip = "";
            Cursor = Cursors.Default;
        }

        public static string StripHtmlTags(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }
            input = input.Replace("<br>", "\n").Replace("<br/>", "\n");
            return Regex.Replace(input, "<.*?>", "").Trim();
        }

        /// <summary>
        /// 设置回放链接状态（如：回放已过期）
        /// </summary>
        public void SetReplayStatus(MatchInfo matchInfo, string status)
        {
            if (matchInfo == null) return;

            // 与当前面板绑定的是同一场时才更新
            if (_matchInfo != null && _matchInfo.GameId == matchInfo.GameId)
            {
                if (status == "回放已过期" || status == "回放已失效")
                {
                    _matchInfo.IsReplayExpired = true;
                }
                Invalidate();
            }
        }
    }
}
