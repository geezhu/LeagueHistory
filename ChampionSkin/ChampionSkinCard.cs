using League.Caching;
using League.Models;
using System.Diagnostics;

namespace League.ChampionSkin
{
    /// <summary>
    /// 单个英雄卡片：上方大图，下方皮肤小格（金黄框为当前选中）。
    /// 图片走 SkinBrowserImageCache；皮肤文件优先 .jpg，兼容旧 .png。
    /// </summary>
    public partial class ChampionSkinCard : UserControl
    {
        #region 字段

        private int _championId;
        private int _selectedSkinId;
        private int _defaultSkinId;
        private Panel? _selectedFrame;
        private SkinBrowserImageCache? _cache;

        public event Action<int, int>? PreferredSkinChanged;

        #endregion

        #region 构造 / 注入

        public ChampionSkinCard()
        {
            InitializeComponent();
            Width = 158;
            BorderStyle = BorderStyle.FixedSingle;
            Padding = new Padding(4);
            Margin = new Padding(6);
        }

        public void SetImageCache(SkinBrowserImageCache cache) => _cache = cache;

        public bool HasHeroImage => picHero != null && picHero.Image != null;
        public int ChampionId => _championId;

        /// <summary>优先 jpg，兼容 png。</summary>
        private static string? FindSkinFile(int skinId)
        {
            string dir = Path.Combine(Application.StartupPath, "Assets", "skins");
            string jpg = Path.Combine(dir, $"{skinId}.jpg");
            try
            {
                if (File.Exists(jpg) && new FileInfo(jpg).Length >= 256)
                    return jpg;
            }
            catch { }

            string png = Path.Combine(dir, $"{skinId}.png");
            try
            {
                if (File.Exists(png) && new FileInfo(png).Length >= 256)
                    return png;
            }
            catch { }

            return null;
        }

        #endregion

        #region 绑定

        public void Bind(ChampionSkinCardModel model)
        {
            if (model == null) return;
            if (picHero == null)
            {
                Debug.WriteLine("[ChampionSkinCard] picHero 为 null，检查 Designer");
                return;
            }

            _championId = model.ChampionId;
            _defaultSkinId = model.DefaultSkinId;

            bool prefOk = model.PreferredSkinId > 0
                && model.PreferredSkinId % 1000 != 0
                && model.Skins != null
                && model.Skins.Any(s => s.SkinId == model.PreferredSkinId);
            _selectedSkinId = prefOk ? model.PreferredSkinId : model.DefaultSkinId;

            picHero.Image = null;
            LoadHeroIcon(model);
            BuildSkinThumbs(model.Skins);
            ApplySelectionHighlight();
            ApplyHeroPreview();
            Tag = model.ChampionId;
        }

        public void RefreshPreference(int preferredSkinId)
        {
            bool ok = preferredSkinId > 0 && preferredSkinId % 1000 != 0;
            _selectedSkinId = ok ? preferredSkinId : _defaultSkinId;
            ApplySelectionHighlight();
            ApplyHeroPreview();
        }

        #endregion

        #region 英雄大图

        private void LoadHeroIcon(ChampionSkinCardModel model)
        {
            if (picHero == null || _cache == null) return;

            var img = _cache.GetOrLoad($"hero:{model.ChampionId}", () =>
            {
                string dir = Path.Combine(Application.StartupPath, "Assets", "champion");
                foreach (var p in new[]
                {
                    Path.Combine(dir, $"{model.ChampionId}.png"),
                    Path.Combine(dir, $"{(model.Name ?? "").Replace(" ", "").Replace("'", "")}.png"),
                    Path.Combine(dir, $"{model.Alias}.png"),
                })
                {
                    var scaled = SkinBrowserImageCache.LoadScaledFromFile(p, 150, 150);
                    if (scaled != null) return scaled;
                }
                return null;
            });

            if (img == null) return;
            picHero.SizeMode = PictureBoxSizeMode.Zoom;
            picHero.Image = img;
        }

        public void SetHeroImage(Image? image)
        {
            if (IsDisposed || image == null || picHero == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(() => SetHeroImage(image));
                return;
            }
            if (picHero.Image != null) return;

            if (_cache != null)
            {
                var img = _cache.GetOrLoad($"hero:{_championId}",
                    () => SkinBrowserImageCache.Scale(image, 150, 150));
                picHero.SizeMode = PictureBoxSizeMode.Zoom;
                picHero.Image = img;
            }
            else
            {
                picHero.SizeMode = PictureBoxSizeMode.Zoom;
                picHero.Image = (Image)image.Clone();
            }

            if (_selectedSkinId % 1000 == 0)
                ApplyHeroPreview();
        }

        private void ApplyHeroPreview()
        {
            if (picHero == null) return;

            bool useSkin = _selectedSkinId > 0 && _selectedSkinId % 1000 != 0;
            if (useSkin && _cache != null)
            {
                string? path = FindSkinFile(_selectedSkinId);
                if (path != null)
                {
                    var skinImg = _cache.GetOrLoad($"skin:{_selectedSkinId}:150", () =>
                        SkinBrowserImageCache.LoadScaledFromFile(path, 150, 150));
                    if (skinImg != null)
                    {
                        picHero.SizeMode = PictureBoxSizeMode.Zoom;
                        picHero.Image = skinImg;
                        return;
                    }
                }
            }

            picHero.Image = _cache?.Get($"hero:{_championId}");
        }

        #endregion

        #region 皮肤小格

        private void BuildSkinThumbs(List<SkinThumbItem>? skins)
        {
            ClearSkinFrames();
            if (skins == null || skins.Count == 0) return;

            string champDir = Path.Combine(Application.StartupPath, "Assets", "champion");
            var tip = new ToolTip { InitialDelay = 50, ReshowDelay = 50, AutoPopDelay = 5000 };

            foreach (var s in skins)
            {
                bool isBase = s.IsBase || (s.SkinId % 1000 == 0);
                var frame = new Panel
                {
                    Width = 32,
                    Height = 32,
                    Margin = new Padding(2),
                    Padding = new Padding(2),
                    BackColor = Color.FromArgb(40, 40, 40),
                    Tag = s
                };
                var box = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BorderStyle = BorderStyle.None,
                    Cursor = Cursors.Hand,
                    BackColor = Color.FromArgb(30, 30, 30),
                    Tag = s
                };

                tip.SetToolTip(box, string.IsNullOrEmpty(s.Name)
                    ? (isBase ? "默认" : s.SkinId.ToString())
                    : s.Name);

                if (isBase && _cache != null)
                {
                    box.Image = _cache.GetOrLoad($"hero:{_championId}:31", () =>
                    {
                        var full = _cache.Get($"hero:{_championId}");
                        if (full != null)
                            return SkinBrowserImageCache.Scale(full, 31, 31);
                        return SkinBrowserImageCache.LoadScaledFromFile(
                            Path.Combine(champDir, $"{_championId}.png"), 31, 31);
                    });
                }
                else if (_cache != null)
                {
                    box.Image = _cache.GetOrLoad($"skin:{s.SkinId}:31", () =>
                    {
                        string? p = FindSkinFile(s.SkinId);
                        return p == null
                            ? null
                            : SkinBrowserImageCache.LoadScaledFromFile(p, 31, 31);
                    });
                }

                box.Click += SkinBox_Click;
                frame.Click += (_, _) => SkinBox_Click(box, EventArgs.Empty);
                frame.Controls.Add(box);
                flpSkins.Controls.Add(frame);
            }
        }

