using System.Net;
using System.Reflection;
using System.Text;
using League;
using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The champion-select assistant against a hand-built champion-select session and stub clients: the poll that opens,
/// switches and retires a selection, the mayhem overview's own fallbacks, the rune application notice and the
/// placement helpers. Every request is answered by a handler over the checked-in fixtures, so no client, network or
/// chat is involved; the main form's own chat sender is cleared and its global hotkeys are released before anything
/// else runs.
/// </summary>
internal static class AssistantSessionChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Statics = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Type Assistant = typeof(FormMain).Assembly.GetType("League.ChampionSelectAssistant")!;
    private static readonly FieldInfo LcuHttpClient = typeof(LcuClient).GetField("_httpClient", Flags)!;
    private static readonly FieldInfo LcuSessionClient = typeof(LcuSession).GetField("_lcuClient", Flags)!;
    private static readonly string[] Rarities = ["kPrismatic", "kGold", "kSilver"];

    public static void Run()
    {
        using var owner = new FormMain();
        // The form arms F9/F11/Ctrl+F7 as global Windows hotkeys; release them before driving the assistant.
        using var hotkeys = (IDisposable)typeof(FormMain).GetField("_hotkeyManager", Flags)!.GetValue(owner)!;
        SetOwner(owner, "_chatSender", null);
        SetOwner(owner, "_appConfig", new LeagueConfig());
        var league = new FakeLeague();
        var previous = FormMain.Globals.lcuClient;
        FormMain.Globals.lcuClient = StubSession(league);
        try
        {
            var meta = CreateMetaFixtures();
            var opgg = new FixtureTransport(OpggFixtures());
            using var assistant = Create(owner);
            StubClients(assistant, meta, opgg);
            PollTransitions(assistant, owner, league, meta);
            MayhemFallbacks(assistant, meta);
            ApplyNotices(assistant, league);
            CheckLoadFailure(assistant, opgg);
            PollFailures(assistant, league);
            PlacementHelpers(assistant);
            Retire(owner, assistant, league);
        }
        finally
        {
            FormMain.Globals.lcuClient = previous;
        }
        Console.WriteLine(
            "PASS: assistant champion-select transitions, mayhem overview fallbacks, rune notices and placement "
                + "helpers against stub clients. No live client, network, chat or input."
        );
    }

    // ---------------------------------------------------------------- assistants

    private static Form Create(FormMain owner)
    {
        var assistant = (Form)Activator.CreateInstance(Assistant, new object[] { owner })!;
        Field<System.Windows.Forms.Timer>(assistant, "_timer").Stop();
        // Shown off screen so the buttons behave normally; placement never moves it while it is dragged.
        assistant.StartPosition = FormStartPosition.Manual;
        assistant.Location = new Point(-30000, -30000);
        assistant.Show();
        Application.DoEvents();
        // Without this the client search would attach the panel to a real client on the developer's machine.
        Set(assistant, "_nextClientSearch", long.MaxValue);
        return assistant;
    }

    private static void StubClients(Form assistant, MetaFixtures meta, FixtureTransport opgg)
    {
        Replace(assistant, "_am", new AramMetaClient(new FixtureTransport(meta.Pick)));
        Replace(assistant, "_opgg", new OpggClient(opgg));
    }

    /// <summary>The panel is only clickable while it is shown; it stays off screen and never takes focus.</summary>
    private static void ShowOffScreen(Form assistant)
    {
        assistant.Location = new Point(-30000, -30000);
        if (!assistant.Visible)
        {
            assistant.Show();
            Application.DoEvents();
        }
    }

    /// <summary>The follow ticks hide a panel whose client handle is missing, so show it again before clicking.</summary>
    private static void ClickApply(Form assistant)
    {
        ShowOffScreen(assistant);
        Field<Button>(assistant, "_apply").PerformClick();
    }

    private static void Replace(Form assistant, string name, IDisposable client)
    {
        Field<IDisposable>(assistant, name).Dispose();
        Set(assistant, name, client);
    }

    private static LcuSession StubSession(FakeLeague league)
    {
        var client = new LcuClient("1", "test-only");
        client.HttpClient.Dispose();
        LcuHttpClient.SetValue(client, new HttpClient(league) { BaseAddress = new Uri("https://127.0.0.1/") });
        var session = new LcuSession();
        LcuSessionClient.SetValue(session, client);
        return session;
    }

    // ---------------------------------------------------------------- the poll

    private static void PollTransitions(Form assistant, FormMain owner, FakeLeague league, MetaFixtures meta)
    {
        Set(assistant, "_closing", true);
        int idle = league.Paths.Count;
        Poll(assistant);
        Check(league.Paths.Count == idle, "A closing assistant stops polling the client.");
        Set(assistant, "_closing", false);

        SetOwner(owner, "_appConfig", null);
        Poll(assistant);
        Check(!Field<bool>(assistant, "_selecting"), "A disabled assistant leaves champion select.");
        SetOwner(owner, "_appConfig", new LeagueConfig());

        var session = FormMain.Globals.lcuClient;
        FormMain.Globals.lcuClient = null!;
        Poll(assistant);
        Check(!Field<bool>(assistant, "_selecting"), "A client that is not connected retires the assistant.");
        FormMain.Globals.lcuClient = session;

        Set(assistant, "_busy", true);
        idle = league.Paths.Count;
        Poll(assistant);
        Check(league.Paths.Count == idle, "A poll that is still running is never started twice.");
        Set(assistant, "_busy", false);

        league.Phase = "InProgress";
        Poll(assistant);
        Check(!Field<bool>(assistant, "_selecting"), "Leaving champion select retires the assistant.");

        league.Phase = "ChampSelect";
        PollSelections(assistant, league, meta);
    }

    /// <summary>The champion-select sessions: nothing picked, a pick, a ranked pick and losing the pick again.</summary>
    private static void PollSelections(Form assistant, FakeLeague league, MetaFixtures meta)
    {
        league.Session = Session(0, 0, "middle");
        Poll(assistant);
        Check(
            Field<Label>(assistant, "_status").Text == "等待选择英雄…" && Field<int>(assistant, "_hero") == 0,
            "An empty champion-select session waits for a hero instead of loading recommendations."
        );

        league.Session = Session(0, 103, "middle");
        league.Flow = Flow(2400);
        Poll(assistant);
        PumpUntil(() => Status(assistant).StartsWith("arammeta", StringComparison.Ordinal));
        Check(
            Field<int>(assistant, "_hero") == 103
                && Field<int>(assistant, "_queue") == 2400
                && Field<bool>(assistant, "_selecting"),
            "The pick action names the hero and Mayhem's queue when our own cell is still empty."
        );
        PumpUntil(() => Children(Host(assistant)).OfType<PictureBox>().Any(p => (string?)p.Tag == "mayhem-augment"));
        CheckAugmentRows(assistant, meta);

        league.Flow = Flow(420);
        league.Session = Session(103, 0, "jungle");
        Poll(assistant);
        PumpUntil(() => Status(assistant).StartsWith("OP.GG", StringComparison.Ordinal));
        Check(
            Status(assistant) == "OP.GG · 版本 16.17" && Field<object>(assistant, "_runes") != null,
            "A ranked selection loads the fixture build with a rune scheme to apply: " + Status(assistant)
        );
        Check(
            Field<string?>(assistant, "_position") == "mid",
            "A ranked lane without rows falls back to the champion's own position."
        );

        league.Session = Session(0, 0, "jungle");
        Poll(assistant);
        Check(
            Field<int>(assistant, "_hero") == 0
                && Field<object?>(assistant, "_runes") == null
                && Field<FlowLayoutPanel>(assistant, "_body").Controls.Count == 0
                && Status(assistant) == "等待选择英雄…",
            "Losing the pick clears the previous hero's recommendations and scheme."
        );
    }

    private static void Poll(Form assistant)
    {
        var poll = (Task)Assistant.GetMethod("PollChampionSessionAsync", Flags)!.Invoke(assistant, null)!;
        PumpUntil(() => poll.IsCompleted);
        Check(poll.IsCompletedSuccessfully, "The assistant poll finishes without a fault.");
    }

    private static void PollFailures(Form assistant, FakeLeague league)
    {
        league.Offline = true;
        league.Phase = "ChampSelect";
        Set(assistant, "_lastSessionRead", Environment.TickCount64 - 20000);
        Poll(assistant);
        Poll(assistant);
        Check(Field<bool>(assistant, "_selecting"), "A briefly unreachable client keeps the assistant on screen.");
        Poll(assistant);
        Check(
            !Field<bool>(assistant, "_selecting") && Field<int>(assistant, "_pollFailures") >= 3,
            "A client that stays unreachable for three polls retires the selection."
        );
        league.Offline = false;
    }

    // ---------------------------------------------------------------- mayhem overview

    private static void MayhemFallbacks(Form assistant, MetaFixtures meta)
    {
        ShowOffScreen(assistant);
        var page = new AramMetaPage(EmptyIndex(), new JObject(), true, DateTime.UtcNow);
        Call(assistant, "ClearBody");
        Call(assistant, "AddMayhemRecommendations", page, 103, CancellationToken.None);
        Application.DoEvents();
        var host = Host(assistant);
        Check(
            Rows(host)
                .Take(Rarities.Length)
                .All(row => row.Controls.Count == 1 && row.Controls[0] is Label { Text: "暂无推荐" }),
            "A hero pool without augments keeps every rarity row labelled instead of blank."
        );
        Check(Labels(host).Any(l => l.Text == "暂无出装推荐"), "A hero without item rows says there is no build.");
        Check(Labels(host).Count(l => l.Text == "暂无数据") == 2, "Spell cards without data say so.");

        var offline = new FixtureTransport(_ => "") { Fail = true };
        Replace(assistant, "_am", new AramMetaClient(offline));
        Call(assistant, "ClearBody");
        Call(assistant, "AddMayhemRecommendations", page with { Detail = null }, 103, CancellationToken.None);
        PumpUntil(() => Retry(assistant) != null);
        Check(
            Labels(Host(assistant)).Any(l => l.Text == "暂无推荐") && IsRetry(Retry(assistant)!),
            "A failed hero pool falls back to the overview's own rows and offers a retry."
        );
        int attempts = offline.Calls;
        ShowOffScreen(assistant);
        Retry(assistant)!.PerformClick();
        PumpUntil(() => offline.Calls > attempts);
        Check(
            Children(Host(assistant)).OfType<Button>().Count(IsRetry) == 1,
            "Retrying a failed hero pool asks the source again and replaces the notice instead of stacking one."
        );
        Replace(assistant, "_am", new AramMetaClient(new FixtureTransport(meta.Pick)));
    }

    private static void CheckAugmentRows(Form assistant, MetaFixtures meta)
    {
        var page = meta.Page;
        var tiles = Children(Host(assistant))
            .OfType<PictureBox>()
            .Where(p => (string?)p.Tag == "mayhem-augment")
            .GroupBy(p => p.Parent!)
            .ToArray();
        int capacity = AugmentCapacity();
        bool complete =
            tiles.Length == Rarities.Length
            && Rarities
                .Select(key => Math.Min(capacity, AramMetaRecommendation.Read(page, 103, key).Length))
                .All(expected => tiles.Any(group => group.Count() == expected));
        Check(
            complete,
            "Each rarity row shows the hero pool's best augments up to the row capacity: "
                + string.Join(",", tiles.Select(g => g.Count()))
        );
        Check(
            Children(Host(assistant)).OfType<Button>().Any(b => b.Text == "配置出装"),
            "The loaded hero pool offers the equipment editor action."
        );
    }

    private static AramMetaIndex EmptyIndex() =>
        new(
            "16.17",
            "test",
            new[] { new AramMetaChampion(103, "Ahri", "", [], .53, .52, 100, new JObject()) },
            new JObject(),
            new JObject()
        );

    private static Button? Retry(Form assistant) => Children(Host(assistant)).OfType<Button>().FirstOrDefault(IsRetry);

    private static bool IsRetry(Button button) => button.Text.StartsWith("推荐加载失败", StringComparison.Ordinal);

    private static FlowLayoutPanel Host(Form assistant) =>
        Field<FlowLayoutPanel>(assistant, "_body").Controls.OfType<FlowLayoutPanel>().Last();

    private static FlowLayoutPanel[] Rows(FlowLayoutPanel host) => host.Controls.OfType<FlowLayoutPanel>().ToArray();

    private static Label[] Labels(Control host) => Children(host).OfType<Label>().ToArray();

    // ---------------------------------------------------------------- applying runes

    private static void ApplyNotices(Form assistant, FakeLeague league)
    {
        ShowOffScreen(assistant);
        league.Phase = "ChampSelect";
        league.Flow = Flow(2400);
        league.Session = Session(103, 0, "middle");
        Poll(assistant);
        PumpUntil(() => Status(assistant).StartsWith("arammeta", StringComparison.Ordinal));
        ClickApply(assistant);
        PumpUntil(() => Status(assistant).StartsWith("配置提示", StringComparison.Ordinal));
        Check(
            league.Chat.Count == 1 && league.Chat[0].Contains("【海克斯大乱斗】", StringComparison.Ordinal),
            "The mayhem action announces the configuration in champion select: " + Status(assistant)
        );
        Check(
            Status(assistant) == "配置提示已发送。",
            "Mayhem reports the announced configuration: " + Status(assistant)
        );

        league.Flow = Flow(420);
        league.Session = Session(103, 0, "mid");
        Poll(assistant);
        PumpUntil(() => Status(assistant).StartsWith("OP.GG", StringComparison.Ordinal));
        Set(
            assistant,
            "_runes",
            new OpggRunes(8000, 8100, Enumerable.Range(1, 9).ToArray(), 100, .55) { PickRate = .4 }
        );
        ClickApply(assistant);
        PumpUntil(() => Status(assistant) == "符文方案 1 已应用。");
        Check(
            league.Chat.Count == 2 && league.Chat[1].Contains("【召唤师峡谷】", StringComparison.Ordinal),
            "Applying runes announces the map, champion and rates in champion select: " + Status(assistant)
        );
        Check(
            league.Chat[1].Contains("符文已配置成功——数据来自 OP.GG", StringComparison.Ordinal)
                && league.Chat[1].Contains("使用率", StringComparison.Ordinal),
            "The announcement carries the source and the rates it applied: " + league.Chat[1]
        );
        Check(
            (league.RunePage?["selectedPerkIds"] as JArray)?.Values<int>().SequenceEqual(Enumerable.Range(1, 9))
                == true,
            "The applied page is the scheme the assistant showed."
        );

        Set(assistant, "_runes", null);
        int applied = league.Chat.Count;
        var idle = (Task)Call(assistant, "ApplyAsync")!;
        PumpUntil(() => idle.IsCompleted);
        Check(league.Chat.Count == applied, "Applying without a loaded scheme sends nothing.");

        league.Session = Session(157, 0, "mid");
        Set(assistant, "_runes", new OpggRunes(8000, 8100, Enumerable.Range(1, 9).ToArray(), 100, .55));
        var changed = (Task)Call(assistant, "ApplyAsync")!;
        PumpUntil(() => changed.IsCompleted);
        Check(
            Status(assistant) == "英雄已变化，请等待推荐更新。",
            "Applying runes to a champion that changed under the panel is refused: " + Status(assistant)
        );

        league.Session = Session(103, 0, "mid");
        CheckAnnounceFailure(assistant, league);
    }

    private static void CheckAnnounceFailure(Form assistant, FakeLeague league)
    {
        league.Rooms = "[]";
        var roomless = Announce(assistant, 2);
        PumpUntil(() => roomless.IsCompleted);
        Check(
            roomless.IsCompletedSuccessfully && Status(assistant) == "方案 2 已应用，聊天提示未发送。",
            "Without a champion-select room the applied runes are still reported: " + Status(assistant)
        );

        league.Rooms = FakeLeague.ChampionSelectRoom;
        league.Offline = true;
        var offline = Announce(assistant, 3);
        PumpUntil(() => offline.IsCompleted);
        Check(
            offline.IsCompletedSuccessfully && Status(assistant) == "方案 3 已应用，聊天提示未发送。",
            "A chat that cannot be reached still reports the applied runes: " + Status(assistant)
        );
        league.Offline = false;
    }

    private static Task Announce(Form assistant, int choice) =>
        (Task)
            Assistant
                .GetMethod("AnnounceAppliedRunesAsync", Flags)!
                .Invoke(
                    assistant,
                    new object[]
                    {
                        FormMain.Globals.lcuClient!.Client!,
                        new OpggRunes(8000, 8100, Enumerable.Range(1, 9).ToArray(), 100, null),
                        "Ahri",
                        "电刑",
                        "极地大乱斗",
                        choice,
                        CancellationToken.None,
                    }
                )!;

    // ---------------------------------------------------------------- failed loads

    private static void CheckLoadFailure(Form assistant, FixtureTransport opgg)
    {
        ShowOffScreen(assistant);
        CheckMapNames(assistant);
        Check(
            Reason(new HttpRequestException("fixture", null, HttpStatusCode.NotFound)) == "推荐站点暂时无法访问",
            "A refused guide names the site, not the exception."
        );
        Check(
            Reason(new TimeoutException()) == "推荐加载超时"
                && Reason(new OperationCanceledException()) == "推荐加载超时",
            "A retired or slow guide reports a timeout."
        );

        opgg.Fail = true;
        // A fresh client has no cached build, so the next load really asks the unreachable guide.
        Replace(assistant, "_opgg", new OpggClient(opgg));
        // Changing a filter reloads the current hero; the failure is readable and clickable while it lasts.
        Field<ComboBox>(assistant, "_mode").SelectedIndex = 1;
        PumpUntil(() => Status(assistant).EndsWith("点击重试", StringComparison.Ordinal));
        Check(
            Status(assistant) == "网络连接失败，请检查网络 · 点击重试"
                && Field<Label>(assistant, "_status").Cursor == Cursors.Hand,
            "An unreachable guide explains itself in the status bar: " + Status(assistant)
        );
        int attempts = opgg.Calls;
        Click(Field<Label>(assistant, "_status"));
        PumpUntil(() => opgg.Calls > attempts);
        Check(
            Status(assistant).EndsWith("点击重试", StringComparison.Ordinal),
            "Clicking the failure notice retries the same hero and keeps the retry available: " + Status(assistant)
        );
        opgg.Fail = false;

        Set(assistant, "_closing", true);
        attempts = opgg.Calls;
        Call(assistant, "LoadHeroAsync");
        Application.DoEvents();
        Check(opgg.Calls == attempts, "A closing assistant never starts a recommendation load.");
        Set(assistant, "_closing", false);
    }

    /// <summary>The map named in the rune notice, one mode at a time; no load is started for it.</summary>
    private static void CheckMapNames(Form assistant)
    {
        var expected = new[]
        {
            (Index: 1, Name: "召唤师峡谷"),
            (Index: 2, Name: "极地大乱斗"),
            (Index: 3, Name: "海克斯大乱斗"),
            (Index: 4, Name: "斗魂竞技场"),
            (Index: 5, Name: "无限火力"),
            (Index: 6, Name: "极限闪击"),
        };
        var mode = Field<ComboBox>(assistant, "_mode");
        Set(assistant, "_syncing", true);
        try
        {
            foreach (var (index, name) in expected)
            {
                mode.SelectedIndex = index;
                Check(
                    (string)Call(assistant, "CurrentMapName")! == name,
                    "The rune notice names " + name + " in mode " + index + "."
                );
            }
        }
        finally
        {
            mode.SelectedIndex = 0;
            Set(assistant, "_syncing", false);
        }
    }

    private static string Reason(Exception error) =>
        (string)Assistant.GetMethod("LoadFailureReason", Statics)!.Invoke(null, new object[] { error })!;

    private static void Click(Control control) =>
        control.GetType().GetMethod("OnClick", Flags)!.Invoke(control, new object[] { EventArgs.Empty });

    // ---------------------------------------------------------------- placement helpers

    private static void PlacementHelpers(Form assistant)
    {
        using var client = HiddenForm(new Size(320, 200));
        client.Show();
        Application.DoEvents();
        Set(assistant, "_clientWindow", client.Handle);
        Set(assistant, "_selecting", true);
        Set(assistant, "_moving", true);
        assistant.Hide();
        Call(assistant, "PositionWindow");
        Check(
            assistant.Visible && assistant.Left == -30000,
            "A dragged panel is shown where the user put it instead of snapping back to the client."
        );
        Call(assistant, "PositionWindow");
        Check(assistant.Visible, "An already visible panel stays attached without being shown twice.");

        var toggle = Field<Form>(assistant, "_toggle");
        toggle.StartPosition = FormStartPosition.Manual;
        toggle.Location = new Point(-30000, -30000);
        toggle.Show();
        Call(assistant, "PositionWindow");
        Check(!toggle.Visible, "The open panel hides the collapsed edge tab.");

        Set(assistant, "_moving", false);
        Field<CheckBox>(assistant, "_pin").Checked = true;
        int wanted = (int)Call(assistant, "PanelHeight")!;
        Call(assistant, "PositionWindow");
        Check(
            assistant.Left == -30000
                && assistant.Height == Math.Min(wanted, Screen.FromHandle(client.Handle).WorkingArea.Height),
            "A pinned panel follows the client's height without moving: " + assistant.Bounds
        );
        Field<CheckBox>(assistant, "_pin").Checked = false;

        toggle.Show();
        Set(assistant, "_selecting", false);
        Call(assistant, "PositionWindow");
        Check(
            !assistant.Visible && !toggle.Visible,
            "Leaving champion select hides the panel and its collapsed edge tab."
        );

        CheckFitToContent(assistant);
        CheckClientSearch(assistant);
    }

    private static void CheckFitToContent(Form assistant)
    {
        var body = Field<FlowLayoutPanel>(assistant, "_body");
        using var tall = new Panel { Size = new Size(10, Dip(900)) };
        body.Controls.Add(tall);
        var screen = Screen.FromControl(assistant).WorkingArea;
        assistant.Top = screen.Bottom - Dip(60);
        Call(assistant, "FitToContent");
        Check(
            body.AutoScroll && assistant.Bottom <= screen.Bottom,
            $"Content taller than the screen scrolls instead of leaving the work area: {assistant.Bounds} in {screen}"
        );
        body.Controls.Remove(tall);
        Call(assistant, "FitToContent");
        Check(!body.AutoScroll, "Content that fits again removes the scrollbar.");
    }

    private static void CheckClientSearch(Form assistant)
    {
        Set(assistant, "_clientWindow", IntPtr.Zero);
        Set(assistant, "_nextClientSearch", 0L);
        var first = (IntPtr)Call(assistant, "FindClientWindow")!;
        var second = (IntPtr)Call(assistant, "FindClientWindow")!;
        Check(first == second, "A missing client is searched at most once per interval, not on every tick.");
        Set(assistant, "_clientWindow", IntPtr.Zero);
        Set(assistant, "_nextClientSearch", long.MaxValue);
    }

    private static void Retire(FormMain owner, Form assistant, FakeLeague league)
    {
        league.Phase = "None";
        var tick = (Task)Call(assistant, "TickAsync")!;
        PumpUntil(() => tick.IsCompleted);
        Check(
            Field<int>(assistant, "_hero") == 0 && Field<string>(assistant, "_key") == "",
            "The follow tick retires the hero once the client leaves champion select."
        );

        using var second = (Form)Activator.CreateInstance(Assistant, new object[] { owner })!;
        Field<System.Windows.Forms.Timer>(second, "_timer").Stop();
        var pending = new TaskCompletionSource();
        Call(second, "TrackAssistantWork", pending.Task);
        var faulted = Task.FromException(new InvalidOperationException("fixture"));
        Check(
            ReferenceEquals(Call(second, "TrackAssistantWork", faulted), faulted),
            "The assistant tracks the work it is given."
        );
        second.Dispose();
        pending.SetException(new InvalidOperationException("fixture"));
        var cleanup = Field<Task>(second, "_cleanupWork");
        PumpUntil(() => cleanup.IsCompleted);
        Check(
            cleanup.IsCompletedSuccessfully,
            "Closing the assistant observes failed work and still releases its clients."
        );
    }

    // ---------------------------------------------------------------- fixtures and stubs

    private static MetaFixtures CreateMetaFixtures()
    {
        var index = Fixture("AramMeta", "index");
        var augments = Fixture("AramMeta", "103-augments");
        var detail = Fixture("AramMeta", "103");
        index["augs"] = augments["augs"]!.DeepClone();
        detail["poolAugments"] = augments["detail"]!["poolAugments"]!.DeepClone();
        return new MetaFixtures { Index = index, Detail = detail };
    }

    private static Func<string, string> OpggFixtures()
    {
        var ranked = Fixture("Opgg", "ranked").ToString();
        var tiers = Fixture("Opgg", "tiers").ToString();
        return path => path.Contains("/champions/ranked/", StringComparison.Ordinal) ? ranked : tiers;
    }

    private static JObject Fixture(string folder, string name) =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", folder, name + ".json")));

    private static string Flow(int queue) =>
        $"{{\"gameData\":{{\"queue\":{{\"id\":{queue},\"gameMode\":\"CLASSIC\"}}}}}}";

    private static string Session(int own, int pick, string position) =>
        "{\"localPlayerCellId\":0,\"myTeam\":[{\"cellId\":0,\"championId\":"
        + own
        + ",\"assignedPosition\":\""
        + position
        + "\"}],\"actions\":"
        + (pick > 0 ? $"[[{{\"actorCellId\":0,\"type\":\"pick\",\"championId\":{pick},\"completed\":true}}]]" : "[]")
        + ",\"benchEnabled\":true,\"benchChampions\":[{\"championId\":134}]}";

    private sealed class MetaFixtures
    {
        public JObject Index { get; init; } = new();
        public JObject Detail { get; init; } = new();

        public AramMetaPage Page => new(AramMetaIndex.Parse(Index), (JObject)Detail.DeepClone(), true, DateTime.UtcNow);

        /// <summary>The tier list answers the index request; the hero detail answers every champion request.</summary>
        public string Pick(string path) =>
            path.EndsWith("tier-list.json", StringComparison.Ordinal) ? Index.ToString() : Detail.ToString();
    }

    /// <summary>A transport over a fixed answer, so a recommendation client never reaches the network.</summary>
    private sealed class FixtureTransport(Func<string, string> pick) : HttpMessageHandler
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            if (Fail)
            {
                throw new HttpRequestException("offline");
            }

            return Task.FromResult(Json(pick(request.RequestUri!.AbsolutePath)));
        }
    }

    /// <summary>The League client: gameflow, champion select, perks and the champion-select chat room.</summary>
    private sealed class FakeLeague : HttpMessageHandler
    {
        public const string ChampionSelectRoom =
            "[{\"id\":\"private\",\"type\":\"chat\"},{\"id\":\"room\",\"type\":\"championSelect\"}]";

        public string Phase { get; set; } = "ChampSelect";
        public string Flow { get; set; } = AssistantSessionChecks.Flow(2400);
        public string Session { get; set; } = AssistantSessionChecks.Session(103, 0, "middle");
        public string Rooms { get; set; } = ChampionSelectRoom;
        public bool Offline { get; set; }
        public JObject? RunePage { get; private set; }
        public List<string> Chat { get; } = [];
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (Offline)
            {
                throw new HttpRequestException("offline");
            }

            if (path == "/lol-gameflow/v1/gameflow-phase")
            {
                return Json("\"" + Phase + "\"");
            }

            if (path == "/lol-gameflow/v1/session")
            {
                return Json(Flow);
            }

            if (path == "/lol-champ-select/v1/session")
            {
                return Json(Session);
            }

            if (path == "/lol-perks/v1/pages")
            {
                return await PerksAsync(request, token);
            }

            if (path == "/lol-perks/v1/currentpage")
            {
                return Json(RunePage?.ToString() ?? "{}");
            }

            if (path.EndsWith("/conversations", StringComparison.Ordinal))
            {
                return Json(Rooms);
            }

            if (path.EndsWith("/messages", StringComparison.Ordinal))
            {
                Chat.Add((string?)JObject.Parse(await request.Content!.ReadAsStringAsync(token))["body"] ?? "");
                return Json("{}");
            }

            return Json("{}");
        }

        private async Task<HttpResponseMessage> PerksAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method != HttpMethod.Post)
            {
                return Json("[]");
            }

            var page = JObject.Parse(await request.Content!.ReadAsStringAsync(token));
            page["id"] = 77;
            RunePage = page;
            return Json(page.ToString());
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // ---------------------------------------------------------------- reflection helpers

    private static T Field<T>(object target, string name) => (T)Assistant.GetField(name, Flags)!.GetValue(target)!;

    private static void Set(object target, string name, object? value) =>
        Assistant.GetField(name, Flags)!.SetValue(target, value);

    private static void SetOwner(FormMain owner, string name, object? value) =>
        typeof(FormMain).GetField(name, Flags)!.SetValue(owner, value);

    private static object? Call(object target, string name, params object?[] arguments) =>
        Assistant.GetMethod(name, Flags)!.Invoke(target, arguments);

    private static string Status(Form assistant) => Field<Label>(assistant, "_status").Text;

    private static int Dip(int value) =>
        (int)Assistant.GetMethod("Dip", Statics, new[] { typeof(int) })!.Invoke(null, new object[] { value })!;

    private static int AugmentCapacity()
    {
        int content = (int)Assistant.GetProperty("ContentWidth", Statics)!.GetValue(null)!,
            tile = Dip(44),
            gap = Dip(3);
        return Math.Max(1, (content + gap) / (tile + gap));
    }
}
