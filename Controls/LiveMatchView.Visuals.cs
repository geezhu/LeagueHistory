using League.Infrastructure;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public sealed partial class LiveMatchView
{
    private readonly Dictionary<string, Task<(Image? image, string name, string description)>> _imageTasks = new();
    private readonly List<Image> _cachedImages = new();
    private readonly Dictionary<string, (string Signature, Panel Card)> _playerCards = new();
    private readonly ToolTip _tips = new() { ShowAlways = true };
    private int _recommendationMode = -1;
    private string _cardsSignature = "",
        _equipmentSignature = "",
        _selectedItems = "";
    private readonly Dictionary<int, JObject> _championDetails = new();
    private static readonly Color Surface = Color.FromArgb(16, 45, 57),
        Muted = Color.FromArgb(139, 170, 181),
        Gold = Color.FromArgb(215, 191, 130);

    private static FlowLayoutPanel Stack() =>
        new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(6),
        };

    private static void Clear(Control parent)
    {
        foreach (Control c in parent.Controls.Cast<Control>().ToArray())
            c.Dispose();
    }

    private Label TextLabel(string text, Color? color = null) =>
        new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(Math.Max(180, _equipment.Width - 24), 0),
            ForeColor = color ?? ForeColor,
            Margin = new Padding(2, 4, 2, 6),
        };

    private void SetMessage(FlowLayoutPanel host, string text)
    {
        Clear(host);
        host.Controls.Add(TextLabel(text, Muted));
    }

    private static string ItemsKey(LiveMatchPlayer? p) =>
        p == null ? ""
        : p.Items == null ? "unknown"
        : string.Join(",", p.Items.Select(i => $"{i.Id}:{i.Count}"));

    private LiveDeathStreak Streak(LiveMatchPlayer p) =>
        _snapshot?.DeathStreaks.GetValueOrDefault(p.Key) ?? LiveDeathStreak.Unavailable();

    private void Render()
    {
        if (_snapshot == null)
            return;
        bool changed = Selected() == null;
        if (changed)
            _selectedKey = (_snapshot.Players.FirstOrDefault(p => p.Self) ?? _snapshot.Players.FirstOrDefault())?.Key;
        string items = ItemsKey(Selected());
        _status.Text =
            (
                _currentQueue == 2400 ? "海克斯大乱斗"
                : _currentQueue == 450 ? "普通大乱斗"
                : "选择模式后查看梯队"
            )
            + " · "
            + _rankStatus;
        _augmentBar.Visible = _currentQueue == 2400;
        RenderCards();
        ShowEquipment();
        if (changed || _recommendationMode != _currentQueue)
        {
            _recommendationMode = _currentQueue;
            _ = LoadRecommendationsAsync();
        }
        else if (items != _selectedItems)
            UpdateOwnedItems();
        _selectedItems = items;
    }

    private void RenderCards()
    {
        if (_snapshot == null || _rendering)
            return;
        string signature =
            _selectedKey
            + "|"
            + _players.Width
            + "|"
            + string.Join(
                ";",
                _snapshot.Players.Select(p =>
                    $"{p.Key}:{p.Level}:{ChampionId(p)}:{ItemsKey(p)}:{_ranks.GetValueOrDefault(ChampionId(p))}:{Streak(p)}"
                )
            );
        if (signature == _cardsSignature)
            return;
        _cardsSignature = signature;
        _rendering = true;
        _players.SuspendLayout();
        var scroll = _players.AutoScrollPosition;
        foreach (var cached in _playerCards.Values)
            if (!cached.Card.IsDisposed)
                cached.Card.Parent?.Controls.Remove(cached.Card);
        Clear(_players);
        var active = new HashSet<string>();
        int width = Math.Max(160, (_players.ClientSize.Width - 42) / 2);
        foreach (
            var team in _snapshot.Players.GroupBy(p => _snapshot.TeamLabel(p)).OrderBy(g => g.Key == "我方" ? 0 : 1)
        )
        {
            var column = Stack();
            column.Width = width;
            column.Padding = Padding.Empty;
            column.Margin = new Padding(0, 0, 8, 0);
            _players.Controls.Add(column);
            column.Controls.Add(
                TextLabel(
                    team.Key + " · " + team.Count() + " 位英雄",
                    team.Key == "对方" ? Color.Salmon : Color.Turquoise
                )
            );
            foreach (var p in team)
            {
                int id = ChampionId(p);
                var rank = _ranks.GetValueOrDefault(id);
                bool selected = p.Key == _selectedKey;
                string cardSignature = $"{width}|{id}|{p.Level}|{ItemsKey(p)}|{rank}|{selected}|{Streak(p)}";
                active.Add(p.Key);
                if (_playerCards.TryGetValue(p.Key, out var retained))
                {
                    if (retained.Signature == cardSignature && !retained.Card.IsDisposed)
                    {
                        column.Controls.Add(retained.Card);
                        continue;
                    }
                    retained.Card.Dispose();
                }
                var card = new Panel
                {
                    Size = new Size(width, 132),
                    BackColor = selected ? Color.FromArgb(23, 76, 89) : Surface,
                    Margin = new Padding(0, 0, 0, 7),
                    Tag = p.Key,
                    AccessibleName = ChampionName(p),
                };
                _playerCards[p.Key] = (cardSignature, card);
                column.Controls.Add(card);
                var portrait = Icon("champion", id, 40, ChampionName(p));
                portrait.Location = new Point(6, 7);
                card.Controls.Add(portrait);
                var name = new Label
                {
                    Text = ChampionName(p),
                    Location = new Point(52, 6),
                    Size = new Size(width - 58, 22),
                    AutoEllipsis = true,
                    ForeColor = Color.White,
                };
                card.Controls.Add(name);
                var badge = new Label
                {
                    Text = rank.Tier ?? "—",
                    Location = new Point(52, 31),
                    Size = new Size(34, 19),
                    ForeColor = AramMetaView.TierColor(rank.Tier ?? "—"),
                    BackColor = Color.FromArgb(9, 32, 44),
                    TextAlign = ContentAlignment.MiddleCenter,
                };
                card.Controls.Add(badge);
                card.Controls.Add(
                    new Label
                    {
                        Text = $"Lv.{p.Level}" + (p.Self ? " · 自己" : ""),
                        Location = new Point(88, 31),
                        Size = new Size(width - 92, 19),
                        ForeColor = Muted,
                    }
                );
                var stats = new Label
                {
                    Text = $"胜 {Rate(rank.Win)} · 选 {Rate(rank.Pick)}",
                    Location = new Point(6, 55),
                    Size = new Size(width - 10, 18),
                    ForeColor = Color.FromArgb(90, 206, 212),
                };
                card.Controls.Add(stats);
                _tips.SetToolTip(stats, _rankStatus);
                var streak = new Label
                {
                    Text = Streak(p).Text,
                    Location = new Point(6, 75),
                    Size = new Size(width - 10, 18),
                    ForeColor = Gold,
                    AutoEllipsis = true,
                };
                card.Controls.Add(streak);
                _tips.SetToolTip(streak, Streak(p).Rule);
                var items = new FlowLayoutPanel
                {
                    Location = new Point(5, 100),
                    Size = new Size(width - 10, 27),
                    WrapContents = false,
                    Margin = Padding.Empty,
                };
                card.Controls.Add(items);
                if (p.Items == null)
                    items.Controls.Add(
                        new Label
                        {
                            Text = "装备暂不可用",
                            AutoSize = true,
                            ForeColor = Muted,
                        }
                    );
                else if (p.Items.Length == 0)
                    items.Controls.Add(
                        new Label
                        {
                            Text = "暂无装备",
                            AutoSize = true,
                            ForeColor = Muted,
                        }
                    );
                else
                    foreach (var item in p.Items.Take(7))
                        items.Controls.Add(Icon("item", item.Id, 22, ItemName(item.Id, item.Name), count: item.Count));
                void Select(Control c)
                {
                    c.Cursor = Cursors.Hand;
                    c.Click += (_, _) =>
                    {
                        _selectedKey = p.Key;
                        _equipmentSignature = "";
                        RenderCards();
                        ShowEquipment();
                        _selectedItems = ItemsKey(Selected());
                        _ = LoadRecommendationsAsync();
                    };
                    foreach (Control child in c.Controls)
                        Select(child);
                }
                Select(card);
            }
        }
        foreach (var key in _playerCards.Keys.Where(k => !active.Contains(k)).ToArray())
        {
            _playerCards[key].Card.Dispose();
            _playerCards.Remove(key);
        }
        _players.ResumeLayout();
        _players.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
        _rendering = false;
    }

    private void ShowEquipment()
    {
        var p = Selected();
        if (p == null)
            return;
        int id = ChampionId(p);
        var rank = _ranks.GetValueOrDefault(id);
        string signature =
            $"{p.Key}|{id}|{p.Level}|{ItemsKey(p)}|{rank}|{_currentQueue}|{Streak(p)}|"
            + string.Join(",", _manualAugments.GetValueOrDefault(p.Key) ?? new());
        if (signature == _equipmentSignature)
            return;
        _equipmentSignature = signature;
        _equipment.SuspendLayout();
        Clear(_equipment);
        var hero = new ProfileBackgroundPanel
        {
            Size = new Size(Math.Max(220, _equipment.Width - 12), 114),
            Margin = new Padding(0, 0, 0, 10),
            BackColor = Surface,
        };
        _equipment.Controls.Add(hero);
        var portrait = Icon("champion", id, 62, ChampionName(p));
        portrait.Location = new Point(12, 16);
        hero.Controls.Add(portrait);
        var title = new Label
        {
            Text = ChampionName(p),
            BackColor = Color.Transparent,
            ForeColor = Color.White,
            Location = new Point(87, 13),
            AutoSize = true,
        };
        var titleFont = new Font(Font.FontFamily, 14, FontStyle.Bold);
        title.Font = titleFont;
        title.Disposed += (_, _) => titleFont.Dispose();
        hero.Controls.Add(title);
        var badge = new Label
        {
            Text = rank.Tier ?? "—",
            Location = new Point(87, 50),
            Size = new Size(38, 23),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(9, 32, 44),
            ForeColor = AramMetaView.TierColor(rank.Tier ?? "—"),
        };
        hero.Controls.Add(badge);
        var streak = new Label
        {
            Text = Streak(p).Text,
            Location = new Point(87, 80),
            Size = new Size(hero.Width - 94, 22),
            AutoEllipsis = true,
            BackColor = Color.Transparent,
            ForeColor = Gold,
        };
        hero.Controls.Add(streak);
        _tips.SetToolTip(streak, Streak(p).Rule);
        hero.Controls.Add(
            new Label
            {
                Text = $"Lv.{p.Level} · 胜率 {Rate(rank.Win)} · 选用率 {Rate(rank.Pick)}",
                BackColor = Color.Transparent,
                ForeColor = Gold,
                Location = new Point(132, 53),
                AutoSize = true,
            }
        );
        _tips.SetToolTip(hero, _rankStatus);
        _ = LoadArtworkAsync(hero, id);
        _equipment.Controls.Add(TextLabel("当前装备", Gold));
        var row = IconRow();
        _equipment.Controls.Add(row);
        if (p.Items is { Length: > 0 })
            foreach (var item in p.Items)
                row.Controls.Add(Icon("item", item.Id, 44, ItemName(item.Id, item.Name), count: item.Count));
        else
            row.Controls.Add(TextLabel(p.Items == null ? "接口未提供装备信息" : "尚未购买装备", Muted));
        if (_currentQueue == 2400)
        {
            _equipment.Controls.Add(TextLabel("已选强化 · 手动记录", Gold));
            var augmentRow = IconRow();
            _equipment.Controls.Add(augmentRow);
            if (_manualAugments.TryGetValue(p.Key, out var ids) && ids.Count > 0)
                foreach (int augmentId in ids)
                {
                    var icon = Icon("augment", augmentId, 44, AugmentName(augmentId));
                    augmentRow.Controls.Add(icon);
                    icon.Click += (_, _) =>
                    {
                        ids.Remove(augmentId);
                        ShowEquipment();
                    };
                }
            else
                augmentRow.Controls.Add(TextLabel("在底部记录已选强化，点击图标可移除。", Muted));
        }
        ResizeSections();
        _equipment.ResumeLayout();
    }

    private string AugmentName(int id) =>
        FormMain.Globals.resLoading.GetAugmentById(id)?.Name
        ?? (string?)_augmentMetadata?[id.ToString()]?["name_zh"]
        ?? $"强化 {id}";

    private FlowLayoutPanel IconRow() =>
        new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MaximumSize = new Size(Math.Max(200, _equipment.Width - 18), 0),
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.Transparent,
        };

    private Button Icon(
        string kind,
        int id,
        int size,
        string name,
        string description = "",
        bool owned = false,
        int count = 1
    )
    {
        var icon = new Button
        {
            Tag = (kind, id),
            Size = new Size(size, size),
            Text = id > 0 ? name[..Math.Min(2, name.Length)] : "?",
            AccessibleName = name,
            Margin = new Padding(2),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(25, 54, 67),
            ForeColor = Color.White,
            BackgroundImageLayout = ImageLayout.Zoom,
        };
        icon.FlatAppearance.BorderSize = owned ? 2 : 0;
        icon.FlatAppearance.BorderColor = Color.Turquoise;
        _tips.SetToolTip(
            icon,
            TooltipText.Format(name + (count > 1 ? $" ×{count}" : "") + (owned ? " · 已拥有" : ""), description)
        );

        _ = Load();
        return icon;
        async Task Load()
        {
            try
            {
                if (id <= 0)
                    return;
                var info = await CachedIconAsync(kind, id);
                if (icon.IsDisposed || _life.IsCancellationRequested)
                    return;
                if (info.image != null)
                {
                    icon.BackgroundImage = info.image;
                    icon.Text = count > 1 ? "×" + count : "";
                    icon.TextAlign = ContentAlignment.BottomRight;
                }
                _tips.SetToolTip(
                    icon,
                    TooltipText.Format(
                        name + (count > 1 ? $" ×{count}" : "") + (owned ? " · 已拥有" : ""),
                        string.IsNullOrWhiteSpace(description) ? info.description : description
                    )
                );
            }
            catch
            { /* Keep a named placeholder when local artwork is unavailable. */
            }
        }
    }

    private void UpdateOwnedItems()
    {
        void Update(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is Button button && button.Tag is ValueTuple<string, int> item && item.Item1 == "item")
                {
                    bool owned = Selected()?.Items?.Any(i => i.Id == item.Item2) == true;
                    button.FlatAppearance.BorderSize = owned ? 2 : 0;
                    string description = "";
                    if (_imageTasks.TryGetValue($"item:{item.Item2}", out var task) && task.IsCompletedSuccessfully)
                        description = task.Result.description;
                    _tips.SetToolTip(
                        button,
                        TooltipText.Format(ItemName(item.Item2) + (owned ? " · 已拥有" : ""), description)
                    );
                }
                Update(child);
            }
        }
        Update(_recommendations);
    }

    private Task<(Image? image, string name, string description)> CachedIconAsync(string kind, int id)
    {
        string key = $"{kind}:{id}";
        if (_imageTasks.TryGetValue(key, out var existing))
            return existing;
        return _imageTasks[key] = Load();
        async Task<(Image? image, string name, string description)> Load()
        {
            try
            {
                var r = FormMain.Globals.resLoading;
                var info =
                    _iconLoader != null
                        ? await _iconLoader(kind, id)
                        : await Task.Run(async () =>
                            kind == "champion" ? await r.GetChampionInfoAsync(id)
                            : kind == "augment" ? await r.GetAugmentInfoAsync(id)
                            : await r.GetItemInfoAsync(id)
                        );
                if (_life.IsCancellationRequested)
                {
                    info.image?.Dispose();
                    return (null, info.name, info.description);
                }
                if (info.image != null)
                    _cachedImages.Add(info.image);
                return info;
            }
            catch
            {
                return (null, "", "");
            }
        }
    }

    private void DisposeImageCache()
    {
        foreach (var cached in _playerCards.Values)
            cached.Card.Dispose();
        _playerCards.Clear();
        foreach (var image in _cachedImages)
            image.Dispose();
        _cachedImages.Clear();
        _imageTasks.Clear();
    }

    private async Task LoadArtworkAsync(ProfileBackgroundPanel panel, int id)
    {
        try
        {
            var client = FormMain.Globals.lcuClient?.Client;
            if (client == null || id <= 0)
                return;
            if (!_championDetails.TryGetValue(id, out var data))
            {
                data = JObject.Parse(
                    await client.GetStringAsync($"/lol-game-data/assets/v1/champions/{id}.json", _life.Token)
                );
                if ((int?)data["id"] != id)
                    return;
                _championDetails[id] = data;
            }
            string? path = ProfileBackgroundPanel.FindSplash(data, id * 1000);
            if (path == null)
                return;
            var image = await new ImageCacheService(client).GetOrDownloadImageAsync(
                path,
                "profile-backgrounds",
                $"{id * 1000}.png"
            );
            if (panel.IsDisposed || _life.IsCancellationRequested)
            {
                image?.Dispose();
                return;
            }
            panel.SetChampionArtwork(image);
        }
        catch { }
    }

    private void RenderRecommendations(
        IReadOnlyList<(int[] Ids, double? Win, double? Pick)> builds,
        IReadOnlyList<(string Title, AramMetaRecommendation[] Rows)> augments
    )
    {
        _recommendations.SuspendLayout();
        Clear(_recommendations);
        _recommendations.Controls.Add(TextLabel("推荐出装", Gold));
        if (builds.Count == 0)
            _recommendations.Controls.Add(TextLabel("暂无组合，可打开完整攻略查看其他方案。", Muted));
        foreach (var build in builds)
        {
            var card = Stack();
            card.BackColor = Surface;
            card.Width = Math.Max(210, _recommendations.Width - 12);
            _recommendations.Controls.Add(card);
            var icons = IconRow();
            card.Controls.Add(icons);
            foreach (int id in build.Ids)
                icons.Controls.Add(
                    Icon("item", id, 38, ItemName(id), owned: Selected()?.Items?.Any(i => i.Id == id) == true)
                );
            card.Controls.Add(
                TextLabel($"胜率 {Rate(build.Win)} · 选用率 {Rate(build.Pick)}", Color.FromArgb(90, 206, 212))
            );
        }
        foreach (var group in augments)
        {
            _recommendations.Controls.Add(TextLabel(group.Title + " · 推荐", Gold));
            var row = IconRow();
            _recommendations.Controls.Add(row);
            foreach (var entry in group.Rows)
            {
                int id = entry.Ids[0];
                var icon = Icon(
                    "augment",
                    id,
                    42,
                    FormMain.Globals.resLoading.GetAugmentById(id)?.Name ?? entry.Name,
                    entry.Description
                );
                row.Controls.Add(icon);
                icon.Click += (_, _) =>
                {
                    var p = Selected();
                    if (p == null)
                        return;
                    if (!_manualAugments.TryGetValue(p.Key, out var list))
                        _manualAugments[p.Key] = list = new();
                    if (!list.Contains(id))
                        list.Add(id);
                    ShowEquipment();
                };
            }
        }
        if (augments.Count > 0)
            _recommendations.Controls.Add(TextLabel("悬浮查看介绍；点击强化可记入手动记录。", Muted));
        ResizeSections();
        _recommendations.ResumeLayout();
    }

    private void ResizeSections()
    {
        foreach (var host in new[] { _equipment, _recommendations })
        foreach (Control c in host.Controls)
        {
            if (c is ProfileBackgroundPanel || c is FlowLayoutPanel { FlowDirection: FlowDirection.TopDown })
            {
                c.MinimumSize = new Size(Math.Max(210, host.Width - 12), 0);
                c.Width = Math.Max(210, host.Width - 12);
            }
            if (c is FlowLayoutPanel row)
                row.MaximumSize = new Size(Math.Max(210, host.Width - 12), 0);
            if (c is Label label)
                label.MaximumSize = new Size(Math.Max(200, host.Width - 18), 0);
        }
    }
}