        private void ApplySelectionHighlight()
        {
            Panel? target = null;
            Panel? baseFrame = null;

            foreach (Control c in flpSkins.Controls)
            {
                if (c is not Panel frame || frame.Tag is not SkinThumbItem item) continue;
                frame.BackColor = Color.FromArgb(40, 40, 40);
                if (item.SkinId == _selectedSkinId) target = frame;
                if (item.IsBase || item.SkinId % 1000 == 0) baseFrame = frame;
            }

            target ??= baseFrame;
            if (target == null) return;

            _selectedFrame = target;
            target.BackColor = Color.FromArgb(255, 220, 0);
            if (target.Tag is SkinThumbItem it)
                _selectedSkinId = it.SkinId;
        }

        private void SkinBox_Click(object? sender, EventArgs e)
        {
            if (sender is not PictureBox box) return;
            if (box.Tag is not SkinThumbItem item) return;

            _selectedSkinId = item.SkinId;

            if (box.Parent is Panel frame)
            {
                foreach (Control c in flpSkins.Controls)
                {
                    if (c is Panel p) p.BackColor = Color.FromArgb(40, 40, 40);
                }
                frame.BackColor = Color.FromArgb(255, 220, 0);
                _selectedFrame = frame;
            }

            bool isBase = item.IsBase || item.SkinId % 1000 == 0;
            if (isBase)
            {
                picHero.Image = _cache?.Get($"hero:{_championId}");
                PreferredSkinChanged?.Invoke(_championId, 0);
            }
            else
            {
                if (_cache != null)
                {
                    string? path = FindSkinFile(item.SkinId);
                    picHero.Image = path == null
                        ? null
                        : _cache.GetOrLoad($"skin:{item.SkinId}:150", () =>
                            SkinBrowserImageCache.LoadScaledFromFile(path, 150, 150));
                }
                PreferredSkinChanged?.Invoke(_championId, item.SkinId);
            }
        }

        public void SetSkinImage(int skinId, Image? image)
        {
            if (IsDisposed || image == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(() => SetSkinImage(skinId, image));
                return;
            }

            Image? thumb = _cache != null
                ? _cache.GetOrLoad($"skin:{skinId}:31",
                    () => SkinBrowserImageCache.Scale(image, 31, 31))
                : SkinBrowserImageCache.Scale(image, 31, 31);

            foreach (Control c in flpSkins.Controls)
            {
                if (c is not Panel frame || frame.Tag is not SkinThumbItem item) continue;
                if (item.SkinId != skinId) continue;

                if (frame.Controls.Count > 0 && frame.Controls[0] is PictureBox pb)
                    pb.Image = thumb;

                if (_selectedSkinId == skinId && _cache != null)
                {
                    string? path = FindSkinFile(skinId);
                    picHero.Image = _cache.GetOrLoad($"skin:{skinId}:150", () =>
                    {
                        if (path != null)
                        {
                            var fromFile = SkinBrowserImageCache.LoadScaledFromFile(path, 150, 150);
                            if (fromFile != null) return fromFile;
                        }
                        return SkinBrowserImageCache.Scale(image, 150, 150);
                    });
                }
                break;
            }
        }

        #endregion

        #region 清理

        private void ClearSkinFrames()
        {
            foreach (Control c in flpSkins.Controls.Cast<Control>().ToList())
                DisposeSkinFrame(c);
            flpSkins.Controls.Clear();
            _selectedFrame = null;
        }

        private void DisposeSkinFrame(Control c)
        {
            if (c is Panel frame)
            {
                foreach (Control child in frame.Controls)
                {
                    if (child is PictureBox pb)
                        pb.Image = null;
                    child.Dispose();
                }
            }
            c.Dispose();
        }

        public void ReleaseResources()
        {
            if (picHero != null)
                picHero.Image = null;
            ClearSkinFrames();
        }

        #endregion
    }
}