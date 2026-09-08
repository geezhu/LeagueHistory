using System.Diagnostics;
using System.Text.RegularExpressions;
using League.Models;

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
            get
            {
                return _matchInfo;
            }
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
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
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
                ShowAlways = true
            };
        }

        // 添加图片到跟踪列表
        public void TrackImage(Image image)
        {
            if (image != null)
                _matchImages.Add(image);
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
                Debug.WriteLine($"[MatchListPanel.DeepClean异常] {ex.Message}");
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


        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

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
            if (MatchInfo == null) return;

            if (Compact)
            {
                var color = MatchInfo.ResultColor;
                e.Graphics.Clear(SelectedMatch ? Color.FromArgb(232,241,253) : Color.White);
                using var stripe = new SolidBrush(color);
                if (SelectedMatch) e.Graphics.FillRectangle(stripe, 0, 0, 3, Height);
                if (MatchInfo.HeroIcon != null) e.Graphics.DrawImage(MatchInfo.HeroIcon, new Rectangle(6,11,40,40));
                void Draw(string text, int x, int y, int width, Color ink) => TextRenderer.DrawText(e.Graphics, text, Font,
                    new Rectangle(x,y,Math.Max(1,width),20), ink, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                var resultArea = new Rectangle(Math.Max(0, Width-100),0,100,Height);
                using var resultBackground = new System.Drawing.Drawing2D.LinearGradientBrush(resultArea,
                    Color.FromArgb(0,color), Color.FromArgb(32,color), 0f);
                e.Graphics.FillRectangle(resultBackground,resultArea);
                Draw(MatchInfo.Mode,50,9,Width-112,Color.FromArgb(36,48,65));
                string date = DateTime.TryParse(MatchInfo.GameTime,out var played) ? played.ToString("MM-dd") : MatchInfo.GameTime;
                Draw(date,50,31,Width-112,Color.FromArgb(111,123,139));
                using var resultFont = new Font(Font.FontFamily, 12f, FontStyle.Bold);
                TextRenderer.DrawText(e.Graphics, MatchInfo.ResultText, resultFont,
                    new Rectangle(Width-58,0,56,Height),color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                _detailsRect = ClientRectangle;
                return;
            }

            var g = e.Graphics;
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
                Color result = MatchInfo.ResultColor;
                using var resultBrush = new SolidBrush(result);
                g.FillRectangle(resultBrush, 0, 0, 4, height);
                // Layout is expressed in 96-DPI pixels; the transform applies display scaling once.
                using var normal = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
                using var strong = new Font("Microsoft YaHei UI", 13.33f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var headline = new Font("Microsoft YaHei UI", 21.33f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var small = new Font("Microsoft YaHei UI", 10.67f, FontStyle.Regular, GraphicsUnit.Pixel);
                using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

                void Text(string? text, Font font, Color color, int x, int y, int w, int h = 24)
                {
                    using var brush = new SolidBrush(color);
                    g.DrawString(text ?? "", font, brush, new RectangleF(x, y, w, h), format);
                }
                void Icon(Image? image, int x, int y, int size, string? hint, bool dark = false)
                {
                    using var fill = new SolidBrush(dark ? Color.FromArgb(40, 49, 63) : Color.FromArgb(239, 243, 248));
                    g.FillRectangle(fill, x, y, size, size);
                    if (image != null) g.DrawImage(image, x, y, size, size);
                    if (!string.IsNullOrWhiteSpace(hint)) _imageHints.Add((ScreenRect(x, y, size, size), hint));
                }

                Text(MatchInfo.ResultText, strong, result, 16, 18, 64);
                Text(MatchInfo.DurationText, small, Muted, 16, 47, 65);
                Text($"#{MatchInfo.Index:00}", small, Muted, 16, 78, 58);
                Icon(MatchInfo.HeroIcon, 88, 18, 52, MatchInfo.ChampionName);
                Text(MatchInfo.ChampionName, strong, Ink, 150, 16, 120);
                Text(MatchInfo.Mode, normal, Muted, 150, 43, 120);
                Text(MatchInfo.GameTime, small, Muted, 88, 80, 184);

                Text($"{MatchInfo.Kills} / {MatchInfo.Deaths} / {MatchInfo.Assists}", headline, Ink, 282, 13, 194, 34);
                for (int i = 0; i < 6; i++)
                {
                    var item = MatchInfo.Items?.ElementAtOrDefault(i);
                    Icon(item?.Icon, 282 + i * 31, 62, 28, item == null ? null : item.Name + "\n" + StripHtmlTags(item.Description));
                }
                for (int i = 0; i < 2; i++)
                    Icon(MatchInfo.SummonerSpells?.ElementAtOrDefault(i), 488 + i * 28, 18, 24,
                        MatchInfo.SpellNames?.ElementAtOrDefault(i) + "\n" + StripHtmlTags(MatchInfo.SpellDescriptions?.ElementAtOrDefault(i)));
                bool augments = MatchInfo.Mode?.Contains("海克斯") == true || MatchInfo.QueueId == "q_2400";
                for (int i = 0; i < 6; i++)
                {
                    var rune = augments ? MatchInfo.Augments?.ElementAtOrDefault(i)
                        : i < 4 ? MatchInfo.PrimaryRunes?.ElementAtOrDefault(i) : MatchInfo.SecondaryRunes?.ElementAtOrDefault(i - 4);
                    Icon(rune?.Icon, 552 + i * (augments ? 27 : 23), 18, augments ? 24 : 21,
                        rune == null ? null : rune.name + "\n" + StripHtmlTags(rune.longDesc ?? rune.Description), dark: true);
                }
                Text(augments ? "海克斯强化" : "技能与符文", small, Muted, 488, 45, 190);
                _detailsRect = ScreenRect(488, 72, 88, 26);
                _replayRect = ScreenRect(582, 72, 90, 26);
                Text("查看详情 →", normal, Accent, 488, 73, 90);
                Text(MatchInfo.IsReplayDownloaded ? "播放回放" : "下载回放", normal, Accent, 582, 73, 90);

                bool showTeams = width >= 980;
                int statsX = augments ? 728 : 704;
                int statsWidth = showTeams ? (int)width - 158 - statsX : (int)width - 16 - statsX;
                Text("伤害  " + MatchInfo.DamageText, small, Ink, statsX, 18, statsWidth);
                Text("经济  " + MatchInfo.GoldText, small, Muted, statsX, 44, statsWidth);
                Text("参团  " + MatchInfo.KPPercentage, small, Muted, statsX, 70, statsWidth);
                if (showTeams)
                {
                    int teamX = (int)width - 148;
                    for (int row = 0; row < 2; row++)
                    {
                        var team = row == 0 ? MatchInfo.RedTeam : MatchInfo.BlueTeam;
                        if (team == null) continue;
                        for (int i = 0; i < Math.Min(5, team.Count); i++)
                        {
                            var player = team[i];
                            if (player == null) continue;
                            int x = teamX + i * 27, y = 18 + row * 34;
                            Icon(player.Avatar, x, y, 24, player.FullName);
                            _playerRegions.Add(Tuple.Create(ScreenRect(x, y, 24, 24), player));
                            if (player.IsSelf)
                            {
                                using var selfPen = new Pen(Accent, 2);
                                g.DrawRectangle(selfPen, x, y, 24, 24);
                            }
                        }
                    }
                    Text("双方阵容", small, Muted, teamX, 82, 140);
                }
            }
            finally { g.Restore(state); }
        }

        private IEnumerable<Tuple<Rectangle, PlayerInfo>> GetPlayerIconRegions() => _playerRegions;
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_replayRect.Contains(e.Location))
            {
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
            string? text = _replayRect.Contains(e.Location) ? "查看本场比赛的回放"
                : _detailsRect.Contains(e.Location) ? "查看本场比赛的详细数据"
                : _imageHints.FirstOrDefault(h => h.Bounds.Contains(e.Location)).Text;
            Cursor = _replayRect.Contains(e.Location) || _detailsRect.Contains(e.Location)
                || _playerRegions.Any(p => p.Item1.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
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
    }
}
