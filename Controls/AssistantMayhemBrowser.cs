using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed class AssistantMayhemBrowser : IDisposable
{
    private enum BrowserTab
    {
        Augments,
        Equipment,
        Spells,
    }

    private AramMetaPage _page;
    private readonly int _hero;
    private readonly FlowLayoutPanel _host;
    private readonly ToolTip _tips;
    private readonly Color _foreColor;
    private readonly Func<CancellationToken, Task<AramMetaPage>> _loadDetail;
    private readonly Func<int[], int, CancellationToken, Task> _applySpells;
    private readonly CancellationTokenSource _lifetime;
    private readonly HashSet<Task> _work = new();
    private CancellationTokenSource? _painting;
    private Task<AramMetaPage>? _detailTask;
    private Task _cleanupWork = Task.CompletedTask;
    private bool _syncing,
        _closing;
    private BrowserTab _tab;
    private int _offset;
    private FlowLayoutPanel _tabs = null!,
        _filters = null!,
        _rows = null!,
        _pager = null!;
    private ComboBox _group = null!,
        _sort = null!;
    private Button[] _tabButtons = [];
    private Button _previous = null!,
        _next = null!;
    private Label _count = null!;

    public AssistantMayhemBrowser(
        AramMetaPage page,
        int hero,
        CancellationToken token,
        FlowLayoutPanel host,
        int initialTab,
        int initialRarity,
        ToolTip tips,
        Color foreColor,
        Func<CancellationToken, Task<AramMetaPage>> loadDetail,
        Func<int[], int, CancellationToken, Task> applySpells
    )
    {
        _page = page;
        _hero = hero;
        _host = host;
        _tips = tips;
        _foreColor = foreColor;
        _loadDetail = loadDetail;
        _applySpells = applySpells;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        _tab = (BrowserTab)initialTab;
        CreateTabs();
        CreateFilters();
        CreateRows();
        CreatePager();
        ConnectEvents(initialRarity);
        _rows.Disposed += (_, _) => Dispose();
    }

    public Task Completion => _cleanupWork;
    public event Action<Task>? WorkRetired;

    private void CreateTabs()
    {
        _tabs = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };
        _host.Controls.Add(_tabs);
        _tabButtons = new[] { BrowserButton("强化", 88), BrowserButton("装备", 88), BrowserButton("技能", 88) };
        foreach (var button in _tabButtons)
        {
            button.FlatAppearance.BorderColor = Color.FromArgb(42, 72, 83);
            _tabs.Controls.Add(button);
        }
    }

    private void CreateFilters()
    {
        _filters = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };
        _host.Controls.Add(_filters);
        _group = new Controls.DarkFilterComboBox { Width = 126, Margin = new Padding(0, 0, 4, 4) };
        _sort = new Controls.DarkFilterComboBox { Width = 146, Margin = new Padding(0, 0, 0, 4) };
        _sort.Items.AddRange(new object[] { "来源推荐顺序", "胜率最高", "选取率最高" });
        _sort.SelectedIndex = 0;
        _filters.Controls.AddRange(new Control[] { _group, _sort });
    }

    private void CreateRows()
    {
        _rows = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            MaximumSize = new Size(282, 0),
            Margin = Padding.Empty,
        };
        _host.Controls.Add(_rows);
    }

    private void CreatePager()
    {
        _pager = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };
        _host.Controls.Add(_pager);
        _previous = BrowserButton("上一页", 68);
        _next = BrowserButton("下一页", 68);
        _count = new Label
        {
            Width = 126,
            Height = 28,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        _pager.Controls.AddRange(new Control[] { _previous, _count, _next });
        _previous.FlatAppearance.BorderColor = _next.FlatAppearance.BorderColor = Color.FromArgb(42, 72, 83);
    }

    private void ConnectEvents(int initialRarity)
    {
        for (int i = 0; i < 3; i++)
        {
            int target = i;
            _tabButtons[i].Click += (_, _) =>
            {
                _tab = (BrowserTab)target;
                _offset = 0;
                SetGroups();
                _ = Track(RenderAsync());
            };
        }
        _group.SelectedIndexChanged += (_, _) =>
        {
            if (!_syncing)
            {
                _offset = 0;
                _ = Track(RenderAsync());
            }
        };
        _sort.SelectedIndexChanged += (_, _) =>
        {
            _offset = 0;
            _ = Track(RenderAsync());
        };
        _previous.Click += (_, _) =>
        {
            _offset--;
            _ = Track(RenderAsync());
        };
        _next.Click += (_, _) =>
        {
            _offset++;
            _ = Track(RenderAsync());
        };
        SetGroups();
        _group.SelectedIndex = Math.Clamp(initialRarity, 0, _group.Items.Count - 1);
        _ = Track(RenderAsync());
    }

    private async Task EnsureDetailsAsync(CancellationToken paintToken)
    {
        if (_tab != BrowserTab.Augments && _page.Detail == null)
        {
            _rows.Controls.Add(new Label { Text = "正在读取装备与技能推荐…", AutoSize = true });
            if (_detailTask == null || _detailTask.IsFaulted || _detailTask.IsCanceled)
            {
                _detailTask = Track(_loadDetail(_lifetime.Token));
            }

            var detailed = await _detailTask;
            paintToken.ThrowIfCancellationRequested();
            _page = detailed;
            foreach (Control child in _rows.Controls.Cast<Control>().ToArray())
            {
                child.Dispose();
            }
        }
    }

    private (
        JObject[] All,
        (string Title, AramMetaRecommendation? Entry)[] Highlights,
        int PageSize
    ) SelectPageEntries()
    {
        string key =
            _tab == BrowserTab.Augments ? new[] { "kPrismatic", "kGold", "kSilver" }[_group.SelectedIndex]
            : _tab == BrowserTab.Equipment ? new[] { "items", "singleItems", "boots" }[_group.SelectedIndex]
            : "spells";
        var recommendations = AramMetaRecommendation.Read(_page, _hero, key);
        var highlights = AramMetaRecommendation.SpellHighlights(recommendations);
        _filters.Visible = _tab != BrowserTab.Spells;
        _pager.Visible = _tab != BrowserTab.Spells;
        var entries =
            _tab == BrowserTab.Spells
                ? highlights.Where(h => h.Entry != null).Select(h => h.Entry!.Raw)
                : AramMetaRecommendation.Sort(recommendations, _sort.SelectedIndex).Select(r => r.Raw);
        var all = entries.ToArray();
        int pageSize = _tab == BrowserTab.Augments ? 4 : 3;
        int total = Math.Max(1, (all.Length + pageSize - 1) / pageSize);
        _offset = Math.Clamp(_offset, 0, total - 1);
        _count.Text = $"{_offset + 1} / {total} · {all.Length} 项";
        _previous.Enabled = _offset > 0;
        _next.Enabled = _offset + 1 < total;
        return (all, highlights, pageSize);
    }

    private (
        Panel Card,
        FlowLayoutPanel Icons,
        Label Title,
        int[] Ids,
        string Name,
        bool Augment,
        bool Spell
    ) CreateRecommendationCard(
        JObject entry,
        (string Title, AramMetaRecommendation? Entry)[] highlights,
        int spellIndex
    )
    {
        bool augment = _tab == BrowserTab.Augments,
            spell = _tab == BrowserTab.Spells;
        int id = (int?)entry["id"] ?? 0;
        string name = augment
            ? FormMain.Globals.resLoading.GetAugmentById(id)?.Name
                ?? (string?)_page.Index.Augments[id.ToString()]?["name_zh"]
                ?? $"强化 {id}"
            : (string?)entry["name_zh"] ?? (string?)entry["name_en"] ?? "推荐组合";
        if (spell)
        {
            name = highlights.Where(h => h.Entry != null).ElementAt(spellIndex).Title;
        }

        var ids = augment
            ? new[] { id }
            : (entry["items"] as JArray ?? new())
                .OfType<JObject>()
                .Select(o => (int?)o["id"] ?? 0)
                .Where(i => i > 0)
                .ToArray();
        var card = new Panel
        {
            Size = new Size(276, augment ? 48 : 64),
            Margin = new Padding(0, 0, 0, 4),
            BackColor = Color.FromArgb(16, 45, 57),
        };
        _rows.Controls.Add(card);
        var icons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(3, 3),
            MaximumSize = new Size(augment ? 38 : 112, 0),
            Margin = Padding.Empty,
        };
        card.Controls.Add(icons);
        int left = augment ? 44 : 120;
        var title = new Label
        {
            Text = name,
            AutoEllipsis = true,
            Location = new Point(left, 3),
            Size = new Size(274 - left, augment ? 19 : 36),
        };
        var stats = new Label
        {
            Text = $"胜 {Rate(entry["wr"])} · 选 {Rate(entry["pick"])} · {(long?)entry["g"]:N0}场",
            Location = new Point(augment ? 44 : 4, augment ? 25 : 43),
            Size = new Size(augment ? 230 : 268, 19),
            ForeColor = Color.FromArgb(81, 190, 197),
        };
        card.Controls.AddRange(new Control[] { title, stats });
        string description = augment
            ? FormMain.Globals.resLoading.GetAugmentById(id)?.Description
                ?? (string?)_page.Index.Augments[id.ToString()]?["desc_zh"]
                ?? ""
            : name;
        _tips.SetToolTip(title, League.Controls.TooltipText.Format(name, description));
        _tips.SetToolTip(
            stats,
            $"来源样本：{(long?)entry["g"]:N0} 场；选取率和胜率均来自 arammeta。"
                + (augment ? "\n强化胜率为调整后胜率。" : "")
        );
        return (card, icons, title, ids, name, augment, spell);
    }

    private async Task LoadCardIconsAsync(
        FlowLayoutPanel icons,
        int[] ids,
        bool augment,
        bool spell,
        CancellationToken paintToken
    )
    {
        foreach (int iconId in ids.Take(augment ? 1 : 3))
        {
            var resources = FormMain.Globals.resLoading;
            var info =
                augment ? await resources.GetAugmentInfoAsync(iconId)
                : spell ? await resources.GetSpellInfoAsync(iconId)
                : await resources.GetItemInfoAsync(iconId);
            if (paintToken.IsCancellationRequested || icons.IsDisposed)
            {
                info.image?.Dispose();
                paintToken.ThrowIfCancellationRequested();
                return;
            }
            Control picture;
            if (info.image != null)
            {
                var image = new PictureBox
                {
                    Image = info.image,
                    Size = new Size(32, 32),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Margin = new Padding(2),
                };
                image.Disposed += (_, _) => image.Image?.Dispose();
                picture = image;
            }
            else
            {
                picture = new Label
                {
                    Text = "?",
                    Size = new Size(32, 32),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(38, 64, 75),
                    Margin = new Padding(2),
                };
            }

            _tips.SetToolTip(picture, League.Controls.TooltipText.Format(info.name, info.description));
            icons.Controls.Add(picture);
        }
    }

    private void MakeSpellCardClickable(Panel card, Label title, int[] ids, string name, bool spell)
    {
        if (spell && ids.Length == 2)
        {
            void Clickable(Control control)
            {
                control.Cursor = Cursors.Hand;
                control.Click += async (_, _) => await _applySpells(ids, _hero, _lifetime.Token);
                foreach (Control child in control.Controls)
                {
                    Clickable(child);
                }
            }
            Clickable(card);
            _tips.SetToolTip(title, name + "\n点击配置这组召唤师技能");
        }
    }

    private static string Rate(JToken? n) => RateValue(AramMetaIndex.Rate(n));

    private static string RateValue(double? n) => n.HasValue ? n.Value.ToString("P1") : "—";

    private Button BrowserButton(string text, int width) =>
        new()
        {
            Text = text,
            Width = width,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(16, 45, 57),
            ForeColor = _foreColor,
            Margin = new Padding(0, 0, 4, 4),
        };

    private void SetGroups()
    {
        _syncing = true;
        try
        {
            _group.Items.Clear();
            _group.Items.AddRange(
                _tab == BrowserTab.Augments ? new object[] { "棱彩强化", "黄金强化", "白银强化" }
                : _tab == BrowserTab.Equipment ? new object[] { "装备组合", "单件装备", "鞋子" }
                : new object[] { "召唤师技能" }
            );
            _group.SelectedIndex = 0;
            _group.Enabled = _tab != BrowserTab.Spells;
        }
        finally
        {
            _syncing = false;
        }
    }

    private async Task RenderAsync()
    {
        if (_closing || _lifetime.IsCancellationRequested || _rows.IsDisposed)
        {
            return;
        }

        Retire(_painting);
        _painting = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var paintToken = _painting.Token;
        foreach (Control child in _rows.Controls.Cast<Control>().ToArray())
        {
            child.Dispose();
        }

        _previous.Enabled = _next.Enabled = false;
        _count.Text = "加载中…";
        for (int i = 0; i < 3; i++)
        {
            _tabButtons[i].BackColor = i == (int)_tab ? Color.FromArgb(0, 126, 139) : Color.FromArgb(16, 45, 57);
        }

        try
        {
            await EnsureDetailsAsync(paintToken);
            var (all, highlights, pageSize) = SelectPageEntries();
            int spellIndex = 0;
            foreach (var entry in all.Skip(_offset * pageSize).Take(pageSize))
            {
                paintToken.ThrowIfCancellationRequested();
                var (card, icons, title, ids, name, augment, spell) = CreateRecommendationCard(
                    entry,
                    highlights,
                    spellIndex++
                );
                await LoadCardIconsAsync(icons, ids, augment, spell, paintToken);
                MakeSpellCardClickable(card, title, ids, name, spell);
            }
            if (all.Length == 0)
            {
                _rows.Controls.Add(new Label { Text = "当前分类暂无推荐数据", AutoSize = true });
            }
        }
        catch (OperationCanceledException)
        {
            // Replaced page and closed dialog work cannot publish into current rows.
        }
        catch (Exception)
        {
            if (!paintToken.IsCancellationRequested && !_rows.IsDisposed)
            {
                _count.Text = "加载失败";
                _rows.Controls.Add(new Label { Text = "详情暂不可用，可切回强化或重试。", AutoSize = true });
                var retry = BrowserButton("重试详情", 100);
                _rows.Controls.Add(retry);
                retry.Click += (_, _) => _ = Track(RenderAsync());
            }
        }
    }

    private T Track<T>(T task)
        where T : Task
    {
        lock (_work)
        {
            _work.Add(task);
        }
        _ = task.ContinueWith(
            completed =>
            {
                lock (_work)
                {
                    _work.Remove(completed);
                }
                if (completed.Exception is { } error)
                {
                    System.Diagnostics.Debug.WriteLine("Mayhem browser: " + error.GetBaseException().GetType().Name);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        return task;
    }

    private Task[] Pending()
    {
        lock (_work)
        {
            return _work.ToArray();
        }
    }

    private void Retire(CancellationTokenSource? source)
    {
        if (source == null)
        {
            return;
        }
        var pending = Pending();
        var cancellation = source.CancelAsync();
        Track(
            Task.WhenAll(pending.Append(cancellation))
                .ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        source.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                )
        );
    }

    public void Dispose()
    {
        if (_closing)
        {
            return;
        }
        _closing = true;
        Retire(_painting);
        _painting = null;
        Retire(_lifetime);
        _cleanupWork = Task.WhenAll(Pending());
        WorkRetired?.Invoke(_cleanupWork);
    }
}
