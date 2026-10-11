using League.Models;
using Newtonsoft.Json.Linq;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    private void AddMayhemBrowser(
        AramMetaPage page,
        int hero,
        CancellationToken token,
        FlowLayoutPanel host,
        int initialTab = 0,
        int initialRarity = 0
    )
    {
        var champion =
            page.Index.Champions.FirstOrDefault(c => c.Id == hero)
            ?? throw new InvalidDataException("该英雄暂无海克斯推荐。");
        _apply.Text = "应用符文";
        _apply.Enabled = !_applying;
        static string Rate(JToken? n) => RateValue(AramMetaIndex.Rate(n));
        static string RateValue(double? n) => n.HasValue ? n.Value.ToString("P1") : "—";
        var tabs = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };
        host.Controls.Add(tabs);
        Button Button(string text, int width) =>
            new()
            {
                Text = text,
                Width = width,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(16, 45, 57),
                ForeColor = ForeColor,
                Margin = new Padding(0, 0, 4, 4),
            };
        int tab = initialTab,
            offset = 0;
        var tabButtons = new[] { Button("强化", 88), Button("装备", 88), Button("技能", 88) };
        foreach (var button in tabButtons)
        {
            button.FlatAppearance.BorderColor = Color.FromArgb(42, 72, 83);
            tabs.Controls.Add(button);
        }
        var filters = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };
        host.Controls.Add(filters);
        var group = new Controls.DarkFilterComboBox { Width = 126, Margin = new Padding(0, 0, 4, 4) };
        var sort = new Controls.DarkFilterComboBox { Width = 146, Margin = new Padding(0, 0, 0, 4) };
        sort.Items.AddRange(new object[] { "来源推荐顺序", "胜率最高", "选取率最高" });
        sort.SelectedIndex = 0;
        filters.Controls.AddRange(new Control[] { group, sort });
        var rows = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            MaximumSize = new Size(282, 0),
            Margin = Padding.Empty,
        };
        host.Controls.Add(rows);
        var pager = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };
        host.Controls.Add(pager);
        var previous = Button("上一页", 68);
        var next = Button("下一页", 68);
        var count = new Label
        {
            Width = 126,
            Height = 28,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        pager.Controls.AddRange(new Control[] { previous, count, next });
        previous.FlatAppearance.BorderColor = next.FlatAppearance.BorderColor = Color.FromArgb(42, 72, 83);
        CancellationTokenSource? painting = null;
        Task<AramMetaPage>? detailTask = null;
        bool syncing = false;
        rows.Disposed += (_, _) =>
        {
            painting?.Cancel();
            painting?.Dispose();
        };
        void SetGroups()
        {
            syncing = true;
            group.Items.Clear();
            group.Items.AddRange(
                tab == 0 ? new object[] { "棱彩强化", "黄金强化", "白银强化" }
                : tab == 1 ? new object[] { "装备组合", "单件装备", "鞋子" }
                : new object[] { "召唤师技能" }
            );
            group.SelectedIndex = 0;
            group.Enabled = tab != 2;
            syncing = false;
        }
        async Task Render()
        {
            if (token.IsCancellationRequested || rows.IsDisposed)
            {
                return;
            }

            painting?.Cancel();
            painting?.Dispose();
            painting = CancellationTokenSource.CreateLinkedTokenSource(token);
            var paintToken = painting.Token;
            foreach (Control child in rows.Controls.Cast<Control>().ToArray())
            {
                child.Dispose();
            }

            previous.Enabled = next.Enabled = false;
            count.Text = "加载中…";
            for (int i = 0; i < 3; i++)
            {
                tabButtons[i].BackColor = i == tab ? Color.FromArgb(0, 126, 139) : Color.FromArgb(16, 45, 57);
            }

            try
            {
                if (tab != 0 && page.Detail == null)
                {
                    rows.Controls.Add(new Label { Text = "正在读取装备与技能推荐…", AutoSize = true });
                    if (detailTask == null || detailTask.IsFaulted || detailTask.IsCanceled)
                    {
                        detailTask = _am.GetAsync(hero, false, token);
                    }

                    var detailed = await detailTask;
                    paintToken.ThrowIfCancellationRequested();
                    page = detailed;
                    foreach (Control child in rows.Controls.Cast<Control>().ToArray())
                    {
                        child.Dispose();
                    }
                }
                string key =
                    tab == 0 ? new[] { "kPrismatic", "kGold", "kSilver" }[group.SelectedIndex]
                    : tab == 1 ? new[] { "items", "singleItems", "boots" }[group.SelectedIndex]
                    : "spells";
                var recommendations = AramMetaRecommendation.Read(page, hero, key);
                var highlights = AramMetaRecommendation.SpellHighlights(recommendations);
                filters.Visible = tab != 2;
                pager.Visible = tab != 2;
                var entries =
                    tab == 2
                        ? highlights.Where(h => h.Entry != null).Select(h => h.Entry!.Raw)
                        : AramMetaRecommendation.Sort(recommendations, sort.SelectedIndex).Select(r => r.Raw);
                var all = entries.ToArray();
                int pageSize = tab == 0 ? 4 : 3;
                int total = Math.Max(1, (all.Length + pageSize - 1) / pageSize);
                offset = Math.Clamp(offset, 0, total - 1);
                count.Text = $"{offset + 1} / {total} · {all.Length} 项";
                previous.Enabled = offset > 0;
                next.Enabled = offset + 1 < total;
                int spellIndex = 0;
                foreach (var entry in all.Skip(offset * pageSize).Take(pageSize))
                {
                    paintToken.ThrowIfCancellationRequested();
                    bool augment = tab == 0,
                        spell = tab == 2;
                    int id = (int?)entry["id"] ?? 0;
                    string name = augment
                        ? FormMain.Globals.resLoading.GetAugmentById(id)?.Name
                            ?? (string?)page.Index.Augments[id.ToString()]?["name_zh"]
                            ?? $"强化 {id}"
                        : (string?)entry["name_zh"] ?? (string?)entry["name_en"] ?? "推荐组合";
                    if (spell)
                    {
                        name = highlights.Where(h => h.Entry != null).ElementAt(spellIndex++).Title;
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
                    rows.Controls.Add(card);
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
                            ?? (string?)page.Index.Augments[id.ToString()]?["desc_zh"]
                            ?? ""
                        : name;
                    _tips.SetToolTip(title, League.Controls.TooltipText.Format(name, description));
                    _tips.SetToolTip(
                        stats,
                        $"来源样本：{(long?)entry["g"]:N0} 场；选取率和胜率均来自 arammeta。"
                            + (augment ? "\n强化胜率为调整后胜率。" : "")
                    );
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
                    if (spell && ids.Length == 2)
                    {
                        void Clickable(Control control)
                        {
                            control.Cursor = Cursors.Hand;
                            control.Click += async (_, _) => await ApplySpellsAsync(ids, hero, token);
                            foreach (Control child in control.Controls)
                            {
                                Clickable(child);
                            }
                        }
                        Clickable(card);
                        _tips.SetToolTip(title, name + "\n点击配置这组召唤师技能");
                    }
                }
                if (all.Length == 0)
                {
                    rows.Controls.Add(new Label { Text = "当前分类暂无推荐数据", AutoSize = true });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!paintToken.IsCancellationRequested && !rows.IsDisposed)
                {
                    count.Text = "加载失败";
                    rows.Controls.Add(new Label { Text = "详情暂不可用，可切回强化或重试。", AutoSize = true });
                    var retry = Button("重试详情", 100);
                    rows.Controls.Add(retry);
                    retry.Click += (_, _) => _ = Render();
                }
            }
        }
        for (int i = 0; i < 3; i++)
        {
            int target = i;
            tabButtons[i].Click += (_, _) =>
            {
                tab = target;
                offset = 0;
                SetGroups();
                _ = Render();
            };
        }
        group.SelectedIndexChanged += (_, _) =>
        {
            if (!syncing)
            {
                offset = 0;
                _ = Render();
            }
        };
        sort.SelectedIndexChanged += (_, _) =>
        {
            offset = 0;
            _ = Render();
        };
        previous.Click += (_, _) =>
        {
            offset--;
            _ = Render();
        };
        next.Click += (_, _) =>
        {
            offset++;
            _ = Render();
        };
        SetGroups();
        group.SelectedIndex = Math.Clamp(initialRarity, 0, group.Items.Count - 1);
        _ = Render();
    }
}
