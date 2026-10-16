using League.Models;
using League.PrimaryElection;
using League.Services;

namespace League.ChampionSkin
{
    /// <summary>
    /// 英雄皮肤：左边是英雄网格（搜索、分路、已有、有皮肤筛选），右边是选中皮肤的原画和该英雄已拥有的皮肤。
    /// 点皮肤预览原画，「设为自动皮肤」或双击后，选人锁定该英雄时自动换成这款皮肤。
    /// 图片按需读取：头像和皮肤小图跨英雄保留，原画只保留当前英雄的。
    /// </summary>
    public partial class ChampionSkinBrowser : Form
    {
        private const int IconSize = 60;
        private const int TileSize = 96;

        private readonly LeagueConfig _config;
        private readonly ISkinBrowserSource _source;
        private readonly Action? _save;
        private readonly CancellationTokenSource _life = new();
        private readonly Dictionary<int, Image> _icons = new();
        private readonly Dictionary<int, Image> _tiles = new();
        private readonly Dictionary<int, Image> _splashes = new();
        private readonly Dictionary<ChampionPosition, SkinBrowserButton> _positionButtons = new();
        private readonly System.Windows.Forms.Timer _searchDelay = new() { Interval = 250 };
        private List<ChampionSkinCardModel> _models = new();
        private ChampionPosition _position = ChampionPosition.All;
        private CancellationTokenSource? _championLife;

        private readonly TextBox _search = new();
        private readonly CheckBox _ownedOnly = new();
        private readonly CheckBox _hasSkinOnly = new();
        private readonly Label _stats = new();
        private readonly SkinBrowserButton _clearAll = new();
        private readonly ChampionGridView _grid = new();
        private readonly SkinSplashView _splash = new();
        private readonly SkinStripView _strip = new();
        private readonly SkinBrowserButton _setPreferred = new(primary: true);
        private readonly SkinBrowserButton _restoreDefault = new();
        private readonly Label _hint = new();

        public ChampionSkinBrowser(LeagueConfig config)
            : this(config, new LcuSkinBrowserSource()) { }

        /// <param name="save">Saves the configuration after a change; by default the owning main window saves it.</param>
        public ChampionSkinBrowser(LeagueConfig config, ISkinBrowserSource source, Action? save = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _source = source;
            _save = save;
            _config.PreferredChampionSkins ??= new Dictionary<int, int>();
            InitializeComponent();
            BackColor = SkinBrowserTheme.Background;
            ForeColor = SkinBrowserTheme.Text;
            BuildLayout();
            BindEvents();
            Load += async (_, _) => await LoadModelsAsync();
        }

        /// <summary>Completes when the champion list is shown; tests wait for it.</summary>
        public Task Loading { get; private set; } = Task.CompletedTask;

        #region 布局

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(BuildToolbar(), 0, 0);
            root.Controls.Add(BuildBody(), 0, 1);
            Controls.Add(root);
        }

        private FlowLayoutPanel BuildToolbar()
        {
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Padding = new Padding(10, 8, 10, 6),
                BackColor = SkinBrowserTheme.Background,
            };
            _search.PlaceholderText = "搜索英雄（名称 / 称号 / 英文）";
            _search.Width = 220;
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.BackColor = SkinBrowserTheme.Panel;
            _search.ForeColor = SkinBrowserTheme.Text;
            _search.Margin = new Padding(0, 4, 12, 4);
            bar.Controls.Add(_search);

            foreach (
                var (position, text) in new[]
                {
                    (ChampionPosition.All, "全部"),
                    (ChampionPosition.Top, "上单"),
                    (ChampionPosition.Jungle, "打野"),
                    (ChampionPosition.Mid, "中单"),
                    (ChampionPosition.ADC, "下路"),
                    (ChampionPosition.Support, "辅助"),
                }
            )
            {
                var button = ToolbarButton(text);
                button.Width = 56;
                button.Margin = new Padding(0, 3, 4, 3);
                button.Click += (_, _) => SelectPosition(position);
                _positionButtons[position] = button;
                bar.Controls.Add(button);
            }

