using League.Clients;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Controls;

/// <summary>Client profile achievements, independent of match-history samples.</summary>
public sealed class ProfileMasteryView : UserControl
{
    private readonly PlayerStatisticsService _service;
    private readonly bool _sharedMastery;
    private readonly bool _ownsChampionImages;
    private readonly Font _headingFont = new("Microsoft YaHei UI", 11f, FontStyle.Bold);
    private readonly Font _posterFont = new("Microsoft YaHei UI", 10f, FontStyle.Bold);
    private readonly Func<int, Task<(Image? image, string name, string description)>> _champion;
    private readonly TableLayoutPanel _cards = new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 5,
        RowCount = 1,
    };
    private readonly Label _heading = new()
    {
        Text = "英雄成就",
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
    };
    public string OwnershipText { get; private set; } = "— / —";
    private string _owned = "—",
        _total = "—";
    private bool _masteryAvailable;

    private void UpdateCountHeading()
    {
        OwnershipText =
            _owned != "—" ? $"拥有 {_owned} / {_total}"
            : _masteryAvailable ? $"成就英雄 {_records.Length} / {_total}"
            : $"英雄总数 {_total}";
        _heading.Text = "英雄成就  ·  " + OwnershipText;
    }

    private readonly Label _status = new()
    {
        Dock = DockStyle.Top,
        Height = 26,
        ForeColor = Theme.Muted,
    };
    private readonly Button _refresh = new()
    {
        Text = "刷新",
        Dock = DockStyle.Right,
        Width = 64,
        FlatStyle = FlatStyle.Flat,
    };
    private readonly Button _all = new()
    {
        Text = "全部 ›",
        Dock = DockStyle.Right,
        Width = 72,
        FlatStyle = FlatStyle.Flat,
        Enabled = false,
    };
    private JObject[] _records = Array.Empty<JObject>();
    public event Action<JObject[]>? AllRequested;
    private CancellationTokenSource? _request;
    private bool _closing;
    private readonly HashSet<Task> _pendingWork = new();
    private Task _cleanupWork = Task.CompletedTask;
    private string _puuid = "";
    public event Action<JObject>? ProfileLoaded;

    public ProfileMasteryView(
        PlayerStatisticsService? service = null,
        Func<int, Task<(Image? image, string name, string description)>>? champion = null
    )
    {
        _service = service ?? new(() => FormMain.Globals.lcuClient.Client);
        _sharedMastery = service == null;
        _ownsChampionImages = champion == null;
        _champion = champion ?? FormMain.Globals.resLoading.GetChampionInfoAsync;
        _heading.Font = _headingFont;
        BackColor = Color.White;
        ForeColor = Theme.Heading;
        Height = 100;
        Padding = new Padding(16);
        var header = new Panel { Dock = DockStyle.Top, Height = 32 };
        header.Controls.Add(_heading);
        header.Controls.Add(_refresh);
        _refresh.Click += (_, _) => LoadPlayer(_puuid, true);
        header.Controls.Add(_all);
        ButtonStyles.Secondary(_refresh);
        ButtonStyles.Ghost(_all);
        _all.Click += (_, _) => AllRequested?.Invoke(_records);
        for (int i = 0; i < 5; i++)
        {
            _cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
        }

        _cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(_cards);
        Controls.Add(_status);
        Controls.Add(header);
        _status.Text = "连接客户端后加载英雄成就。";
    }

    public static JObject[] SelectTop(JToken? data) =>
        SelectAll(data).Where(p => (long)p["championPoints"]! > 0).Take(5).ToArray();

    // The client also lists the placeholder (-1) and classic-mode variants (60000+).
    public static JObject[] StandardChampions(JToken? data) =>
        data is JArray array
            ? array
                .OfType<JObject>()
                .Where(p => p["id"]?.Type == JTokenType.Integer && (int)p["id"]! > 0 && (int)p["id"]! < 60000)
                .GroupBy(p => (int)p["id"]!)
                .Select(g => g.First())
                .ToArray()
            : Array.Empty<JObject>();

    public static JObject[] SelectAll(JToken? data) =>
        data is JArray records
            ? records
                .OfType<JObject>()
                .Where(p =>
                    p["championId"]?.Type == JTokenType.Integer
                    && (int)p["championId"]! > 0
                    && p["championPoints"]?.Type == JTokenType.Integer
                    && (long)p["championPoints"]! >= 0
                )
                .GroupBy(p => (int)p["championId"]!)
                .Select(g => g.OrderByDescending(p => (long)p["championPoints"]!).First())
                .OrderByDescending(p => (long)p["championPoints"]!)
                .ThenBy(p => (int)p["championId"]!)
                .ToArray()
            : Array.Empty<JObject>();

    public void LoadPlayer(string puuid, bool refresh = false)
    {
        if (string.IsNullOrWhiteSpace(puuid) || _closing)
        {
            return;
        }
        _puuid = puuid;
        RetireRequest();
        var request = _request = new CancellationTokenSource();
        var token = request.Token;
        PrepareLoadingState();
        if (_closing || token.IsCancellationRequested)
        {
            return;
        }
        _ = TrackWork(LoadProfileAsync(puuid, token));
        _ = TrackWork(LoadMasteryAsync(puuid, refresh, token));
    }

    private void PrepareLoadingState()
    {
        ClearCards();
        Height = 100;
        _refresh.Enabled = false;
        _status.Visible = true;
        _status.Text = "正在加载英雄成就…";
        _records = Array.Empty<JObject>();
        _all.Enabled = false;
        _owned = _total = "—";
        _masteryAvailable = false;
        UpdateCountHeading();
    }

    private async Task LoadMasteryAsync(string puuid, bool refresh, CancellationToken token)
    {
        await Task.Yield();
        try
        {
            token.ThrowIfCancellationRequested();
            var (response, stale) = await ReadMasteryAsync(puuid, refresh, token);
            if (_closing || token.IsCancellationRequested)
            {
                return;
            }
            var top = ShowMasterySummary(response, stale);
            if (top.Length > 0)
            {
                await RenderTopChampionsAsync(top, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A new player or closed profile no longer owns these records.
        }
        catch (Exception ex)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "成就加载失败", ex);
            if (!_closing && !token.IsCancellationRequested)
            {
                _status.Text = "英雄成就加载失败，请重试。";
            }
        }
        finally
        {
            if (!_closing && !token.IsCancellationRequested)
            {
                _refresh.Enabled = true;
            }
        }
    }

    private async Task<(StatisticsResponse Response, bool Stale)> ReadMasteryAsync(
        string puuid,
        bool refresh,
        CancellationToken token
    )
    {
        if (_sharedMastery)
        {
            var page = await ChampionMasteryService.Shared.GetAsync(puuid, token, refresh);
            return (new(page.Data, page.Message), page.Stale);
        }
        var response = await _service.GetAsync(
            $"/lol-champion-mastery/v1/{Uri.EscapeDataString(puuid)}/champion-mastery",
            token
        );
        return (response, false);
    }

    private JObject[] ShowMasterySummary(StatisticsResponse response, bool stale)
    {
        _records = SelectAll(response.Data);
        _all.Enabled = _records.Length > 0;
        _masteryAvailable = response.Data is JArray;
        UpdateCountHeading();
        var top = SelectTop(response.Data);
        if (top.Length == 0)
        {
            _status.Text =
                response.Data is JArray ? "暂无英雄成就记录。"
                : string.IsNullOrEmpty(response.Message) ? "暂时无法获取英雄成就。"
                : response.Message;
            return top;
        }
        _status.Text = stale ? "当前显示旧成就缓存，后台检查更新；点击刷新可等待最新结果。" : "";
        _status.Visible = stale;
        Height = 360;
        return top;
    }

    private async Task RenderTopChampionsAsync(JObject[] top, CancellationToken token)
    {
        int column = 0;
        foreach (var mastery in top)
        {
            token.ThrowIfCancellationRequested();
            int championId = (int)mastery["championId"]!;
            var resource = await _champion(championId);
            // Default game resources return a fresh image; injected providers retain ownership of their images.
            using var ownedImage = _ownsChampionImages ? resource.image : null;
            if (_closing || token.IsCancellationRequested)
            {
                return;
            }
            var poster = AddMasteryCard(mastery, resource.image, resource.name, column++);
            _ = TrackWork(poster.LoadArtworkAsync(championId));
        }
    }

    private MasteryPoster AddMasteryCard(JObject mastery, Image? image, string name, int column)
    {
        int championId = (int)mastery["championId"]!;
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            MaximumSize = new Size(210, 280),
            Margin = new Padding(6),
        };
        var poster = new MasteryPoster(column + 1, image);
        string level =
            mastery["championLevel"]?.Type == JTokenType.Integer ? $"{mastery["championLevel"]} 级英雄成就" : "";
        poster.Controls.Add(
            new Label
            {
                Dock = DockStyle.Bottom,
                Height = 90,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Font = _posterFont,
                Text =
                    $"{(string.IsNullOrWhiteSpace(name) ? "英雄 " + championId : name)}\n{level}\n{(long)mastery["championPoints"]!:N0} 成就点数",
            }
        );
        card.Controls.Add(poster);
        _cards.Controls.Add(card, column, 0);
        return poster;
    }

    private async Task LoadProfileAsync(string puuid, CancellationToken cancellationToken)
    {
        // Track both profile and mastery workers before a subscriber can close or replace the view.
        await Task.Yield();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profile = await _service.GetAsync(
                "/lol-summoner/v1/summoners-by-puuid-cached/" + Uri.EscapeDataString(puuid),
                cancellationToken
            );
            if (!_closing && !cancellationToken.IsCancellationRequested && profile.Data is JObject summoner)
            {
                ProfileLoaded?.Invoke(summoner);
            }

            if (_closing || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var catalogTask = _service.GetAsync("/lol-game-data/assets/v1/champion-summary.json", cancellationToken);
            var inventoryTask =
                profile.Data?["summonerId"]?.Type == JTokenType.Integer
                    ? _service.GetAsync(
                        $"/lol-champions/v1/inventories/{profile.Data["summonerId"]}/champions-minimal",
                        cancellationToken
                    )
                    : Task.FromResult(new StatisticsResponse(null, ""));
            await Task.WhenAll(catalogTask, inventoryTask);
            var catalogResponse = await catalogTask;
            var inventoryResponse = await inventoryTask;
            if (_closing || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var catalog = StandardChampions(catalogResponse.Data);
            var inventory = StandardChampions(inventoryResponse.Data);
            string total = catalog.Length > 0 ? catalog.Length.ToString() : "—";
            string owned =
                inventoryResponse.Data is JArray
                && catalog.Length > 0
                && catalog.All(c =>
                    inventory.Any(i =>
                        (int)i["id"]! == (int)c["id"]! && i["ownership"]?["owned"]?.Type == JTokenType.Boolean
                    )
                )
                    ? inventory
                        .Count(i =>
                            (bool?)i["ownership"]?["owned"] == true && catalog.Any(c => (int)c["id"]! == (int)i["id"]!)
                        )
                        .ToString()
                    : "—";
            _owned = owned;
            _total = total;
            UpdateCountHeading();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Replaced profiles must not update the ownership heading.
        }
        catch (Exception ex)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "玩家资料加载失败", ex);
        }
    }

    private Task TrackWork(Task task)
    {
        lock (_pendingWork)
        {
            _pendingWork.Add(task);
        }
        _ = task.ContinueWith(
            completed =>
            {
                lock (_pendingWork)
                {
                    _pendingWork.Remove(completed);
                }
                if (completed.Exception is { } error)
                {
                    System.Diagnostics.Debug.WriteLine($"mastery view: {error.GetBaseException().GetType().Name}");
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
        lock (_pendingWork)
        {
            return _pendingWork.ToArray();
        }
    }

    private void RetireRequest()
    {
        var source = _request;
        _request = null;
        if (source == null)
        {
            return;
        }
        var pending = PendingWork();
        var cancellation = source.CancelAsync();
        TrackWork(
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

    private void ClearCards()
    {
        foreach (Control card in _cards.Controls.Cast<Control>().ToArray())
        {
            card.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            RetireRequest();
            _cleanupWork = Task.WhenAll(PendingWork());
            ClearCards();
            _headingFont.Dispose();
            _posterFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
