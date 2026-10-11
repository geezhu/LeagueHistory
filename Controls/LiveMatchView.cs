using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public sealed partial class LiveMatchView : UserControl
{
    private readonly LiveMatchClient _live;
    private readonly Func<string, int, Task<(Image? image, string name, string description)>>? _iconLoader;
    private readonly OpggClient _opgg;
    private readonly AramMetaClient _am;
    private readonly Func<Task<int>> _queue;
    private readonly Action<int, int> _guide;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };
    private readonly CancellationTokenSource _life = new();
    private readonly HashSet<Task> _work = new();
    private bool _closing;
    private readonly bool _ownsLive;
    private readonly bool _ownsOpgg;
    private readonly bool _ownsAramMeta;
    private Task _cleanupWork = Task.CompletedTask;
    private CancellationTokenSource? _detailLoad;
    private readonly Label _status = new()
    {
        Dock = DockStyle.Top,
        Height = 48,
        Padding = new Padding(10),
        Text = "进入游戏后，在这里查看双方英雄与装备。",
    };
    private readonly ComboBox _mode = new DarkFilterComboBox { Width = 165 };
    private readonly FlowLayoutPanel _players = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        Padding = new Padding(8),
    };
    private readonly FlowLayoutPanel _equipment = Stack();
    private readonly FlowLayoutPanel _recommendations = Stack();
    private string? _selectedKey;
    private readonly ComboBox _augment = new DarkFilterComboBox { Width = 230 };
    private readonly FlowLayoutPanel _augmentBar = new()
    {
        Dock = DockStyle.Bottom,
        Height = 42,
        AutoSize = true,
        WrapContents = true,
    };
    private readonly Dictionary<string, List<int>> _manualAugments = new();
    private readonly Dictionary<int, (string Tier, double? Win, double? Pick)> _ranks = new();
    private LiveMatchSnapshot? _snapshot;
    private bool _busy,
        _rendering;
    private int _currentQueue,
        _rankQueue = -1;
    private DateTime _rankChecked;
    private Task? _rankLoad;
    private CancellationTokenSource? _rankCancellation;
    private readonly Func<TimeSpan> _cacheLifetime;
    private string? _pendingRecommendation;

    private sealed record RecommendationCache(
        DateTime Fetched,
        List<(int[] Ids, double? Win, double? Pick)> Builds,
        List<(string Title, AramMetaRecommendation[] Rows)> Augments
    );

    private readonly Dictionary<string, RecommendationCache> _recommendationCache = new();
    private string _rankStatus = "";
    private JObject? _augmentMetadata;

    private sealed record AugmentChoice(int Id, string Name)
    {
        public override string ToString() => Name;
    }

    public LiveMatchView(
        Func<Task<int>> queue,
        Action<int, int> guide,
        Func<TimeSpan> cacheLifetime,
        LiveMatchClient? live = null,
        Func<string, int, Task<(Image? image, string name, string description)>>? iconLoader = null,
        OpggClient? opgg = null,
        AramMetaClient? aramMeta = null
    )
    {
        _cacheLifetime = cacheLifetime;
        _queue = queue;
        _guide = guide;
        _ownsLive = live == null;
        _ownsOpgg = opgg == null;
        _ownsAramMeta = aramMeta == null;
        _live = live ?? new();
        _opgg = opgg ?? new();
        _am = aramMeta ?? new(cacheLifetime: cacheLifetime);
        _iconLoader = iconLoader;
        _artworkLoader = ReadChampionArtworkAsync;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(8, 29, 40);
        ForeColor = Color.FromArgb(215, 225, 226);
        CreateLayout();
        BindUpdates();
    }

    private void CreateLayout()
    {
        var details = Stack();
        details.Dock = DockStyle.Fill;
        details.AutoSize = false;
        details.AutoScroll = true;
        details.Controls.Add(_equipment);
        details.Controls.Add(_recommendations);
        var panels = CreateMatchColumns(details);
        details.SizeChanged += (_, _) => ResizeDetails(details);
        _players.SizeChanged += (_, _) =>
        {
            _cardsSignature = "";
            RenderCards();
        };
        Controls.Add(panels);
        Controls.Add(CreateAugmentToolbar());
        Controls.Add(CreateToolbar());
        Controls.Add(_status);
        Controls.Add(
            new ChatSendStatusLabel
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 28,
                AutoEllipsis = true,
                Padding = new Padding(8, 4, 8, 2),
                ForeColor = Muted,
            }
        );
    }

    private TableLayoutPanel CreateMatchColumns(FlowLayoutPanel details)
    {
        var panels = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(6),
        };
        panels.ColumnStyles.Add(new(SizeType.Percent, 48));
        panels.ColumnStyles.Add(new(SizeType.Percent, 52));
        panels.Controls.Add(_players, 0, 0);
        panels.Controls.Add(details, 1, 0);
        bool? wideLayout = null;
        panels.SizeChanged += (_, _) =>
        {
            bool wide = panels.Width >= 820;
            if (wideLayout == wide || _closing)
            {
                return;
            }
            wideLayout = wide;
            ArrangeColumns(panels, details, wide);
        };
        return panels;
    }

    private void ArrangeColumns(TableLayoutPanel panels, Control details, bool wide)
    {
        panels.SuspendLayout();
        try
        {
            panels.Controls.Remove(_players);
            panels.Controls.Remove(details);
            panels.ColumnStyles.Clear();
            panels.RowStyles.Clear();
            panels.ColumnCount = wide ? 2 : 1;
            panels.RowCount = wide ? 1 : 2;
            panels.ColumnStyles.Add(new(SizeType.Percent, wide ? 48 : 100));
            if (wide)
            {
                panels.ColumnStyles.Add(new(SizeType.Percent, 52));
            }
            panels.RowStyles.Add(new(SizeType.Percent, wide ? 100 : 45));
            if (!wide)
            {
                panels.RowStyles.Add(new(SizeType.Percent, 55));
            }
            panels.Controls.Add(_players, 0, 0);
            panels.Controls.Add(details, wide ? 1 : 0, wide ? 0 : 1);
        }
        finally
        {
            panels.ResumeLayout();
        }
    }

    private void ResizeDetails(Control details)
    {
        if (_closing)
        {
            return;
        }
        int width = Math.Max(220, details.ClientSize.Width - 28);
        foreach (Control child in details.Controls)
        {
            child.MinimumSize = new Size(width, 0);
            child.Width = width;
        }
        ResizeSections();
    }

    private FlowLayoutPanel CreateToolbar()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
        };
        _mode.Items.AddRange(new object[] { "自动识别模式", "普通大乱斗", "海克斯大乱斗" });
        _mode.SelectedIndex = 0;
        var refresh = Button("刷新对局");
        refresh.Click += (_, _) => RefreshMatch();
        var guide = Button("所选英雄完整攻略");
        guide.Click += (_, _) => OpenSelectedGuide();
        toolbar.Controls.AddRange(new Control[] { _mode, refresh, guide });
        return toolbar;
    }

    private FlowLayoutPanel CreateAugmentToolbar()
    {
        var record = Button("记录已选");
        record.Click += (_, _) =>
        {
            if (_augment.SelectedItem is AugmentChoice choice)
            {
                RecordAugment(choice.Id);
            }
        };
        var clear = Button("清除记录");
        clear.Click += (_, _) =>
        {
            if (Selected() is { } player)
            {
                _manualAugments.Remove(player.Key);
            }
            ShowEquipment();
        };
        _augmentBar.Controls.AddRange(
            new Control[]
            {
                new Label
                {
                    Text = "手动记录海克斯",
                    AutoSize = true,
                    Margin = new Padding(8, 8, 4, 0),
                },
                _augment,
                record,
                clear,
            }
        );
        _augmentBar.Visible = false;
        return _augmentBar;
    }

    private void RecordAugment(int id)
    {
        var player = Selected();
        if (_closing || player == null)
        {
            return;
        }
        if (!_manualAugments.TryGetValue(player.Key, out var ids))
        {
            _manualAugments[player.Key] = ids = new();
        }
        if (!ids.Contains(id))
        {
            ids.Add(id);
        }
        ShowEquipment();
    }

    private void RefreshMatch()
    {
        _rankChecked = DateTime.MinValue;
        _recommendationCache.Clear();
        _ = PollAsync();
        _ = LoadRecommendationsAsync();
    }

    private void OpenSelectedGuide()
    {
        int id = ChampionId(Selected());
        if (!_closing && id > 0 && _currentQueue is 450 or 2400)
        {
            _guide(id, _currentQueue);
        }
    }

    private void BindUpdates()
    {
        _mode.SelectedIndexChanged += (_, _) =>
        {
            _rankChecked = DateTime.MinValue;
            _ = PollAsync();
        };
        _timer.Tick += (_, _) => _ = PollAsync();
        _timer.Start();
        VisibleChanged += (_, _) =>
        {
            if (Visible)
            {
                _ = PollAsync();
            }
        };
    }

    private Button Button(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(16, 45, 57),
            ForeColor = ForeColor,
        };

    private LiveMatchPlayer? Selected() => _snapshot?.Players.FirstOrDefault(p => p.Key == _selectedKey);

    private static int ChampionId(LiveMatchPlayer? player) =>
        player == null
            ? 0
            : LiveChampionIdentity.Resolve(player, FormMain.Globals.resLoading.Champions.Champions)?.Id ?? 0;

    private static string ChampionName(LiveMatchPlayer player) =>
        FormMain.Globals.resLoading.GetChampionById(ChampionId(player))?.Name ?? player.ChampionName;

    private static string ItemName(int id, string fallback = "") =>
        FormMain.Globals.resLoading.GetItemById(id)?.Name ?? (fallback.Length > 0 ? fallback : $"装备 {id}");

    private static string Rate(double? value) => value.HasValue ? value.Value.ToString("P1") : "—";

    private Task PollAsync() => StartWork(PollCurrentMatchAsync);

    private async Task PollCurrentMatchAsync()
    {
        if (_closing || _busy || !Visible)
        {
            return;
        }
        _busy = true;
        try
        {
            var snapshot = await _live.GetAsync(_life.Token);
            int detected = await DetectQueueAsync();
            if (_closing)
            {
                return;
            }
            int queue = _mode.SelectedIndex switch
            {
                1 => 450,
                2 => 2400,
                _ => LiveMatchMode.RecommendationQueue(detected, snapshot),
            };
            ApplySnapshot(snapshot, queue);
            RefreshRanks(queue);
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested)
        {
            // Closing cancels the polling request without replacing its last display with an error.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Live match polling: {ex.GetType().Name}");
            if (!_closing)
            {
                ShowDisconnected();
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<int> DetectQueueAsync()
    {
        try
        {
            return await _queue();
        }
        catch (Exception ex)
        {
            // The client may disappear while the game remains available on port 2999.
            System.Diagnostics.Debug.WriteLine($"Live match queue fallback: {ex.GetType().Name}");
            return 0;
        }
    }

    private void ApplySnapshot(LiveMatchSnapshot snapshot, int queue)
    {
        if (
            _snapshot != null
            && (
                snapshot.Time + 2 < _snapshot.Time
                || !snapshot.Players.Select(p => p.Key).SequenceEqual(_snapshot.Players.Select(p => p.Key))
            )
        )
        {
            _manualAugments.Clear();
        }
        _snapshot = snapshot;
        _currentQueue = queue;
        if (_rankQueue != queue)
        {
            RetireSource(ref _rankCancellation);
            _ranks.Clear();
            _rankQueue = queue;
            _rankChecked = DateTime.MinValue;
            _rankStatus = "";
            RetireSource(ref _detailLoad);
            SetMessage(_recommendations, queue is 450 or 2400 ? "正在切换推荐模式…" : "当前模式暂无推荐。");
        }
        Render();
    }

    private void RefreshRanks(int queue)
    {
        if (
            queue is not (450 or 2400)
            || DateTime.UtcNow - _rankChecked <= TimeSpan.FromMinutes(1)
            || _rankLoad is { IsCompleted: false }
        )
        {
            return;
        }
        _rankChecked = DateTime.UtcNow;
        RetireSource(ref _rankCancellation);
        _rankCancellation = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        var token = _rankCancellation.Token;
        _rankLoad = StartWork(() => LoadRanksAsync(queue, token));
    }

    private void ShowDisconnected()
    {
        RetireSource(ref _rankCancellation);
        RetireSource(ref _detailLoad);
        _rankChecked = DateTime.MinValue;
        _snapshot = null;
        _selectedKey = null;
        _cardsSignature = "";
        _equipmentSignature = "";
        _manualAugments.Clear();
        Clear(_players);
        Clear(_equipment);
        SetMessage(_recommendations, "进入游戏后，选择英雄查看出装与强化推荐。");
        _augmentBar.Visible = false;
        _status.Text = "暂未连接到游戏数据。请进入游戏后查看；断开后不保留上一局的数据。";
    }

    private bool CanPublishRanks(int queue, CancellationToken token) =>
        !_closing && !token.IsCancellationRequested && _currentQueue == queue && _snapshot != null;

    private async Task LoadRanksAsync(int queue, CancellationToken token)
    {
        try
        {
            if (queue == 2400)
            {
                var page = await _am.GetAsync(0, false, token);
                if (!CanPublishRanks(queue, token))
                {
                    return;
                }
                ApplyAramMetaRanks(page);
            }
            else
            {
                var page = await _opgg.GetTiersAsync(new("global", "aram", "all", "none", 0), false, token);
                if (!CanPublishRanks(queue, token))
                {
                    return;
                }
                foreach (var champion in page.Rows)
                {
                    _ranks[champion.ChampionId] = (
                        champion.Tier.HasValue ? "T" + champion.Tier : "—",
                        champion.WinRate,
                        champion.PickRate
                    );
                }
                _rankStatus = $"OP.GG {page.Version} · 全球 / 全段位 · 胜率 / 登场率";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Live match ranks: {ex.GetType().Name}");
            if (!CanPublishRanks(queue, token))
            {
                return;
            }
            _rankStatus = "梯队更新失败，可刷新重试；当前装备仍可查看。";
        }
        if (CanPublishRanks(queue, token))
        {
            Render();
        }
    }

    private void ApplyAramMetaRanks(AramMetaPage page)
    {
        _augmentMetadata = page.Index.Augments;
        foreach (var champion in page.Index.Champions)
        {
            _ranks[champion.Id] = (champion.Tier, champion.WinRate, page.Index.PickRate(champion));
        }
        _rankStatus = $"arammeta {page.Index.Patch} · 调整后胜率 / 样本选取率" + (page.UpdateFailed ? " · 旧缓存" : "");
        if (_augment.Items.Count > 0)
        {
            return;
        }
        foreach (var entry in page.Index.Augments.Properties())
        {
            if (int.TryParse(entry.Name, out int id))
            {
                _augment.Items.Add(
                    new AugmentChoice(
                        id,
                        FormMain.Globals.resLoading.GetAugmentById(id)?.Name
                            ?? (string?)entry.Value["name_zh"]
                            ?? $"强化 {id}"
                    )
                );
            }
        }
    }

    private Task LoadRecommendationsAsync() => StartWork(LoadSelectedRecommendationsAsync);

    private bool CanPublishRecommendation(CancellationTokenSource request) =>
        !_closing && ReferenceEquals(_detailLoad, request) && !request.IsCancellationRequested;

    private async Task LoadSelectedRecommendationsAsync()
    {
        int id = ChampionId(Selected());
        int queue = _currentQueue;
        string key = $"{queue}:{id}";
        if (_pendingRecommendation == key && _detailLoad is { IsCancellationRequested: false })
        {
            return;
        }
        RetireSource(ref _detailLoad);
        _pendingRecommendation = null;
        if (id <= 0 || queue is not (450 or 2400))
        {
            SetMessage(_recommendations, "选择已识别的英雄查看推荐。");
            return;
        }
        if (_recommendationCache.TryGetValue(key, out var saved) && DateTime.UtcNow - saved.Fetched < _cacheLifetime())
        {
            RenderRecommendations(saved.Builds, saved.Augments);
            return;
        }
        _detailLoad = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        var request = _detailLoad;
        _pendingRecommendation = key;
        SetMessage(_recommendations, "正在读取推荐…");
        try
        {
            var recommendation = await ReadRecommendationsAsync(id, queue, request.Token);
            if (CanPublishRecommendation(request))
            {
                _recommendationCache[key] = recommendation;
                RenderRecommendations(recommendation.Builds, recommendation.Augments);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // A newer selection or disposal owns the display now.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Live match recommendations: {ex.GetType().Name}");
            if (CanPublishRecommendation(request))
            {
                SetMessage(_recommendations, "推荐读取失败，可点击刷新对局重试。");
            }
        }
        finally
        {
            if (ReferenceEquals(_detailLoad, request))
            {
                _pendingRecommendation = null;
            }
        }
    }

    private async Task<RecommendationCache> ReadRecommendationsAsync(int id, int queue, CancellationToken token)
    {
        var builds = new List<(int[] Ids, double? Win, double? Pick)>();
        var augments = new List<(string Title, AramMetaRecommendation[] Rows)>();
        if (queue == 2400)
        {
            var page = await _am.GetAsync(id, false, token);
            foreach (var item in AramMetaRecommendation.Read(page, id, "items").Take(3))
            {
                builds.Add((item.Ids, item.WinRate, item.PickRate));
            }
            foreach (
                var (category, title) in new[]
                {
                    ("kPrismatic", "棱彩强化"),
                    ("kGold", "黄金强化"),
                    ("kSilver", "白银强化"),
                }
            )
            {
                augments.Add((title, AramMetaRecommendation.Read(page, id, category).Take(5).ToArray()));
            }
        }
        else
        {
            var page = await _opgg.GetBuildAsync(new("global", "aram", "all", "none", id), false, token);
            foreach (var item in (page.Data["core_items"] as JArray ?? new()).OfType<JObject>().Take(3))
            {
                builds.Add(
                    (
                        Services.OpggItemSetService.Ids(item),
                        OpggBuild.Ratio(item["win"], item["play"]),
                        (double?)item["pick_rate"]
                    )
                );
            }
        }
        return new(DateTime.UtcNow, builds, augments);
    }

    private Task StartWork(Func<Task> operation)
    {
        if (_closing)
        {
            return Task.CompletedTask;
        }
        return TrackWork(RunAsync());

        async Task RunAsync()
        {
            // Register ownership before a synchronous fake, cache hit or callback can close the view.
            await Task.Yield();
            if (!_closing)
            {
                await operation();
            }
        }
    }

    private T TrackWork<T>(T task)
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
                    System.Diagnostics.Debug.WriteLine($"Live match work: {error.GetBaseException().GetType().Name}");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        return task;
    }

    private Task[] PendingWork()
    {
        lock (_work)
        {
            return _work.ToArray();
        }
    }

    private void RetireSource(ref CancellationTokenSource? source)
    {
        var retired = source;
        source = null;
        if (retired == null)
        {
            return;
        }
        var pending = PendingWork();
        var cancellation = retired.CancelAsync();
        TrackWork(DisposeSourceAsync(retired, pending, cancellation));
    }

    private static async Task DisposeSourceAsync(CancellationTokenSource source, Task[] pending, Task cancellation)
    {
        try
        {
            await Task.WhenAll(pending.Append(cancellation)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Live match request cleanup: {ex.GetType().Name}");
        }
        finally
        {
            source.Dispose();
        }
    }

    private async Task CleanupAsync(Task[] pending, Task cancellation)
    {
        try
        {
            await Task.WhenAll(pending.Append(cancellation)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Live match cleanup: {ex.GetType().Name}");
        }
        DisposeImageCache();
        if (_ownsLive)
        {
            _live.Dispose();
        }
        if (_ownsOpgg)
        {
            _opgg.Dispose();
        }
        if (_ownsAramMeta)
        {
            _am.Dispose();
        }
        _life.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            _timer.Stop();
            _timer.Dispose();
            var cancellation = _life.CancelAsync();
            RetireSource(ref _rankCancellation);
            RetireSource(ref _detailLoad);
            DisposePlayerCards();
            var pending = PendingWork();
            _cleanupWork = Task.Run(() => CleanupAsync(pending, cancellation), CancellationToken.None);
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
