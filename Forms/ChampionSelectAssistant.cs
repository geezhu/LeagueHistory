using System.Diagnostics;
using System.Runtime.InteropServices;
using League.Clients;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League;

internal sealed partial class ChampionSelectAssistant : Form
{
    // The process is system-DPI aware: sizes below are 96-DPI layout units scaled once for the system DPI.
    private static readonly int SystemDpi = (int)GetDpiForSystem();
    private const int LayoutWidth = 320;
    private const int MinimumLayoutHeight = 460;
    private const int TabWidth = 22;
    private const int ToolbarHeaderHeight = 28;
    private static int ContentWidth => Dip(282);

    // Gold marks only the tier result; teal marks what can be clicked; headings stay muted.
    private static readonly Color MutedHeading = Color.FromArgb(150, 172, 182);
    private static readonly Color ActionText = Color.FromArgb(86, 200, 206);
    private static readonly Color KeyResult = Color.FromArgb(208, 185, 130);

    private readonly HashSet<Task> _assistantWork = new();
    private Task _cleanupWork = Task.CompletedTask;
    private bool _closing;
    private Font? _assistantFont;
    private readonly FormMain _owner;
    private readonly AssistantWindowLayer _windowLayer;
    private readonly Controls.AssistantToggleTab _toggle;
    private readonly AssistantWindowLayer _toggleLayer;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 750 };
    private readonly OpggClient _opgg = new();
    private readonly AramMetaClient _am;

    private sealed class BufferedFlow : FlowLayoutPanel
    {
        public BufferedFlow()
        {
            DoubleBuffered = true;
        }
    }

    private readonly FlowLayoutPanel _body = new BufferedFlow()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = DipPadding(12, 4, 12, 4),
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Top,
        Height = Dip(26),
        Padding = DipPadding(10, 4, 10, 4),
        Text = "等待选择英雄…",
    };
    private readonly CheckBox _pin = new() { Text = "固定位置", AutoSize = true };
    private readonly Button _apply = new()
    {
        Text = "应用符文",
        AutoSize = true,
        Enabled = false,
    };
    private readonly ComboBox _lane = new Controls.DarkFilterComboBox() { Width = Dip(95), Margin = DipPadding(2) };

    // The assistant opens without activation so hovering must also work while LOL has focus.
    private readonly ToolTip _tips = new() { ShowAlways = true };
    private CancellationTokenSource? _load,
        _poll;
    private bool _busy,
        _selecting,
        _syncing,
        _applying;
    private int _hero,
        _queue;
    private string? _position;
    private string _key = "";
    private int _pollFailures;
    private readonly AssistantVisibility _visibility = new();
    private IntPtr _clientWindow;
    private bool _moving;
    private bool _fitQueued;
    private string? _failureText;
    private long _nextClientSearch;

    // Each champion select starts open; the edge tab or the hide button closes it until the next one.
    private bool _panelOpen = true;
    private long _lastSessionRead;
    private OpggRunes? _runes;
    private int _runeChoice;
    protected override bool ShowWithoutActivation => true;

    public ChampionSelectAssistant(FormMain owner)
    {
        _owner = owner;
        _windowLayer = new(this);
        _am = new(cacheLifetime: () =>
            TimeSpan.FromMinutes(Math.Clamp(owner.GetAppConfig()?.AramMetaCacheMinutes ?? 1440, 1, 10080))
        );
        Text = "选人助手";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(8, 29, 40);
        ForeColor = Color.FromArgb(215, 225, 226);
        Font = _assistantFont = new Font("Microsoft YaHei UI", 9);
        ClientSize = Dip(LayoutWidth, MinimumLayoutHeight);
        _status.ForeColor = Color.FromArgb(135, 162, 173);
        var toolbar = CreateAssistantToolbar();
        var (footer, guide) = CreateAssistantFooter();
        var statusBar = CreateAssistantStatusBar();
        var analysisBar = CreateTeamAnalysisBar();
        Controls.Add(_body);
        Controls.Add(analysisBar);
        Controls.Add(statusBar);
        Controls.Add(toolbar);
        Controls.Add(footer);
        StyleAssistantButtons(footer);
        StyleFooterHierarchy(guide);
        _apply.BackColor = League.Controls.AccentColors.Fill;
        BindAssistantFilters();
        SyncFilters();
        _toggle = CreateToggleTab();
        _toggleLayer = new(_toggle);
        BindAssistantActions(guide);
        BindLoadFailureRetry();
        BindAssistantClosing();
        ResizeBegin += (_, _) => _moving = true;
        ResizeEnd += (_, _) => _moving = false;
        _body.Layout += (_, _) => QueueFitToContent();
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    // Most players keep the same filters, so the toolbar is one summary line that opens a menu of
    // them on click. The window action stays at the top right; hiding uses the title bar or edge tab.
    private Panel CreateAssistantToolbar()
    {
        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Padding = DipPadding(10, 6, 10, 6),
            BackColor = Color.FromArgb(10, 32, 43),
        };
        var header = new Panel { Dock = DockStyle.Top, Height = Dip(ToolbarHeaderHeight) };
        _filterSummary.Dock = DockStyle.Fill;
        _filterSummary.FlatStyle = FlatStyle.Flat;
        _filterSummary.FlatAppearance.BorderSize = 0;
        _filterSummary.FlatAppearance.MouseOverBackColor = Color.FromArgb(16, 45, 57);
        _filterSummary.TextAlign = ContentAlignment.MiddleLeft;
        _filterSummary.ForeColor = Color.FromArgb(215, 225, 226);
        _filterSummary.Cursor = Cursors.Hand;
        League.Controls.DarkMenuRenderer.Apply(_filterMenu);
        _filterMenu.Font = Font;
        _filterSummary.Click += (_, _) =>
        {
            BuildFilterMenu();
            _filterMenu.Show(_filterSummary, new Point(0, _filterSummary.Height));
        };
        _tips.SetToolTip(_filterSummary, "攻略模式、统计地区、段位和分路；点击修改。默认跟随选人模式。");
        _pin.FlatStyle = FlatStyle.Flat;
        _pin.Dock = DockStyle.Right;
        _pin.Margin = Padding.Empty;
        _tips.SetToolTip(_pin, "固定后不再自动贴靠客户端，可拖动到任意位置。");
        header.Controls.Add(_filterSummary);
        header.Controls.Add(_pin);
        toolbar.Controls.Add(header);
        toolbar.Height = header.Height + toolbar.Padding.Vertical;
        _lane.Items.AddRange(new object[] { "自动分路", "上路", "打野", "中路", "下路", "辅助" });
        _lane.SelectedIndex = 0;
        return toolbar;
    }

    private (FlowLayoutPanel Footer, Button Guide) CreateAssistantFooter()
    {
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Dip(44),
            Padding = DipPadding(8),
            WrapContents = false,
        };
        var guide = new Button { Text = "完整攻略 ›" };
        _apply.AutoSize = false;
        FitButton(guide, 28);
        FitButton(_apply, 28, "应用方案 9");
        _voiceButton.Size = Dip(34, 28);
        footer.Controls.AddRange(new Control[] { guide, _apply, _voiceButton });
        foreach (Control button in footer.Controls)
        {
            button.Margin = DipPadding(2, 0, 2, 0);
        }

        _tips.SetToolTip(_voiceButton, "语音输入：中文录音后编辑确认，再发送到选人聊天；也可以手动输入。");
        _voiceButton.Click += async (_, _) => await TrackAssistantWork(ShowVoiceInputAsync());
        return (footer, guide);
    }

    private Panel CreateAssistantStatusBar()
    {
        var statusBar = new Panel { Dock = DockStyle.Top, Height = Dip(26) };
        _status.Dock = DockStyle.Fill;
        _status.AutoEllipsis = true;
        _rosterButton.BackColor = Color.FromArgb(16, 45, 57);
        _rosterButton.ForeColor = ActionText;
        _rosterButton.FlatAppearance.BorderColor = Color.FromArgb(49, 113, 126);
        _rosterButton.FlatAppearance.BorderSize = 1;
        _tips.SetToolTip(_rosterButton, "等待客户端提供大乱斗选人数据");
        _rosterButton.Click += (_, _) => ShowRoster();
        statusBar.Controls.Add(_status);
        statusBar.Controls.Add(_rosterButton);
        return statusBar;
    }

    private static void StyleAssistantButtons(FlowLayoutPanel footer)
    {
        foreach (var button in footer.Controls.OfType<Button>())
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(49, 83, 95);
            button.BackColor = Color.FromArgb(16, 45, 57);
            button.ForeColor = Color.FromArgb(220, 230, 226);
        }
    }

    // Only the rune action is a filled button; the guide reads as a link and voice as an icon.
    private void StyleFooterHierarchy(Button guide)
    {
        guide.FlatAppearance.BorderSize = 0;
        guide.BackColor = BackColor;
        guide.ForeColor = ActionText;
        guide.FlatAppearance.MouseOverBackColor = Color.FromArgb(16, 45, 57);
        var icon = new Font("Segoe MDL2 Assets", 11);
        _voiceButton.Font = icon;
        _voiceButton.Disposed += (_, _) => icon.Dispose();
        _voiceButton.Text = "\uE720"; // Microphone glyph
        _voiceButton.AccessibleName = "语音输入";
    }

    private void BindAssistantFilters()
    {
        foreach (var box in new[] { _mode, _region, _tier, _lane })
        {
            box.SelectedIndexChanged += (_, _) =>
            {
                if (_syncing)
                {
                    return;
                }

                SyncFilters();
                if (_hero > 0)
                {
                    _ = LoadHeroAsync();
                }
            };
        }
    }

    private Controls.AssistantToggleTab CreateToggleTab()
    {
        var toggle = new Controls.AssistantToggleTab(Font);
        toggle.Size = toggle.PreferredTabSize(Dip(TabWidth), Dip(8));
        toggle.Toggled += (_, _) => SetPanelOpen(!_panelOpen);
        _tips.SetToolTip(toggle, "收起选人助手；每次进入选人默认打开。");
        return toggle;
    }

    private void SetPanelOpen(bool open)
    {
        _panelOpen = open;
        _toggle.Open = open;
        _tips.SetToolTip(_toggle, open ? "收起选人助手；每次进入选人默认打开。" : "展开选人助手");
        if (!open)
        {
            CloseVoiceInput();
            if (Visible)
            {
                Hide();
            }
        }

        PositionWindow();
    }

    private void BindAssistantActions(Button guide)
    {
        guide.Click += (_, _) =>
        {
            if (_hero > 0)
            {
                _owner.OpenAssistantGuide(
                    _hero,
                    SelectedMode == "mayhem" ? 2400 : 0,
                    _position,
                    SelectedMode == "mayhem" ? null : SelectedQuery
                );
            }
        };
        _apply.Click += async (_, _) => await TrackAssistantWork(ApplyAsync());
    }

    // Retry lives in the status bar and is clickable only while it shows the latest load failure.
    private void BindLoadFailureRetry()
    {
        _status.Click += (_, _) =>
        {
            if (_failureText != null && _hero > 0)
            {
                _ = LoadHeroAsync();
            }
        };
        _status.TextChanged += (_, _) =>
        {
            if (_failureText != null && _status.Text != _failureText)
            {
                ClearLoadFailure();
            }
        };
    }

    private void ShowLoadFailure(Exception error)
    {
        AssistantDiagnostics.Shared.Report("recommendations", error);
        _failureText = LoadFailureReason(error) + " · 点击重试";
        _status.Text = _failureText;
        _status.ForeColor = ActionText;
        _status.Cursor = Cursors.Hand;
        _tips.SetToolTip(_status, "点击重新加载推荐；详细原因已写入日志：" + AssistantDiagnostics.Shared.LogPath);
    }

    private void ClearLoadFailure()
    {
        _failureText = null;
        _status.ForeColor = Color.FromArgb(135, 162, 173);
        _status.Cursor = Cursors.Default;
        _tips.SetToolTip(_status, null);
    }

    internal static string LoadFailureReason(Exception error) =>
        error switch
        {
            InvalidDataException => error.Message,
            HttpRequestException { StatusCode: not null } => "推荐站点暂时无法访问",
            HttpRequestException => "网络连接失败，请检查网络",
            TimeoutException or OperationCanceledException => "推荐加载超时",
            Newtonsoft.Json.JsonException => "推荐数据格式变化，暂时无法读取",
            _ => "推荐暂时无法加载",
        };

    private void BindAssistantClosing()
    {
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                SetPanelOpen(false);
            }
        };
    }

    internal static Rectangle Placement(Rectangle client, Rectangle screen, Size size, int gap)
    {
        int width = Math.Min(size.Width, screen.Width),
            height = Math.Min(size.Height, screen.Height);
        int x = client.Left - width - gap;
        if (x < screen.Left && client.Right + gap + width <= screen.Right)
        {
            x = client.Right + gap;
        }

        return new Rectangle(
            Math.Clamp(x, screen.Left, screen.Right - width),
            Math.Clamp(client.Top, screen.Top, screen.Bottom - height),
            width,
            height
        );
    }

    /// <summary>The tab sits in the gap between the client and the side the panel uses.</summary>
    internal static Rectangle TabPlacement(Rectangle client, Rectangle screen, Rectangle panel, Size size)
    {
        int x = panel.Left >= client.Right ? client.Right : client.Left - size.Width;
        int y = client.Top + (client.Height - size.Height) / 2;
        return new Rectangle(
            Math.Clamp(x, screen.Left, screen.Right - size.Width),
            Math.Clamp(y, screen.Top, screen.Bottom - size.Height),
            size.Width,
            size.Height
        );
    }

    internal static int Dip(int value) => (int)Math.Round(value * SystemDpi / 96.0);

    private static Size Dip(int width, int height) => new(Dip(width), Dip(height));

    private static Padding DipPadding(int all) => new(Dip(all));

    private static Padding DipPadding(int left, int top, int right, int bottom) =>
        new(Dip(left), Dip(top), Dip(right), Dip(bottom));

    // Flat buttons draw Chinese labels with GDI text, which is wider and taller than GDI+ measures;
    // fixed sizes clipped "完整攻略" and the mastery button, so size from the real text metrics.
    private void FitButton(Button button, int minimumHeight, string? widestText = null)
    {
        var text = TextRenderer.MeasureText(widestText ?? button.Text, Font);
        button.Size = new Size(text.Width + Dip(18), Math.Max(Dip(minimumHeight), text.Height + Dip(7)));
    }

    // The panel grows with its recommendations instead of scrolling; Placement caps it at the screen,
    // and only then does the body scroll (see UpdateBodyScroll).
    private int PanelHeight()
    {
        int chrome = ClientSize.Height - _body.Height;
        // Preferred size uses each child's own visibility, so it is valid before the panel is shown.
        int content = _body.GetPreferredSize(new Size(_body.ClientSize.Width, 0)).Height;
        int clientHeight = Math.Max(Dip(MinimumLayoutHeight), chrome + content);
        return SizeFromClientSize(new Size(ClientSize.Width, clientHeight)).Height;
    }

    private void QueueFitToContent()
    {
        if (_fitQueued || _closing || !IsHandleCreated)
        {
            return;
        }

        // Layout fires repeatedly while recommendations stream in; resize once after the batch.
        _fitQueued = true;
        BeginInvoke(
            new Action(() =>
            {
                _fitQueued = false;
                if (!_closing && !IsDisposed && Visible && !_moving)
                {
                    FitToContent();
                }
            })
        );
    }

    // Only the height changes here; the follow timer still owns placement and visibility.
    private void FitToContent()
    {
        var screen = Screen.FromControl(this).WorkingArea;
        int wanted = PanelHeight(),
            height = Math.Min(wanted, screen.Height);
        if (Height != height)
        {
            Height = height;
            if (Bottom > screen.Bottom)
            {
                Top = Math.Max(screen.Top, screen.Bottom - height);
            }
        }

        UpdateBodyScroll(wanted);
    }

    // A scrollbar appears only when the screen cannot fit the content. Always-on auto scrolling showed
    // one whenever content filled the panel exactly, and let the wheel nudge a page that had nothing hidden.
    private void UpdateBodyScroll(int wantedHeight)
    {
        bool overflow = wantedHeight > Height;
        if (_body.AutoScroll != overflow)
        {
            _body.AutoScroll = overflow;
        }
    }

    private void PositionWindow()
    {
        IntPtr handle = _selecting ? FindClientWindow() : IntPtr.Zero;
        bool show = _visibility.ShouldShow(
            _selecting,
            handle != IntPtr.Zero && IsIconic(handle),
            handle != IntPtr.Zero,
            _moving,
            Environment.TickCount64
        );
        if (!show)
        {
            HideAttached();
            return;
        }
        // Grace keeps the current windows while the client handle is briefly unavailable.
        if (
            handle == IntPtr.Zero
            || !GetWindowRect(handle, out var rect)
            || rect.Right <= rect.Left
            || rect.Bottom <= rect.Top
        )
        {
            return;
        }

        var client = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var screen = Screen.FromHandle(handle).WorkingArea;
        int wanted = PanelHeight();
        var panel = Placement(client, screen, new Size(Width, wanted), _toggle.Width);
        _toggle.PanelOnLeft = panel.Left < client.Left;
        PlaceToggle(handle, TabPlacement(client, screen, panel, _toggle.Size));
        if (!_panelOpen)
        {
            return;
        }

        if (_moving)
        {
            // The user is dragging; keep the window where they put it.
        }
        else if (_pin.Checked)
        {
            Height = panel.Height;
        }
        else if (Bounds != panel)
        {
            Bounds = panel;
        }

        UpdateBodyScroll(wanted);

        ShowAttached(this, _windowLayer, handle);
    }

    // Process enumeration runs on the UI thread, so a missing client is searched at most every 2 s
    // and never outside champion select.
    private IntPtr FindClientWindow()
    {
        if (_clientWindow != IntPtr.Zero && IsWindow(_clientWindow) && IsWindowVisible(_clientWindow))
        {
            return _clientWindow;
        }

        _clientWindow = IntPtr.Zero;
        long now = Environment.TickCount64;
        if (now < _nextClientSearch)
        {
            return IntPtr.Zero;
        }

        foreach (var process in Process.GetProcessesByName("LeagueClientUx"))
        {
            using (process)
            {
                try
                {
                    var candidate = process.MainWindowHandle;
                    if (_clientWindow == IntPtr.Zero && candidate != IntPtr.Zero && IsWindowVisible(candidate))
                    {
                        _clientWindow = candidate;
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited during enumeration; keep looking at the others.
                }
            }
        }

        _nextClientSearch = _clientWindow == IntPtr.Zero ? now + 2000 : 0;
        return _clientWindow;
    }

    private void PlaceToggle(IntPtr client, Rectangle bounds)
    {
        if (_toggle.Bounds != bounds)
        {
            _toggle.Bounds = bounds;
        }

        ShowAttached(_toggle, _toggleLayer, client);
    }

    private static void ShowAttached(Form form, AssistantWindowLayer layer, IntPtr client)
    {
        if (!layer.Attach(client) || form.Visible)
        {
            return;
        }

        // Stack before and after showing so a background client never gets a panel above other apps.
        layer.StackAboveClient();
        form.Show();
        layer.Attach(client);
        layer.StackAboveClient();
    }

    private void HideAttached()
    {
        if (Visible)
        {
            Hide();
        }

        if (_toggle.Visible)
        {
            _toggle.Hide();
        }
    }

    private Task TickAsync() => TrackAssistantWork(PollChampionSessionAsync());

    private async Task PollChampionSessionAsync()
    {
        if (_closing)
        {
            return;
        }
        if (!(_owner.GetAppConfig()?.EnableChampionSelectAssistant ?? false))
        {
            Reset();
            return;
        }
        PositionWindow();
        if (_busy)
        {
            return;
        }

        var client = FormMain.Globals.lcuClient?.Client;
        if (client == null)
        {
            Reset();
            return;
        }
        _busy = true;
        RetireAssistantSource(_poll);
        var request = _poll = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var token = request.Token;
        try
        {
            var phase = (await client.GetStringAsync("/lol-gameflow/v1/gameflow-phase", token)).Trim('"');
            if (_closing || token.IsCancellationRequested)
            {
                return;
            }
            if (phase != "ChampSelect")
            {
                Reset();
                return;
            }
            var flow = JObject.Parse(await client.GetStringAsync("/lol-gameflow/v1/session", token));
            var session = JObject.Parse(await client.GetStringAsync("/lol-champ-select/v1/session", token));
            if (IsDisposed)
            {
                return;
            }

            ApplyChampionSession(flow, session);
        }
        catch (Exception ex)
        {
            if (!IsDisposed && ++_pollFailures >= 3 && Environment.TickCount64 - _lastSessionRead > 15000)
            {
                Reset();
            }

            Debug.WriteLine("Assistant poll: " + ex.GetType().Name);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// The queue whose statistics apply. Custom games have no matchmaking queue (-1), so their game mode decides
    /// (KIWI is Mayhem, ARAM, URF and so on); Mayhem's other queue 3270 counts as 2400.
    /// </summary>
    internal static int SessionQueue(JObject flow) => GameQueues.Effective(flow["gameData"]);

    private void ApplyChampionSession(JObject flow, JObject session)
    {
        _pollFailures = 0;
        _lastSessionRead = Environment.TickCount64;
        _selecting = true;
        _voiceButton.Enabled = !_openingVoice;
        int queue = SessionQueue(flow);
        _queue = queue;
        UpdateRoster(session, queue);
        var own = (session["myTeam"] as JArray)
            ?.OfType<JObject>()
            .FirstOrDefault(p => (int?)p["cellId"] == (int?)session["localPlayerCellId"]);
        _masteryPuuid = (string?)own?["puuid"] ?? "";
        if (string.IsNullOrWhiteSpace(_masteryPuuid) || _masteryPuuid == Guid.Empty.ToString())
        {
            _masteryPuuid = _owner.GetCurrentPuuid() ?? "";
        }

        int hero = SelectedChampion(session, own);
        if (hero <= 0)
        {
            if (_hero != 0)
            {
                CancelHeroRequest();
                _hero = 0;
                _key = "";
                _runes = null;
                _apply.Enabled = false;
                ClearBody();
            }
            _status.Text = "等待选择英雄…";
            PositionWindow();
            return;
        }
        string? lane = ((string?)own?["assignedPosition"])?.ToLowerInvariant() switch
        {
            "middle" => "mid",
            "bottom" => "adc",
            "utility" => "support",
            "top" => "top",
            "jungle" => "jungle",
            _ => null,
        };
        string key = $"{_masteryPuuid}:{hero}:{queue}:{lane}";
        if (key != _key)
        {
            bool differentHero = hero != _hero;
            _hero = hero;
            _queue = queue;
            _position = lane;
            _key = key;
            if (differentHero)
            {
                _syncing = true;
                try
                {
                    _lane.SelectedIndex = 0;
                }
                finally
                {
                    _syncing = false;
                }
            }
            _ = LoadHeroAsync();
        }
        TrackAssistantWork(RefreshMastery());
        PositionWindow();
    }

    private static int SelectedChampion(JObject session, JObject? own)
    {
        int hero = (int?)own?["championId"] ?? 0;
        if (hero <= 0)
        {
            hero = (session["actions"] as JArray ?? new())
                .OfType<JArray>()
                .SelectMany(a => a.OfType<JObject>())
                .Where(a =>
                    (int?)a["actorCellId"] == (int?)session["localPlayerCellId"] && (string?)a["type"] == "pick"
                )
                .Select(a => (int?)a["championId"] ?? 0)
                .LastOrDefault(id => id > 0);
        }

        return hero;
    }

    private void Reset()
    {
        _selecting = false;
        CloseVoiceInput();
        _key = "";
        _hero = 0;
        CancelHeroRequest();
        _runes = null;
        _apply.Enabled = false;
        ClearRoster();
        HideAttached();
        _panelOpen = true;
        _toggle.Open = true;
        _windowLayer.Detach();
        _toggleLayer.Detach();
    }

    private void ClearBody()
    {
        _masteryLabel = null;
        _masteryRow = null;
        _banner = null;
        _heroInfo = null;
        _sendMastery = null;
        _mastery = null;
        foreach (Control control in _body.Controls.Cast<Control>().ToArray())
        {
            control.Dispose();
        }
    }

    private void Line(string text, bool heading = false) =>
        _body.Controls.Add(
            new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = DipPadding(0, 2, 0, 2),
                ForeColor = heading ? MutedHeading : Color.FromArgb(195, 213, 219),
            }
        );

    private Task LoadHeroAsync() => TrackAssistantWork(LoadHeroRecommendationsAsync());

    private async Task LoadHeroRecommendationsAsync()
    {
        if (_closing)
        {
            return;
        }
        RetireAssistantSource(_load);
        var request = _load = new CancellationTokenSource();
        int hero = _hero;
        var token = request.Token;
        _runes = null;
        _runeChoice = 0;
        _apply.Text = "应用符文";
        _apply.Enabled = false;
        ClearBody();
        _status.Text = "正在加载推荐…";
        SyncFilters();
        string mode = SelectedMode;
        try
        {
            TrackAssistantWork(LoadSkillsAsync(hero, token));
            AddMasterySummary();
            if (mode == "mayhem")
            {
                await LoadMayhemRecommendationsAsync(hero, token);
            }
            else
            {
                var query = await ResolveRecommendationQueryAsync(mode, hero, token);
                var build = await _opgg.GetBuildAsync(query, false, token);
                token.ThrowIfCancellationRequested();
                RenderOpggTierAndRunes(build, query, mode, hero, token);
                await RenderOpggEquipmentAndSpellsAsync(build, token);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer hero selection owns the replacement controls and status.
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !IsDisposed)
            {
                ShowLoadFailure(ex);
            }
        }
    }

    private async Task LoadMayhemRecommendationsAsync(int hero, CancellationToken token)
    {
        var page = await _am.GetAsync(0, false, token);
        token.ThrowIfCancellationRequested();
        AddMayhemRecommendations(page, hero, token);
        // The mode is already in the header; the status names only the source and its version.
        _status.Text = $"arammeta · 版本 {page.Index.Patch}" + (page.UpdateFailed ? " · 旧缓存" : "");
    }

    private async Task<OpggQuery> ResolveRecommendationQueryAsync(string mode, int hero, CancellationToken token)
    {
        string? selected = new string?[] { null, "top", "jungle", "mid", "adc", "support" }[_lane.SelectedIndex];
        var query = SelectedQuery;
        if (mode == "ranked" && selected == null)
        {
            var tiers = await _opgg.GetTiersAsync(query, false, token);
            query = query with { Position = OpggQuery.PositionForChampion(tiers.Rows, hero, _position) ?? "mid" };
        }
        return query;
    }

    private void RenderOpggTierAndRunes(
        OpggBuild build,
        OpggQuery query,
        string mode,
        int hero,
        CancellationToken token
    )
    {
        _position = query.Position;
        _runes = build.Runes.FirstOrDefault();
        string laneName = query.Position switch
        {
            "top" => "上路",
            "jungle" => "打野",
            "mid" => "中路",
            "adc" => "下路",
            "support" => "辅助",
            _ => "",
        };
        var tier = (int?)(build.Stats["tier_data"]?["tier"] ?? build.Stats["tier"]);
        var summary = AddTierSummary(
            tier.HasValue ? "T" + tier : "—",
            $"胜率 {((double?)build.Stats["win_rate"] ?? OpggBuild.Ratio(build.Stats["win"], build.Stats["play"])):P2} · {laneName}"
        );
        if (mode == "aram")
        {
            AddBalanceHint(summary, hero, token);
        }

        if (_runes != null)
        {
            Line("推荐符文", true);
            AddRuneChoices(build.Runes, token);
        }
    }

    private async Task RenderOpggEquipmentAndSpellsAsync(OpggBuild build, CancellationToken token)
    {
        var skills = build.Data["skill_masteries"]?.First?["ids"] as JArray;
        Line("核心装备" + (skills != null ? "  ·  加点 " + string.Join(" → ", skills.Values<string>()) : ""), true);
        AddEquipmentChoices(build, token);
        Line("召唤师技能", true);
        await AddSpellRecommendations(build.Data["summoner_spells"] as JArray ?? new(), token);
        _status.Text = $"OP.GG · 版本 {build.Version}";
        _apply.Enabled = _runes != null && !_applying;
    }

    private async Task IconRow(IEnumerable<int> ids, string kind, CancellationToken token, Control? parent = null)
    {
        token.ThrowIfCancellationRequested();
        var container = parent ?? _body;
        int contentWidth =
            parent?.MaximumSize.Width > 0 ? parent.MaximumSize.Width - parent.Padding.Horizontal : ContentWidth;
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MaximumSize = new Size(contentWidth, 0),
            Margin = Padding.Empty,
        };
        container.Controls.Add(row);
        foreach (int id in ids)
        {
            var resources = FormMain.Globals.resLoading;
            var info = kind switch
            {
                "champion" => await resources.GetChampionInfoAsync(id),
                "augment" => await resources.GetAugmentInfoAsync(id),
                "item" => await resources.GetItemInfoAsync(id),
                "spell" => await resources.GetSpellInfoAsync(id),
                _ => await resources.GetRuneInfoAsync(id),
            };
            if (token.IsCancellationRequested || row.IsDisposed)
            {
                info.image?.Dispose();
                token.ThrowIfCancellationRequested();
                return;
            }
            if (info.image == null)
            {
                row.Controls.Add(
                    new Label
                    {
                        Text = info.name,
                        AutoSize = true,
                        MaximumSize = new Size(Math.Min(Dip(260), contentWidth - Dip(6)), 0),
                    }
                );
                continue;
            }
            int size = Dip(
                kind == "champion" ? 52
                : kind == "rune" ? 26
                : 32
            );
            var icon = new PictureBox
            {
                Image = info.image,
                Size = new Size(size, size),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = DipPadding(2),
            };
            icon.Disposed += (_, _) => icon.Image?.Dispose();
            _tips.SetToolTip(icon, League.Controls.TooltipText.Format(info.name, info.description));
            row.Controls.Add(icon);
        }
    }

    private async Task ApplyAsync()
    {
        if (SelectedMode == "mayhem")
        {
            await SendMayhemNoticeAsync();
            return;
        }
        if (_applying || _runes == null || _hero <= 0)
        {
            return;
        }

        int hero = _hero,
            choice = _runeChoice + 1;
        var runes = _runes;
        _applying = true;
        _apply.Enabled = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_load?.Token ?? default);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        string map = CurrentMapName();
        _status.Text = "正在应用并校验符文…";
        try
        {
            var client = FormMain.Globals.lcuClient?.Client ?? throw new InvalidOperationException("客户端已断开。");
            await VerifySelectedChampionAsync(client, hero, timeout.Token);
            string champion = FormMain.Globals.resLoading?.GetChampionById(hero)?.Name ?? hero.ToString();
            string keystone = FormMain.Globals.resLoading?.GetRuneById(runes.Perks[0])?.name ?? "推荐符文";
            await OpggRuneService.ApplyAsync(client, runes, champion, timeout.Token);
            if (!IsDisposed)
            {
                _status.Text = $"符文方案 {choice} 已应用。";
            }

            await AnnounceAppliedRunesAsync(client, runes, champion, keystone, map, choice, timeout.Token);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _status.Text = ex is OperationCanceledException ? "应用中断或超时，请确认当前符文后重试。" : ex.Message;
                _tips.SetToolTip(_status, _status.Text);
            }
        }
        finally
        {
            _applying = false;
            if (!IsDisposed)
            {
                _apply.Enabled = _runes != null && _selecting;
            }
        }
    }

    private string CurrentMapName()
    {
        return SelectedMode switch
        {
            "ranked" => "召唤师峡谷",
            "aram" => "极地大乱斗",
            "arena" => "斗魂竞技场",
            "urf" => "无限火力",
            "nexus_blitz" => "极限闪击",
            "mayhem" => "海克斯大乱斗",
            _ => "召唤师峡谷",
        };
    }

    private static async Task VerifySelectedChampionAsync(HttpClient client, int hero, CancellationToken token)
    {
        var session = JObject.Parse(await client.GetStringAsync("/lol-champ-select/v1/session", token));
        var own = (session["myTeam"] as JArray)
            ?.OfType<JObject>()
            .FirstOrDefault(p => (int?)p["cellId"] == (int?)session["localPlayerCellId"]);
        int selectedHero = SelectedChampion(session, own);
        if (selectedHero != hero)
        {
            throw new InvalidOperationException("英雄已变化，请等待推荐更新。");
        }
    }

    private async Task AnnounceAppliedRunesAsync(
        HttpClient client,
        OpggRunes runes,
        string champion,
        string keystone,
        string map,
        int choice,
        CancellationToken token
    )
    {
        try
        {
            var rates = new List<string>();
            if (runes.WinRate is double win && double.IsFinite(win) && win >= 0 && win <= 1)
            {
                rates.Add($"胜率 {win:P2}");
            }

            if (runes.PickRate is double pick && double.IsFinite(pick) && pick >= 0 && pick <= 1)
            {
                rates.Add($"使用率 {pick:P2}");
            }

            string statistics = rates.Count > 0 ? "（" + string.Join(" / ", rates) + "）" : "";
            bool sent = await AnnounceRunesAsync(
                client,
                $"【{map}】{champion} · {keystone}{statistics}符文已配置成功——数据来自 OP.GG",
                token
            );
            if (!sent && !IsDisposed)
            {
                _status.Text = $"方案 {choice} 已应用，聊天提示未发送。";
            }
        }
        catch (Exception ex)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "符文应用后的聊天提示未发送", ex);
            if (!IsDisposed)
            {
                _status.Text = $"方案 {choice} 已应用，聊天提示未发送。";
            }
        }
    }

    private Task TrackAssistantWork(Task task)
    {
        lock (_assistantWork)
        {
            _assistantWork.Add(task);
        }
        _ = task.ContinueWith(
            completed =>
            {
                lock (_assistantWork)
                {
                    _assistantWork.Remove(completed);
                }
                if (completed.Exception is { } error)
                {
                    Debug.WriteLine("Assistant work: " + error.GetBaseException().GetType().Name);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        return task;
    }

    private Task[] PendingAssistantWork()
    {
        lock (_assistantWork)
        {
            return _assistantWork.ToArray();
        }
    }

    private void RetireAssistantSource(CancellationTokenSource? source)
    {
        if (source == null)
        {
            return;
        }
        var pending = PendingAssistantWork();
        var cancellation = source.CancelAsync();
        TrackAssistantWork(
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

    private void CancelHeroRequest()
    {
        var source = _load;
        _load = null;
        RetireAssistantSource(source);
    }

    private async Task CleanupAssistantResourcesAsync(Task[] pending)
    {
        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Work faults are observed; client cleanup must still complete.
            AppLog.Shared.Warn(LogCategory.Ui, "Assistant cleanup", ex);
        }
        _opgg.Dispose();
        _am.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            CloseVoiceInput();
            _timer.Stop();
            _timer.Dispose();
            CancelHeroRequest();
            RetireAssistantSource(_poll);
            _poll = null;
            ClearBody();
            ClearRoster();
            _windowLayer.Dispose();
            _toggleLayer.Dispose();
            _toggle.Dispose();
            var pending = PendingAssistantWork();
            _cleanupWork = Task.Run(() => CleanupAssistantResourcesAsync(pending), CancellationToken.None);
            _tips.Dispose();
            _filterMenu.Dispose();
            foreach (var box in new[] { _mode, _region, _tier, _lane })
            {
                box.Dispose();
            }

            _assistantFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left,
            Top,
            Right,
            Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out RECT rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
}
