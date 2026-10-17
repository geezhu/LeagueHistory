using System.Collections.Concurrent;
using System.Reflection;
using League.Controls;
using League.Models;
using League.States;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The history page's state machine: what it cleans up, which filter and page-size changes reload it, the
/// two-hundred-match ceiling, the forced refresh, the profile icon guard and the calls that arrive from another
/// thread. Everything runs against invented records, so no client and no network is involved.
/// </summary>
internal static class MatchTabPageSurfaceChecks
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly MethodInfo UpdateMatches = typeof(MatchTabPageContent).GetMethod(
        "UpdateMatchesDisplay",
        Any
    )!;

    public static void Run()
    {
        CleanupPaths();
        FilterPaths();
        OutcomeFailurePath();
        PageLimitPaths();
        RefreshPaths();
        ProfilePaths();
        SafeCallPaths();
        ParserCancelPaths();
        CrossThreadPaths();
        Console.WriteLine(
            "PASS: the history page's cleanup, its filter, page-size and outcome reloads, the two-hundred-match "
                + "ceiling, a forced refresh, the profile icon paths, the thread-safe calls, a cancelled parse and "
                + "the cross-thread paths."
        );
    }

    private static void CleanupPaths()
    {
        using var host = HiddenForm(new Size(1200, 800));
        var view = Wired(host, () => Task.FromResult(Records(3)));
        var panel = new MatchListPanel(Info(1));
        view.flowLayoutPanelRight.Controls.Add(panel);
        Field<List<WeakReference>>(view, "_allParsedPanels").Add(new WeakReference(panel));
        Set(view, "_currentMatches", new JArray(new JObject { ["gameId"] = 1L, ["queueId"] = 450 }));
        var tracked = new Bitmap(8, 8);
        Field<List<Image>>(view, "_loadedImages").Add(tracked);
        var avatar = Field<Control>(view, "picChampionId");
        SetAvatar(avatar, new Bitmap(8, 8));

        view.ForceCleanup();
        Check(panel.IsDisposed, "Cleaning up disposes the cards the page parsed");
        Check(Disposed(tracked), "Cleaning up disposes the images the page tracked");
        Check(Field<JArray?>(view, "_currentMatches") == null, "Cleaning up drops the match document");
        Check(view.flowLayoutPanelRight.Controls.Count == 0, "Cleaning up empties the history list");

        Set(view, "_currentMatches", new JArray(new JObject { ["gameId"] = 2L }));
        view.flowLayoutPanelRight.Controls.Add(new MatchListPanel(Info(2)));
        view.Cleanup();
        Check(Field<JArray?>(view, "_currentMatches") == null, "Deep cleaning also drops the document");

        Invoke(view, "DeepCleanJArray", new object?[] { null });
        Invoke(view, "DeepCleanJObject", new object?[] { null });
        Check(view.flowLayoutPanelRight.Controls.Count == 0, "An absent document is skipped without complaint");

        view.Dispose();
        view.Dispose();
        Check(Field<bool>(view, "_disposed"), "Disposing the page twice keeps the first result");
        host.Close();
    }

    private static void FilterPaths()
    {
        using var host = HiddenForm(new Size(1200, 800));
        int loads = 0;
        var view = Wired(
            host,
            () =>
            {
                loads++;
                return Task.FromResult(Records(3));
            }
        );
        Set(view, "_puuid", Player);
        try
        {
            view.comboPage.SelectedItem = 20;
            PumpUntil(() => loads > 0);
            Check(Field<int>(view, "_pageSize") == 20, "Choosing a page size loads that many matches");
            Check(
                Scope(view).StartsWith("第 1 页", StringComparison.Ordinal),
                $"A loaded page says which: {Scope(view)}"
            );

            view.comboFilter.SelectedIndex = 1;
            PumpUntil(() => view.CurrentFilter == "q_420" && Field<int>(view, "_pageSize") == 7);
            Check(view.CurrentFilter == "q_420", "Choosing a queue filters the history");
            Check(Field<int>(view, "_pageSize") == 7, "Choosing a queue restores the default page size");

            var outcome = Field<ComboBox>(view, "_outcomeFilter");
            Set(view, "_puuid", string.Empty);
            outcome.SelectedIndex = 1;
            Check(Field<bool?>(view, "_selectedWin") == true, "The outcome filter remembers a win-only choice");
            outcome.SelectedIndex = 0;
            Check(Field<bool?>(view, "_selectedWin") == null, "Clearing the outcome filter shows every result");
            Set(view, "_puuid", Player);
            int before = loads;
            outcome.SelectedIndex = 2;
            PumpUntil(() => loads > before);
            Check(Field<bool?>(view, "_selectedWin") == false, "The outcome filter remembers a loss-only choice");
            PumpUntil(() => Scope(view).Contains("已检查", StringComparison.Ordinal));
            Check(
                Scope(view).Contains("已检查", StringComparison.Ordinal),
                $"A filtered page reports its scan: {Scope(view)}"
            );

            FilterTexts(view);
        }
        finally
        {
            view.Dispose();
            host.Close();
        }
    }

    /// <summary>The display name behind a queue value, including the fallback used when the list has no selection.</summary>
    private static void FilterTexts(MatchTabPageContent view)
    {
        view.comboFilter.SelectedItem = null;
        Check(
            (string)Invoke(view, "GetSelectedFilterText")! == "全部",
            "Without a queue the filter line says everything"
        );
        foreach (
            var (value, expected) in new[]
            {
                ("q_420", "单双排位"),
                ("q_440", "灵活排位"),
                ("q_430", "匹配模式"),
                ("q_450", "极地大乱斗"),
                ("q_2400", "海克斯大乱斗"),
                ("q_1700", "斗魂竞技场"),
                ("q_999", "全部"),
            }
        )
        {
            Set(view, "_selectedQueueId", value);
            Check(
                (string)Invoke(view, "GetSelectedFilterText")! == expected,
                $"The queue {value} is named in the filter line"
            );
        }
    }

    /// <summary>An outcome filter that cannot reload the page reports it instead of unwinding into the message loop.</summary>
    private static void OutcomeFailurePath()
    {
        using var host = HiddenForm(new Size(900, 600));
        var view = Wired(host, () => Task.FromResult(Records(1)));
        try
        {
            Set(view, "_puuid", Player);
            Field<SemaphoreSlim>(view, "_loadSemaphore").Dispose();
            Field<ComboBox>(view, "_outcomeFilter").SelectedIndex = 1;
            Drain();
            Check(Field<bool?>(view, "_selectedWin") == true, "A filter that cannot reload still records the choice");
        }
        finally
        {
            view.Dispose();
            host.Close();
        }
    }

    private static void PageLimitPaths()
    {
        using var host = HiddenForm(new Size(1200, 800));
        var view = Wired(host, () => Task.FromResult(Records(3)));
        Set(view, "_puuid", Player);
        try
        {
            Set(view, "_pageSize", 60);
            Set(view, "_currentPage", 4);
            Await((Task)UpdateMatches.Invoke(view, null)!);
            Check(
                Field<int>(view, "_pageSize") == 60 && view.CurrentPage == 4,
                "A page that reaches past the ceiling is still loaded"
            );

            Set(view, "_currentPage", 5);
            Await((Task)UpdateMatches.Invoke(view, null)!);
            Check(Says(view, "已到达API接口查询上限"), "Past the ceiling the page says there is nothing more");
            Check(!view.btnNext.Enabled, "Past the ceiling the page cannot go further");
        }
        finally
        {
            view.Dispose();
            host.Close();
        }
    }

    private static void RefreshPaths()
    {
        using var host = HiddenForm(new Size(1200, 800));
        var view = Wired(host, () => Task.FromResult(Records(3)));
        try
        {
            Await(view.SafeInitializeAsync(string.Empty));
            Check(Field<string?>(view, "_puuid") == null, "An empty player is refused before anything loads");

            Await(view.SafeInitializeAsync(Player, "summoner-7"));
            Check(view.CurrentPage == 1, "A player's page starts on the first page");
            Check(Field<string>(view, "_summonerId") == "summoner-7", "The player's summoner id is remembered");

            view.Initialize(Player);
            PumpUntil(() => view.flowLayoutPanelRight.Controls.Count > 0);
            Check(view.flowLayoutPanelRight.Controls.Count > 0, "Opening the page for a player starts its first load");

            Await(view.ForceRefreshData("other-player"));
            Check(view.CurrentPage == 1, "A forced refresh for another player is ignored");

            Set(view, "_currentMatches", new JArray(new JObject { ["gameId"] = 5L }));
            view.flowLayoutPanelRight.Controls.Add(new MatchListPanel(Info(5)));
            Await(view.ForceRefreshData(Player));
            Check(view.CurrentPage == 1, "A forced refresh reloads the first page");
            Check(Field<JArray?>(view, "_currentMatches") == null, "A forced refresh drops the old document");
            Await(view.ForceRefreshData(Player));
            Check(RefreshState.ForceMatchRefresh == false, "A forced refresh clears the global refresh request");

            Set(view, "_lastRefreshTime", DateTime.MinValue);
            Set(view, "_isRefreshing", true);
            Await(view.ForceRefreshData(Player));
            Set(view, "_isRefreshing", false);
            Check(RefreshState.ForceMatchRefresh == false, "A refresh already running is not started again");

            Set(view, "_lastRefreshTime", DateTime.MinValue);
            var crossThread = Task.Run(() => (Task)Invoke(view, "ForceRefreshData", Player)!);
            PumpUntil(() => crossThread.IsCompleted);
            Await(crossThread);
            Check(RefreshState.ForceMatchRefresh == false, "A forced refresh from another thread still completes");

            Set(view, "_lastRefreshTime", DateTime.MinValue);
            Field<SemaphoreSlim>(view, "_loadSemaphore").Dispose();
            Await(view.ForceRefreshData(Player));
            Check(RefreshState.ForceMatchRefresh == false, "A refresh that cannot load is reported, not thrown");
        }
        finally
        {
            view.Dispose();
            host.Close();
        }
    }

    /// <summary>The profile header: the rank update, the icon it shows and the icon id it remembers.</summary>
    private static void ProfilePaths()
    {
        using var host = HiddenForm(new Size(1200, 800));
        var view = Wired(host, () => Task.FromResult(Records(1)));
        var avatar = Field<Control>(view, "picChampionId");
        var cache =
            (ConcurrentDictionary<string, Image>)
                typeof(Profileicon)
                    .GetField("_imageCache", BindingFlags.Static | BindingFlags.NonPublic)!
                    .GetValue(null)!;
        var broken = new Bitmap(8, 8);
        broken.Dispose();
        cache["profileicon/990001"] = broken;
        cache["profileicon/990002"] = new Bitmap(12, 12);
        try
        {
            Await((Task)Invoke(view, "UpdateProfileIcon", 0)!);
            Check(Avatar(avatar) == null, "A picture for an unusable id is never looked for");

            Await((Task)Invoke(view, "UpdateProfileIcon", 990001)!);
            Check(Avatar(avatar) == null, "A picture that cannot be copied leaves the avatar alone");

            var previous = new Bitmap(8, 8);
            SetAvatar(avatar, previous);
            Await((Task)Invoke(view, "UpdateProfileIcon", 990002)!);
            Check(Avatar(avatar) != null, "A usable picture becomes the avatar");
            Check((string?)avatar.Tag == "990002", "The avatar remembers which picture it shows");
            Check(Disposed(previous), "The picture the avatar replaced is disposed");

            Check(
                (int)Invoke(view, "GetCurrentProfileIconId")! == 990002,
                "The page can tell which picture the avatar already shows"
            );
            Await(
                (Task)
                    Invoke(
                        view,
                        "InitiaRank",
                        new object?[]
                        {
                            "检查用玩家#0001",
                            "0",
                            "1",
                            "公开",
                            new Dictionary<string, RankedStats>(),
                            true,
                        }
                    )!
            );
            Check(
                Field<Control>(view, "lblLevel").Text.Contains("1", StringComparison.Ordinal),
                "A profile update writes the player's level"
            );

            var brokenStats = new Dictionary<string, RankedStats> { ["单双排"] = null! };
            Await(
                (Task)
                    Invoke(
                        view,
                        "InitiaRank",
                        new object?[] { "检查用玩家#0001", "0", "1", "隐藏", brokenStats, false }
                    )!
            );
            Check(
                Field<Control>(view, "lblPrivacy").Text.Contains("隐藏", StringComparison.Ordinal),
                "A profile update that cannot be completed is reported, not thrown"
            );
        }
        finally
        {
            cache.TryRemove("profileicon/990001", out _);
            cache.TryRemove("profileicon/990002", out _);
            view.Dispose();
            host.Close();
        }
    }

    /// <summary>The page's thread-safe call helpers: absent and disposed targets, and calls that unwind.</summary>
    private static void SafeCallPaths()
    {
        using var host = HiddenForm(new Size(900, 600));
        var view = Wired(host, () => Task.FromResult(Records(1)));
        try
        {
            int ran = 0;
            var action = (Action)(() => ran++);
            Safe(view, null, action);
            var disposed = new Panel();
            disposed.Dispose();
            Safe(view, disposed, action);
            Check(ran == 0, "A call for an absent or disposed control runs nothing");
            Safe(view, view.flowLayoutPanelRight, action);
            Check(ran == 1, "A call for a live control runs once");

            Check(
                Swallowed(view, view.flowLayoutPanelRight),
                "A control that goes away during its own call is swallowed"
            );
            Check(Swallowed(view, null), "A page that goes away during its own call is swallowed");
        }
        finally
        {
            view.Dispose();
            host.Close();
        }
    }

    private static void Safe(Control view, Control? target, Action action) =>
        InvokeTyped(view, "SafeInvoke", new[] { typeof(Control), typeof(Action) }, target, action);

    private static bool Swallowed(Control view, Control? target)
    {
        try
        {
            if (target is null)
            {
                InvokeTyped(
                    view,
                    "SafeInvoke",
                    new[] { typeof(Action) },
                    (Action)(() => throw new ObjectDisposedException("检查用控件"))
                );
            }
            else
            {
                Safe(view, target, () => throw new ObjectDisposedException("检查用控件"));
            }
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static void ParserCancelPaths()
    {
        using var host = HiddenForm(new Size(1200, 800));
        var view = Wired(host, () => Task.FromResult(Records(2)));
        var matches = Records(2);
        try
        {
            using (var already = new CancellationTokenSource())
            {
                _ = already.CancelAsync();
                var work = (Task)Invoke(view, "ParsePanelsAsync", matches, already.Token)!;
                Await(work);
                Check(
                    Field<List<WeakReference>>(view, "_allParsedPanels").Count == 0,
                    "A parse that was cancelled before it started returns nothing"
                );
            }

            view.ParsePanelRequested += (_, _, _) => Task.FromCanceled<Panel>(new CancellationToken(canceled: true));
            var stopped = (Task)Invoke(view, "ParsePanelsAsync", matches, CancellationToken.None)!;
            Await(stopped);
            Check(
                Field<List<WeakReference>>(view, "_allParsedPanels").Count == 0,
                "A parse that is cancelled halfway stops collecting panels"
            );
        }
        finally
        {
            view.Dispose();
            host.Close();
        }
    }

    private static void CrossThreadPaths()
    {
        using var host = HiddenForm(new Size(1200, 800));
        var view = Wired(host, () => Task.FromResult(Records(2)));
        try
        {
            var matches = Records(2);
            var background = Task.Run(() => (Task)Invoke(view, "UpdateMatchList", matches)!);
            PumpUntil(() => background.IsCompleted);
            Await(background);
            Check(
                view.flowLayoutPanelRight.Controls.OfType<MatchListPanel>().Count() == 2,
                "A list update that arrives from another thread still fills the page"
            );

            var status = Task.Run(() => Invoke(view, "ShowStatusMessage", "检查用提示", true));
            PumpUntil(() => status.IsCompleted);
            Check(Says(view, "检查用提示"), "A status line from another thread still reaches the page");

            var panel = view.flowLayoutPanelRight;
            var safe = Task.Run(() =>
                InvokeTyped(
                    view,
                    "SafeInvoke",
                    new[] { typeof(Control), typeof(Action) },
                    panel,
                    (Action)(() => panel.AccessibleName = "检查用线程安全调用")
                )
            );
            PumpUntil(() => safe.IsCompleted);
            Check(panel.AccessibleName == "检查用线程安全调用", "A call from another thread reaches the control");

            var ranking = Task.Run(() =>
                Invoke(
                    view,
                    "InitiaRank",
                    new object?[] { "检查用玩家#0001", "0", "1", "公开", new Dictionary<string, RankedStats>(), false }
                )
            );
            PumpUntil(() => ranking.IsCompleted);
            Await((Task)ranking.Result!);
            Check(
                Field<Control>(view, "lblPrivacy").Text.Contains("公开", StringComparison.Ordinal),
                "A profile update from another thread is marshalled onto the page"
            );
        }
        finally
        {
            view.Dispose();
            host.Close();
        }
    }

    private static MatchTabPageContent Wired(Control host, Func<Task<JArray>> load)
    {
        var view = new MatchTabPageContent { Dock = DockStyle.Fill };
        host.Controls.Add(view);
        host.Show();
        view.LoadDataRequested += (_, _, _, _) => load();
        view.ParsePanelRequested += (_, _, index) => Task.FromResult<Panel>(new MatchListPanel(Info(index)));
        return view;
    }

    private static string Player => "self-puuid-0001";

    private static MatchInfo Info(int index) =>
        new()
        {
            GameId = 7412345670L + index,
            ResultText = "胜利",
            Mode = "极地大乱斗",
            QueueId = "q_450",
            AllParticipants = [new JObject { ["teamId"] = 100 }],
        };

    private static JArray Records(int count) =>
        new(
            Enumerable
                .Range(1, count)
                .Select(index =>
                    (JToken)
                        new JObject
                        {
                            ["gameId"] = 7412345670L + index,
                            ["queueId"] = 450,
                            ["win"] = true,
                            ["participants"] = new JArray(
                                new JObject
                                {
                                    ["puuid"] = Player,
                                    ["summonerId"] = 7,
                                    ["teamId"] = 100,
                                    ["championId"] = 1,
                                }
                            ),
                        }
                )
        );

    private static Image? Avatar(Control avatar) =>
        (Image?)avatar.GetType().GetProperty("Image", Any)!.GetValue(avatar);

    private static void SetAvatar(Control avatar, Image image) =>
        avatar.GetType().GetProperty("Image", Any)!.SetValue(avatar, image);

    private static string Scope(Control view) => Field<Label>(view, "_filterScope").Text;

    private static bool Says(Control view, string text) =>
        Children(view).OfType<Label>().Any(label => label.Text.Contains(text, StringComparison.Ordinal));

    private static bool Disposed(Image image)
    {
        try
        {
            _ = image.Width;
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void Await(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void Drain()
    {
        for (int i = 0; i < 40; i++)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static FieldInfo Field(object target, string name) =>
        target.GetType().GetField(name, Any) ?? throw new InvalidOperationException($"Missing field {name}.");

    private static T Field<T>(object target, string name) => (T)Field(target, name).GetValue(target)!;

    private static void Set(object target, string name, object? value) => Field(target, name).SetValue(target, value);

    private static object? Invoke(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, Any)!.Invoke(target, args);

    private static object? InvokeTyped(object target, string method, Type[] types, params object?[] args) =>
        target.GetType().GetMethod(method, Any, null, types, null)!.Invoke(target, args);
}
