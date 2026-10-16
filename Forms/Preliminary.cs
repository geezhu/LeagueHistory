using League.ChampionSkin;
using League.Controls;
using League.Models;
using League.PrimaryElection;
using League.Services;

namespace League
{
    /// <summary>
    /// 英雄预选：左边是英雄网格（搜索、分路筛选），点头像加入右边当前的预选列表；右边按位置分成通用、上单、打野、
    /// 中单、下路、辅助、乱斗七个列表，可拖动排序。选人时排位、匹配用分到位置的列表，乱斗类模式用乱斗列表，
    /// 为空时都用通用列表（见 <see cref="PreliminaryLists"/>）。
    /// </summary>
    public partial class Preliminary : Form
    {
        private const int IconSize = 60;

        private readonly LeagueConfig _config;
        private readonly IPreliminarySource _source;
        private readonly Action? _save;
        private readonly CancellationTokenSource _life = new();
        private readonly Dictionary<int, Image> _icons = new();
        private readonly Dictionary<int, PrimaryChampion> _byId = new();
        private readonly Dictionary<string, SkinBrowserButton> _tabs = new();
        private readonly Dictionary<ChampionPosition, SkinBrowserButton> _laneButtons = new();
        private readonly System.Windows.Forms.Timer _searchDelay = new() { Interval = 200 };
        private List<PrimaryChampion> _champions = new();
        private string _listKey = PreliminaryLists.General;
        private ChampionPosition _lane = ChampionPosition.All;

        private readonly TextBox _search = new();
        private readonly Label _count = new();
        private readonly ChampionGridView<PrimaryChampion> _grid = new();
        private readonly PreliminaryListView _list = new();
        private readonly Label _listTitle = new();
        private readonly Label _hint = new();
        private readonly SkinBrowserButton _clear = new();

        public Preliminary(LeagueConfig config)
            : this(config, new LcuPreliminarySource()) { }

        /// <param name="save">Saves the configuration after a change; by default the owning main window saves it.</param>
        public Preliminary(LeagueConfig config, IPreliminarySource source, Action? save = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _source = source;
            _save = save;
            _config.Preliminary ??= new PreliminaryConfig();
            InitializeComponent();
            BackColor = SkinBrowserTheme.Background;
            ForeColor = SkinBrowserTheme.Text;
            BuildLayout();
            BindEvents();
            Load += async (_, _) => await LoadChampionsAsync();
        }

        /// <summary>Completes when the champion grid is shown; tests wait for it.</summary>
        public Task Loading { get; private set; } = Task.CompletedTask;

        private List<PreliminaryHero> CurrentList => PreliminaryLists.Get(_config.Preliminary, _listKey);

        #region 布局

