using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public sealed partial class LiveMatchView : UserControl
{
    private readonly LiveMatchClient _live;
    private readonly Func<string, int, Task<(Image? image, string name, string description)>>? _iconLoader;
    private readonly OpggClient _opgg = new();
    private readonly AramMetaClient _am;
    private readonly Func<Task<int>> _queue;
    private readonly Action<int, int> _guide;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };
    private readonly CancellationTokenSource _life = new();
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
        Func<string, int, Task<(Image? image, string name, string description)>>? iconLoader = null
    )
    {
        _cacheLifetime = cacheLifetime;
        _queue = queue;
        _guide = guide;
        _live = live ?? new();
        _iconLoader = iconLoader;
        _am = new(cacheLifetime: cacheLifetime);
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(8, 29, 40);
        ForeColor = Color.FromArgb(215, 225, 226);
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
        };
        _mode.Items.AddRange(new object[] { "自动识别模式", "普通大乱斗", "海克斯大乱斗" });
        _mode.SelectedIndex = 0;
        var refresh = Button("刷新对局");
        var guideButton = Button("所选英雄完整攻略");
        toolbar.Controls.AddRange(new Control[] { _mode, refresh, guideButton });
        var panels = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(6),
        };
        panels.ColumnStyles.Add(new(SizeType.Percent, 48));
        panels.ColumnStyles.Add(new(SizeType.Percent, 52));
        var details = Stack();
        details.Dock = DockStyle.Fill;
        details.AutoSize = false;
        details.AutoScroll = true;
        details.Controls.Add(_equipment);
        details.Controls.Add(_recommendations);
        panels.Controls.Add(_players, 0, 0);
        panels.Controls.Add(details, 1, 0);
        bool? wideLayout = null;
        panels.SizeChanged += (_, _) =>
        {
            bool wide = panels.Width >= 820;
            if (wideLayout == wide)
                return;
            wideLayout = wide;
            panels.SuspendLayout();
            panels.Controls.Remove(_players);
            panels.Controls.Remove(details);
            panels.ColumnStyles.Clear();
            panels.RowStyles.Clear();
            panels.ColumnCount = wide ? 2 : 1;
            panels.RowCount = wide ? 1 : 2;
            panels.ColumnStyles.Add(new(SizeType.Percent, wide ? 48 : 100));
            if (wide)
                panels.ColumnStyles.Add(new(SizeType.Percent, 52));
            panels.RowStyles.Add(new(SizeType.Percent, wide ? 100 : 45));
            if (!wide)
                panels.RowStyles.Add(new(SizeType.Percent, 55));
            panels.Controls.Add(_players, 0, 0);
            panels.Controls.Add(details, wide ? 1 : 0, wide ? 0 : 1);
            panels.ResumeLayout();
        };
        details.SizeChanged += (_, _) =>
        {
            int width = Math.Max(220, details.ClientSize.Width - 28);
            foreach (Control c in details.Controls)
            {
                c.MinimumSize = new Size(width, 0);
                c.Width = width;
            }
            ResizeSections();
        };
        _players.SizeChanged += (_, _) =>
        {
            _cardsSignature = "";
            if (_snapshot != null)
                RenderCards();
        };
        var record = Button("记录已选");
        var clear = Button("清除记录");
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
        Controls.Add(panels);
        Controls.Add(_augmentBar);
        Controls.Add(toolbar);
        Controls.Add(_status);
        var chatStatus = new ChatSendStatusLabel
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = 28,
            AutoEllipsis = true,
            Padding = new Padding(8, 4, 8, 2),
            ForeColor = Muted,
        };
        Controls.Add(chatStatus);
        _augmentBar.Visible = false;
        refresh.Click += (_, _) =>
        {
            _rankChecked = DateTime.MinValue;
            _recommendationCache.Clear();
            _ = PollAsync();
            _ = LoadRecommendationsAsync();
        };
        _mode.SelectedIndexChanged += (_, _) =>
        {
            _rankChecked = DateTime.MinValue;
            _ = PollAsync();
        };
        guideButton.Click += (_, _) =>
        {
            var player = Selected();
            int id = ChampionId(player);
            if (id > 0 && _currentQueue is 450 or 2400)
                _guide(id, _currentQueue);
        };
        record.Click += (_, _) =>
        {
            var player = Selected();
            if (player == null || _augment.SelectedItem is not AugmentChoice choice)
                return;
            if (!_manualAugments.TryGetValue(player.Key, out var list))
                _manualAugments[player.Key] = list = new();
            if (!list.Contains(choice.Id))
                list.Add(choice.Id);
            ShowEquipment();
        };
        clear.Click += (_, _) =>
        {
            if (Selected() is { } player)
                _manualAugments.Remove(player.Key);
            ShowEquipment();
        };
        _timer.Tick += (_, _) => _ = PollAsync();
        _timer.Start();
        VisibleChanged += (_, _) =>
        {
            if (Visible)
                _ = PollAsync();
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

    private async Task PollAsync()
    {
        if (_busy || !Visible || IsDisposed)
            return;
        _busy = true;
        try
        {
            var snapshot = await _live.GetAsync(_life.Token);
            int detected;
            try
            {
                detected = await _queue();
            }
            catch
            {
                detected = 0;
            }
            if (IsDisposed || _life.IsCancellationRequested)
                return;
            int queue = _mode.SelectedIndex switch
            {
                1 => 450,
                2 => 2400,
                _ => LiveMatchMode.RecommendationQueue(detected, snapshot),
            };
            if (
                _snapshot != null
                && (
                    snapshot.Time + 2 < _snapshot.Time
                    || string.Join("|", snapshot.Players.Select(p => p.Key))
                        != string.Join("|", _snapshot.Players.Select(p => p.Key))
                )
            )
                _manualAugments.Clear();
            _snapshot = snapshot;
            _currentQueue = queue;
            if (_rankQueue != queue)
            {
                _rankCancellation?.Cancel();
                _ranks.Clear();
                _rankQueue = queue;
                _rankChecked = DateTime.MinValue;
                _rankStatus = "";
                _detailLoad?.Cancel();
                SetMessage(_recommendations, queue is 450 or 2400 ? "正在切换推荐模式…" : "当前模式暂无推荐。");
            }
            Render();
            if (
                queue is 450 or 2400
                && DateTime.UtcNow - _rankChecked > TimeSpan.FromMinutes(1)
                && (_rankLoad == null || _rankLoad.IsCompleted)
            )
            {
                _rankChecked = DateTime.UtcNow;
                _rankCancellation?.Dispose();
                _rankCancellation = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
                _rankLoad = LoadRanksAsync(queue, _rankCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
        catch
        {
            if (!IsDisposed)
            {
                _rankCancellation?.Cancel();
                _rankChecked = DateTime.MinValue;
                _detailLoad?.Cancel();
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
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task LoadRanksAsync(int queue, CancellationToken token)
    {
        try
        {
            if (queue == 2400)
            {
                var page = await _am.GetAsync(0, false, token);
                if (token.IsCancellationRequested || _currentQueue != queue || _snapshot == null)
                    return;
                _augmentMetadata = page.Index.Augments;
                foreach (var c in page.Index.Champions)
                    _ranks[c.Id] = (c.Tier, c.WinRate, page.Index.PickRate(c));
                _rankStatus =
                    $"arammeta {page.Index.Patch} · 调整后胜率 / 样本选取率" + (page.UpdateFailed ? " · 旧缓存" : "");
                if (_augment.Items.Count == 0)
                    foreach (var entry in page.Index.Augments.Properties())
                        if (int.TryParse(entry.Name, out int id))
                            _augment.Items.Add(
                                new AugmentChoice(
                                    id,
                                    FormMain.Globals.resLoading.GetAugmentById(id)?.Name
                                        ?? (string?)entry.Value["name_zh"]
                                        ?? $"强化 {id}"
                                )
                            );
            }
            else
            {
                var page = await _opgg.GetTiersAsync(new("global", "aram", "all", "none", 0), false, token);
                if (token.IsCancellationRequested || _currentQueue != queue || _snapshot == null)
                    return;
                foreach (var c in page.Rows)
                    _ranks[c.ChampionId] = (c.Tier.HasValue ? "T" + c.Tier : "—", c.WinRate, c.PickRate);
                _rankStatus = $"OP.GG {page.Version} · 全球 / 全段位 · 胜率 / 登场率";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            _rankStatus = "梯队更新失败，可刷新重试；当前装备仍可查看。";
        }
        if (IsDisposed || token.IsCancellationRequested || _snapshot == null)
            return;
        Render();
    }

    private async Task LoadRecommendationsAsync()
    {
        var player = Selected();
        int id = ChampionId(player),
            queue = _currentQueue;
        string key = $"{queue}:{id}";
        if (_pendingRecommendation == key && _detailLoad is { IsCancellationRequested: false })
            return;
        _detailLoad?.Cancel();
        _detailLoad?.Dispose();
        _detailLoad = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        var request = _detailLoad;
        var token = request.Token;
        _pendingRecommendation = null;
        if (_recommendationCache.TryGetValue(key, out var saved) && DateTime.UtcNow - saved.Fetched < _cacheLifetime())
        {
            RenderRecommendations(saved.Builds, saved.Augments);
            return;
        }
        _pendingRecommendation = key;
        if (id <= 0 || queue is not (450 or 2400))
        {
            _pendingRecommendation = null;
            SetMessage(_recommendations, "选择已识别的英雄查看推荐。");
            return;
        }
        SetMessage(_recommendations, "正在读取推荐…");
        try
        {
            var builds = new List<(int[] Ids, double? Win, double? Pick)>();
            var augments = new List<(string Title, AramMetaRecommendation[] Rows)>();
            if (queue == 2400)
            {
                var page = await _am.GetAsync(id, false, token);
                foreach (var item in AramMetaRecommendation.Read(page, id, "items").Take(3))
                    builds.Add((item.Ids, item.WinRate, item.PickRate));
                foreach (
                    var (category, title) in new[]
                    {
                        ("kPrismatic", "棱彩强化"),
                        ("kGold", "黄金强化"),
                        ("kSilver", "白银强化"),
                    }
                )
                    augments.Add((title, AramMetaRecommendation.Read(page, id, category).Take(5).ToArray()));
            }
            else
            {
                var page = await _opgg.GetBuildAsync(new("global", "aram", "all", "none", id), false, token);
                foreach (var item in (page.Data["core_items"] as JArray ?? new()).OfType<JObject>().Take(3))
                    builds.Add(
                        (
                            Services.OpggItemSetService.Ids(item),
                            OpggBuild.Ratio(item["win"], item["play"]),
                            (double?)item["pick_rate"]
                        )
                    );
            }
            if (!token.IsCancellationRequested && !IsDisposed)
            {
                _recommendationCache[key] = new(DateTime.UtcNow, builds, augments);
                RenderRecommendations(builds, augments);
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!token.IsCancellationRequested && !IsDisposed)
                SetMessage(_recommendations, "推荐读取失败，可点击刷新对局重试。");
        }
        finally
        {
            if (ReferenceEquals(_detailLoad, request))
                _pendingRecommendation = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_life.IsCancellationRequested)
        {
            _timer.Stop();
            _timer.Dispose();
            _life.Cancel();
            _rankCancellation?.Cancel();
            _detailLoad?.Cancel();
            _detailLoad?.Dispose();
            _tips.Dispose();
            DisposeImageCache();
            _live.Dispose();
            _opgg.Dispose();
            _am.Dispose();
        }
        base.Dispose(disposing);
    }
}
