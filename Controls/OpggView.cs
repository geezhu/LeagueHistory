using League.Clients;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed partial class OpggView : UserControl
{
    private readonly OpggClient _client;
    private readonly Font _viewFont = new("Microsoft YaHei UI", 9f);
    private Font? _sectionHeadingFont;
    private Font? _championHeadingFont;
    private bool _closing;
    private readonly HashSet<Task> _viewWork = new();
    private Task _cleanupWork = Task.CompletedTask;

    internal Task OpenQueryAsync(OpggQuery query)
    {
        if (_closing)
        {
            return Task.CompletedTask;
        }
        query = query.Normalize();
        _follow.Checked = false;
        _assignedPosition = query.Position;
        _syncing = true;
        SelectHero(query.ChampionId);
        SetValue(_mode, query.Mode);
        SetValue(_region, query.Region);
        SetValue(_position, query.Mode == "ranked" ? query.Position : "mid");
        SetValue(_tier, query.Tier);
        _modeTiers[query.Mode] = query.Tier;
        _syncing = false;
        _position.Visible = query.Mode == "ranked";
        _tier.Visible = query.Mode != "arena";
        return LoadAsync(false);
    }

    internal Task OpenChampionAsync(int id, int queue, string? position)
    {
        if (_closing)
        {
            return Task.CompletedTask;
        }
        _follow.Checked = false;
        _assignedPosition = position;
        var mode = OpggQuery.ModeForFollowing(queue);
        _syncing = true;
        SelectHero(id);
        SetValue(_mode, mode);
        SetValue(_position, position ?? "mid");
        SetValue(_tier, _modeTiers[mode]);
        _syncing = false;
        _position.Visible = mode == "ranked";
        _tier.Visible = mode != "arena";
        return LoadAsync(false);
    }

    private int _championId;
    private readonly ComboBox _region = Select("全球|global", "韩服|kr", "北美|na", "西欧|euw");
    private readonly ComboBox _mode = Select(
        "召唤师峡谷|ranked",
        "极地大乱斗|aram",
        "斗魂竞技场|arena",
        "无限火力|urf",
        "极限闪击|nexus_blitz"
    );
    private readonly ComboBox _tier = Select(
        "全部段位|all",
        "黄金及以下|ibsg",
        "黄金及以上|gold_plus",
        "铂金及以上|platinum_plus",
        "翡翠及以上|emerald_plus",
        "钻石及以上|diamond_plus",
        "大师|master",
        "大师及以上|master_plus",
        "宗师|grandmaster",
        "王者|challenger"
    );
    private readonly Dictionary<string, string> _modeTiers = new()
    {
        ["ranked"] = "emerald_plus",
        ["aram"] = "all",
        ["arena"] = "all",
        ["urf"] = "all",
        ["nexus_blitz"] = "all",
    };
    private readonly ComboBox _position = Select("中路|mid", "上路|top", "打野|jungle", "下路|adc", "辅助|support");
    private readonly CheckBox _follow = new()
    {
        Text = "跟随选人",
        AutoSize = true,
        Checked = true,
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Top,
        Height = 32,
        Padding = new Padding(12, 4, 8, 4),
        Text = "OP.GG 英雄攻略 · 选择英雄后查询",
        ForeColor = Color.SteelBlue,
    };
    private readonly FlowLayoutPanel _content = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(12),
    };
    private readonly OpggRankingsView _tiers = new();
    private readonly ToolTip _tips = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2500 };
    private readonly System.Windows.Forms.Timer _filterTimer = new() { Interval = 300 };
    private CancellationTokenSource? _request;
    private CancellationTokenSource? _poll;
    private string _selection = "";
    private string? _assignedPosition;
    private bool _syncing,
        _polling;
    private OpggBuild? _displayed;
    private Func<HttpClient?> _selectionClient = () => FormMain.Globals.lcuClient?.Client;
    private Func<int, string, Task<(Image? image, string name, string description)>> _resourceLoader =
        DefaultResourceAsync;

    private sealed record Choice(string Text, string Value)
    {
        public override string ToString() => Text;
    }

    private static ComboBox Select(params string[] options)
    {
        var box = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        box.Items.AddRange(
            options
                .Select(o =>
                {
                    var p = o.Split('|');
                    return new Choice(p[0], p[1]);
                })
                .ToArray()
        );
        box.SelectedIndex = 0;
        return box;
    }

    private static string Value(ComboBox box) => ((Choice)box.SelectedItem!).Value;

    private static void SetValue(ComboBox box, string value)
    {
        var item = box.Items.Cast<Choice>().FirstOrDefault(c => c.Value == value);
        if (item != null)
        {
            box.SelectedItem = item;
        }
    }

    private static string ChampionName(int id) =>
        FormMain.Globals.resLoading?.GetChampionById(id)?.Name ?? $"英雄 {id}";

    public OpggView()
        : this(new OpggClient()) { }

    internal OpggView(OpggClient client)
    {
        _client = client;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(244, 247, 251);
        Font = _viewFont;
        SetValue(_tier, _modeTiers["ranked"]);
        var refresh = ConfigureToolbar();
        BindNavigation(refresh);
        BindFilters();
        BindRankings();
        BindTimers();
        _content.SizeChanged += (_, _) => ResizeContent();
    }

    private Button ConfigureToolbar()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(10),
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        var refresh = new Button { Text = "刷新数据", AutoSize = true };
        toolbar.Controls.AddRange(
            new Control[] { _back, _forward, _mode, _region, _tier, _position, refresh, _follow }
        );
        Controls.Add(_content);
        Controls.Add(_tiers);
        Controls.Add(_status);
        Controls.Add(toolbar);
        _tips.SetToolTip(_follow, "跟随当前选人；没有对应攻略的模式使用召唤师峡谷数据。仅在此页面可见时更新。");
        _tips.SetToolTip(_region, "OP.GG 统计地区，与当前登录账号大区无关。");
        _tips.SetToolTip(_mode, "可自由切换攻略模式，与当前房间无关。召唤师峡谷使用排位统计。");
        _tips.SetToolTip(_back, "后退到上一个浏览页面");
        _tips.SetToolTip(_forward, "前进到下一个浏览页面");
        return refresh;
    }

    private void BindNavigation(Button refresh)
    {
        _back.Click += async (_, _) =>
        {
            if (MoveNavigation(-1))
            {
                await LoadAsync(false);
            }
        };
        _forward.Click += async (_, _) =>
        {
            if (MoveNavigation(1))
            {
                await LoadAsync(false);
            }
        };
        RecordNavigation();
        refresh.Click += async (_, _) => await LoadAsync(true);
    }

    private void BindFilters()
    {
        foreach (var box in new[] { _region, _mode, _tier, _position })
        {
            box.SelectedIndexChanged += (_, _) => FilterChanged(box);
        }
        _follow.CheckedChanged += (_, _) => _selection = "";
    }

    private void FilterChanged(ComboBox box)
    {
        if (_syncing || _closing)
        {
            return;
        }
        _follow.Checked = false;
        if (box == _mode)
        {
            _championId = 0;
        }
        Cancel();
        ClearContent();
        _tiers.ClearRows();
        _syncing = true;
        try
        {
            if (box == _mode)
            {
                SetValue(_tier, _modeTiers[Value(_mode)]);
            }
            else if (box == _tier)
            {
                _modeTiers[Value(_mode)] = Value(_tier);
            }
            UpdateFilterVisibility();
            _position.Enabled = true;
            _tier.Enabled = true;
        }
        finally
        {
            _syncing = false;
        }
        _status.Text = "筛选条件已变化，正在准备加载…";
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void UpdateFilterVisibility()
    {
        _position.Visible = Value(_mode) == "ranked";
        _tier.Visible = Value(_mode) != "arena";
    }

    private void BindRankings()
    {
        _tiers.ChampionRequested += async row =>
        {
            if (_closing)
            {
                return;
            }
            _follow.Checked = false;
            _syncing = true;
            try
            {
                SelectHero(row.ChampionId);
                if (Value(_mode) == "ranked")
                {
                    SetValue(_position, row.Position.ToLowerInvariant());
                }
            }
            finally
            {
                _syncing = false;
            }
            await LoadAsync(false);
        };
    }

    private void BindTimers()
    {
        _timer.Tick += async (_, _) => await PollSelectionAsync();
        _filterTimer.Tick += async (_, _) =>
        {
            _filterTimer.Stop();
            if (Visible && !_closing)
            {
                await LoadAsync(false);
            }
        };
    }

    private void ResizeContent()
    {
        foreach (Control section in _content.Controls)
        {
            ResizeSection(section);
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_closing)
        {
            return;
        }
        if (Visible)
        {
            _filterTimer.Start();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _filterTimer.Stop();
            RetirePoll();
            Cancel();
            _selection = "";
            _status.Text = "已暂停加载，点击查询攻略可重新加载。";
        }
    }

    private void SelectHero(int id) => _championId = id;

    private void Cancel()
    {
        var source = _request;
        _request = null;
        RetireSource(source);
        _displayed = null;
    }

    private void ClearContent()
    {
        foreach (Control c in _content.Controls.Cast<Control>().ToArray())
        {
            c.Dispose();
        }
    }

    private Task LoadAsync(bool refresh)
    {
        if (_closing)
        {
            return Task.CompletedTask;
        }
        _filterTimer.Stop();
        RecordNavigation();
        Cancel();
        var source = _request = new CancellationTokenSource();
        var token = source.Token;
        var query = new OpggQuery(
            Value(_region),
            Value(_mode),
            Value(_tier),
            Value(_position),
            _championId
        ).Normalize();
        ClearContent();
        _tiers.ClearRows();
        _tiers.Visible = query.ChampionId == 0;
        _content.Visible = query.ChampionId != 0;
        return TrackWork(LoadPageAsync(query, refresh, token));
    }

    private async Task LoadPageAsync(OpggQuery query, bool refresh, CancellationToken token)
    {
        // Register the page worker before cache results can create controls or invoke host callbacks.
        await Task.Yield();
        try
        {
            token.ThrowIfCancellationRequested();
            OpggClient.Validate(query);
            _status.Text = "正在加载 OP.GG 数据…";
            var tiers = await _client.GetTiersAsync(query, refresh, token);
            token.ThrowIfCancellationRequested();
            if (_closing)
            {
                return;
            }
            query = ResolveFollowingPosition(query, tiers.Rows);
            _tiers.SetRows(
                tiers
                    .Rows.Where(r =>
                        query.Mode != "ranked"
                        || string.Equals(r.Position, query.Position, StringComparison.OrdinalIgnoreCase)
                    )
                    .ToArray()
            );
            if (query.ChampionId == 0)
            {
                ShowRankings(tiers.Version, query);
                return;
            }
            _tiers.Visible = false;
            _content.Visible = true;
            _content.BringToFront();
            var build = await _client.GetBuildAsync(query, refresh, token);
            token.ThrowIfCancellationRequested();
            if (_closing)
            {
                return;
            }
            await RenderAsync(build, token);
            token.ThrowIfCancellationRequested();
            if (!_closing)
            {
                _displayed = build;
                _status.Text =
                    $"OP.GG · 版本 {build.Version} · {_region.Text} · {_mode.Text} · {(query.Mode == "ranked" ? _position.Text : "全位置")} · {(query.Mode == "arena" ? "全部段位" : _tier.Text)}";
            }
        }
        catch (OperationCanceledException)
        {
            if (!token.IsCancellationRequested && !_closing)
            {
                _status.Text = "请求超时，请重试。";
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !_closing)
            {
                ClearContent();
                _status.Text = "加载失败：" + ex.Message;
            }
        }
    }

    private OpggQuery ResolveFollowingPosition(OpggQuery query, OpggTier[] rows)
    {
        if (query.ChampionId <= 0 || query.Mode != "ranked" || !_follow.Checked)
        {
            return query;
        }
        var position = OpggQuery.PositionForChampion(rows, query.ChampionId, _assignedPosition);
        if (position == null)
        {
            return query;
        }
        query = query with { Position = position };
        _syncing = true;
        try
        {
            SetValue(_position, position);
        }
        finally
        {
            _syncing = false;
        }
        // Automatic resolution corrects this visit rather than creating another history entry.
        if (_navigationIndex >= 0)
        {
            _navigation[_navigationIndex] = new(Value(_region), Value(_mode), Value(_tier), position, query.ChampionId);
        }
        return query;
    }

    private void ShowRankings(string version, OpggQuery query)
    {
        _content.Visible = false;
        _tiers.Visible = true;
        _tiers.BringToFront();
        _status.Text =
            $"OP.GG · 游戏版本 {version} · {_mode.Text} · {(query.Mode == "ranked" ? _position.Text : "全位置")} · 点击英雄查看攻略";
    }

    private static string Percent(double? value) => value.HasValue ? $"{value:P1}" : "—";

    private void ResizeSection(Control section)
    {
        int width = Math.Max(320, _content.ClientSize.Width - 32);
        section.MinimumSize = new Size(width, 0);
        section.MaximumSize = new Size(width, 0);
        section.Width = width;
        foreach (Control child in section.Controls)
        {
            if (child is Label)
            {
                child.MaximumSize = new Size(width - 24, 0);
            }

            if (child is FlowLayoutPanel)
            {
                child.MinimumSize = new Size(width - 24, 0);
                child.MaximumSize = new Size(width - 24, 0);
            }
            if (child is TableLayoutPanel)
            {
                child.Width = width - 24;
            }
        }
    }

    private FlowLayoutPanel Section(string title)
    {
        int width = Math.Max(320, _content.ClientSize.Width - 32);
        var panel = new FlowLayoutPanel
        {
            Width = width,
            MinimumSize = new Size(width, 0),
            MaximumSize = new Size(width, 0),
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 8),
        };
        panel.Controls.Add(
            new Label
            {
                Text = title,
                AutoSize = true,
                Font = _sectionHeadingFont ??= new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 8),
            }
        );
        panel.ControlAdded += (_, _) => ResizeSection(panel);
        _content.Controls.Add(panel);
        return panel;
    }

    private static Task<(Image? image, string name, string description)> DefaultResourceAsync(int id, string kind) =>
        // Disk-cache hits still decode images; keep resource decoding away from the UI.
        Task.Run(
            async () =>
            {
                var resources = FormMain.Globals.resLoading;
                if (resources == null)
                {
                    return ((Image?)null, $"{kind} {id}", "");
                }
                return await (
                    kind switch
                    {
                        "item" => resources.GetItemInfoAsync(id),
                        "spell" => resources.GetSpellInfoAsync(id),
                        "rune" => resources.GetRuneInfoAsync(id),
                        "champion" => resources.GetChampionInfoAsync(id),
                        _ => resources.GetAugmentInfoAsync(id),
                    }
                );
            },
            CancellationToken.None
        );

    private async Task<(Image? image, string name, string description)> ReadResourceAsync(
        int id,
        string kind,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        var work = _resourceLoader(id, kind);
        try
        {
            return await work.WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The resource cache is shared and cannot be cancelled by one view. Dispose its late owned copy.
            _ = TrackWork(DisposeLateResourceAsync(work));
            throw;
        }
    }

    private static async Task DisposeLateResourceAsync(Task<(Image? image, string name, string description)> work)
    {
        try
        {
            var resource = await work.ConfigureAwait(false);
            resource.image?.Dispose();
        }
        catch (Exception ex)
        {
            // Observe optional resource failures even after the waiting render has been cancelled.
            System.Diagnostics.Debug.WriteLine($"OP.GG retired resource: {ex.GetType().Name}");
        }
    }

    private async Task AddIconAsync(Control row, int id, string kind, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Image? image = null;
        string name = $"{kind} {id}";
        try
        {
            string description;
            (image, name, description) = await ReadResourceAsync(id, kind, token);
            if (_closing || row.IsDisposed || token.IsCancellationRequested)
            {
                image?.Dispose();
                token.ThrowIfCancellationRequested();
                return;
            }
            var icon = CreateResourceIcon(image);
            _tips.SetToolTip(
                icon,
                image == null ? $"{name} ({id}) · 图标暂不可用" : TooltipText.Format(name, description)
            );
            row.Controls.Add(icon);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            image?.Dispose();
            System.Diagnostics.Debug.WriteLine($"OP.GG icon: {ex.GetType().Name}");
            if (!_closing && !row.IsDisposed && !token.IsCancellationRequested)
            {
                row.Controls.Add(new Label { AutoSize = true, Text = name });
            }
        }
    }

    private static Control CreateResourceIcon(Image? image)
    {
        if (image == null)
        {
            return new Label
            {
                Size = new Size(32, 32),
                Text = "?",
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(231, 238, 246),
                ForeColor = Color.SlateGray,
                Margin = new Padding(2),
            };
        }
        var icon = new PictureBox
        {
            Size = new Size(32, 32),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(2),
            Image = image,
        };
        icon.Disposed += (_, _) =>
        {
            icon.Image?.Dispose();
            icon.Image = null;
        };
        return icon;
    }

    private Task PollSelectionAsync()
    {
        if (_closing || _polling || !_follow.Checked || !Visible)
        {
            return Task.CompletedTask;
        }
        var client = _selectionClient();
        if (client == null)
        {
            return Task.CompletedTask;
        }
        _polling = true;
        RetirePoll();
        _poll = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return TrackWork(PollCurrentSelectionAsync(client, _poll.Token));
    }

    private async Task PollCurrentSelectionAsync(HttpClient client, CancellationToken token)
    {
        await Task.Yield();
        try
        {
            using var response = await client.GetAsync("/lol-champ-select/v1/session", token);
            if (!response.IsSuccessStatusCode)
            {
                _selection = "";
                return;
            }
            var session = JObject.Parse(await response.Content.ReadAsStringAsync(token));
            var player = (session["myTeam"] as JArray)
                ?.OfType<JObject>()
                .FirstOrDefault(p => (int?)p["cellId"] == (int?)session["localPlayerCellId"]);
            int champion = (int?)player?["championId"] ?? 0;
            if (champion <= 0 || HasPendingPick(session))
            {
                return;
            }
            using var gameResponse = await client.GetAsync("/lol-gameflow/v1/session", token);
            gameResponse.EnsureSuccessStatusCode();
            var game = JObject.Parse(await gameResponse.Content.ReadAsStringAsync(token));
            token.ThrowIfCancellationRequested();
            int queue = (int?)game["gameData"]?["queue"]?["id"] ?? 0;
            string? position = ((string?)player?["assignedPosition"])?.ToLowerInvariant() switch
            {
                "middle" or "mid" => "mid",
                "bottom" or "adc" => "adc",
                "utility" or "support" => "support",
                "top" => "top",
                "jungle" => "jungle",
                _ => null,
            };
            string key = $"{queue}:{champion}:{position}";
            if (key == _selection || _closing || !Visible || !_follow.Checked)
            {
                return;
            }
            _selection = key;
            FollowHero(champion, queue, position);
        }
        catch (OperationCanceledException)
        {
            // Polling may time out or be cancelled when browsing is paused.
        }
        catch (Exception ex) when (ex is HttpRequestException or Newtonsoft.Json.JsonException)
        {
            // A disconnected or transient client response must not interrupt manual browsing.
            System.Diagnostics.Debug.WriteLine($"OP.GG selection: {ex.GetType().Name}");
        }
        finally
        {
            _polling = false;
        }
    }

    private static bool HasPendingPick(JObject session) =>
        (session["actions"] as JArray ?? new())
            .OfType<JArray>()
            .SelectMany(a => a.OfType<JObject>())
            .Any(a =>
                (int?)a["actorCellId"] == (int?)session["localPlayerCellId"]
                && (string?)a["type"] == "pick"
                && (bool?)a["completed"] != true
            );

    private void FollowHero(int champion, int queue, string? position)
    {
        Cancel();
        ClearContent();
        var mode = OpggQuery.ModeForFollowing(queue);
        _assignedPosition = position;
        _syncing = true;
        try
        {
            SelectHero(champion);
            SetValue(_mode, mode);
            SetValue(_position, position ?? "mid");
            SetValue(_tier, _modeTiers[mode]);
        }
        finally
        {
            _syncing = false;
        }
        UpdateFilterVisibility();
        _ = LoadAsync(false);
    }

    private TTask TrackWork<TTask>(TTask task)
        where TTask : Task
    {
        lock (_viewWork)
        {
            _viewWork.Add(task);
        }
        _ = task.ContinueWith(
            completed =>
            {
                lock (_viewWork)
                {
                    _viewWork.Remove(completed);
                }
                if (completed.Exception is { } error)
                {
                    System.Diagnostics.Debug.WriteLine($"OP.GG view: {error.GetBaseException().GetType().Name}");
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
        lock (_viewWork)
        {
            return _viewWork.ToArray();
        }
    }

    private void RetireSource(CancellationTokenSource? source)
    {
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

    private void RetirePoll()
    {
        var source = _poll;
        _poll = null;
        RetireSource(source);
    }

    private async Task CleanupResourcesAsync(Task[] pending)
    {
        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OP.GG cleanup: {ex.GetType().Name}");
        }
        _client.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            _timer.Stop();
            _timer.Dispose();
            _filterTimer.Stop();
            _filterTimer.Dispose();
            RetirePoll();
            Cancel();
            var pending = PendingWork();
            _cleanupWork = CleanupResourcesAsync(pending);
            _tips.Dispose();
            _sectionHeadingFont?.Dispose();
            _championHeadingFont?.Dispose();
            _viewFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