        private void BuildLayout()
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                BackColor = SkinBrowserTheme.Border,
                SplitterWidth = 3,
            };
            split.Panel1.BackColor = SkinBrowserTheme.Panel;
            split.Panel2.BackColor = SkinBrowserTheme.Background;
            split.Panel1.Controls.Add(BuildChampionSide());
            split.Panel2.Controls.Add(BuildListSide());
            Shown += (_, _) => split.SplitterDistance = Math.Max(split.Panel1MinSize, ClientSize.Width / 2 - 20);
            Controls.Add(split);
        }

        private TableLayoutPanel BuildChampionSide()
        {
            var side = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
            };
            // A single column must be told to fill; an auto-sized one grows to the widest child and gets clipped.
            side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(8, 8, 8, 4),
                BackColor = SkinBrowserTheme.Background,
            };
            _search.PlaceholderText = "搜索英雄（名称 / 称号 / 英文）";
            _search.Width = 190;
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.BackColor = SkinBrowserTheme.Panel;
            _search.ForeColor = SkinBrowserTheme.Text;
            _search.Margin = new Padding(0, 4, 10, 4);
            bar.Controls.Add(_search);
            foreach (var (lane, text) in Lanes)
            {
                var button = new SkinBrowserButton
                {
                    Text = text,
                    Width = 46,
                    Height = 26,
                    Margin = new Padding(0, 3, 3, 3),
                };
                button.Click += (_, _) => SelectLane(lane);
                _laneButtons[lane] = button;
                bar.Controls.Add(button);
            }

            _count.AutoSize = true;
            _count.ForeColor = SkinBrowserTheme.MutedText;
            _count.Margin = new Padding(8, 8, 0, 0);
            bar.Controls.Add(_count);
            _grid.Dock = DockStyle.Fill;
            _grid.Icon = id => _icons.GetValueOrDefault(id);
            _grid.Badge = champion =>
                CurrentList.FindIndex(h => h.ChampionId == champion.Id) is int i and >= 0 ? (i + 1).ToString() : null;
            side.Controls.Add(bar, 0, 0);
            side.Controls.Add(_grid, 0, 1);
            HighlightLane();
            return side;
        }

        private static readonly (ChampionPosition Lane, string Text)[] Lanes =
        [
            (ChampionPosition.All, "全部"),
            (ChampionPosition.Top, "上单"),
            (ChampionPosition.Jungle, "打野"),
            (ChampionPosition.Mid, "中单"),
            (ChampionPosition.ADC, "下路"),
            (ChampionPosition.Support, "辅助"),
        ];

        private TableLayoutPanel BuildListSide()
        {
            var side = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
            };
            side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var tabs = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(10, 8, 10, 4),
                BackColor = SkinBrowserTheme.Background,
            };
            foreach (var list in PreliminaryLists.All)
            {
                var tab = new SkinBrowserButton
                {
                    Width = 62,
                    Height = 28,
                    Margin = new Padding(0, 0, 4, 0),
                };
                tab.Click += (_, _) => SelectList(list.Key);
                _tabs[list.Key] = tab;
                tabs.Controls.Add(tab);
            }

            _listTitle.AutoSize = true;
            _listTitle.ForeColor = SkinBrowserTheme.MutedText;
            _listTitle.Margin = new Padding(12, 4, 12, 6);
            _listTitle.MaximumSize = new Size(560, 0);
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(0);
            _list.Icon = id => _icons.GetValueOrDefault(id);
            _list.Champion = id => _byId.GetValueOrDefault(id);
            side.Controls.Add(tabs, 0, 0);
            side.Controls.Add(_listTitle, 0, 1);
            side.Controls.Add(_list, 0, 2);
            side.Controls.Add(BuildFooter(), 0, 3);
            return side;
        }

        private TableLayoutPanel BuildFooter()
        {
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(12, 8, 12, 10),
                BackColor = SkinBrowserTheme.Background,
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _hint.AutoSize = true;
            _hint.ForeColor = SkinBrowserTheme.MutedText;
            _hint.Margin = new Padding(0, 6, 8, 0);
            _hint.Text = "点左边头像加入当前列表，拖动或用 ↑ ↓ 调整顺序。按模式开关在设置「英雄预选」中。";
            _clear.Text = "清空当前列表";
            _clear.AutoSize = true;
            _clear.MinimumSize = new Size(0, 30);
            footer.Controls.Add(_hint, 0, 0);
            footer.Controls.Add(_clear, 1, 0);
            return footer;
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
            _grid.ItemActivated += (_, _) => Add(_grid.Selected);
            _list.Changed += (_, _) => ListChanged();
            _clear.Click += (_, _) => ClearCurrentList();
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
            };
        }

        private void SelectLane(ChampionPosition lane)
        {
            _lane = lane;
            HighlightLane();
            ApplyFilter();
        }

        private void HighlightLane()
        {
            foreach (var (lane, button) in _laneButtons)
            {
                button.Active = lane == _lane;
            }
        }

        /// <summary>Shows a list; a lane list also filters the grid to that lane.</summary>
        private void SelectList(string key)
        {
            _listKey = key;
            var info = PreliminaryLists.All.First(l => l.Key == key);
            _list.EmptyText = $"「{info.Name}」列表还没有英雄。\n点左边的头像加入。";
            _list.SetList(CurrentList);
            _listTitle.Text = Description(key);
            if (info.Lane != ChampionPosition.All || _lane != ChampionPosition.All)
            {
                _lane = info.Lane;
                HighlightLane();
            }

            UpdateTabs();
            ApplyFilter();
        }

        private static string Description(string key) =>
            key switch
            {
                PreliminaryLists.General =>
                    "通用：分到的位置或乱斗模式没有自己的列表时使用；以前保存的预选列表在这里。",
                PreliminaryLists.Aram =>
                    "乱斗：大乱斗、海克斯大乱斗、无限乱斗从板凳席换英雄时按这个顺序；为空时用「通用」。",
                _ =>
                    $"{PreliminaryLists.NameOf(key)}：排位、匹配中分到{PreliminaryLists.NameOf(key)}时，预选第一个没被禁用或选走的英雄；为空时用「通用」。",
            };

        private void UpdateTabs()
        {
            foreach (var list in PreliminaryLists.All)
            {
                int count = PreliminaryLists.Get(_config.Preliminary, list.Key).Count;
                var tab = _tabs[list.Key];
                tab.Text = count > 0 ? $"{list.Name} {count}" : list.Name;
                tab.Active = list.Key == _listKey;
            }
        }

        #endregion

        #region 数据

        private Task LoadChampionsAsync()
        {
            Loading = LoadAndShowAsync();
            return Loading;
        }

        private async Task LoadAndShowAsync()
        {
            _list.EmptyText = "正在读取英雄…";
            try
            {
                _champions = await _source.LoadChampionsAsync(_life.Token);
            }
            catch (OperationCanceledException) when (_life.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is HttpRequestException or InvalidOperationException or IOException)
            {
                AppLog.Shared.Warn(LogCategory.Ui, "英雄预选：读取英雄失败", e);
                _list.EmptyText = "读取英雄失败：" + e.Message;
                _list.Invalidate();
                return;
            }

            foreach (var champion in _champions)
            {
                _byId[champion.Id] = champion;
            }

            SelectList(_listKey);
            _ = LoadIconsAsync(_life.Token);
        }

        private void ApplyFilter()
        {
            var list = Filter(_champions, _lane, _search.Text);
            _grid.SetItems(list);
            _count.Text = $"{list.Count} 个英雄";
        }

        /// <summary>A search covers every lane; otherwise the lane filter applies. Sorted by name.</summary>
        public static List<PrimaryChampion> Filter(
            IEnumerable<PrimaryChampion> champions,
            ChampionPosition lane,
            string? keyword
        )
        {
            string text = keyword?.Trim() ?? "";
            return champions
                .Where(c =>
                    text.Length > 0
                        ? c.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                            || c.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                            || c.Alias.Contains(text, StringComparison.OrdinalIgnoreCase)
                        : lane == ChampionPosition.All || c.GetPositions().Contains(lane)
                )
                .OrderBy(c => c.Name)
                .ToList();
        }

        /// <summary>Adds the champion at the end of the current list, or points at it when it is already there.</summary>
        private void Add(PrimaryChampion? champion)
        {
            if (champion == null)
            {
                return;
            }

            var list = CurrentList;
            if (list.All(h => h.ChampionId != champion.Id))
            {
                list.Add(
                    new PreliminaryHero
                    {
                        ChampionId = champion.Id,
                        ChampionName = champion.Name,
                        Position = champion.GetPositions().FirstOrDefault().ToString(),
                        AddedTime = DateTime.Now,
                        Priority = list.Count + 1,
                    }
                );
                ListChanged();
            }

            _list.Refresh(champion.Id);
        }

        private void ClearCurrentList()
        {
            if (CurrentList.Count == 0)
            {
                return;
            }

            string name = PreliminaryLists.NameOf(_listKey);
            if (
                MessageBox.Show(
                    this,
                    $"确定清空「{name}」列表？",
                    "清空列表",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                ) != DialogResult.Yes
            )
            {
                return;
            }

            CurrentList.Clear();
            _list.SetList(CurrentList);
            ListChanged();
        }

        private void ListChanged()
        {
            UpdateTabs();
            _grid.Invalidate();
            Save();
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

        private async Task LoadIconsAsync(CancellationToken token)
        {
            int size = LogicalToDeviceUnits(IconSize);
            int loaded = 0;
            foreach (var champion in _champions.ToList())
            {
                Image? original;
                try
                {
                    original = await _source.LoadIconAsync(champion.Id, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }

                if (original == null)
                {
                    continue;
                }

                var icon = await Task.Run(() => Scale(original, size), CancellationToken.None);
                if (token.IsCancellationRequested)
                {
                    icon.Dispose();
                    return;
                }

                _icons[champion.Id] = icon;
                if (++loaded % 12 == 0)
                {
                    _grid.Invalidate();
                    _list.Invalidate();
                }
            }

            _grid.Invalidate();
            _list.Invalidate();
        }

        // A small copy keeps every icon cheap to hold; the original is released here.
        private static Image Scale(Image original, int size)
        {
            using (original)
            {
                var scaled = new Bitmap(size, size);
                using var g = Graphics.FromImage(scaled);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(original, 0, 0, size, size);
                return scaled;
            }
        }

        #endregion

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _searchDelay.Stop();
            _life.Cancel();
            Save();
            base.OnFormClosed(e);
        }

        // Icons go only after the views that draw them are gone.
        protected override void OnHandleDestroyed(EventArgs e)
        {
            base.OnHandleDestroyed(e);
            if (!RecreatingHandle)
            {
                _grid.Icon = _ => null;
                _list.Icon = _ => null;
                foreach (var image in _icons.Values)
                {
                    image.Dispose();
                }

                _icons.Clear();
                _searchDelay.Dispose();
                _life.Dispose();
            }
        }
    }
}