            ConfigureCheck(_ownedOnly, "仅已有英雄", _config.SkinBrowserOwnedOnly);
            ConfigureCheck(_hasSkinOnly, "仅有皮肤的英雄", _config.SkinBrowserHasSkinOnly);
            _ownedOnly.Margin = new Padding(14, 6, 6, 3);
            bar.Controls.Add(_ownedOnly);
            bar.Controls.Add(_hasSkinOnly);

            _stats.AutoSize = true;
            _stats.ForeColor = SkinBrowserTheme.MutedText;
            _stats.Margin = new Padding(14, 8, 0, 3);
            bar.Controls.Add(_stats);
            HighlightPosition();
            return bar;
        }

        private SplitContainer BuildBody()
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                BackColor = SkinBrowserTheme.Border,
                SplitterWidth = 3,
                Margin = new Padding(0),
            };
            split.Panel1.BackColor = SkinBrowserTheme.Panel;
            split.Panel2.BackColor = SkinBrowserTheme.Background;
            _grid.Dock = DockStyle.Fill;
            _grid.Icon = id => _icons.GetValueOrDefault(id);
            split.Panel1.Controls.Add(_grid);
            split.Panel2.Controls.Add(BuildDetail());
            // The splitter distance needs the final size: set it once the window has its layout.
            Shown += (_, _) => split.SplitterDistance = LogicalToDeviceUnits(372);
            return split;
        }

        private TableLayoutPanel BuildDetail()
        {
            var detail = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
            };
            detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detail.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(176)));
            detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _splash.Dock = DockStyle.Fill;
            _splash.Margin = new Padding(0);
            _strip.Dock = DockStyle.Fill;
            _strip.Margin = new Padding(0);
            _strip.Tile = id => _tiles.GetValueOrDefault(id);
            detail.Controls.Add(_splash, 0, 0);
            detail.Controls.Add(_strip, 0, 1);
            detail.Controls.Add(BuildActions(), 0, 2);
            return detail;
        }

        // Actions for the shown champion on the left; clearing every champion sits apart on the right.
        private TableLayoutPanel BuildActions()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(12, 8, 12, 10),
                BackColor = SkinBrowserTheme.Background,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0),
            };
            _setPreferred.Text = "★ 设为自动皮肤";
            _restoreDefault.Text = "恢复默认皮肤";
            foreach (var button in new[] { _setPreferred, _restoreDefault })
            {
                button.AutoSize = true;
                button.MinimumSize = new Size(120, 32);
                button.Margin = new Padding(0, 0, 8, 0);
                actions.Controls.Add(button);
            }

            _hint.AutoSize = true;
            _hint.ForeColor = SkinBrowserTheme.MutedText;
            _hint.Margin = new Padding(8, 8, 0, 0);
            actions.Controls.Add(_hint);

            _clearAll.Text = "清除全部自动皮肤";
            _clearAll.AutoSize = true;
            _clearAll.MinimumSize = new Size(0, 32);
            _clearAll.Margin = new Padding(8, 0, 0, 0);
            row.Controls.Add(actions, 0, 0);
            row.Controls.Add(_clearAll, 1, 0);
            return row;
        }

        private static SkinBrowserButton ToolbarButton(string text) => new() { Text = text, Height = 26 };

        private static void ConfigureCheck(CheckBox check, string text, bool value)
        {
            check.Text = text;
            check.Checked = value;
            check.AutoSize = true;
            check.ForeColor = SkinBrowserTheme.Text;
            check.Margin = new Padding(6, 6, 6, 3);
        }

        private void HighlightPosition()
        {
            foreach (var (position, button) in _positionButtons)
            {
                button.Active = position == _position;
            }
        }

        #endregion

        #region 事件

        private void BindEvents()
        {
            _searchDelay.Tick += (_, _) =>
            {
                _searchDelay.Stop();
                ApplyFilter();
            };
            _search.TextChanged += (_, _) =>
            {
                _searchDelay.Stop();
                _searchDelay.Start();
            };
            _ownedOnly.CheckedChanged += (_, _) =>
            {
                _config.SkinBrowserOwnedOnly = _ownedOnly.Checked;
                ApplyFilter();
            };
            _hasSkinOnly.CheckedChanged += (_, _) =>
            {
                _config.SkinBrowserHasSkinOnly = _hasSkinOnly.Checked;
                ApplyFilter();
            };
            _clearAll.Click += (_, _) => ClearAllPreferences();
            _grid.SelectionChanged += (_, _) => ShowChampion(_grid.Selected);
            _strip.SelectionChanged += (_, _) => ShowSkin();
            _strip.Activated += (_, _) => SetPreferred(_strip.Selected);
            _setPreferred.Click += (_, _) => SetPreferred(_strip.Selected);
            _restoreDefault.Click += (_, _) => SetPreferred(_grid.Selected?.Skins.FirstOrDefault(s => s.IsDefault));
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
            };
        }

        private void SelectPosition(ChampionPosition position)
        {
            _position = position;
            HighlightPosition();
            ApplyFilter();
        }

        #endregion

        #region 数据与筛选

        private Task LoadModelsAsync()
        {
            Loading = LoadAndShowAsync();
            return Loading;
        }

        private async Task LoadAndShowAsync()
        {
            _splash.Show(null, "", "", false, "正在读取英雄和皮肤…");
            try
            {
                _models = await _source.LoadAsync(_config.PreferredChampionSkins, _life.Token);
            }
            catch (OperationCanceledException) when (_life.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is HttpRequestException or InvalidOperationException or IOException)
            {
                AppLog.Shared.Warn(LogCategory.Ui, "皮肤浏览：读取英雄失败", e);
                _splash.Show(null, "", "", false, "读取英雄失败：" + e.Message);
                return;
            }

            if (_models.Count == 0)
            {
                _splash.Show(null, "", "", false, "没有读到英雄数据。");
                return;
            }

            ApplyFilter();
            _ = LoadIconsAsync(_life.Token);
        }

        private void ApplyFilter()
        {
            var list = Filter(_models, _position, _ownedOnly.Checked, _hasSkinOnly.Checked, _search.Text);
            _grid.SetItems(list);
            UpdateStats();
            UpdateHint();
        }

        private void UpdateStats()
        {
            var listed = _grid.Items;
            _stats.Text =
                $"{listed.Count} 个英雄 · {listed.Count(m => m.HasNonDefaultSkin)} 个有皮肤 · "
                + $"{_models.Count(m => m.PreferredSkinId > 0)} 个已设自动皮肤";
        }

        /// <summary>按分路、已拥有、有皮肤和搜索词（名称、称号、英文名）筛选，按名称排序。</summary>
        public static List<ChampionSkinCardModel> Filter(
            IEnumerable<ChampionSkinCardModel> models,
            ChampionPosition position,
            bool ownedOnly,
            bool hasSkinOnly,
            string? keyword
        )
        {
            string text = keyword?.Trim() ?? "";
            return models
                .Where(m => position == ChampionPosition.All || m.Positions.Contains(position))
                .Where(m => !ownedOnly || m.IsOwned)
                .Where(m => !hasSkinOnly || m.HasNonDefaultSkin)
                .Where(m =>
                    text.Length == 0
                    || m.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || m.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || m.Alias.Contains(text, StringComparison.OrdinalIgnoreCase)
                )
                .OrderBy(m => m.Name)
                .ToList();
        }

        #endregion

        #region 英雄与皮肤

        private void ShowChampion(ChampionSkinCardModel? champion)
        {
            _championLife?.Cancel();
            _championLife?.Dispose();
            _championLife = null;
            ReleaseSplashes();
            if (champion == null)
            {
                _strip.SetItems([], 0, 0);
                _splash.Show(null, "", "", false, _models.Count > 0 ? "没有符合条件的英雄" : "");
                UpdateButtons();
                return;
            }

            _championLife = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
            int selected = champion.PreferredSkinId > 0 ? champion.PreferredSkinId : champion.DefaultSkinId;
            _strip.SetItems(champion.Skins, selected, champion.PreferredSkinId);
            _ = LoadTilesAsync(champion, _championLife.Token);
        }

        private void ShowSkin()
        {
            var champion = _grid.Selected;
            var skin = _strip.Selected;
            UpdateButtons();
            if (champion == null || skin == null)
            {
                return;
            }

            string title = SkinTitle(champion, skin);
            string subtitle = champion.Name + (champion.Title.Length > 0 ? " · " + champion.Title : "");
            bool preferred = !skin.IsDefault && champion.PreferredSkinId == skin.SkinId;
            var image = _splashes.GetValueOrDefault(skin.SkinId);
            _splash.Show(image, title, subtitle, preferred, image == null ? "原画加载中…" : "");
            if (image == null && _championLife != null)
            {
                _ = LoadSplashAsync(champion, skin, _championLife.Token);
            }
        }

        // The client names the default look after the champion, e.g. 光辉女郎 拉克丝.
        private static string SkinTitle(ChampionSkinCardModel champion, SkinThumbItem skin) =>
            skin.IsDefault ? (champion.Name + " " + champion.Title).Trim() : skin.Name;

        private void UpdateButtons()
        {
            var champion = _grid.Selected;
            var skin = _strip.Selected;
            bool already = skin != null && !skin.IsDefault && champion?.PreferredSkinId == skin.SkinId;
            _setPreferred.Text = already ? "★ 已是自动皮肤" : "★ 设为自动皮肤";
            _setPreferred.Enabled = champion != null && skin != null && !skin.IsDefault && !already;
            _restoreDefault.Enabled = champion?.PreferredSkinId > 0;
        }

        private void UpdateHint() =>
            _hint.Text = _config.EnableAutoSkinSelect
                ? "自动换皮肤已开启：选人锁定英雄后换成 ★ 皮肤。双击皮肤也可设定。"
                : "自动换皮肤未开启：在设置「英雄预选」中打开后，锁定英雄时换成 ★ 皮肤。";

        /// <summary>The default skin clears the champion's preference; another owned skin becomes it.</summary>
        private void SetPreferred(SkinThumbItem? skin)
        {
            var champion = _grid.Selected;
            if (champion == null || skin == null)
            {
                return;
            }

            champion.PreferredSkinId = skin.IsDefault ? 0 : skin.SkinId;
            if (champion.PreferredSkinId == 0)
            {
                _config.PreferredChampionSkins.Remove(champion.ChampionId);
            }
            else
            {
                _config.PreferredChampionSkins[champion.ChampionId] = champion.PreferredSkinId;
            }

            Save();
            _strip.SetPreferred(champion.PreferredSkinId);
            _grid.Invalidate();
            UpdateStats();
            ShowSkin();
        }

        private void ClearAllPreferences()
        {
            if (
                MessageBox.Show(
                    this,
                    "确定清除全部自动皮肤，所有英雄恢复默认皮肤？",
                    "清除全部自动皮肤",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                ) != DialogResult.Yes
            )
            {
                return;
            }

            _config.PreferredChampionSkins.Clear();
            foreach (var model in _models)
            {
                model.PreferredSkinId = 0;
            }

            Save();
            ApplyFilter();
        }

        private void Save()
        {
            if (_save != null)
            {
                _save();
            }
            else if (Owner is FormMain main)
            {
                main.SaveAppConfig();
            }
        }

        #endregion

        #region 图片

        private async Task LoadIconsAsync(CancellationToken token)
        {
            int size = LogicalToDeviceUnits(IconSize);
            int loaded = 0;
            // Listed champions first, so the visible grid fills in before the hidden ones.
            foreach (var champion in _grid.Items.Concat(_models).DistinctBy(m => m.ChampionId).ToList())
            {
                var icon = await LoadScaledAsync(() => _source.LoadIconAsync(champion, token), size, token);
                if (token.IsCancellationRequested)
                {
                    icon?.Dispose();
                    return;
                }

                if (icon != null)
                {
                    _icons[champion.ChampionId] = icon;
                    if (++loaded % 12 == 0)
                    {
                        _grid.Invalidate();
                    }
                }
            }

            _grid.Invalidate();
        }

        private async Task LoadTilesAsync(ChampionSkinCardModel champion, CancellationToken token)
        {
            int size = LogicalToDeviceUnits(TileSize);
            foreach (var skin in champion.Skins.Where(s => !_tiles.ContainsKey(s.SkinId)).ToList())
            {
                var tile = await LoadScaledAsync(
                    () => _source.LoadSkinAsync(champion, skin, SkinImageKind.Tile, token),
                    size,
                    token
                );
                if (token.IsCancellationRequested)
                {
                    tile?.Dispose();
                    return;
                }

                if (tile != null && !_tiles.TryAdd(skin.SkinId, tile))
                {
                    tile.Dispose();
                }

                _strip.Invalidate();
            }
        }

        private async Task LoadSplashAsync(ChampionSkinCardModel champion, SkinThumbItem skin, CancellationToken token)
        {
            Image? image;
            try
            {
                image = await _source.LoadSkinAsync(champion, skin, SkinImageKind.Splash, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }

            // A late result for a champion that is no longer shown is dropped.
            if (token.IsCancellationRequested)
            {
                image?.Dispose();
                return;
            }

            if (image != null && !_splashes.TryAdd(skin.SkinId, image))
            {
                image.Dispose();
            }

            if (_strip.Selected?.SkinId == skin.SkinId)
            {
                if (image == null)
                {
                    _splash.Show(
                        null,
                        SkinTitle(champion, skin),
                        champion.Name,
                        false,
                        "读不到这张原画：客户端未连接或资源缺失。"
                    );
                }
                else
                {
                    ShowSkin();
                }
            }
        }

        // Scaled copies keep a long session small; the original decode is released immediately.
        private static async Task<Image?> LoadScaledAsync(Func<Task<Image?>> load, int size, CancellationToken token)
        {
            Image? original;
            try
            {
                original = await load();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return null;
            }

            if (original == null)
            {
                return null;
            }

            return await Task.Run(
                () =>
                {
                    using (original)
                    {
                        var scaled = new Bitmap(size, size);
                        using var g = Graphics.FromImage(scaled);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(original, 0, 0, size, size);
                        return (Image?)scaled;
                    }
                },
                CancellationToken.None
            );
        }

        private void ReleaseSplashes()
        {
            _splash.Show(null, "", "", false, "");
            foreach (var image in _splashes.Values)
            {
                image.Dispose();
            }

            _splashes.Clear();
        }

        #endregion

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _searchDelay.Stop();
            _life.Cancel();
            _championLife?.Cancel();
            Save();
            base.OnFormClosed(e);
        }

        // Images go only after the views that draw them are gone.
        protected override void OnHandleDestroyed(EventArgs e)
        {
            base.OnHandleDestroyed(e);
            if (!RecreatingHandle)
            {
                _grid.Icon = _ => null;
                _strip.Tile = _ => null;
                ReleaseSplashes();
                foreach (var image in _icons.Values.Concat(_tiles.Values))
                {
                    image.Dispose();
                }

                _icons.Clear();
                _tiles.Clear();
                _searchDelay.Dispose();
                _championLife?.Dispose();
                _life.Dispose();
            }
        }
    }
}
