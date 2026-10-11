using League.Clients;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed class AssistantRosterWindow : Form
{
    private readonly int _queue;
    private readonly bool _mayhem;
    private readonly ToolTip _tips;
    private readonly Func<ChampionSelectRoster?> _getRoster;
    private readonly Func<int> _currentQueue;
    private readonly Func<string?> _currentPuuid;
    private readonly Func<TimeSpan> _analysisLifetime;
    private readonly Func<
        CancellationToken,
        Task<(Dictionary<int, ChampionRosterTier> Ranks, string Version)>
    > _loadRanks;
    private readonly Func<string, CancellationToken, Task<bool>> _announce;
    private readonly Action<int> _openGuide;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly System.Windows.Forms.Timer _masteryTimer = new() { Interval = 10000 };
    private readonly List<Func<Task>> _masteryRefresh = new();
    private readonly HashSet<Task> _work = new();
    private readonly ChampionMasteryService _masteryService;
    private readonly Dictionary<int, ChampionRosterTier> _ranks = new();
    private readonly Func<int, Task<Image?>> _portraitLoader = id =>
        FormMain.Globals.resLoading.GetChampionIconAsync(id);
    private CancellationTokenSource? _painting;
    private Task _cleanupWork = Task.CompletedTask;
    private bool _loading,
        _sending,
        _closing;
    private string _loadedVersion = "";
    private FlowLayoutPanel _toolbar = null!,
        _content = null!;
    private ComboBox _sort = null!;
    private Button _retry = null!,
        _analysisButton = null!,
        _sendTier = null!;
    private ContextMenuStrip _sendMenu = null!;
    private ToolStripItem _sendAll = null!,
        _sendTeam = null!,
        _sendBench = null!;
    private Label _status = null!;
    private ChampionCompositionView _analysis = null!;

    public AssistantRosterWindow(
        Control owner,
        int queue,
        bool mayhem,
        ToolTip tips,
        Func<ChampionSelectRoster?> getRoster,
        Func<int> currentQueue,
        Func<string?> currentPuuid,
        Func<TimeSpan> analysisLifetime,
        Func<CancellationToken, Task<(Dictionary<int, ChampionRosterTier>, string)>> loadRanks,
        Func<string, CancellationToken, Task<bool>> announce,
        Action<int> openGuide,
        ChampionMasteryService masteryService
    )
    {
        _queue = queue;
        _mayhem = mayhem;
        _tips = tips;
        _getRoster = getRoster;
        _currentQueue = currentQueue;
        _currentPuuid = currentPuuid;
        _analysisLifetime = analysisLifetime;
        _loadRanks = loadRanks;
        _announce = announce;
        _openGuide = openGuide;
        _masteryService = masteryService;
        Text = "当前可选英雄 · " + (mayhem ? "海克斯大乱斗" : "大乱斗");
        ClientSize = new Size(760, 550);
        BackColor = owner.BackColor;
        ForeColor = owner.ForeColor;
        Font = owner.Font;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        CreateToolbar();
        CreateStatus();
        CreateContent();
        CreateAnalysis();
        ConnectEvents();
        _masteryTimer.Tick += (_, _) =>
        {
            foreach (var update in _masteryRefresh.ToArray())
            {
                _ = Track(update());
            }
        };
        _masteryTimer.Start();
        Shown += (_, _) =>
        {
            RenderRoster();
            _ = Track(LoadRankingsAsync());
        };
    }

    public Task Completion => _cleanupWork;
    public event Action<Task>? WorkRetired;

    public void UpdateRoster()
    {
        RenderRoster();
        if (_analysis.Visible && _getRoster() is { } roster)
        {
            _analysis.SetRoster(roster);
        }
    }

    private void CreateToolbar()
    {
        _toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 42),
            Padding = new Padding(10, 5, 10, 5),
        };
        _sort = new Controls.DarkFilterComboBox { Width = 125 };
        _sort.Items.AddRange(new object[] { "梯队优先", "胜率优先" });
        _sort.SelectedIndex = 0;
        _retry = new Button
        {
            Text = "重试数据",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
        };
        _toolbar.Controls.AddRange(new Control[] { _sort, _retry });
        _analysisButton = new Button
        {
            Text = "阵容分析",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
        };
        _toolbar.Controls.Add(_analysisButton);
        _sendTier = new Button
        {
            Text = "发送梯队 ▾",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            Enabled = false,
        };
        _toolbar.Controls.Add(_sendTier);
        _sendMenu = new ContextMenuStrip();
        _sendAll = _sendMenu.Items.Add("发送全部梯队");
        _sendTeam = _sendMenu.Items.Add("发送我方梯队");
        _sendBench = _sendMenu.Items.Add("发送交换池梯队");
        _sendTier.Click += (_, _) =>
        {
            _sendBench.Enabled = _getRoster()?.Bench.Length > 0;
            _sendMenu.Show(_sendTier, new Point(0, _sendTier.Height));
        };
    }

    private void CreateStatus()
    {
        _status = new Label
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(10, 3, 10, 3),
            Text = "正在读取英雄梯队…",
            ForeColor = Color.FromArgb(135, 162, 173),
        };
    }

    private void CreateContent()
    {
        _content = new BufferedRosterPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10),
        };
        this.Controls.Add(_content);
        this.Controls.Add(_status);
        this.Controls.Add(_toolbar);
    }

    private void CreateAnalysis()
    {
        _analysis = new Controls.ChampionCompositionView(
            id =>
            {
                var hero = FormMain.Globals.resLoading.GetChampionById(id);
                return (hero?.Name ?? $"英雄 {id}", hero?.Alias ?? "");
            },
            _analysisLifetime
        )
        {
            Dock = DockStyle.Fill,
            Visible = false,
        };
        this.Controls.Add(_analysis);
        _analysis.BringToFront();
        _analysisButton.Click += (_, _) =>
        {
            _analysis.Visible = !_analysis.Visible;
            _content.Visible = !_analysis.Visible;
            _status.Visible = !_analysis.Visible;
            _sort.Visible = !_analysis.Visible;
            _retry.Visible = !_analysis.Visible;
            _analysisButton.Text = _analysis.Visible ? "返回英雄梯队" : "阵容分析";
            if (_analysis.Visible && _getRoster() != null)
            {
                _analysis.SetRoster(_getRoster()!);
            }
        };
    }

    private void ConnectEvents()
    {
        this.SizeChanged += (_, _) => FitStatus();
        _status.TextChanged += (_, _) => FitStatus();
        _sendAll.Click += (_, _) => _ = Track(SendTiersAsync(null));
        _sendTeam.Click += (_, _) => _ = Track(SendTiersAsync(false));
        _sendBench.Click += (_, _) => _ = Track(SendTiersAsync(true));
        _sort.SelectedIndexChanged += (_, _) => RenderRoster();
        _retry.Click += (_, _) => _ = Track(LoadRankingsAsync());
        _content.SizeChanged += (_, _) =>
        {
            foreach (var grid in _content.Controls.OfType<FlowLayoutPanel>())
            {
                grid.MaximumSize = new Size(Math.Max(260, _content.ClientSize.Width - 36), 0);
            }
        };
    }

    private List<string> BuildTierMessages(bool? selection, ChampionSelectRoster roster)
    {
        var messages = new List<string>();
        foreach (bool bench in selection.HasValue ? new[] { selection.Value } : new[] { false, true })
        {
            var entries = bench
                ? roster.Bench.Select(id => (Id: id, Title: ChampionName(id)))
                : roster
                    .Team.Where(e => e.ChampionId > 0)
                    .Select(e => (Id: e.ChampionId, Title: ChampionName(e.ChampionId) + (e.Preview ? "（预选）" : "")));
            var rows = entries
                .OrderBy(e => TierSortOrder(_ranks.GetValueOrDefault(e.Id).Tier ?? "—"))
                .Select(e =>
                {
                    var rank = _ranks.GetValueOrDefault(e.Id);
                    return new ChampionTierChatEntry(e.Title, rank.Tier ?? "暂无梯队", rank.Win, rank.Pick);
                });
            messages.AddRange(
                ChampionTierChatMessage.Build(
                    _mayhem ? "海克斯大乱斗" : "大乱斗",
                    _mayhem ? "arammeta" : "OP.GG",
                    _loadedVersion,
                    bench ? "交换池" : "我方",
                    rows,
                    _mayhem
                )
            );
        }
        return messages;
    }

    private void AddHeroCard(FlowLayoutPanel grid, ChampionSelectRosterEntry entry, CancellationToken token)
    {
        var card = new Panel
        {
            Size = new Size(132, 136),
            Margin = new Padding(0, 0, 6, 6),
            BackColor = Color.FromArgb(16, 45, 57),
        };
        grid.Controls.Add(card);
        int id = entry.ChampionId;
        var rank = _ranks.GetValueOrDefault(id);
        string tier = rank.Tier ?? "—";
        var icon = new PictureBox
        {
            Location = new Point(7, 7),
            Size = new Size(40, 40),
            SizeMode = PictureBoxSizeMode.Zoom,
        };
        card.Controls.Add(icon);
        icon.Disposed += (_, _) => icon.Image?.Dispose();
        AddHeroLabels(card, entry, rank, tier);
        if (id > 0)
        {
            AddMastery(card, entry, token);
            _ = Track(LoadHeroIconAsync(icon, id, token));
            MakeGuideClickable(card, id, token);
        }
    }

    private void AddHeroLabels(Panel card, ChampionSelectRosterEntry entry, ChampionRosterTier rank, string tier)
    {
        card.Controls.Add(
            new Label
            {
                Text = tier,
                Location = new Point(56, 9),
                Size = new Size(66, 22),
                ForeColor = League.Controls.AramMetaView.TierColor(tier),
            }
        );
        card.Controls.Add(
            new Label
            {
                Text = rank.Win.HasValue ? $"{rank.Win:P1}" : "暂无统计",
                Location = new Point(56, 32),
                Size = new Size(72, 20),
            }
        );
        card.Controls.Add(
            new Label
            {
                Text = entry.ChampionId > 0 ? ChampionName(entry.ChampionId) : "等待选择",
                AutoEllipsis = true,
                Location = new Point(7, 57),
                Size = new Size(118, 22),
            }
        );
        card.Controls.Add(
            new Label
            {
                Text =
                    entry.CellId < 0
                        ? "交换池"
                        : (entry.IsSelf ? "自己" : "队友 " + (entry.CellId + 1)) + (entry.Preview ? " · 预选" : ""),
                Location = new Point(7, 84),
                Size = new Size(118, 22),
                ForeColor = Color.FromArgb(135, 162, 173),
            }
        );
    }

    private void AddMastery(Panel card, ChampionSelectRosterEntry entry, CancellationToken token)
    {
        var achievement = new Label
        {
            Text = "成就读取中…",
            AutoEllipsis = true,
            Location = new Point(7, 107),
            Size = new Size(118, 22),
            ForeColor = Color.FromArgb(164, 188, 199),
        };
        card.Controls.Add(achievement);
        string puuid = entry.CellId < 0 ? _currentPuuid() ?? "" : entry.Puuid;
        if (entry.IsSelf && (string.IsNullOrWhiteSpace(puuid) || puuid == Guid.Empty.ToString()))
        {
            puuid = _currentPuuid() ?? "";
        }

        var state = new CardMasteryState(achievement, entry, puuid, token);
        _masteryRefresh.Add(() => RefreshCardMasteryAsync(state));
        _ = Track(RefreshCardMasteryAsync(state));
    }

    private async Task RefreshCardMasteryAsync(CardMasteryState state)
    {
        if (state.Busy || state.Label.IsDisposed || state.Token.IsCancellationRequested)
        {
            return;
        }

        state.Busy = true;
        try
        {
            var page = await _masteryService.GetAsync(state.Puuid, state.Token);
            if (state.Label.IsDisposed || state.Token.IsCancellationRequested)
            {
                return;
            }

            var value = page.ForChampion(state.Entry.ChampionId);
            state.Label.Text = value.Text;
            _tips.SetToolTip(state.Label, (state.Entry.CellId < 0 ? "自己使用此英雄的成就\n" : "") + value.Description);
        }
        catch (OperationCanceledException)
        {
            // Closed or replaced cards cannot publish into the current roster.
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine("Roster mastery: " + error.GetType().Name);
            if (!state.Label.IsDisposed && !state.Token.IsCancellationRequested)
            {
                state.Label.Text = "成就暂不可用";
            }
        }
        finally
        {
            state.Busy = false;
        }
    }

    private async Task LoadHeroIconAsync(PictureBox icon, int id, CancellationToken token)
    {
        try
        {
            var image = await _portraitLoader(id);
            if (icon.IsDisposed || token.IsCancellationRequested)
            {
                image?.Dispose();
                return;
            }
            icon.Image = image;
        }
        catch (Exception error)
        {
            // Optional portraits do not prevent tier cards from displaying.
            System.Diagnostics.Debug.WriteLine("Roster icon: " + error.GetType().Name);
        }
    }

    private void MakeGuideClickable(Panel card, int id, CancellationToken token)
    {
        void Clickable(Control c)
        {
            c.Cursor = Cursors.Hand;
            c.Click += (_, _) =>
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                _openGuide(id);
            };
            foreach (Control child in c.Controls)
            {
                Clickable(child);
            }
        }
        Clickable(card);
    }

    private void FitStatus() =>
        _status.Height = Math.Max(
            42,
            TextRenderer
                .MeasureText(
                    _status.Text,
                    Font,
                    new Size(Math.Max(200, this.ClientSize.Width - 20), 0),
                    TextFormatFlags.WordBreak
                )
                .Height + 8
        );

    private static int TierSortOrder(string tier) =>
        tier == "OP" ? 0
        : int.TryParse(tier.TrimStart('T'), out int n) ? n
        : 99;

    private static string ChampionName(int id) => FormMain.Globals.resLoading.GetChampionById(id)?.Name ?? $"英雄 {id}";

    private async Task SendTiersAsync(bool? selection)
    {
        var roster = _getRoster();
        if (_closing || _sending || _loading || roster == null || _ranks.Count == 0)
        {
            return;
        }

        var messages = BuildTierMessages(selection, roster);
        if (messages.Count == 0)
        {
            _status.Text = "当前没有可发送的英雄。";
            return;
        }
        _sending = true;
        _sendTier.Enabled = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            int sent = 0;
            foreach (string message in messages)
            {
                if (!await _announce(message, timeout.Token))
                {
                    break;
                }

                sent++;
            }
            if (!IsDisposed)
            {
                _status.Visible = true;
                _status.Text =
                    sent == messages.Count
                        ? "英雄梯队已发送到选人聊天。"
                        : $"发送未完成（{sent}/{messages.Count}），请确认仍在选人阶段。";
            }
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !_lifetime.IsCancellationRequested)
            {
                _status.Visible = true;
                _status.Text = "发送超时，请检查选人聊天。";
            }
        }
        catch (Exception)
        {
            if (!IsDisposed)
            {
                _status.Visible = true;
                _status.Text = "梯队发送失败，请检查客户端连接后重试。";
            }
        }
        finally
        {
            _sending = false;
            if (!IsDisposed)
            {
                _sendTier.Enabled = !_loading && _ranks.Count > 0;
            }
        }
    }

    private void RenderRoster()
    {
        var roster = _getRoster();
        if (_closing || IsDisposed || roster == null)
        {
            return;
        }

        if (_queue != _currentQueue())
        {
            this.Close();
            return;
        }
        Retire(_painting);
        _painting = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _painting.Token;
        _masteryRefresh.Clear();
        _content.SuspendLayout();
        try
        {
            foreach (Control c in _content.Controls.Cast<Control>().ToArray())
            {
                c.Dispose();
            }

            AddGroup("我方阵容", roster.Team, "尚未读取到我方阵容", token);
            AddGroup(
                "交换池",
                roster.Bench.Select(id => new ChampionSelectRosterEntry(id, -1, false, false)),
                roster.BenchEnabled ? "交换池当前为空" : "客户端未提供交换池",
                token
            );
        }
        finally
        {
            _content.ResumeLayout();
        }
    }

    private void AddGroup(
        string title,
        IEnumerable<ChampionSelectRosterEntry> entries,
        string empty,
        CancellationToken token
    )
    {
        _content.Controls.Add(
            new Label
            {
                Text = title,
                AutoSize = true,
                ForeColor = Color.FromArgb(208, 185, 130),
                Margin = new Padding(0, 8, 0, 6),
            }
        );
        var grid = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MaximumSize = new Size(Math.Max(260, _content.ClientSize.Width - 36), 0),
            Margin = Padding.Empty,
        };
        _content.Controls.Add(grid);
        var ordered =
            _sort.SelectedIndex == 0
                ? entries
                    .OrderBy(e => TierSortOrder(_ranks.GetValueOrDefault(e.ChampionId).Tier ?? "—"))
                    .ThenByDescending(e => _ranks.GetValueOrDefault(e.ChampionId).Win)
                : entries
                    .OrderByDescending(e => _ranks.GetValueOrDefault(e.ChampionId).Win)
                    .ThenBy(e => TierSortOrder(_ranks.GetValueOrDefault(e.ChampionId).Tier ?? "—"));
        foreach (var entry in ordered)
        {
            AddHeroCard(grid, entry, token);
        }
        if (grid.Controls.Count == 0)
        {
            grid.Controls.Add(new Label { Text = empty, AutoSize = true });
        }
    }

    private async Task LoadRankingsAsync()
    {
        if (_closing || _loading)
        {
            return;
        }

        _loading = true;
        _retry.Enabled = false;
        _sendTier.Enabled = false;
        try
        {
            var (ranks, version) = await _loadRanks(_lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            _ranks.Clear();
            foreach (var pair in ranks)
            {
                _ranks.Add(pair.Key, pair.Value);
            }
            if (IsDisposed)
            {
                return;
            }

            _loadedVersion = version;
            _status.Text =
                $"{(_mayhem ? "arammeta · 调整后胜率" : "OP.GG · 胜率")} · {version}\n点击英雄查看攻略；队友英雄仅供查看，助手继续跟随自己的选人。";
            RenderRoster();
        }
        catch (OperationCanceledException)
        {
            // Closed or replaced cards cannot publish into the current roster.
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _status.Text = "梯队数据暂不可用，可重试；仍显示客户端提供的英雄。";
                _tips.SetToolTip(_status, ex.Message);
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }
        finally
        {
            _loading = false;
            if (!IsDisposed)
            {
                _retry.Enabled = true;
                _sendTier.Enabled = !_sending && _ranks.Count > 0;
            }
        }
    }

    private sealed class BufferedRosterPanel : FlowLayoutPanel
    {
        public BufferedRosterPanel()
        {
            DoubleBuffered = true;
        }
    }

    private sealed class CardMasteryState(
        Label label,
        ChampionSelectRosterEntry entry,
        string puuid,
        CancellationToken token
    )
    {
        public Label Label { get; } = label;
        public ChampionSelectRosterEntry Entry { get; } = entry;
        public string Puuid { get; } = puuid;
        public CancellationToken Token { get; } = token;
        public bool Busy { get; set; }
    }

    private Task Track(Task task)
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
                    System.Diagnostics.Debug.WriteLine("Roster work: " + error.GetBaseException().GetType().Name);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            _masteryTimer.Stop();
            _masteryTimer.Dispose();
            Retire(_painting);
            _painting = null;
            Retire(_lifetime);
            _cleanupWork = Task.WhenAll(Pending());
            WorkRetired?.Invoke(_cleanupWork);
            _sendMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
