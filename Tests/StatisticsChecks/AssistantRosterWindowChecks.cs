using System.Collections;
using System.Net;
using System.Reflection;
using League;
using League.Clients;
using League.Infrastructure;
using League.Models;
using static StatisticsCheckSupport;

/// <summary>
/// The "champions you can pick" window of the assistant: the tier cards it renders from a stub roster, sorting,
/// switching to the team analysis, the send menu and its confirmation text, the ranking load and its retry, and the
/// mastery and portrait work each card starts. The window is built directly with stub delegates and shown off screen,
/// so no client, chat, network or champion art is involved.
/// </summary>
internal static class AssistantRosterWindowChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type WindowType = typeof(League.FormMain).Assembly.GetType(
        "League.Controls.AssistantRosterWindow"
    )!;
    private static readonly Type TierType = typeof(League.FormMain).Assembly.GetType(
        "League.Models.ChampionRosterTier"
    )!;
    private static readonly Type RankMapType = typeof(Dictionary<,>).MakeGenericType(typeof(int), TierType);
    private static readonly Type RankTupleType = typeof(ValueTuple<,>).MakeGenericType(RankMapType, typeof(string));
    private static readonly Type RankFuncType = typeof(Func<,>).MakeGenericType(
        typeof(CancellationToken),
        typeof(Task<>).MakeGenericType(RankTupleType)
    );

    public static void Run()
    {
        var previousResources = FormMain.Globals.resLoading;
        FormMain.Globals.resLoading = new ResourceLoading();
        try
        {
            Rendering();
            RankingFailure();
            Analysis();
            Mastery();
            Sending();
        }
        finally
        {
            FormMain.Globals.resLoading = previousResources;
        }

        Console.WriteLine(
            "PASS: the roster window renders tier cards and re-sorts them, keeps the client's roster when the "
                + "rankings fail and retries, toggles the team analysis, publishes mastery and portrait results with "
                + "their failures, and sends the tier or analysis lines once with every confirmation text."
        );
    }

    private static void Rendering()
    {
        using var fixture = new Fixture();
        fixture.SetRanks((103, "OP", .55, .12), (1, "T2", .5, .08), (157, "—", null, null));
        fixture.Open();
        Check(fixture.Window.Text.Contains("海克斯大乱斗"), "A mayhem window names the mode it shows");
        Check(
            fixture.Status.Text.Contains("arammeta") && fixture.Status.Text.Contains("16.17"),
            "The version is shown"
        );
        Check(fixture.SendTier.Enabled && !fixture.SendAnalysis.Visible, "Loaded tiers offer the send menu");

        var grids = fixture.Content.Controls.OfType<FlowLayoutPanel>().ToArray();
        Check(grids.Length == 2, "The window shows my team and the swap bench as separate groups");
        Check(Label(fixture, "我方阵容") && Label(fixture, "交换池"), "Both groups are titled");
        Check(Label(fixture, "英雄 103"), "A card names the champion from the catalog fallback");

        fixture.Sort.SelectedIndex = 1;
        Application.DoEvents();
        Check(fixture.Status.Text.Length > 0, "Sorting by win rate re-renders the window");

        fixture.Roster = new ChampionSelectRoster(
            [new ChampionSelectRosterEntry(103, 0, true, false), new ChampionSelectRosterEntry(157, 1, false, true)],
            [],
            false
        );
        fixture.Update();
        Check(Label(fixture, "客户端未提供交换池"), "An empty bench explains why it is empty");

        fixture.Roster = new ChampionSelectRoster([], [], true);
        fixture.Update();
        Check(Label(fixture, "尚未读取到我方阵容"), "An empty team explains why it is empty");

        fixture.Roster = null;
        fixture.Update();
        Check(!fixture.Window.IsDisposed, "A window without a roster keeps the last cards");

        fixture.Roster = new ChampionSelectRoster([new ChampionSelectRosterEntry(103, 0, true, false)], [], true);
        fixture.Queue = 450;
        fixture.Update();
        Check(fixture.Window.IsDisposed, "Leaving for another queue closes the window");
    }

    private static void RankingFailure()
    {
        using var fixture = new Fixture();
        fixture.SetRanks();
        fixture.RankError = new InvalidOperationException("梯队源不可用");
        fixture.Open();
        Check(!fixture.SendTier.Enabled && fixture.Retry.Enabled, "A failed ranking load leaves retry available");
        Check(fixture.Status.Text.Contains("暂不可用"), "A failed ranking load keeps the client's champions visible");
        Check(Label(fixture, "英雄 103"), "Cards still show the client's champions after the failure");

        fixture.RankError = null;
        fixture.SetRanks((103, "OP", .55, .12), (1, "T2", .5, .08));
        fixture.Retry.PerformClick();
        Application.DoEvents();
        Check(fixture.Status.Text.Contains("16.17") && fixture.SendTier.Enabled, "Retry loads the rankings again");

        Set(fixture.Window, "_loading", true);
        Invoke(fixture.Window, "LoadRankingsAsync");
        Check(fixture.Status.Text.Contains("16.17"), "A load already in flight is not started twice");

        Set(fixture.Window, "_loading", false);
        fixture.RankError = new OperationCanceledException();
        fixture.Retry.PerformClick();
        Application.DoEvents();
        Check(fixture.Status.Text.Contains("16.17"), "A cancelled ranking load keeps the last status");
        fixture.RankError = null;

        Invoke(fixture.Window, "Track", Task.FromException(new InvalidOperationException("后台任务失败")));
        Check(!fixture.Window.IsDisposed, "A failed background task is observed without disturbing the window");

        using var late = new Fixture();
        late.SetRanks((103, "OP", .55, .12));
        late.Open();
        late.Window.Dispose();
        Set(late.Window, "_closing", false);
        using var lifetime = new CancellationTokenSource();
        Set(late.Window, "_lifetime", lifetime);
        Invoke(late.Window, "LoadRankingsAsync");
        Check(late.Window.IsDisposed, "A ranking answer that arrives after closing is dropped");
    }

    private static void Analysis()
    {
        using var fixture = new Fixture();
        fixture.SetRanks((103, "OP", .55, .12), (1, "T2", .5, .08));
        fixture.Open();

        fixture.AnalysisButton.PerformClick();
        Settle();
        Check(
            fixture.Analysis.Visible && !fixture.Content.Visible && fixture.SendAnalysis.Visible,
            "The analysis button replaces the tier list with the team analysis"
        );
        Check(fixture.AnalysisButton.Text.Contains("返回"), "The analysis button offers the way back");
        fixture.Update();
        Check(fixture.Analysis.Visible, "Updating the roster keeps the visible analysis");

        fixture.AnalysisButton.PerformClick();
        Application.DoEvents();
        Check(
            !fixture.Analysis.Visible && fixture.Content.Visible && fixture.AnalysisButton.Text.Contains("阵容分析"),
            "The analysis button returns to the tier list"
        );
    }

    private static void Mastery()
    {
        using var fixture = new Fixture();
        fixture.SetRanks((103, "OP", .55, .12), (1, "T2", .5, .08));
        fixture.Mastery = Service((_, _) => Answer(MasteryPage()), Path.Combine(fixture.Root, "ok"));
        fixture.Open();
        Check(
            fixture.WaitFor(() => Labels(fixture).Any(l => l.Text.Contains("18.6万"))),
            "A mastery page fills the card"
        );
        Check(
            Labels(fixture).Any(l => ToolTipOf(fixture, l).Contains("英雄成就")),
            "The achievement tooltip explains the card"
        );
        fixture.TickMasteryTimer();
        Check(Labels(fixture).Any(l => l.Text.Contains("18.6万")), "The mastery timer refreshes the cards it built");

        using var failing = new Fixture();
        failing.SetRanks((103, "OP", .55, .12));
        failing.Mastery = Service(
            (_, _) => throw new InvalidOperationException("成就源失败"),
            Path.Combine(failing.Root, "fail")
        );
        failing.Open();
        Check(
            failing.WaitFor(() => Labels(failing).Any(l => l.Text.Contains("成就暂不可用"))),
            "A failing mastery request is reported on the card"
        );

        using var cancelled = new Fixture();
        cancelled.SetRanks((103, "OP", .55, .12));
        cancelled.Mastery = Service(Gated(), Path.Combine(cancelled.Root, "cancel"));
        cancelled.Open();
        cancelled.FirstRefresh();
        cancelled.CancelCards();
        Settle();
        Check(
            Labels(cancelled).Any(l => l.Text.Contains("成就读取中")),
            "A cancelled mastery request publishes nothing, and a card already reading is not asked twice"
        );

        using var broken = new Fixture();
        broken.SetRanks((103, "OP", .55, .12));
        broken.Mastery = BrokenService(Path.Combine(broken.Root, "broken"));
        broken.Open();
        Check(
            broken.WaitFor(() => Labels(broken).Any(l => l.Text.Contains("成就暂不可用"))),
            "A mastery failure the client cannot classify is still reported on the card"
        );

        Portraits();
    }

    private static void Portraits()
    {
        using var failing = new Fixture();
        failing.SetRanks((103, "OP", .55, .12));
        failing.PortraitError = new InvalidOperationException("头像源失败");
        failing.Open();
        Check(Label(failing, "英雄 103"), "A failing portrait source still leaves the tier card readable");

        using var loaded = new Fixture();
        loaded.SetRanks((103, "OP", .55, .12));
        loaded.Open();
        using var disposed = new PictureBox();
        disposed.Dispose();
        var pending = (Task)Call(loaded.Window, "LoadHeroIconAsync", disposed, 103, CancellationToken.None)!;
        Check(pending.IsCompleted, "A portrait that arrives for a disposed card is dropped");

        using var guide = new Fixture();
        guide.SetRanks((103, "OP", .55, .12));
        guide.Open();
        var label = Labels(guide).First(l => l.Text == "英雄 103");
        Click(label);
        Check(guide.Guided.Contains(103), "Clicking a card opens that champion's guide");
        guide.Sort.SelectedIndex = 1;
        Settle();
        guide.Guided.Clear();
        Click(label);
        Check(guide.Guided.Count == 0, "A card from a retired roster opens nothing");
    }

    private static void Sending()
    {
        using var fixture = new Fixture();
        fixture.SetRanks((103, "OP", .55, .12), (1, "T2", .5, .08));
        fixture.Open();

        fixture.SendTier.PerformClick();
        Check(fixture.SendBench.Enabled, "The send menu offers the swap bench the client reported");
        fixture.SendMenu.Close();

        fixture.SendAll.PerformClick();
        Check(
            fixture.WaitFor(() => fixture.Status.Text.Contains("已发送")) && fixture.Sent.Count == 2,
            "Sending all tiers sends my team and the bench once each"
        );
        Check(fixture.Sent.All(m => m.Length <= 180), "Every tier message fits one chat line");

        fixture.Sent.Clear();
        fixture.Answer = false;
        fixture.SendTeam.PerformClick();
        Check(
            fixture.WaitFor(() => fixture.Status.Text.Contains("发送未完成")),
            "A refused tier message reports what was not sent"
        );

        fixture.Sent.Clear();
        fixture.Answer = true;
        fixture.FailSend = new OperationCanceledException();
        fixture.SendBench.PerformClick();
        Check(fixture.WaitFor(() => fixture.Status.Text.Contains("超时")), "A timed out tier send says so");

        fixture.FailSend = new InvalidOperationException("连接断了");
        fixture.SendAll.PerformClick();
        Check(fixture.WaitFor(() => fixture.Status.Text.Contains("发送失败")), "A failed tier send says so");
        fixture.FailSend = null;

        fixture.Roster = new ChampionSelectRoster([], [], true);
        fixture.Update();
        fixture.SendAll.PerformClick();
        Check(
            fixture.WaitFor(() => fixture.Status.Text.Contains("没有可发送")),
            "A roster without a picked champion has nothing to send"
        );

        int before = fixture.Sent.Count;
        fixture.Ranks.Clear();
        fixture.Roster = Champions();
        fixture.Update();
        fixture.SendAll.PerformClick();
        Check(fixture.Sent.Count == before, "Without loaded tiers nothing is sent at all");

        AnalysisSend(fixture);
    }

    private static void AnalysisSend(Fixture fixture)
    {
        fixture.SetRanks((103, "OP", .55, .12));
        fixture.Roster = Champions();
        fixture.Update();
        fixture.Sent.Clear();
        Invoke(fixture.Window, "SendAnalysisAsync", null!, "阵容分析");
        Check(
            fixture.WaitFor(() => fixture.Status.Text.Contains("尚未读取完成")),
            "The analysis explains why it cannot be sent yet"
        );

        Invoke(fixture.Window, "SendAnalysisAsync", "【阵容】我方胜率估计 52.0%", "阵容分析");
        Check(
            fixture.WaitFor(() => fixture.Status.Text.Contains("阵容分析已发送")) && fixture.Sent.Count == 1,
            "A ready analysis is sent as one line"
        );

        fixture.Answer = false;
        Invoke(fixture.Window, "SendAnalysisAsync", "【能力】前排 B", "能力等级");
        Check(fixture.WaitFor(() => fixture.Status.Text.Contains("未发送")), "A refused analysis is reported");
        fixture.Answer = true;

        fixture.FailSend = new OperationCanceledException();
        Invoke(fixture.Window, "SendAnalysisAsync", "【能力】前排 B", "能力等级");
        Check(fixture.WaitFor(() => fixture.Status.Text.Contains("超时")), "A timed out analysis send says so");

        fixture.FailSend = new InvalidOperationException("连接断了");
        Invoke(fixture.Window, "SendAnalysisAsync", "【能力】前排 B", "能力等级");
        Check(fixture.WaitFor(() => fixture.Status.Text.Contains("发送失败")), "A failed analysis send says so");
        fixture.FailSend = null;

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Sent.Clear();
        fixture.SendGate = gate;
        Invoke(fixture.Window, "SendAnalysisAsync", "【阵容】第一条", "阵容分析");
        Invoke(fixture.Window, "SendAnalysisAsync", "【阵容】第二条", "阵容分析");
        Check(fixture.Sent.Count == 1, "One analysis line is in flight at a time");
        gate.SetResult();
        Check(fixture.WaitFor(() => fixture.Status.Text.Contains("已发送")), "The gated analysis line arrives");
        fixture.SendGate = null;

        fixture.Window.Dispose();
        Invoke(fixture.Window, "ShowAnalysisStatus", "关闭后的提示");
        Check(fixture.Window.IsDisposed, "A closed window ignores a late status line");
        Check(
            fixture.WaitFor(() => Completion(fixture.Window).IsCompleted),
            "Closing the window finishes the work it tracked"
        );
    }

    private static Task Completion(Form window) => (Task)WindowType.GetProperty("Completion")!.GetValue(window)!;

    // ------------------------------------------------------------------ stubs

    private static AramMetaIndex Index() =>
        new(
            "16.17",
            "test",
            Enumerable
                .Range(0, 6)
                .Select(i => new AramMetaChampion(
                    100 + i,
                    "英雄 " + (100 + i),
                    "Hero" + i,
                    [],
                    .5 + i / 100d,
                    .5,
                    100,
                    new()
                ))
                .ToArray(),
            new(),
            new()
        );

    private static string MasteryPage() =>
        """[{"championId":103,"championLevel":12,"championPoints":186321},{"championId":1,"championLevel":3,"championPoints":900}]""";

    private static Task<HttpResponseMessage> Answer(string body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });

    private static ChampionMasteryService Service(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        string folder
    )
    {
        // The service compares the client instance before and after a request, so the stub is created once.
        var client = new HttpClient(new Handler(handler)) { BaseAddress = new Uri("https://roster.invalid/") };
        return new ChampionMasteryService(() => client, () => "roster-check", folder);
    }

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Gated()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return async (_, token) =>
        {
            await gate.Task.WaitAsync(token);
            return await Answer(MasteryPage());
        };
    }

    /// <summary>A service whose key cannot even be built, so the failure is not a readable statistics response.</summary>
    private static ChampionMasteryService BrokenService(string folder) =>
        new(
            () =>
                new HttpClient(new Handler((_, _) => Answer(MasteryPage())))
                {
                    BaseAddress = new Uri("https://r.invalid/"),
                },
            () => throw new InvalidOperationException("统计作用域不可用"),
            folder
        );

    private static ChampionSelectRoster Champions() =>
        new(
            [
                new ChampionSelectRosterEntry(103, 0, true, false, "self"),
                new ChampionSelectRosterEntry(1, 1, false, true, "mate"),
            ],
            [134],
            true
        );

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            action(request, token);
    }

    /// <summary>Holds the tuple a stub ranking load returns; the tier type itself is internal to the application.</summary>
    private sealed class RankSource<T>
    {
        public T Value = default!;
        public Exception? Error;

        public Task<T> Load(CancellationToken token) =>
            Error == null ? Task.FromResult(Value) : Task.FromException<T>(Error);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ToolTip _tips = new();
        private readonly object _source;
        private readonly List<Image> _portraits = new();
        private ChampionMasteryService _mastery;

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "roster-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            _source = Activator.CreateInstance(typeof(RankSource<>).MakeGenericType(RankTupleType))!;
            _mastery = Service((_, _) => Answer(MasteryPage()), Path.Combine(Root, "default"));
            Window = (Form)
                Activator.CreateInstance(
                    WindowType,
                    new object[]
                    {
                        new Panel { BackColor = Color.FromArgb(8, 29, 40) },
                        2400,
                        true,
                        _tips,
                        (Func<ChampionSelectRoster?>)(() => Roster),
                        (Func<int>)(() => Queue),
                        (Func<string?>)(() => "self"),
                        (Func<CancellationToken, Task<AramMetaIndex>>)(_ => Task.FromResult(Index())),
                        Delegate.CreateDelegate(RankFuncType, _source, _source.GetType().GetMethod("Load")!),
                        (Func<string, CancellationToken, Task<bool>>)AnnounceAsync,
                        (Action<int>)(id => Guided.Add(id)),
                        _mastery,
                    }
                )!;
            Window.StartPosition = FormStartPosition.Manual;
            Window.Location = new Point(-30000, -30000);
            MasteryTimer.Stop();
            Set(Window, "_portraitLoader", (Func<int, Task<Image?>>)Portrait);
            SetRanks();
        }

        public Form Window { get; }
        public string Root { get; }
        public ChampionSelectRoster? Roster { get; set; } = Champions();
        public int Queue { get; set; } = 2400;
        public List<string> Sent { get; } = new();
        public List<int> Guided { get; } = new();
        public bool Answer { get; set; } = true;
        public Exception? FailSend { get; set; }
        public Exception? PortraitError { get; set; }
        public TaskCompletionSource? SendGate { get; set; }
        public FlowLayoutPanel Content => Private<FlowLayoutPanel>(Window, "_content");
        public Label Status => Private<Label>(Window, "_status");
        public ComboBox Sort => Private<ComboBox>(Window, "_sort");
        public Button Retry => Private<Button>(Window, "_retry");
        public Button AnalysisButton => Private<Button>(Window, "_analysisButton");
        public Button SendTier => Private<Button>(Window, "_sendTier");
        public Button SendAnalysis => Private<Button>(Window, "_sendAnalysis");
        public ToolStripItem SendAll => Private<ToolStripItem>(Window, "_sendAll");
        public ToolStripItem SendTeam => Private<ToolStripItem>(Window, "_sendTeam");
        public ToolStripItem SendBench => Private<ToolStripItem>(Window, "_sendBench");
        public ContextMenuStrip SendMenu => Private<ContextMenuStrip>(Window, "_sendMenu");
        public Control Analysis => Private<Control>(Window, "_analysis");
        public IDictionary Ranks => (IDictionary)Private<object>(Window, "_ranks");

        private System.Windows.Forms.Timer MasteryTimer => Private<System.Windows.Forms.Timer>(Window, "_masteryTimer");

        public Exception? RankError
        {
            get => (Exception?)_source.GetType().GetField("Error")!.GetValue(_source);
            set => _source.GetType().GetField("Error")!.SetValue(_source, value);
        }

        public ChampionMasteryService Mastery
        {
            get => _mastery;
            set
            {
                _mastery = value;
                Set(Window, "_masteryService", value);
            }
        }

        public void SetRanks(params (int Id, string Tier, double? Win, double? Pick)[] rows)
        {
            var map = (IDictionary)Activator.CreateInstance(RankMapType)!;
            foreach (var row in rows)
            {
                map[row.Id] = Activator.CreateInstance(TierType, row.Tier, row.Win, row.Pick)!;
            }

            Ranks.Clear();
            foreach (DictionaryEntry pair in map)
            {
                Ranks[pair.Key] = pair.Value;
            }

            _source
                .GetType()
                .GetField("Value")!
                .SetValue(_source, Activator.CreateInstance(RankTupleType, map, "16.17"));
        }

        public void Open()
        {
            Window.Show();
            Application.DoEvents();
            Check(!Window.IsDisposed, "The roster window stays open");
        }

        public void Update() => Invoke(Window, "UpdateRoster");

        /// <summary>Asks the first card to read its mastery again, as the window's timer does.</summary>
        public void FirstRefresh() => Private<List<Func<Task>>>(Window, "_masteryRefresh")[0]();

        public void TickMasteryTimer() =>
            typeof(System.Windows.Forms.Timer).GetMethod("OnTick", Flags)!.Invoke(MasteryTimer, [EventArgs.Empty]);

        public void CancelCards()
        {
            var painting = (CancellationTokenSource?)Window.GetType().GetField("_painting", Flags)!.GetValue(Window);
            if (painting != null)
            {
                _ = painting.CancelAsync();
            }
        }

        public bool WaitFor(Func<bool> ready)
        {
            PumpUntil(ready);
            return ready();
        }

        public void Dispose()
        {
            if (!Window.IsDisposed)
            {
                MasteryTimer.Stop();
            }

            Window.Dispose();
            _tips.Dispose();
            foreach (var image in _portraits)
            {
                image.Dispose();
            }

            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, true);
                }
            }
            catch (IOException)
            {
                // A mastery file still being written is left in the temp folder.
            }
        }

        private async Task<bool> AnnounceAsync(string message, CancellationToken token)
        {
            if (FailSend != null)
            {
                return await Task.FromException<bool>(FailSend);
            }

            Sent.Add(message);
            if (SendGate is { } gate)
            {
                await gate.Task.WaitAsync(token);
            }

            return Answer;
        }

        private Task<Image?> Portrait(int id)
        {
            if (PortraitError != null)
            {
                return Task.FromException<Image?>(PortraitError);
            }

            var image = new Bitmap(40, 40);
            _portraits.Add(image);
            return Task.FromResult<Image?>(image);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static void Settle()
    {
        for (int i = 0; i < 20; i++)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static bool Label(Fixture fixture, string text) => Labels(fixture).Any(l => l.Text == text);

    private static Label[] Labels(Fixture fixture) => Children(fixture.Window).OfType<Label>().ToArray();

    private static string ToolTipOf(Fixture fixture, Label label) =>
        Private<ToolTip>(fixture.Window, "_tips").GetToolTip(label) ?? "";

    private static void Click(Control control) =>
        typeof(Control).GetMethod("OnClick", Flags)!.Invoke(control, [EventArgs.Empty]);

    private static void Invoke(object owner, string name, params object[] args) => _ = Call(owner, name, args);

    private static object? Call(object owner, string name, params object[] args) =>
        owner
            .GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(owner, args);

    private static void Set(object owner, string name, object value) =>
        owner.GetType().GetField(name, Flags)!.SetValue(owner, value);

    private static T Private<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, Flags)!.GetValue(owner)!;
}
