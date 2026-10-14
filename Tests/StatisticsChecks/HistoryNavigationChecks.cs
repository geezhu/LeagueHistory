using System.Reflection;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;

internal static class HistoryNavigationChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        ProfileLayout();
        DetailReplacement();
        DetailReturnAndClose();
        OutcomeCacheBoundaries();
        OutcomeRequestRetirement();
        OutcomeLastPage();
        CleanupWithoutWaiting();
        CancelledUpdateReleasesPanels();
        Console.WriteLine(
            "PASS: history profile layout, detail switching, return/close, player/queue cache boundaries, stale outcome requests and cleanup without UI waits."
        );
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, PrivateInstance | BindingFlags.Public)!.GetValue(target)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, PrivateInstance)!.SetValue(target, value);

    private static object? Invoke(object target, string method, params object?[] arguments) =>
        target.GetType().GetMethod(method, PrivateInstance)!.Invoke(target, arguments);

    private static void PumpUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready())
        {
            Application.DoEvents();
            Thread.Sleep(1);
            Check(DateTime.UtcNow < deadline, "History navigation simulation timed out.");
        }
    }

    private static Form Host(Control view)
    {
        var host = new Form
        {
            ClientSize = new Size(1100, 700),
            ShowInTaskbar = false,
            Location = new Point(-30000, -30000),
            StartPosition = FormStartPosition.Manual,
        };
        host.Controls.Add(view);
        host.Show();
        return host;
    }

    private static MatchInfo Match(long id) =>
        new()
        {
            GameId = id,
            AllParticipants = new() { new JObject { ["teamId"] = 100 } },
        };

    private static MatchListPanel AddCard(MatchTabPageContent view, MatchInfo match)
    {
        var card = new MatchListPanel(match);
        Field<FlowLayoutPanel>(view, "flowLayoutPanelRight").Controls.Add(card);
        return card;
    }

    private static Task OpenDetail(MatchTabPageContent view, MatchInfo match) =>
        (Task)Invoke(view, "OpenInlineMatchAsync", match)!;

    private static void ConfigurePreparation(MatchTabPageContent view, Func<MatchDetailView, Task> prepare)
    {
        SetField(
            view,
            "_prepareInlineDetail",
            (Func<MatchDetailView, Task>)(
                detail =>
                {
                    // Rendering assets belongs to the detail's own checks. This test controls only navigation timing.
                    SetField(detail, "_started", true);
                    return prepare(detail);
                }
            )
        );
    }

    // Cleanup runs on the UI thread that pending parse tasks need for their panels, so it cancels instead of waiting.
    private static void CleanupWithoutWaiting()
    {
        foreach (
            var cleanup in new Action<MatchTabPageContent>[] { view => view.ForceCleanup(), view => view.Cleanup() }
        )
        {
            using Control control = new MatchTabPageContent();
            var view = (MatchTabPageContent)control;
            using var host = Host(view);
            SetField(view, "_currentParsingTask", new TaskCompletionSource().Task);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            cleanup(view);
            Check(
                watch.ElapsedMilliseconds < 300,
                $"Cleanup does not wait for parsing: {watch.ElapsedMilliseconds} ms."
            );
        }
    }

    // An update cancelled while parsing releases the panels it produced instead of showing them.
    private static void CancelledUpdateReleasesPanels()
    {
        using Control control = new MatchTabPageContent();
        var view = (MatchTabPageContent)control;
        using var host = Host(view);
        using var cancellation = new CancellationTokenSource();
        var late = new Panel();
        view.ParsePanelRequested += async (_, _, _) =>
        {
            await Task.Delay(50, cancellation.Token);
            await cancellation.CancelAsync();
            return late;
        };
        var update = (Task)Invoke(view, "UpdateMatchListInternal", new JArray(new JObject()), cancellation.Token)!;
        PumpUntil(() => update.IsCompleted);
        Check(update.Status == TaskStatus.RanToCompletion, "A cancelled update completes without an error.");
        Check(
            late.IsDisposed && !Field<FlowLayoutPanel>(view, "flowLayoutPanelRight").Controls.Contains(late),
            "A panel parsed after cancellation is released, not shown."
        );
    }

    private static void ProfileLayout()
    {
        using Control view = new MatchTabPageContent();
        using var host = Host(view);
        var history = Field<Panel>(view, "_historySurface");
        var identity = Field<Panel>(view, "_profileBackground");
        Check(history.Dock == DockStyle.Right && history.Width == 300, "Profile history remains on the right.");
        var name = Field<LinkLabel>(view, "linkGameName");
        name.Text = "很长的玩家名字用于验证复制按钮的布局#12345";
        var copy = identity.Controls.Cast<Control>().Single(control => control.AccessibleName == "复制 Riot ID");
        foreach (int width in new[] { 1100, 700 })
        {
            host.ClientSize = new Size(width, 700);
            view.PerformLayout();
            Check(
                copy.Left > name.Left && copy.Right <= identity.ClientSize.Width,
                "Copy action stays beside the truncated identity."
            );
            Check(Field<Panel>(view, "_profileSurface").Visible, "Resizing preserves the player profile.");
        }
        Check(Field<ComboBox>(view, "_outcomeFilter").Items.Count == 3, "All three result filters remain available.");
        Check(Field<Button>(view, "btnPrev").AccessibleName == "上一页", "Paging arrows retain accessible names.");
        host.ClientSize = new Size(1100, 700);
        Field<Panel>(view, "_profileSurface").AutoScrollPosition = Point.Empty;
        view.PerformLayout();
        using var bitmap = new Bitmap(view.Width, view.Height);
        view.DrawToBitmap(bitmap, view.ClientRectangle);
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, "history-navigation-quality.png"));
    }

    private static void DetailReplacement()
    {
        using Control control = new MatchTabPageContent();
        var view = (MatchTabPageContent)control;
        using var host = Host(view);
        using var first = Match(1);
        using var second = Match(2);
        var firstCard = AddCard(view, first);
        var secondCard = AddCard(view, second);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        ConfigurePreparation(view, _ => ++calls == 1 ? waiting.Task : Task.CompletedTask);
        var retiredWork = OpenDetail(view, first);
        var retiredDetail = Field<MatchDetailView>(view, "_pendingDetail");
        OpenDetail(view, second).GetAwaiter().GetResult();
        var current = Field<MatchDetailView>(view, "_detailView");
        Check(
            retiredDetail.IsDisposed && secondCard.SelectedMatch && !firstCard.SelectedMatch,
            "Only the replacement match becomes selected."
        );
        Check(
            Field<Panel>(view, "_historySurface").Dock == DockStyle.Left
                && !Field<Panel>(view, "_profileSurface").Visible,
            "A prepared detail uses the left history rail."
        );
        waiting.SetResult();
        PumpUntil(() => retiredWork.IsCompleted);
        retiredWork.GetAwaiter().GetResult();
        Check(
            ReferenceEquals(current, Field<MatchDetailView>(view, "_detailView")),
            "Late preparation cannot replace the selected match."
        );
        int replayCalls = 0;
        secondCard.ReplayClicked += _ => replayCalls++;
        // Field-like events are invoked here to validate routing without launching a replay.
        ((Action)typeof(MatchDetailView).GetField("ReplayRequested", PrivateInstance)!.GetValue(current)!)();
        Check(replayCalls == 1, "Replay routes to the selected history card.");
        Field<FlowLayoutPanel>(view, "flowLayoutPanelRight").Controls.Remove(secondCard);
        Check(
            current.IsDisposed && Field<Panel>(view, "_profileSurface").Visible,
            "Removing the selected card returns to the profile."
        );
        secondCard.Dispose();
    }

    private static void DetailReturnAndClose()
    {
        using Control control = new MatchTabPageContent();
        var view = (MatchTabPageContent)control;
        using var host = Host(view);
        using var match = Match(3);
        var card = AddCard(view, match);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfigurePreparation(view, _ => waiting.Task);
        var work = OpenDetail(view, match);
        var pending = Field<MatchDetailView>(view, "_pendingDetail");
        Invoke(view, "ReturnToProfile");
        Check(pending.IsDisposed && !card.SelectedMatch, "Returning during preparation retires the hidden detail.");
        waiting.SetResult();
        PumpUntil(() => work.IsCompleted);
        work.GetAwaiter().GetResult();
        Check(
            Field<MatchDetailView?>(view, "_detailView") == null,
            "Retired preparation cannot reopen details after return."
        );
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfigurePreparation(view, _ => closing.Task);
        work = OpenDetail(view, match);
        pending = Field<MatchDetailView>(view, "_pendingDetail");
        // Also cover the existing upstream cleanup entry, which closes just the history flow.
        Field<FlowLayoutPanel>(view, "flowLayoutPanelRight").Dispose();
        Check(
            Field<bool>(view, "_navigationClosing") && pending.IsDisposed,
            "Closing the history flow retires owned detail state."
        );
        closing.SetResult();
        PumpUntil(() => work.IsCompleted);
        work.GetAwaiter().GetResult();
        Check(Field<MatchDetailView?>(view, "_detailView") == null, "Closing prevents late detail publication.");
    }

    private static void SelectOutcome(
        MatchTabPageContent view,
        string player,
        string queue = "",
        bool win = true,
        int page = 1
    )
    {
        SetField(view, "_puuid", player);
        SetField(view, "_selectedQueueId", queue);
        SetField(view, "_selectedWin", win);
        SetField(view, "_currentPage", page);
        SetField(view, "_pageSize", 2);
    }

    private static JArray Records(string player, int start, int count) =>
        new(
            Enumerable
                .Range(start, count)
                .Select(id => new JObject
                {
                    ["gameId"] = id,
                    ["participants"] = new JArray(
                        new JObject
                        {
                            ["puuid"] = player,
                            ["stats"] = new JObject { ["win"] = true },
                        }
                    ),
                })
        );

    private static Task<JArray> OutcomePage(MatchTabPageContent view) => (Task<JArray>)Invoke(view, "LoadOutcomePage")!;

    private static void OutcomeCacheBoundaries()
    {
        using Control control = new MatchTabPageContent();
        var view = (MatchTabPageContent)control;
        var calls = new List<(string Player, int Offset, string Queue)>();
        view.LoadDataRequested += (player, offset, count, queue) =>
        {
            calls.Add((player, offset, queue));
            return Task.FromResult(Records(player, offset, count));
        };
        SelectOutcome(view, "first");
        Check(OutcomePage(view).GetAwaiter().GetResult().Count == 2, "Initial filter fills only the requested page.");
        OutcomePage(view).GetAwaiter().GetResult();
        Check(calls.Count == 1, "Repeated pages reuse the current player's scan.");
        SelectOutcome(view, "first", page: 2);
        OutcomePage(view).GetAwaiter().GetResult();
        Check(calls[^1].Offset == 2, "The next page resumes from the cached scan offset.");
        SelectOutcome(view, "second");
        var next = OutcomePage(view).GetAwaiter().GetResult();
        Check(calls[^1] == ("second", 0, "") && next.Count == 2, "Changing players starts a separate scan at zero.");
        SelectOutcome(view, "second", "q_450");
        OutcomePage(view).GetAwaiter().GetResult();
        Check(calls[^1] == ("second", 0, "q_450"), "Changing queues starts a separate cache.");
        Check(Field<Label>(view, "_filterScope").Text.Contains("找到 2 场"), "Scope describes the current scan only.");
    }

    private static void OutcomeRequestRetirement()
    {
        foreach (bool closing in new[] { false, true })
        {
            using Control control = new MatchTabPageContent();
            var view = (MatchTabPageContent)control;
            var pending = new TaskCompletionSource<JArray>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            view.LoadDataRequested += (_, _, _, _) =>
            {
                calls++;
                return pending.Task;
            };
            SelectOutcome(view, "old");
            var work = OutcomePage(view);
            if (closing)
            {
                control.Dispose();
            }
            else
            {
                SelectOutcome(view, "new", "q_450");
            }
            pending.SetResult(Records("old", 0, 1));
            PumpUntil(() => work.IsCompleted);
            Check(
                work.IsCanceled && calls == 1,
                "A retired response must stop scanning instead of requesting more old records."
            );
            Check(
                Field<OutcomeHistoryPager>(view, "_outcomePager").Scanned == 0,
                "A retired response cannot enter the cached record set."
            );
            if (!closing)
            {
                Check(
                    !Field<Label>(view, "_filterScope").Text.StartsWith("已检查"),
                    "An old request cannot publish scope for the new selection."
                );
            }
        }
    }

    private static void OutcomeLastPage()
    {
        using Control control = new MatchTabPageContent();
        var view = (MatchTabPageContent)control;
        int calls = 0;
        view.LoadDataRequested += (player, _, _, _) =>
            Task.FromResult(++calls == 1 ? Records(player, 0, 3) : new JArray());
        SelectOutcome(view, "self", page: 5);
        var result = OutcomePage(view).GetAwaiter().GetResult();
        Check(
            result.Count == 1 && Field<int>(view, "_currentPage") == 2 && view.TotalPages == 2,
            "Exhausted scans clamp the requested page to the available last page."
        );
        Check(
            Field<Label>(view, "_filterScope").Text.Contains("第 2 页") && calls == 2,
            "The clamped scope preserves counts and avoids extra fetches."
        );
    }
}
