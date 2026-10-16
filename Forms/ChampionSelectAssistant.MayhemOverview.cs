using League.Models;
using League.Services;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    private const int MayhemAugmentTile = 44;

    private void AddMayhemRecommendations(AramMetaPage page, int hero, CancellationToken token)
    {
        var champion =
            page.Index.Champions.FirstOrDefault(c => c.Id == hero)
            ?? throw new InvalidDataException("该英雄暂无海克斯推荐。");
        _apply.Text = "应用符文";
        _apply.Enabled = !_applying;
        var summary = AddTierSummary(champion.Tier, $"调整后胜率 {champion.WinRate:P1}");
        _tips.SetToolTip(
            summary,
            $"原始胜率 {champion.RawWinRate:P1}\n样本选取率 {page.Index.PickRate(champion):P1}\n来源样本 {champion.Games:N0} 场"
        );
        var host = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = Padding.Empty,
        };
        _body.Controls.Add(host);
        var augments = AddMayhemAugmentRows(page, hero, token, host);
        var (items, spells) = CreateMayhemDetailPanels(page, hero, token, host);
        TrackAssistantWork(LoadMayhemDetailsAsync(new(page, hero, token, items, spells, augments)));
    }

    private sealed class MayhemOverviewState(
        AramMetaPage page,
        int hero,
        CancellationToken token,
        FlowLayoutPanel items,
        FlowLayoutPanel spells,
        (string Key, FlowLayoutPanel Row)[] augments
    )
    {
        public AramMetaPage Page { get; set; } = page;
        public int Hero { get; } = hero;
        public CancellationToken Token { get; } = token;
        public FlowLayoutPanel Items { get; } = items;
        public FlowLayoutPanel Spells { get; } = spells;
        public (string Key, FlowLayoutPanel Row)[] Augments { get; } = augments;
    }

    private (string Key, FlowLayoutPanel Row)[] AddMayhemAugmentRows(
        AramMetaPage page,
        int hero,
        CancellationToken token,
        FlowLayoutPanel host
    )
    {
        var rows = new List<(string Key, FlowLayoutPanel Row)>();
        foreach (
            var (key, title, index) in new[]
            {
                ("kPrismatic", "棱彩强化", 0),
                ("kGold", "黄金强化", 1),
                ("kSilver", "白银强化", 2),
            }
        )
        {
            var heading = MayhemHeading(title);
            _tips.SetToolTip(heading, "查看全部" + title);
            host.Controls.Add(heading);
            heading.Click += (_, _) => BrowseMayhem(page, hero, token, 0, index);
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = Padding.Empty,
            };
            host.Controls.Add(row);
            if (page.Detail == null)
            {
                ShowMayhemAugmentPlaceholder(row);
            }
            else
            {
                FillMayhemAugmentRow(row, page, hero, key, token);
            }

            rows.Add((key, row));
        }

        return rows.ToArray();
    }

    // A tile-high placeholder keeps the row height stable until the full hero pool arrives,
    // instead of drawing the overview's three picks and then jumping to a full row.
    private void ShowMayhemAugmentPlaceholder(FlowLayoutPanel row)
    {
        row.Controls.Add(
            new Label
            {
                Text = "正在读取完整海克斯…",
                AutoSize = false,
                Size = new Size(ContentWidth, Dip(MayhemAugmentTile + 3)),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(135, 162, 173),
                Margin = Padding.Empty,
            }
        );
    }

    private void FillMayhemAugmentRow(
        FlowLayoutPanel row,
        AramMetaPage page,
        int hero,
        string key,
        CancellationToken token
    )
    {
        foreach (Control child in row.Controls.Cast<Control>().ToArray())
        {
            child.Dispose();
        }

        int tile = Dip(MayhemAugmentTile),
            minimumGap = Dip(3),
            count = Math.Max(1, (ContentWidth + minimumGap) / (tile + minimumGap)),
            gap = count > 1 ? (ContentWidth - count * tile) / (count - 1) : 0;
        var entries = AramMetaRecommendation
            .Sort(AramMetaRecommendation.Read(page, hero, key), 1)
            .Take(count)
            .ToArray();
        for (int i = 0; i < entries.Length; i++)
        {
            int spacing = i < entries.Length - 1 ? gap : 0;
            TrackAssistantWork(AddMayhemAugmentTileAsync(row, entries[i], spacing, token));
        }

        if (row.Controls.Count == 0)
        {
            row.Controls.Add(new Label { Text = "暂无推荐", AutoSize = true });
        }
    }

    private (FlowLayoutPanel Items, FlowLayoutPanel Spells) CreateMayhemDetailPanels(
        AramMetaPage page,
        int hero,
        CancellationToken token,
        FlowLayoutPanel host
    )
    {
        var itemsHeader = MayhemHeading("出装推荐");
        host.Controls.Add(itemsHeader);
        itemsHeader.Click += (_, _) => BrowseMayhem(page, hero, token, 1);
        var items = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = Padding.Empty,
        };
        host.Controls.Add(items);
        var spellsHeader = new Label
        {
            Text = "召唤师技能",
            AutoSize = true,
            ForeColor = MutedHeading,
            Margin = DipPadding(0, 2, 0, 2),
        };
        _tips.SetToolTip(spellsHeader, "点击下方技能卡片即可配置");
        host.Controls.Add(spellsHeader);
        var spells = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = Padding.Empty,
        };
        host.Controls.Add(spells);
        return (items, spells);
    }

    private void AddMayhemEquipment(MayhemOverviewState state)
    {
        var equipment = AramMetaRecommendation.Read(state.Page, state.Hero, "items");
        if (equipment.Length > 0)
        {
            var build = Services.OpggItemSetService.FromAramMeta(state.Page, state.Hero);
            var cores = equipment.Select(r => r.ToOption()).ToArray();
            var select = new Controls.EquipmentComboBox(cores, state.Token)
            {
                Width = Dip(190),
                Margin = DipPadding(0, 0, 4, 0),
            };
            var configure = MayhemLink("配置出装", 82);
            configure.Height = select.Height;
            configure.Margin = Padding.Empty;
            configure.FlatAppearance.BorderColor = Color.FromArgb(42, 72, 83);
            state.Items.Controls.AddRange(new Control[] { select, configure });
            int bootsIndex = 0;
            configure.Click += (_, _) =>
            {
                if (state.Token.IsCancellationRequested)
                {
                    return;
                }

                ShowEquipmentEditor(
                    build,
                    cores,
                    select.SelectedIndex,
                    0,
                    bootsIndex,
                    state.Token,
                    (core, starter, boots) =>
                    {
                        if (select.IsDisposed || state.Token.IsCancellationRequested)
                        {
                            return;
                        }

                        select.SelectedIndex = core;
                        bootsIndex = boots;
                    },
                    "arammeta"
                );
            };
        }
        else
        {
            state.Items.Controls.Add(new Label { Text = "暂无出装推荐", AutoSize = true });
        }
    }

    private void AddMayhemSpells(MayhemOverviewState state)
    {
        var options = AramMetaRecommendation.Read(state.Page, state.Hero, "spells");
        var highlights = AramMetaRecommendation.SpellHighlights(options);
        if (
            highlights.Length == 2
            && highlights[0].Entry is { } popular
            && highlights[1].Entry is { } winning
            && popular.Ids.OrderBy(id => id).SequenceEqual(winning.Ids.OrderBy(id => id))
        )
        {
            AddMayhemSpellCard(state, "使用率 · 胜率最高", popular);
            return;
        }

        foreach (var (title, entry) in highlights)
        {
            AddMayhemSpellCard(state, title, entry);
        }
    }

    private void AddMayhemSpellCard(MayhemOverviewState state, string title, AramMetaRecommendation? entry)
    {
        var card = new Panel
        {
            Size = Dip(136, 58),
            BackColor = Color.FromArgb(16, 45, 57),
            Margin = DipPadding(0, 0, 4, 0),
        };
        state.Spells.Controls.Add(card);
        card.Controls.Add(
            new Label
            {
                Text = title,
                Location = new Point(Dip(4), Dip(1)),
                Size = Dip(128, 18),
                ForeColor = MutedHeading,
            }
        );
        if (entry == null)
        {
            card.Controls.Add(
                new Label
                {
                    Text = "暂无数据",
                    Location = new Point(Dip(4), Dip(25)),
                    AutoSize = true,
                }
            );
            return;
        }
        card.Controls.Add(
            new Label
            {
                Text = title == "使用率最高" ? $"{entry.PickRate:P1}" : $"{entry.WinRate:P1}",
                Location = new Point(Dip(65), Dip(27)),
                Size = Dip(69, 22),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(61, 188, 193),
            }
        );
        void Clickable(Control c)
        {
            c.Cursor = Cursors.Hand;
            c.Click += async (_, _) => await ApplySpellsAsync(entry.Ids, state.Hero, state.Token);
        }
        Clickable(card);
        foreach (Control child in card.Controls)
        {
            Clickable(child);
        }

        for (int i = 0; i < entry.Ids.Length; i++)
        {
            var picture = new PictureBox
            {
                Size = Dip(26, 26),
                Location = new Point(Dip(4 + i * 30), Dip(25)),
                SizeMode = PictureBoxSizeMode.Zoom,
            };
            card.Controls.Add(picture);
            Clickable(picture);
            picture.Disposed += (_, _) => picture.Image?.Dispose();
            TrackAssistantWork(LoadMayhemSpellIconAsync(picture, entry.Ids[i], state.Token));
        }
        _tips.SetToolTip(
            card,
            $"点击配置 · 胜率 {entry.WinRate:P1} · 使用率 {entry.PickRate:P1} · 样本 {entry.Games:N0} 场"
        );
    }

    private Button MayhemLink(string text, int width) =>
        new()
        {
            Text = text,
            Width = Dip(width),
            Height = Dip(21),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(16, 45, 57),
            ForeColor = ActionText,
            Margin = DipPadding(0, 1, 0, 1),
        };

    private static Label MayhemHeading(string text) =>
        new()
        {
            Text = text + "  ›",
            Size = new Size(ContentWidth - Dip(6), Dip(18)),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = MutedHeading,
            Margin = Padding.Empty,
            Cursor = Cursors.Hand,
        };

    private void BrowseMayhem(AramMetaPage page, int hero, CancellationToken token, int tab, int rarity = 0)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        using var dialog = new Form
        {
            Text = "arammeta · 推荐比较",
            ClientSize = Dip(304, 460),
            StartPosition = FormStartPosition.Manual,
            BackColor = BackColor,
            ForeColor = ForeColor,
            Font = Font,
            ShowInTaskbar = false,
        };
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = DipPadding(10),
        };
        dialog.Controls.Add(content);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        AddMayhemBrowser(page, hero, lifetime.Token, content, tab, rarity);
        dialog.Shown += (_, _) =>
        {
            PositionEquipmentEditor(dialog, Dip(304, 400));
        };
        using var watcher = new System.Windows.Forms.Timer { Interval = 250 };
        watcher.Tick += (_, _) =>
        {
            if (token.IsCancellationRequested)
            {
                dialog.Close();
            }
        };
        watcher.Start();
        dialog.FormClosed += (_, _) => TrackAssistantWork(lifetime.CancelAsync());
        try
        {
            dialog.ShowDialog(this);
        }
        finally
        {
            dialog.Dispose();
            RetireAssistantSource(lifetime);
        }
    }

    private async Task AddMayhemAugmentTileAsync(
        FlowLayoutPanel parent,
        AramMetaRecommendation recommendation,
        int spacing,
        CancellationToken token
    )
    {
        string name = FormMain.Globals.resLoading.GetAugmentById(recommendation.Ids[0])?.Name ?? recommendation.Name;
        var card = new PictureBox
        {
            Tag = "mayhem-augment",
            AccessibleName = name,
            Size = Dip(MayhemAugmentTile, MayhemAugmentTile),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(16, 45, 57),
            Margin = new Padding(0, 0, spacing, Dip(3)),
        };
        parent.Controls.Add(card);
        card.Disposed += (_, _) => card.Image?.Dispose();
        var rate = new Label
        {
            Text = recommendation.WinRate.HasValue ? $"{recommendation.WinRate:P1}" : "—",
            Dock = DockStyle.Bottom,
            Height = Dip(17),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(130, 237, 231),
        };
        card.Controls.Add(rate);
        void Describe(string description)
        {
            string tip =
                League.Controls.TooltipText.Format(name, description)
                + $"\n调整后胜率 {recommendation.WinRate:P1}\n选取率 {recommendation.PickRate:P1} · 样本 {recommendation.Games:N0} 场";
            _tips.SetToolTip(card, tip);
            _tips.SetToolTip(rate, tip);
        }
        Describe(
            FormMain.Globals.resLoading.GetAugmentById(recommendation.Ids[0])?.Description ?? recommendation.Description
        );
        (Image? image, string name, string description) info;
        try
        {
            info = await FormMain.Globals.resLoading.GetAugmentInfoAsync(recommendation.Ids[0]);
        }
        catch (Exception error)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "Mayhem augment icon", error);
            return;
        }
        if (token.IsCancellationRequested || card.IsDisposed)
        {
            info.image?.Dispose();
            return;
        }
        card.Image = info.image;
        if (!string.IsNullOrWhiteSpace(info.description))
        {
            Describe(info.description);
        }
    }

    private async Task LoadMayhemDetailsAsync(MayhemOverviewState state)
    {
        if (state.Token.IsCancellationRequested || state.Items.IsDisposed)
        {
            return;
        }

        foreach (var panel in new[] { state.Items, state.Spells })
        {
            foreach (Control child in panel.Controls.Cast<Control>().ToArray())
            {
                child.Dispose();
            }

            panel.Controls.Add(new Label { Text = "加载推荐…", AutoSize = true });
        }
        try
        {
            if (state.Page.Detail == null)
            {
                state.Page = await _am.GetAsync(state.Hero, false, state.Token);
            }

            state.Token.ThrowIfCancellationRequested();
            if (state.Items.IsDisposed)
            {
                return;
            }

            foreach (var panel in new[] { state.Items, state.Spells })
            {
                foreach (Control child in panel.Controls.Cast<Control>().ToArray())
                {
                    child.Dispose();
                }
            }

            foreach (var (key, row) in state.Augments)
            {
                if (!row.IsDisposed)
                {
                    FillMayhemAugmentRow(row, state.Page, state.Hero, key, state.Token);
                }
            }

            AddMayhemEquipment(state);
            AddMayhemSpells(state);
        }
        catch (OperationCanceledException)
        {
            // Hero changes retire both recommendation panels.
        }
        catch
        {
            if (state.Items.IsDisposed || state.Token.IsCancellationRequested)
            {
                return;
            }

            foreach (var panel in new[] { state.Items, state.Spells })
            {
                foreach (Control child in panel.Controls.Cast<Control>().ToArray())
                {
                    child.Dispose();
                }
            }

            // Without the hero pool, fall back to the overview's own picks rather than empty rows.
            foreach (var (key, row) in state.Augments)
            {
                if (!row.IsDisposed)
                {
                    FillMayhemAugmentRow(row, state.Page, state.Hero, key, state.Token);
                }
            }

            var retry = MayhemLink("推荐加载失败 · 重试", 276);
            state.Items.Controls.Add(retry);
            retry.Click += (_, _) => TrackAssistantWork(LoadMayhemDetailsAsync(state));
        }
    }

    private async Task LoadMayhemSpellIconAsync(PictureBox picture, int id, CancellationToken token)
    {
        try
        {
            var info = await FormMain.Globals.resLoading.GetSpellInfoAsync(id);
            if (picture.IsDisposed || token.IsCancellationRequested)
            {
                info.image?.Dispose();
                return;
            }
            picture.Image = info.image;
            _tips.SetToolTip(
                picture,
                League.Controls.TooltipText.Format(info.name, info.description) + "\n点击配置这组召唤师技能"
            );
        }
        catch (Exception error)
        {
            // Optional icon failure leaves the actionable recommendation card visible.
            AppLog.Shared.Warn(LogCategory.Ui, "Mayhem spell icon", error);
        }
    }
}
