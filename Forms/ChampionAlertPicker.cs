using League.ChampionSkin;
using League.PrimaryElection;
using League.Services;

namespace League;

/// <summary>
/// 英雄提醒：和英雄预选同样的布局。左边是英雄网格（搜索、分路筛选），点头像加入右边的提醒列表；右边点头像移除。
/// 游戏开始时敌方阵容里有列表中的英雄就播放提示音（见 <see cref="ChampionAlerts"/>）。
/// </summary>
public sealed class ChampionAlertPicker : Form
{
    private const int IconSize = 60;

    private readonly IPreliminarySource _source;
    private readonly Action<IReadOnlyList<int>> _save;
    private readonly Action _preview;
    private readonly CancellationTokenSource _life = new();
    private readonly Dictionary<int, Image> _icons = new();
    private readonly Dictionary<ChampionPosition, SkinBrowserButton> _laneButtons = new();
    private readonly System.Windows.Forms.Timer _searchDelay = new() { Interval = 200 };
    private readonly List<int> _watched;
    private List<PrimaryChampion> _champions = new();
    private ChampionPosition _lane = ChampionPosition.All;

    private readonly TextBox _search = new();
    private readonly Label _count = new();
    private readonly ChampionGridView<PrimaryChampion> _grid = new();
    private readonly ChampionGridView<PrimaryChampion> _list = new();
    private readonly Label _listTitle = new();
    private readonly Label _status = new();
    private readonly SkinBrowserButton _test = new() { Text = "试听提示音" };
    private readonly SkinBrowserButton _clear = new() { Text = "清空列表" };

    /// <param name="watched">The champions already on the list.</param>
    /// <param name="save">Receives the whole list after every change.</param>
    /// <param name="preview">Plays the alert sound once.</param>
    public ChampionAlertPicker(
        IReadOnlyList<int> watched,
        Action<IReadOnlyList<int>> save,
        Action preview,
        IPreliminarySource? source = null
    )
    {
        // Lists saved before variants were filtered may hold a classic-mode variant's id (60000+);
        // the grid lists the champion itself.
        _watched = watched.Select(ChampionAlerts.BaseId).Distinct().ToList();
        _save = save;
        _preview = preview;
        _source = source ?? new LcuPreliminarySource();
        Text = "英雄提醒";
        Font = new Font(SkinBrowserTheme.FontName, 9F);
        ClientSize = new Size(1120, 720);
        MinimumSize = new Size(880, 560);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        BackColor = SkinBrowserTheme.Background;
        ForeColor = SkinBrowserTheme.Text;
        BuildLayout();
        BindEvents();
        Load += (_, _) => Loading = LoadAndShowAsync();
    }

    /// <summary>Completes when the champion grid is shown; tests wait for it.</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>The champions on the list, in the order they were added.</summary>
    public IReadOnlyList<int> Watched => _watched;

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
        Shown += (_, _) => split.SplitterDistance = Math.Max(split.Panel1MinSize, ClientSize.Width * 3 / 5);
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
        _grid.Marked = champion => _watched.Contains(champion.Id);
        _grid.MarkGlyph = "✓";
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
            RowCount = 3,
        };
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _listTitle.AutoSize = true;
        _listTitle.ForeColor = SkinBrowserTheme.Text;
        _listTitle.Margin = new Padding(12, 12, 12, 8);
        _listTitle.MaximumSize = new Size(440, 0);
        _list.Dock = DockStyle.Fill;
        _list.Icon = id => _icons.GetValueOrDefault(id);
        side.Controls.Add(_listTitle, 0, 0);
        side.Controls.Add(_list, 0, 1);
        side.Controls.Add(BuildFooter(), 0, 2);
        return side;
    }

    private FlowLayoutPanel BuildFooter()
    {
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 8, 12, 10),
            BackColor = SkinBrowserTheme.Background,
        };
        _status.AutoSize = true;
        _status.ForeColor = SkinBrowserTheme.MutedText;
        _status.MaximumSize = new Size(420, 0);
        _status.Margin = new Padding(0, 0, 0, 8);
        _status.Text = "点左边头像加入，点右边头像移除。游戏开始时敌方出现列表中的英雄会播放提示音。";
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        foreach (var button in new[] { _test, _clear })
        {
            button.AutoSize = true;
            button.MinimumSize = new Size(0, 30);
            button.Margin = new Padding(0, 0, 8, 0);
            buttons.Controls.Add(button);
        }

        footer.Controls.Add(_status);
        footer.Controls.Add(buttons);
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
        _list.ItemActivated += (_, _) => Remove(_list.Selected);
        _test.Click += (_, _) => _preview();
        _clear.Click += (_, _) => Clear();
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

    #endregion

    #region 数据

    private async Task LoadAndShowAsync()
    {
        _listTitle.Text = "正在读取英雄…";
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
            AppLog.Shared.Warn(LogCategory.Ui, "英雄提醒：读取英雄失败", e);
            _listTitle.Text = "读取英雄失败：" + e.Message;
            return;
        }

        ApplyFilter();
        ShowList();
        _ = LoadIconsAsync(_life.Token);
    }

    private void ApplyFilter()
    {
        var list = Preliminary.Filter(_champions, _lane, _search.Text);
        _grid.SetItems(list);
        _count.Text = $"{list.Count} 个英雄";
    }

    private void ShowList()
    {
        var byId = _champions.ToDictionary(c => c.Id);
        _list.SetItems(_watched.Select(id => byId.GetValueOrDefault(id)).OfType<PrimaryChampion>().ToList());
        _listTitle.Text =
            _watched.Count == 0 ? "提醒列表还没有英雄，提醒处于关闭状态。" : $"提醒列表：{_watched.Count} 个英雄";
        _grid.Invalidate();
    }

    private void Add(PrimaryChampion? champion)
    {
        if (champion == null || _watched.Contains(champion.Id))
        {
            return;
        }

        _watched.Add(champion.Id);
        Changed();
        _list.SelectChampion(champion.Id);
    }

    private void Remove(PrimaryChampion? champion)
    {
        if (champion == null || !_watched.Remove(champion.Id))
        {
            return;
        }

        Changed();
    }

    private void Clear()
    {
        if (_watched.Count == 0)
        {
            return;
        }

        if (
            MessageBox.Show(this, "确定清空提醒列表？", "清空列表", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            != DialogResult.Yes
        )
        {
            return;
        }

        _watched.Clear();
        Changed();
    }

    private void Changed()
    {
        ShowList();
        _save(_watched.ToArray());
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
