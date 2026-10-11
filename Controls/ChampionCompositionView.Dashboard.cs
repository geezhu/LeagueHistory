using League.Infrastructure;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public sealed partial class ChampionCompositionView
{
    private readonly FlowLayoutPanel _heroes = new()
    {
        Dock = DockStyle.Top,
        Height = 98,
        WrapContents = false,
        Padding = new Padding(0, 4, 0, 4),
    };
    private readonly FlowLayoutPanel _overview = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
    };
    private readonly Panel _basic = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Dictionary<int, Image> _portraits = new();
    private readonly HashSet<int> _portraitLoads = new();
    private Func<int, Task<Image?>> _portraitLoader = id =>
        FormMain.Globals.resLoading?.GetChampionIconAsync(id) ?? Task.FromResult<Image?>(null);
    private Func<HttpClient?> _skillClient = () => FormMain.Globals.lcuClient?.Client;
    private readonly Dictionary<int, ChampionSelectAssistant.Skill[]> _skills = new();
    private readonly Dictionary<string, Image> _skillIcons = new();
    private readonly HashSet<int> _skillRequests = new();
    private readonly Dictionary<int, DateTime> _skillRetryAfter = new();
    private readonly ToolTip _abilityTips = new() { AutoPopDelay = 20000 };
    private int _selectedHero;
    private bool _dashboardDisposed,
        _resizingDashboard;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 100 };
    private bool _renderingDashboard;

    private void QueueDashboardRefresh()
    {
        if (!_dashboardDisposed && !_refreshTimer.Enabled)
        {
            _refreshTimer.Start();
        }
    }

    private static readonly (string Title, string Tag, string Note)[] Capabilities =
    [
        ("硬控制", "Trait_ImmobilizingCCSpell", "技能带有限制移动标签；具体类型、时长和命中条件需核对。"),
        ("护盾", "Trait_Shield", "技能带有护盾标签；不代表能保护队友，目标与强度需核对。"),
        ("范围效果", "Trait_AoE", "技能带有范围标签；可能是伤害、控制或增益，不等于清线强度。"),
        ("远程穿透", "Trait_Ranged_Piercing", "技能带有远程穿透标签；不用于推算最远射程或消耗强度。"),
    ];

    private void InitializeDashboard(FlowLayoutPanel bar)
    {
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            if (!_dashboardDisposed)
            {
                Render();
            }
        };
        _summary.Height = 78;
        _basic.Controls.Add(_details);
        _basic.Controls.Add(_grid);
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        foreach (var title in new[] { "阵容总览", "基础属性与技能" })
        {
            var button = ActionButton(title);
            tabs.Controls.Add(button);
            button.Click += (_, _) =>
            {
                _basic.Visible = title != "阵容总览";
                _overview.Visible = !_basic.Visible;
                foreach (Button tab in tabs.Controls)
                {
                    tab.BackColor = tab == button ? Color.FromArgb(0, 126, 142) : Color.FromArgb(16, 45, 57);
                }
            };
        }
        tabs.Controls[0].BackColor = Color.FromArgb(0, 126, 142);
        Controls.Add(_overview);
        Controls.Add(_basic);
        Controls.Add(tabs);
        Controls.Add(bar);
        Controls.Add(_heroes);
        Controls.Add(_summary);
        _overview.SizeChanged += (_, _) => ResizeDashboard();
        _heroes.SizeChanged += (_, _) => ResizeDashboard();
    }

    private Button ActionButton(string title) =>
        new()
        {
            Text = title,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            BackColor = Color.FromArgb(16, 45, 57),
            ForeColor = ForeColor,
            Margin = new Padding(0, 2, 8, 2),
            Padding = new Padding(6, 2, 6, 2),
        };

    private FlowLayoutPanel Card(string title, string note)
    {
        var card = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(Math.Max(280, _overview.ClientSize.Width - 24), 0),
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(10),
            BackColor = Color.FromArgb(16, 45, 57),
        };
        card.Controls.Add(
            new Label
            {
                Text = title,
                AutoSize = true,
                ForeColor = Color.FromArgb(232, 199, 121),
                Margin = new Padding(0, 0, 0, 8),
            }
        );
        card.Controls.Add(
            new Label
            {
                Text = note,
                AutoSize = true,
                MaximumSize = new Size(Math.Max(240, _overview.ClientSize.Width - 54), 0),
                ForeColor = Color.FromArgb(150, 177, 189),
                Margin = new Padding(0, 0, 0, 8),
            }
        );
        _overview.Controls.Add(card);
        return card;
    }

    private string[] Sources(int id, string tag) =>
        _snapshots.TryGetValue(id, out var snapshot)
            ? snapshot
                .Report.Spells.Where(s => s.Tags.Contains(tag, StringComparer.Ordinal))
                .Select(s => s.Key)
                .Distinct()
                .ToArray()
            : [];

    private void SelectHero(int id)
    {
        _selectedHero = id;
        var row = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (int?)r.Tag == id);
        if (row != null)
        {
            _grid.CurrentCell = row.Cells[0];
        }

        RenderDashboard();
    }

    private void RenderDashboard()
    {
        if (_dashboardDisposed)
        {
            return;
        }
        if (_renderingDashboard)
        {
            QueueDashboardRefresh();
            return;
        }
        _renderingDashboard = true;
        var scroll = _overview.AutoScrollPosition;
        var team = Team();
        if (_selectedHero <= 0 || !team.Any(e => e.ChampionId == _selectedHero))
        {
            _selectedHero = team.FirstOrDefault(e => e.ChampionId > 0)?.ChampionId ?? 0;
        }
        _heroes.SuspendLayout();
        _overview.SuspendLayout();
        try
        {
            ClearDashboardCards();
            foreach (var entry in team)
            {
                AddHeroCard(entry, team.Length);
            }
            RenderWeaknesses();
            bool preview = (_replacement.SelectedItem as Choice)?.Id > 0;
            if (preview)
            {
                RenderSwap();
            }
            RenderCapabilityCoverage(team);
            if (!preview)
            {
                RenderSwap();
            }
            RenderSelectedAbilities();
        }
        finally
        {
            _heroes.ResumeLayout();
            _overview.ResumeLayout();
            _renderingDashboard = false;
        }
        ResizeDashboard();
        _overview.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
        foreach (int id in AnalysisChampions())
        {
            _ = TrackLoad(LoadSkillsAsync(id));
        }
    }

    private void ClearDashboardCards()
    {
        foreach (Control control in _heroes.Controls.Cast<Control>().ToArray())
        {
            control.Dispose();
        }
        foreach (Control control in _overview.Controls.Cast<Control>().ToArray())
        {
            control.Dispose();
        }
    }

    private void AddHeroCard(ChampionSelectRosterEntry entry, int slots)
    {
        int id = entry.ChampionId;
        var card = new Panel
        {
            Tag = id,
            Width = Math.Max(100, (_heroes.ClientSize.Width - 20) / Math.Max(1, slots)),
            Height = 84,
            BackColor = id == _selectedHero ? Color.FromArgb(25, 76, 87) : Color.FromArgb(16, 45, 57),
            Margin = new Padding(0, 0, 4, 0),
            Cursor = Cursors.Hand,
        };
        card.Controls.Add(
            new PictureBox
            {
                Location = new Point(8, 8),
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = _portraits.GetValueOrDefault(id),
            }
        );
        card.Controls.Add(
            new Label
            {
                Text = id > 0 ? _champion(id).Name : "等待选择",
                Location = new Point(7, 43),
                Size = new Size(card.Width - 10, 19),
                AutoEllipsis = true,
            }
        );
        card.Controls.Add(
            new Label
            {
                Text =
                    entry.Preview ? "预选"
                    : entry.IsSelf ? "自己"
                    : "队友",
                Location = new Point(45, 14),
                AutoSize = true,
                ForeColor = Color.FromArgb(135, 204, 214),
            }
        );
        card.Controls.Add(
            new Label
            {
                Text = DataStatus(id),
                Location = new Point(7, 63),
                AutoSize = true,
                ForeColor = Color.FromArgb(150, 177, 189),
            }
        );
        card.Click += (_, _) => SelectHero(id);
        foreach (Control child in card.Controls)
        {
            child.Click += (_, _) => SelectHero(id);
        }
        _heroes.Controls.Add(card);
        if (id > 0)
        {
            _ = TrackLoad(LoadPortraitAsync(id));
        }
    }

    private void RenderCapabilityCoverage(ChampionSelectRosterEntry[] team)
    {
        int loaded = team.Count(e => _snapshots.ContainsKey(e.ChampionId));
        var coverage = Card(
            "能力覆盖",
            $"已读取 {loaded}/{team.Length} 个位置。以下是技能标签记录，未记录不代表没有；不包含装备、符文、强化和地图修正。"
        );
        foreach (var capability in Capabilities)
        {
            var providers = team.Where(e => Sources(e.ChampionId, capability.Tag).Length > 0).ToArray();
            AddCoverageGroup(coverage, capability.Title, capability.Tag, providers);
        }
    }

    private void AddCoverageGroup(Control coverage, string title, string tag, ChampionSelectRosterEntry[] providers)
    {
        var group = new FlowLayoutPanel
        {
            AutoSize = true,
            MaximumSize = new Size(Math.Max(240, _overview.Width - 60), 0),
            Margin = new Padding(0, 2, 0, 6),
        };
        group.Controls.Add(
            new Label
            {
                Text = $"{title} · {providers.Length} 个位置有记录",
                AutoSize = true,
                Margin = new Padding(0, 7, 12, 0),
            }
        );
        foreach (var entry in providers)
        {
            int hero = entry.ChampionId;
            var button = ActionButton(_champion(hero).Name + " · " + string.Join(" / ", Sources(hero, tag)));
            button.Click += (_, _) => SelectHero(hero);
            group.Controls.Add(button);
        }
        if (providers.Length == 0)
        {
            group.Controls.Add(
                new Label
                {
                    Text = "尚未确认",
                    AutoSize = true,
                    Margin = new Padding(0, 7, 0, 0),
                }
            );
        }
        coverage.Controls.Add(group);
    }

    private void RenderSelectedAbilities()
    {
        int id = _selectedHero;
        var selected = Card(
            id > 0 ? _champion(id).Name + " · 能力来源" : "能力来源",
            "点击英雄切换；技能标签不用于推算控制链、必中概率或实战伤害。"
        );
        if (!_snapshots.TryGetValue(id, out var snapshot))
        {
            selected.Controls.Add(
                new Label { Text = _errors.GetValueOrDefault(id) ?? "等待英雄数据", AutoSize = true }
            );
            return;
        }
        foreach (var spell in snapshot.Report.Spells)
        {
            AddAbilitySource(selected, id, spell);
        }
        selected.Controls.Add(
            new Label
            {
                Text = $"CommunityDragon · {snapshot.Report.Version} · {snapshot.FetchedAt.LocalDateTime:MM-dd HH:mm}",
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 0),
                ForeColor = Color.FromArgb(150, 177, 189),
            }
        );
    }

    private void AddAbilitySource(Control selected, int id, SpellAnalysis spell)
    {
        var labels = Capabilities.Where(c => spell.Tags.Contains(c.Tag, StringComparer.Ordinal)).ToArray();
        var line = new FlowLayoutPanel
        {
            AutoSize = true,
            MaximumSize = new Size(Math.Max(240, _overview.Width - 60), 0),
        };
        var skill = _skills.GetValueOrDefault(id)?.FirstOrDefault(s => s.Key == spell.Key);
        var button = CreateSkillButton(spell.Key, skill);
        line.Controls.Add(button);
        line.Controls.Add(
            new Label
            {
                Text = labels.Length > 0 ? string.Join(" · ", labels.Select(c => c.Title)) : "能力分类待确认",
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0),
            }
        );
        button.Click += (_, _) => ShowSkillEvidence(id, spell, skill?.Description, labels.Select(c => c.Note));
        selected.Controls.Add(line);
    }

    private Button CreateSkillButton(string key, ChampionSelectAssistant.Skill? skill)
    {
        var button = ActionButton(key + (skill == null ? "" : " · " + skill.Name));
        if (skill == null)
        {
            return button;
        }
        if (_skillIcons.TryGetValue(skill.Icon, out var icon))
        {
            button.Image = icon;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
        }
        _abilityTips.SetToolTip(button, TooltipText.Format(skill.Name, skill.Description));
        return button;
    }

    private void ShowSkillEvidence(int id, SpellAnalysis spell, string? description, IEnumerable<string> notes)
    {
        _details.Text =
            $"{_champion(id).Name} · {spell.Key}\n\n"
            + string.Join("\n\n", notes)
            + "\n\n"
            + (description ?? "本地技能说明尚未加载")
            + "\n\n目标类型："
            + spell.Targeting
            + "\n\n"
            + string.Join("\n", spell.Warnings);
        _basic.Visible = true;
        _overview.Visible = false;
    }

    private void ResizeDashboard()
    {
        if (_resizingDashboard || _dashboardDisposed)
        {
            return;
        }

        _resizingDashboard = true;
        _overview.SuspendLayout();
        try
        {
            int heroWidth = Math.Max(
                80,
                (_heroes.ClientSize.Width - 4 * _heroes.Controls.Count) / Math.Max(1, _heroes.Controls.Count)
            );
            foreach (Control hero in _heroes.Controls)
            {
                hero.Width = heroWidth;
                foreach (var label in hero.Controls.OfType<Label>().Where(l => !l.AutoSize))
                {
                    label.Width = heroWidth - 10;
                }
            }
            int width = Math.Max(280, _overview.ClientSize.Width - 24);
            foreach (Control card in _overview.Controls)
            {
                card.SuspendLayout();
                try
                {
                    card.MinimumSize = new Size(width, 0);
                    card.MaximumSize = new Size(width, 0);
                    card.Width = width;
                    foreach (Control child in card.Controls)
                    {
                        child.MaximumSize = new Size(width - 24, 0);
                        if (child is CompositionRadar)
                        {
                            child.Width = width - 24;
                        }
                    }
                }
                finally
                {
                    card.ResumeLayout();
                }
            }
        }
        finally
        {
            _overview.ResumeLayout();
            _resizingDashboard = false;
        }
    }

    private void RenderSwap()
    {
        int next = (_replacement.SelectedItem as Choice)?.Id ?? 0;
        int original = _roster?.Team.FirstOrDefault(e => e.IsSelf)?.ChampionId ?? 0;
        var card = Card(
            "换人预览",
            next == 0
                ? "从上方交换池选择英雄，查看替换自己后的能力变化。"
                : _champion(original).Name + " → " + _champion(next).Name + "（只预览，不操作客户端）"
        );
        if (next == 0)
        {
            return;
        }

        if (!_snapshots.ContainsKey(original) || !_snapshots.ContainsKey(next))
        {
            card.Controls.Add(new Label { Text = "等待双方数据后比较", AutoSize = true });
            return;
        }
        foreach (
            var change in CompositionWeakness.Compare(
                _roster?.Team.Select(e => e.ChampionId).ToArray() ?? [],
                Team().Select(e => e.ChampionId).ToArray(),
                Reports()
            )
        )
        {
            WeaknessLine(card, change, true);
        }

        foreach (var category in Capabilities)
        {
            bool before = Sources(original, category.Tag).Length > 0,
                after = Sources(next, category.Tag).Length > 0;
            string change =
                before == after
                    ? before
                        ? "保留来源"
                        : "双方均未记录"
                    : after
                        ? "新增来源"
                        : "移除已记录来源";
            int remaining = Team().Count(e => Sources(e.ChampionId, category.Tag).Length > 0);
            card.Controls.Add(
                new Label
                {
                    Text = $"{category.Title}：{change} · 替换后 {remaining} 个位置有记录",
                    AutoSize = true,
                    Margin = new Padding(0, 3, 0, 3),
                    ForeColor =
                        before == after ? ForeColor
                        : after ? Color.FromArgb(93, 216, 183)
                        : Color.FromArgb(235, 182, 116),
                }
            );
        }
    }

    private async Task LoadPortraitAsync(int id)
    {
        if (_dashboardDisposed || _portraits.ContainsKey(id) || !_portraitLoads.Add(id))
        {
            return;
        }

        try
        {
            var image = await _portraitLoader(id);
            if (image == null)
            {
                return;
            }

            if (_dashboardDisposed)
            {
                image.Dispose();
                return;
            }
            _portraits[id] = image;
            foreach (Panel card in _heroes.Controls)
            {
                if (card.Tag is int hero && hero == id)
                {
                    card.Controls.OfType<PictureBox>().First().Image = image;
                }
            }
        }
        catch (Exception ex)
        {
            // Optional artwork failure leaves the hero's name and analysis available.
            System.Diagnostics.Debug.WriteLine($"composition portrait: {ex.GetType().Name}");
        }
        finally
        {
            _portraitLoads.Remove(id);
        }
    }

    private async Task LoadSkillsAsync(int id)
    {
        if (_dashboardDisposed)
        {
            return;
        }
        var client = _skillClient();
        if (
            client == null
            || _skills.ContainsKey(id)
            || _skillRetryAfter.GetValueOrDefault(id) > DateTime.UtcNow
            || !_skillRequests.Add(id)
        )
        {
            return;
        }

        var token = _viewLifetime.Token;
        try
        {
            var document = JObject.Parse(
                await client.GetStringAsync($"/lol-game-data/assets/v1/champions/{id}.json", token)
            );
            if (_dashboardDisposed || (int?)document["id"] != id)
            {
                return;
            }

            var skills = ChampionSelectAssistant.ParseSkills(document);
            _skills[id] = skills;
            QueueDashboardRefresh();
            await LoadSkillIconsAsync(id, skills, client, token);
            if (!_dashboardDisposed && _selectedHero == id)
            {
                QueueDashboardRefresh();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Closing the view stops skill requests without scheduling a retry.
        }
        catch (Exception ex)
        {
            if (!_dashboardDisposed)
            {
                _skillRetryAfter[id] = DateTime.UtcNow.AddSeconds(30);
            }
            System.Diagnostics.Debug.WriteLine($"composition skills: {ex.GetType().Name}");
        }
        finally
        {
            _skillRequests.Remove(id);
        }
    }

    private async Task LoadSkillIconsAsync(
        int id,
        ChampionSelectAssistant.Skill[] skills,
        HttpClient client,
        CancellationToken token
    )
    {
        var cache = new ImageCacheService(client);
        foreach (var skill in skills.Where(s => !string.IsNullOrWhiteSpace(s.Icon)))
        {
            token.ThrowIfCancellationRequested();
            if (_skillIcons.ContainsKey(skill.Icon))
            {
                continue;
            }
            // The shared cache has no cancellation overload; retain the result to dispose any late bitmap.
            using var source = await cache.GetOrDownloadImageAsync(
                skill.Icon,
                "champion-abilities",
                $"{id}-{skill.Key}.png"
            );
            if (_dashboardDisposed)
            {
                return;
            }
            if (source != null && !_skillIcons.ContainsKey(skill.Icon))
            {
                _skillIcons[skill.Icon] = new Bitmap(source, new Size(28, 28));
            }
        }
    }

    private void DisposeDashboard()
    {
        _dashboardDisposed = true;
        _refreshTimer.Stop();
        _refreshTimer.Dispose();
        _abilityTips.Dispose();
        foreach (var icon in _skillIcons.Values)
        {
            icon.Dispose();
        }

        _skillIcons.Clear();
        foreach (var image in _portraits.Values)
        {
            image.Dispose();
        }

        _portraits.Clear();
    }
}
