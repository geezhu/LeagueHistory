using League.Models;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
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
            MaximumSize = new Size(282, 0),
            Margin = Padding.Empty,
        };
        _body.Controls.Add(host);
        AddMayhemAugmentRows(page, hero, token, host);
        var (items, spells) = CreateMayhemDetailPanels(page, hero, token, host);
        TrackAssistantWork(LoadMayhemDetailsAsync(new(page, hero, token, items, spells)));
    }

    private sealed class MayhemOverviewState(
        AramMetaPage page,
        int hero,
        CancellationToken token,
        FlowLayoutPanel items,
        FlowLayoutPanel spells
    )
    {
        public AramMetaPage Page { get; set; } = page;
        public int Hero { get; } = hero;
        public CancellationToken Token { get; } = token;
        public FlowLayoutPanel Items { get; } = items;
        public FlowLayoutPanel Spells { get; } = spells;
    }

    private void AddMayhemAugmentRows(AramMetaPage page, int hero, CancellationToken token, FlowLayoutPanel host)
    {
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
            host.Controls.Add(heading);
            heading.Click += (_, _) => BrowseMayhem(page, hero, token, 0, index);
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MaximumSize = new Size(282, 0),
                Margin = Padding.Empty,
            };
            host.Controls.Add(row);
            foreach (var entry in AramMetaRecommendation.Read(page, hero, key).Take(5))
            {
                TrackAssistantWork(AddMayhemAugmentTileAsync(row, entry, token));
            }

            if (row.Controls.Count == 0)
            {
                row.Controls.Add(new Label { Text = "暂无推荐", AutoSize = true });
            }
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
            MaximumSize = new Size(282, 0),
            Margin = Padding.Empty,
        };
        host.Controls.Add(items);
        var spellsHeader = new Label
        {
            Text = "召唤师技能 · 点击配置",
            AutoSize = true,
            ForeColor = Color.FromArgb(208, 185, 130),
            Margin = new Padding(0, 2, 0, 2),
        };
        host.Controls.Add(spellsHeader);
        var spells = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MaximumSize = new Size(282, 0),
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
                Width = 190,
                Margin = new Padding(0, 0, 4, 0),
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
        foreach (var (title, entry) in AramMetaRecommendation.SpellHighlights(options))
        {
            AddMayhemSpellCard(state, title, entry);
        }
    }

    private void AddMayhemSpellCard(MayhemOverviewState state, string title, AramMetaRecommendation? entry)
    {
        var card = new Panel
        {
            Size = new Size(136, 58),
            BackColor = Color.FromArgb(16, 45, 57),
            Margin = new Padding(0, 0, 4, 0),
        };
        state.Spells.Controls.Add(card);
        card.Controls.Add(
            new Label
            {
                Text = title,
                Location = new Point(4, 1),
                Size = new Size(128, 18),
                ForeColor = Color.FromArgb(208, 185, 130),
            }
        );
        if (entry == null)
        {
            card.Controls.Add(
                new Label
                {
                    Text = "暂无数据",
                    Location = new Point(4, 25),
                    AutoSize = true,
                }
            );
            return;
        }
        card.Controls.Add(
            new Label
            {
                Text = title == "使用率最高" ? $"{entry.PickRate:P1}" : $"{entry.WinRate:P1}",
                Location = new Point(65, 27),
                Size = new Size(69, 22),
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
                Size = new Size(26, 26),
                Location = new Point(4 + i * 30, 25),
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
            Width = width,
            Height = 21,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(16, 45, 57),
            ForeColor = Color.FromArgb(208, 185, 130),
            Margin = new Padding(0, 1, 0, 1),
        };

    private static Label MayhemHeading(string text) =>
        new()
        {
            Text = text + "  ›",
            Size = new Size(276, 18),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(208, 185, 130),
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
            ClientSize = new Size(304, 460),
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
            Padding = new Padding(10),
        };
        dialog.Controls.Add(content);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        AddMayhemBrowser(page, hero, lifetime.Token, content, tab, rarity);
        dialog.Shown += (_, _) =>
        {
            PositionEquipmentEditor(dialog, new Size(304, 400));
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
        CancellationToken token
    )
    {
        string name = FormMain.Globals.resLoading.GetAugmentById(recommendation.Ids[0])?.Name ?? recommendation.Name;
        var card = new PictureBox
        {
            Tag = "mayhem-augment",
            AccessibleName = name,
            Size = new Size(44, 44),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(16, 45, 57),
            Margin = new Padding(0, 0, 10, 3),
        };
        parent.Controls.Add(card);
        card.Disposed += (_, _) => card.Image?.Dispose();
        var rate = new Label
        {
            Text = recommendation.WinRate.HasValue ? $"{recommendation.WinRate:P1}" : "—",
            Dock = DockStyle.Bottom,
            Height = 17,
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
            System.Diagnostics.Debug.WriteLine("Mayhem augment icon: " + error.GetType().Name);
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
            System.Diagnostics.Debug.WriteLine("Mayhem spell icon: " + error.GetType().Name);
        }
    }
}
