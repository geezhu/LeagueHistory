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
    private Func<int, CancellationToken, Task<Image?>> _artworkLoader;
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
        {
            c.Dispose();
        }
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
        if (_closing || _snapshot == null)
        {
            return;
        }

        bool changed = Selected() == null;
        if (changed)
        {
            _selectedKey = (_snapshot.Players.FirstOrDefault(p => p.Self) ?? _snapshot.Players.FirstOrDefault())?.Key;
        }

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
        {
            UpdateOwnedItems();
        }

        _selectedItems = items;
    }

    private void RenderCards()
    {
        if (_closing || _snapshot == null || _rendering)
        {
            return;
        }
        string signature =
            _selectedKey
            + "|"
            + _players.Width
            + "|"
            + string.Join(
                ";",
                _snapshot.Players.Select(player =>
                    $"{player.Key}:{player.Level}:{ChampionId(player)}:{ItemsKey(player)}:{_ranks.GetValueOrDefault(ChampionId(player))}:{Streak(player)}"
                )
            );
        if (signature == _cardsSignature)
        {
            return;
        }
        _rendering = true;
        _players.SuspendLayout();
        var scroll = _players.AutoScrollPosition;
        try
        {
            DetachPlayerCards();
            Clear(_players);
            var active = new HashSet<string>();
            int width = Math.Max(160, (_players.ClientSize.Width - 42) / 2);
            foreach (
                var team in _snapshot
                    .Players.GroupBy(player => _snapshot.TeamLabel(player))
                    .OrderBy(group => group.Key == "我方" ? 0 : 1)
            )
            {
                var column = CreateTeamColumn(team.Key, team.Count(), width);
                _players.Controls.Add(column);
                foreach (var player in team)
                {
                    active.Add(player.Key);
                    column.Controls.Add(GetPlayerCard(player, width));
                }
            }
            RemoveRetiredCards(active);
            _cardsSignature = signature;
        }
        finally
        {
            _players.ResumeLayout();
            _players.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
            _rendering = false;
        }
    }

    private void DetachPlayerCards()
    {
        foreach (var cached in _playerCards.Values)
        {
            if (!cached.Card.IsDisposed)
            {
                cached.Card.Parent?.Controls.Remove(cached.Card);
            }
        }
    }

    private void RemoveRetiredCards(HashSet<string> active)
    {
        foreach (var key in _playerCards.Keys.Where(key => !active.Contains(key)).ToArray())
        {
            _playerCards[key].Card.Dispose();
            _playerCards.Remove(key);
        }
    }

    private FlowLayoutPanel CreateTeamColumn(string team, int count, int width)
    {
        var column = Stack();
        column.Width = width;
        column.Padding = Padding.Empty;
        column.Margin = new Padding(0, 0, 8, 0);
        column.Controls.Add(TextLabel($"{team} · {count} 位英雄", team == "对方" ? Color.Salmon : Color.Turquoise));
        return column;
    }

    private Panel GetPlayerCard(LiveMatchPlayer player, int width)
    {
        int id = ChampionId(player);
        var rank = _ranks.GetValueOrDefault(id);
        bool selected = player.Key == _selectedKey;
        string signature = $"{width}|{id}|{player.Level}|{ItemsKey(player)}|{rank}|{selected}|{Streak(player)}";
        if (_playerCards.TryGetValue(player.Key, out var retained))
        {
            if (retained.Signature == signature && !retained.Card.IsDisposed)
            {
                return retained.Card;
            }
            retained.Card.Dispose();
        }
        var card = new Panel
        {
            Size = new Size(width, 132),
            BackColor = selected ? Color.FromArgb(23, 76, 89) : Surface,
            Margin = new Padding(0, 0, 0, 7),
            Tag = player.Key,
            AccessibleName = ChampionName(player),
        };
        _playerCards[player.Key] = (signature, card);
        AddPlayerIdentity(card, player, rank.Tier, width, id);
        AddPlayerStatistics(card, player, rank.Win, rank.Pick, width);
        card.Controls.Add(CreatePlayerItems(player, width));
        BindPlayerSelection(card, player.Key);
        return card;
    }

    private void AddPlayerIdentity(Panel card, LiveMatchPlayer player, string? tier, int width, int id)
    {
        var portrait = Icon("champion", id, 40, ChampionName(player));
        portrait.Location = new Point(6, 7);
        card.Controls.Add(portrait);
        var name = new Label
        {
            Text = ChampionName(player),
            Location = new Point(52, 6),
            Size = new Size(width - 58, 22),
            AutoEllipsis = true,
            ForeColor = Color.White,
        };
        card.Controls.Add(name);
        var badge = new Label
        {
            Text = tier ?? "—",
            Location = new Point(52, 31),
            Size = new Size(34, 19),
            ForeColor = AramMetaView.TierColor(tier ?? "—"),
            BackColor = Color.FromArgb(9, 32, 44),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        card.Controls.Add(badge);
        card.Controls.Add(
            new Label
            {
                Text = $"Lv.{player.Level}" + (player.Self ? " · 自己" : ""),
                Location = new Point(88, 31),
                Size = new Size(width - 92, 19),
                ForeColor = Muted,
            }
        );
    }

    private void AddPlayerStatistics(Panel card, LiveMatchPlayer player, double? win, double? pick, int width)
    {
        var stats = new Label
        {
            Text = $"胜 {Rate(win)} · 选 {Rate(pick)}",
            Location = new Point(6, 55),
            Size = new Size(width - 10, 18),
            ForeColor = Color.FromArgb(90, 206, 212),
        };
        card.Controls.Add(stats);
        _tips.SetToolTip(stats, _rankStatus);
        var streak = new Label
        {
            Text = Streak(player).Text,
            Location = new Point(6, 75),
            Size = new Size(width - 10, 18),
            ForeColor = Gold,
            AutoEllipsis = true,
        };
        card.Controls.Add(streak);
        _tips.SetToolTip(streak, Streak(player).Rule);
    }

    private FlowLayoutPanel CreatePlayerItems(LiveMatchPlayer player, int width)
    {
        var items = new FlowLayoutPanel
        {
            Location = new Point(5, 100),
            Size = new Size(width - 10, 27),
            WrapContents = false,
            Margin = Padding.Empty,
        };
        if (player.Items is { Length: > 0 })
        {
            foreach (var item in player.Items.Take(7))
            {
                items.Controls.Add(Icon("item", item.Id, 22, ItemName(item.Id, item.Name), count: item.Count));
            }
        }
        else
        {
            items.Controls.Add(
                new Label
                {
                    Text = player.Items == null ? "装备暂不可用" : "暂无装备",
                    AutoSize = true,
                    ForeColor = Muted,
                }
            );
        }
        return items;
    }

    private void BindPlayerSelection(Control control, string key)
    {
        control.Cursor = Cursors.Hand;
        control.Click += (_, _) => SelectPlayer(key);
        foreach (Control child in control.Controls)
        {
            BindPlayerSelection(child, key);
        }
    }

    private void SelectPlayer(string key)
    {
        if (_closing)
        {
            return;
        }
        _selectedKey = key;
        _equipmentSignature = "";
        RenderCards();
        ShowEquipment();
        _selectedItems = ItemsKey(Selected());
        _ = LoadRecommendationsAsync();
    }

    private void ShowEquipment()
    {
        var player = Selected();
        if (_closing || player == null)
        {
            return;
        }
        int id = ChampionId(player);
        var rank = _ranks.GetValueOrDefault(id);
        string signature =
            $"{player.Key}|{id}|{player.Level}|{ItemsKey(player)}|{rank}|{_currentQueue}|{Streak(player)}|"
            + string.Join(",", _manualAugments.GetValueOrDefault(player.Key) ?? new());
        if (signature == _equipmentSignature)
        {
            return;
        }
        _equipment.SuspendLayout();
        try
        {
            Clear(_equipment);
            _equipment.Controls.Add(CreateHeroSummary(player, id, rank));
            AddCurrentItems(player);
            if (_currentQueue == 2400)
            {
                AddManualAugments(player);
            }
            ResizeSections();
            _equipmentSignature = signature;
        }
        finally
        {
            _equipment.ResumeLayout();
        }
    }

    private ProfileBackgroundPanel CreateHeroSummary(
        LiveMatchPlayer player,
        int id,
        (string Tier, double? Win, double? Pick) rank
    )
    {
        var hero = new ProfileBackgroundPanel
        {
            Size = new Size(Math.Max(220, _equipment.Width - 12), 114),
            Margin = new Padding(0, 0, 0, 10),
            BackColor = Surface,
        };
        var portrait = Icon("champion", id, 62, ChampionName(player));
        portrait.Location = new Point(12, 16);
        hero.Controls.Add(portrait);
        var title = new Label
        {
            Text = ChampionName(player),
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
            Text = Streak(player).Text,
            Location = new Point(87, 80),
            Size = new Size(hero.Width - 94, 22),
            AutoEllipsis = true,
            BackColor = Color.Transparent,
            ForeColor = Gold,
        };
        hero.Controls.Add(streak);
        _tips.SetToolTip(streak, Streak(player).Rule);
        hero.Controls.Add(
            new Label
            {
                Text = $"Lv.{player.Level} · 胜率 {Rate(rank.Win)} · 选用率 {Rate(rank.Pick)}",
                BackColor = Color.Transparent,
                ForeColor = Gold,
                Location = new Point(132, 53),
                AutoSize = true,
            }
        );
        _tips.SetToolTip(hero, _rankStatus);
        _ = StartWork(() => LoadArtworkAsync(hero, id));
        return hero;
    }

    private void AddCurrentItems(LiveMatchPlayer player)
    {
        _equipment.Controls.Add(TextLabel("当前装备", Gold));
        var row = IconRow();
        _equipment.Controls.Add(row);
        if (player.Items is { Length: > 0 })
        {
            foreach (var item in player.Items)
            {
                row.Controls.Add(Icon("item", item.Id, 44, ItemName(item.Id, item.Name), count: item.Count));
            }
        }
        else
        {
            row.Controls.Add(TextLabel(player.Items == null ? "接口未提供装备信息" : "尚未购买装备", Muted));
        }
    }

    private void AddManualAugments(LiveMatchPlayer player)
    {
        _equipment.Controls.Add(TextLabel("已选强化 · 手动记录", Gold));
        var row = IconRow();
        _equipment.Controls.Add(row);
        if (_manualAugments.TryGetValue(player.Key, out var ids) && ids.Count > 0)
        {
            foreach (int id in ids)
            {
                var icon = Icon("augment", id, 44, AugmentName(id));
                row.Controls.Add(icon);
                icon.Click += (_, _) =>
                {
                    ids.Remove(id);
                    ShowEquipment();
                };
            }
        }
        else
        {
            row.Controls.Add(TextLabel("在底部记录已选强化，点击图标可移除。", Muted));
        }
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

        _ = StartWork(() => LoadIconAsync(icon, kind, id, name, description, owned, count));
        return icon;
    }

    private async Task LoadIconAsync(
        Button icon,
        string kind,
        int id,
        string name,
        string description,
        bool owned,
        int count
    )
    {
        if (id <= 0)
        {
            return;
        }
        try
        {
            var info = await CachedIconAsync(kind, id);
            if (_closing || icon.IsDisposed)
            {
                return;
            }
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
        catch (Exception ex)
        {
            // Missing local artwork leaves a named placeholder; the statistics remain usable.
            System.Diagnostics.Debug.WriteLine($"Live match icon: {ex.GetType().Name}");
        }
    }

    private void UpdateOwnedItems() => UpdateOwnedItems(_recommendations);

    private void UpdateOwnedItems(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Button button && button.Tag is ValueTuple<string, int> item && item.Item1 == "item")
            {
                bool owned = Selected()?.Items?.Any(current => current.Id == item.Item2) == true;
                button.FlatAppearance.BorderSize = owned ? 2 : 0;
                string description = "";
                if (_imageTasks.TryGetValue($"item:{item.Item2}", out var task) && task.IsCompletedSuccessfully)
                {
                    description = task.Result.description;
                }
                _tips.SetToolTip(
                    button,
                    TooltipText.Format(ItemName(item.Item2) + (owned ? " · 已拥有" : ""), description)
                );
            }
            UpdateOwnedItems(child);
        }
    }

    private Task<(Image? image, string name, string description)> CachedIconAsync(string kind, int id)
    {
        string key = $"{kind}:{id}";
        if (_imageTasks.TryGetValue(key, out var existing))
        {
            return existing;
        }

        return _imageTasks[key] = TrackWork(Load());
        async Task<(Image? image, string name, string description)> Load()
        {
            await Task.Yield();
            if (_closing)
            {
                return (null, "", "");
            }
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
                if (_closing || _life.IsCancellationRequested)
                {
                    info.image?.Dispose();
                    return (null, info.name, info.description);
                }
                if (info.image != null)
                {
                    _cachedImages.Add(info.image);
                }

                return info;
            }
            catch (Exception ex)
            {
                // Keep one failed placeholder per asset instead of retrying it on every poll.
                System.Diagnostics.Debug.WriteLine($"Live match cached icon: {ex.GetType().Name}");
                return (null, "", "");
            }
        }
    }

    private void DisposePlayerCards()
    {
        foreach (var cached in _playerCards.Values)
        {
            cached.Card.Dispose();
        }

        _playerCards.Clear();
    }

    private void DisposeImageCache()
    {
        foreach (var image in _cachedImages)
        {
            image.Dispose();
        }

        _cachedImages.Clear();
        _imageTasks.Clear();
    }

    private async Task LoadArtworkAsync(ProfileBackgroundPanel panel, int id)
    {
        Image? image = null;
        try
        {
            image = await _artworkLoader(id, _life.Token);
            if (_closing || panel.IsDisposed)
            {
                image?.Dispose();
                return;
            }
            panel.SetChampionArtwork(image);
            image = null;
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested)
        {
            image?.Dispose();
            // Closing abandons decoration; the panel is already being disposed.
        }
        catch (Exception ex)
        {
            image?.Dispose();
            // Champion artwork is optional when the client disconnects or a local asset is missing.
            System.Diagnostics.Debug.WriteLine($"Live match artwork: {ex.GetType().Name}");
        }
    }

    private async Task<Image?> ReadChampionArtworkAsync(int id, CancellationToken token)
    {
        var client = FormMain.Globals.lcuClient?.Client;
        if (client == null || id <= 0 || _closing)
        {
            return null;
        }
        if (!_championDetails.TryGetValue(id, out var data))
        {
            data = JObject.Parse(await client.GetStringAsync($"/lol-game-data/assets/v1/champions/{id}.json", token));
            if (_closing || (int?)data["id"] != id)
            {
                return null;
            }
            _championDetails[id] = data;
        }
        string? path = ProfileBackgroundPanel.FindSplash(data, id * 1000);
        if (path == null)
        {
            return null;
        }
        // The shared cache does not accept cancellation; decode off the UI thread and own its returned copy.
        return await Task.Run(
            () =>
                new ImageCacheService(client).GetOrDownloadImageAsync(path, "profile-backgrounds", $"{id * 1000}.png"),
            CancellationToken.None
        );
    }

    private void RenderRecommendations(
        IReadOnlyList<(int[] Ids, double? Win, double? Pick)> builds,
        IReadOnlyList<(string Title, AramMetaRecommendation[] Rows)> augments
    )
    {
        if (_closing)
        {
            return;
        }
        _recommendations.SuspendLayout();
        try
        {
            Clear(_recommendations);
            _recommendations.Controls.Add(TextLabel("推荐出装", Gold));
            if (builds.Count == 0)
            {
                _recommendations.Controls.Add(TextLabel("暂无组合，可打开完整攻略查看其他方案。", Muted));
            }
            foreach (var build in builds)
            {
                _recommendations.Controls.Add(CreateBuildRecommendation(build));
            }
            foreach (var group in augments)
            {
                AddAugmentRecommendations(group.Title, group.Rows);
            }
            if (augments.Count > 0)
            {
                _recommendations.Controls.Add(TextLabel("悬浮查看介绍；点击强化可记入手动记录。", Muted));
            }
            ResizeSections();
        }
        finally
        {
            _recommendations.ResumeLayout();
        }
    }

    private FlowLayoutPanel CreateBuildRecommendation((int[] Ids, double? Win, double? Pick) build)
    {
        var card = Stack();
        card.BackColor = Surface;
        card.Width = Math.Max(210, _recommendations.Width - 12);
        var icons = IconRow();
        card.Controls.Add(icons);
        foreach (int id in build.Ids)
        {
            icons.Controls.Add(
                Icon("item", id, 38, ItemName(id), owned: Selected()?.Items?.Any(item => item.Id == id) == true)
            );
        }
        card.Controls.Add(
            TextLabel($"胜率 {Rate(build.Win)} · 选用率 {Rate(build.Pick)}", Color.FromArgb(90, 206, 212))
        );
        return card;
    }

    private void AddAugmentRecommendations(string title, AramMetaRecommendation[] rows)
    {
        _recommendations.Controls.Add(TextLabel(title + " · 推荐", Gold));
        var row = IconRow();
        _recommendations.Controls.Add(row);
        foreach (var entry in rows.Where(entry => entry.Ids.Length > 0))
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
            icon.Click += (_, _) => RecordAugment(id);
        }
    }

    private void ResizeSections()
    {
        foreach (var host in new[] { _equipment, _recommendations })
        {
            foreach (Control c in host.Controls)
            {
                if (c is ProfileBackgroundPanel || c is FlowLayoutPanel { FlowDirection: FlowDirection.TopDown })
                {
                    c.MinimumSize = new Size(Math.Max(210, host.Width - 12), 0);
                    c.Width = Math.Max(210, host.Width - 12);
                }
                if (c is FlowLayoutPanel row)
                {
                    row.MaximumSize = new Size(Math.Max(210, host.Width - 12), 0);
                }

                if (c is Label label)
                {
                    label.MaximumSize = new Size(Math.Max(200, host.Width - 18), 0);
                }
            }
        }
    }
}
