using System.Net;
using System.Reflection;
using League;
using League.Clients;
using League.Controls;
using League.Infrastructure;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The OP.GG page beyond its contract checks: the navigation buttons, the champion-select polling driven by a
/// stubbed client, the arena and empty-data layouts, the guide's own error handling and its retired resources.
/// Every transport is stubbed, so no op.gg request and no League client is involved.
/// </summary>
internal static class OpggViewSurfaceChecks
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type ViewType = typeof(PlayerCardControl).Assembly.GetType("League.Controls.OpggView")!;

    private static readonly Func<int, string, Task<(Image? image, string name, string description)>> NoIcons = (
        id,
        kind
    ) => Task.FromResult<(Image?, string, string)>((null, $"{kind} {id}", ""));

    private static readonly Func<int, string, Task<(Image? image, string name, string description)>> Icons = (
        id,
        kind
    ) => Task.FromResult<(Image?, string, string)>((new Bitmap(16, 16), $"{kind} {id}", "检查用说明"));

    public static void Run()
    {
        RankingsPage();
        ChampionBuilds();
        ArenaBuild();
        EmptySections();
        Following();
        Polling();
        FailureStatus();
        ClosingGuards();
        Retirement();
        Console.WriteLine(
            "PASS: the OP.GG page's navigation, ranking rows, arena and empty layouts, follow-selection polling, "
                + "error handling and retired resources, all against stubbed transports."
        );
    }

    private static void RankingsPage()
    {
        using var handler = new Handler((request, _) => Task.FromResult(Reply(request)));
        using var client = new OpggClient(handler);
        using var host = HiddenForm(new Size(1100, 700));
        var view = CreateView(client, NoIcons);
        host.Controls.Add(view);
        host.Show();
        StopTimers(view);
        try
        {
            Await((Task)Invoke(view, "OpenQueryAsync", Query("global", "ranked", "emerald_plus", "mid", 0))!);
            Check(Says(view, "点击英雄查看攻略"), "Without a champion the ranking table is offered instead of a guide");
            Check(Field<Control>(view, "_tiers").Visible, "The ranking table is the visible page");

            Field<Action<OpggTier>>(Field<Control>(view, "_tiers"), "ChampionRequested")
                .Invoke(new OpggTier(103, "MID", 0.5, 0.02, 0.01, 1, 1));
            Settle(view);
            Check(Field<int>(view, "_championId") == 103, "Choosing a ranking row opens that champion's guide");
            Check(Value(view, "_position") == "mid", "A ranked row also selects the lane it was listed under");
            Check(Says(view, "OP.GG"), "The guide for the chosen row is loaded");

            Click(Field<Control>(view, "_back"));
            Settle(view);
            Check(Field<int>(view, "_championId") == 0, "Going back returns to the ranking table");
            Click(Field<Control>(view, "_forward"));
            Settle(view);
            Check(Field<int>(view, "_championId") == 103, "Going forward returns to the champion");
            Click(Children(view).OfType<Button>().First(button => button.Text == "刷新数据"));
            Settle(view);
            Check(
                Says(view, "OP.GG"),
                $"Refreshing keeps the page's status line: {Field<Label>(view, "_status").Text}"
            );

            Set(view, "_closing", true);
            Field<Action<OpggTier>>(Field<Control>(view, "_tiers"), "ChampionRequested")
                .Invoke(new OpggTier(1, "MID", 0.5, 0.02, 0.01, 1, 1));
            Check(Field<int>(view, "_championId") == 103, "A closing page ignores a ranking row");
            Set(view, "_closing", false);
            NavigationOverflow(view);
        }
        finally
        {
            Close(view);
        }
    }

    /// <summary>The visit list is capped, so browsing far enough must drop its oldest entry.</summary>
    private static void NavigationOverflow(Control view)
    {
        for (int champion = 1; champion <= 105; champion++)
        {
            Set(view, "_championId", champion);
            Invoke(view, "RecordNavigation");
        }
        var navigation = Field<List<OpggQuery>>(view, "_navigation");
        Check(navigation.Count == 100, $"The visit list keeps its last hundred entries: {navigation.Count}");
        Check(navigation[^1].ChampionId == 105, "The newest visit is the one that was just recorded");
    }

    /// <summary>The four corners of the resize helper, including the table it can be given.</summary>
    private static void ResizeSections(Control view)
    {
        var section = new Panel { Width = 400, Height = 200 };
        var table = new TableLayoutPanel { Width = 100, Height = 60 };
        var flow = new FlowLayoutPanel { Width = 100, Height = 60 };
        var label = new Label { Text = "检查用标签", AutoSize = true };
        section.Controls.AddRange(new Control[] { table, flow, label });
        Invoke(view, "ResizeSection", section);
        Check(table.Width == section.Width - 24, "A table inside a section follows the page's width");
        Check(
            flow.MaximumSize.Width == section.Width - 24 && label.MaximumSize.Width == section.Width - 24,
            "A row and a label inside a section both follow the page's width"
        );
        section.Dispose();
    }

    private static void ChampionBuilds()
    {
        using var handler = new Handler((request, _) => Task.FromResult(Reply(request)));
        using var client = new OpggClient(handler);
        using var host = HiddenForm(new Size(1100, 700));
        var view = CreateView(client, Icons);
        host.Controls.Add(view);
        host.Show();
        StopTimers(view);
        try
        {
            Await((Task)Invoke(view, "OpenQueryAsync", Query("global", "aram", "all", "none", 103))!);
            Check(Says(view, "推荐符文") && Says(view, "对局分析"), "A guide renders its rune and matchup sections");
            Check(
                Field<OpggBuild?>(view, "_displayed")?.Query.ChampionId == 103,
                "The rendered guide is the one the page reports"
            );

            Await((Task)Invoke(view, "OpenChampionAsync", 25, 450, "bottom")!);
            Check(Field<int>(view, "_championId") == 25, "A champion opened from elsewhere becomes the current one");
            Check(!Field<Control>(view, "_position").Visible, "A mode without lanes hides the lane selector");

            Tick(Field<System.Windows.Forms.Timer>(view, "_timer"));
            Tick(Field<System.Windows.Forms.Timer>(view, "_filterTimer"));
            Settle(view);
            Check(Says(view, "OP.GG"), "A settled filter change reloads the page");
            ResizeSections(view);
        }
        finally
        {
            Close(view);
        }
    }

    private static void ArenaBuild()
    {
        using var handler = new Handler((request, _) => Task.FromResult(Reply(request)));
        using var client = new OpggClient(handler);
        using var host = HiddenForm(new Size(1100, 700));
        var view = CreateView(client, Icons);
        host.Controls.Add(view);
        host.Show();
        StopTimers(view);
        try
        {
            var work = (Task)Invoke(view, "OpenQueryAsync", Query("global", "arena", "all", "none", 103))!;
            Await(work);
            Check(work.IsCompletedSuccessfully, "The arena guide loads");
            Check(!Field<Control>(view, "_tier").Visible, "Arena hides the rank selector");
            Check(
                Says(view, "竞技场搭档") && Says(view, "竞技场强化符文"),
                "The arena guide adds partners and augments"
            );
            Check(Says(view, "平均名次"), "The arena summary counts placements instead of a win rate alone");
        }
        finally
        {
            Close(view);
        }
    }

    private static void EmptySections()
    {
        using var handler = new Handler((request, _) => Task.FromResult(Reply(request)));
        using var client = new OpggClient(handler);
        using var host = HiddenForm(new Size(1100, 700));
        var view = CreateView(client, NoIcons);
        host.Controls.Add(view);
        host.Show();
        StopTimers(view);
        try
        {
            var data = Fixture("ranked");
            data["data"]!["core_items"] = new JArray();
            data["data"]!["runes"] = new JArray();
            data["data"]!["skills"] = new JArray(new JObject());
            foreach (var counter in (data["data"]!["counters"] as JArray ?? new()).OfType<JObject>())
            {
                counter["win"] = counter["play"];
            }

            var build = OpggBuild.Parse(data, Query("global", "ranked", "emerald_plus", "mid", 103));
            Check(build.Runes.Length == 0, "The emptied fixture really carries no rune schemes");
            Await((Task)Invoke(view, "RenderAsync", build, CancellationToken.None)!);
            Check(Says(view, "此模式暂无符文方案"), "A guide without rune schemes says so");
            Check(Says(view, "暂无数据"), "An empty equipment list says so");
            Check(Says(view, "暂无符合条件的对局"), "A matchup group without rows says so");

            var rich = OpggBuild.Parse(Fixture("ranked"), Query("global", "ranked", "emerald_plus", "mid", 103));
            Await((Task)Invoke(view, "RenderAsync", rich, CancellationToken.None)!);
            RuneCallbacks(view, rich);
        }
        finally
        {
            Close(view);
        }
    }

    /// <summary>The page hands three closures to its rune component; the check calls them the way it would.</summary>
    private static void RuneCallbacks(Control view, OpggBuild build)
    {
        Set(view, "_displayed", build);
        var recommendations = Children(Field<Control>(view, "_content"))
            .First(control => control.GetType().Name == "OpggRuneRecommendations");
        var allowed = Field<Func<bool>>(recommendations, "_canApply");
        Check(allowed(), "The displayed guide authorizes applying its runes");
        var report = Field<Action<string>>(recommendations, "_showError");
        report("检查用符文提示");
        Check(Field<Label>(view, "_status").Text == "检查用符文提示", "A rune preview reports through the page");

        Set(view, "_displayed", null);
        Check(!allowed(), "A guide the page no longer displays may not apply its runes");
        Set(view, "_displayed", build);
        ApplyRunes(recommendations, build.Runes[0]);
    }

    private static void ApplyRunes(Control recommendations, OpggRunes plan)
    {
        var apply = Field<Func<OpggRunes, CancellationToken, Task>>(recommendations, "_applyPlan");
        var previous = FormMain.Globals.lcuClient;
        JObject? written = null;
        using var handler = new Handler(
            async (request, token) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path == "/lol-perks/v1/pages" && request.Method == HttpMethod.Post)
                {
                    written = JObject.Parse(await request.Content!.ReadAsStringAsync(token));
                    written["id"] = 12;
                    return Json(written);
                }
                if (path == "/lol-perks/v1/currentpage")
                {
                    return Json(written ?? new JObject());
                }
                return Json(new JArray());
            }
        );
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://127.0.0.1:1/") };
        try
        {
            var session = new LcuSession();
            var lcu = new LcuClient("1", "stub");
            var replaced = lcu.HttpClient;
            typeof(LcuClient).GetField("_httpClient", Any)!.SetValue(lcu, http);
            typeof(LcuSession).GetField("_lcuClient", Any)!.SetValue(session, lcu);
            replaced.Dispose();
            FormMain.Globals.lcuClient = session;
            Await(apply(plan, CancellationToken.None));
            Check(written != null, "Applying a rune scheme writes it to the client's own page");
        }
        finally
        {
            FormMain.Globals.lcuClient = previous;
            http.Dispose();
        }
    }

    private static void Following()
    {
        using var handler = new Handler((request, _) => Task.FromResult(Reply(request)));
        using var client = new OpggClient(handler);
        using var host = HiddenForm(new Size(1100, 700));
        var view = CreateView(client, NoIcons);
        host.Controls.Add(view);
        host.Show();
        StopTimers(view);
        try
        {
            var started = (Task)Invoke(view, "OpenQueryAsync", Query("global", "ranked", "emerald_plus", "mid", 999))!;
            Field<CheckBox>(view, "_follow").Checked = true;
            Await(started);
            Check(Field<int>(view, "_championId") == 999, "A champion absent from the table still opens its guide");
            Check(Field<string>(view, "_assignedPosition") == "mid", "Without table data the assigned lane is kept");

            var known = (Task)Invoke(view, "OpenQueryAsync", Query("global", "ranked", "emerald_plus", "mid", 103))!;
            Field<CheckBox>(view, "_follow").Checked = true;
            Await(known);
            var navigation = Field<List<OpggQuery>>(view, "_navigation");
            Check(Field<int>(view, "_championId") == 103, "A champion the table knows opens too");
            Check(
                navigation[^1].Position == "mid",
                "The followed visit records the lane the champion was listed under"
            );

            ResolvedLane(view);
            Invoke(view, "FollowHero", 103, 420, "support");
            Settle(view);
            Check(Value(view, "_position") == "support", "Following a support pick selects that lane");
            Check(Value(view, "_mode") == "ranked", "A ranked queue follows into ranked statistics");
            Invoke(view, "FollowHero", 103, 2400, "bottom");
            Settle(view);
            Check(Value(view, "_mode") == "ranked", "An unknown queue falls back to ranked statistics");
            Invoke(view, "FollowHero", 103, 450, null);
            Settle(view);
            Check(Value(view, "_mode") == "aram", "An ARAM pick swaps modes");
            Check(!Field<Control>(view, "_position").Visible, "An ARAM pick also hides the lane selector");
        }
        finally
        {
            Close(view);
        }
    }

    /// <summary>A lane the table does not list for that champion falls back to the one it plays most.</summary>
    private static void ResolvedLane(Control view)
    {
        Invoke(view, "RecordNavigation");
        Set(view, "_assignedPosition", "adc");
        var rows = new[]
        {
            new OpggTier(103, "TOP", 0.5, 0.2, 0.01, 1, 1),
            new OpggTier(103, "MID", 0.5, 0.05, 0.01, 2, 3),
        };
        int before = Field<List<OpggQuery>>(view, "_navigation").Count;
        Field<CheckBox>(view, "_follow").Checked = true;
        var resolved = (OpggQuery)
            Invoke(view, "ResolveFollowingPosition", Query("global", "ranked", "all", "adc", 103), rows)!;
        var navigation = Field<List<OpggQuery>>(view, "_navigation");
        Check(resolved.Position == "top", "An unavailable lane falls back to the champion's most played one");
        Check(Value(view, "_position") == "top", "The lane selector shows the lane that was resolved");
        Check(
            navigation.Count == before && navigation[^1].Position == "top",
            "The resolved lane corrects the visit instead of adding another one"
        );
        Field<CheckBox>(view, "_follow").Checked = false;
    }

    private static void Polling()
    {
        using var host = HiddenForm(new Size(1000, 700));
        using var client = new OpggClient(new Handler((request, _) => Task.FromResult(Reply(request))));
        using var stub = new SelectionStub();
        var view = CreateView(client, NoIcons);
        Set(view, "_selectionClient", (Func<HttpClient?>)(() => stub.Client));
        host.Controls.Add(view);
        host.Show();
        StopTimers(view);
        Field<System.Windows.Forms.Timer>(view, "_timer").Stop();
        try
        {
            Check(view.Visible, "A shown page polls for the champion selection");

            Poll(view, stub.Set(SelectionStub.Case.NotFound), "a session that is not there");
            Check(Field<string>(view, "_selection").Length == 0, "A refused session forgets the last pick");
            Poll(view, stub.Set(SelectionStub.Case.BrokenJson), "an unreadable answer");
            Poll(view, stub.Set(SelectionStub.Case.Disconnected), "a disconnected client");
            Poll(view, stub.Set(SelectionStub.Case.Cancelled), "a request that runs out of time");
            Poll(view, stub.Pick(103, "mid", completed: false), "a pick that is not finished");
            Check(
                Field<int>(view, "_championId") == 0,
                $"An unfinished pick opens nothing: champion={Field<int>(view, "_championId")}"
            );
            Poll(view, stub.Pick(0, "mid", completed: true), "a cell without a champion");
            Check(Field<int>(view, "_championId") == 0, "A cell without a champion opens nothing");

            Poll(view, stub.Pick(103, "utility", completed: true), "a finished support pick");
            Settle(view);
            Check(
                Field<string>(view, "_selection") == "420:103:support",
                "A finished pick is remembered by queue, champion and lane"
            );
            Check(Says(view, "推荐符文"), "Following a pick loads that champion's guide");

            foreach (var lane in new[] { "bottom", "top", "jungle", "fill" })
            {
                Poll(view, stub.Pick(103, lane, completed: true), $"a pick assigned to {lane}");
            }
            Settle(view);
            Check(
                Field<string>(view, "_selection") == "420:103:",
                "A lane the client cannot name still opens the pick"
            );

            Poll(view, stub.Pick(103, "utility", completed: true), "the same pick again");
            Check(Field<int>(view, "_championId") == 103, "The same pick is not opened twice");
            Poll(view, stub.Pick(103, "bottom", completed: true), "a changed lane");
            Check(Field<string>(view, "_selection") == "420:103:adc", "A changed lane counts as a different visit");

            PollGuards(view, stub);
        }
        finally
        {
            Close(view);
        }
    }

    private static void Poll(Control view, SelectionStub stub, string what)
    {
        var task = (Task)Invoke(view, "PollSelectionAsync")!;
        Await(task);
        Check(task.IsCompletedSuccessfully, $"Polling survives {what}");
        Check(!Field<bool>(view, "_polling"), $"Polling {what} leaves no poll running");
    }

    private static void PollGuards(Control view, SelectionStub stub)
    {
        Set(view, "_polling", true);
        Check(((Task)Invoke(view, "PollSelectionAsync")!).IsCompleted, "A poll already running is not started again");
        Set(view, "_polling", false);

        var follow = Field<CheckBox>(view, "_follow");
        follow.Checked = false;
        Check(((Task)Invoke(view, "PollSelectionAsync")!).IsCompleted, "Following switched off stops the polling");
        follow.Checked = true;

        Set(view, "_selectionClient", (Func<HttpClient?>)(() => null));
        Check(((Task)Invoke(view, "PollSelectionAsync")!).IsCompleted, "Without a client there is nothing to poll");
        Set(view, "_selectionClient", (Func<HttpClient?>)(() => stub.Client));

        view.Visible = false;
        Check(((Task)Invoke(view, "PollSelectionAsync")!).IsCompleted, "A hidden page does not poll");
        Check(Says(view, "已暂停加载"), "A hidden page says that it paused");
        view.Visible = true;

        DefaultSelectionClient();
    }

    /// <summary>The page's own client lookup, which must answer "nothing connected" without touching a real client.</summary>
    private static void DefaultSelectionClient()
    {
        using var host = HiddenForm(new Size(900, 600));
        using var client = new OpggClient(new Handler((request, _) => Task.FromResult(Reply(request))));
        var view = (Control)Activator.CreateInstance(ViewType, Any, null, new object[] { client }, null)!;
        Set(view, "_resourceLoader", NoIcons);
        host.Controls.Add(view);
        host.Show();
        StopTimers(view);
        var previous = FormMain.Globals.lcuClient;
        try
        {
            FormMain.Globals.lcuClient = new LcuSession();
            Check(
                Field<Func<HttpClient?>>(view, "_selectionClient")() == null,
                "An unconnected session offers no client"
            );
            Check(
                ((Task)Invoke(view, "PollSelectionAsync")!).IsCompleted,
                "A page without a connected client never starts a poll"
            );
        }
        finally
        {
            FormMain.Globals.lcuClient = previous;
            Close(view);
        }
    }

    private static void FailureStatus()
    {
        CheckStatus(
            new HttpResponseMessage(HttpStatusCode.NotFound),
            "加载失败",
            "A guide the server refuses is explained"
        );
        CheckStatus(null, "请求超时，请重试。", "A request that the transport gives up on is reported as a timeout");
    }

    private static void CheckStatus(HttpResponseMessage? response, string expected, string what)
    {
        using var handler = new Handler(
            (_, _) =>
                response != null
                    ? Task.FromResult(response)
                    : Task.FromCanceled<HttpResponseMessage>(new CancellationToken(canceled: true))
        );
        using var client = new OpggClient(handler);
        using var host = HiddenForm(new Size(900, 600));
        var view = CreateView(client, NoIcons);
        host.Controls.Add(view);
        host.Show();
        StopTimers(view);
        try
        {
            Await((Task)Invoke(view, "OpenQueryAsync", Query("global", "ranked", "all", "mid", 103))!);
            string status = Field<Label>(view, "_status").Text;
            Check(status.Contains(expected, StringComparison.Ordinal), $"{what}: {status}");
        }
        finally
        {
            Close(view);
        }
    }

    private static void ClosingGuards()
    {
        using var handler = new Handler((request, _) => Task.FromResult(Reply(request)));
        using var client = new OpggClient(handler);
        using var host = HiddenForm(new Size(900, 600));
        var view = CreateView(client, NoIcons);
        host.Controls.Add(view);
        host.Show();
        StopTimers(view);
        try
        {
            var build = OpggBuild.Parse(Fixture("ranked"), Query("global", "ranked", "all", "mid", 103));
            Set(view, "_closing", true);
            Check(
                ((Task)Invoke(view, "OpenQueryAsync", Query("global", "ranked", "all", "mid", 103))!).IsCompleted,
                "A closing page starts no query"
            );
            Check(
                ((Task)Invoke(view, "OpenChampionAsync", 103, 420, "mid")!).IsCompleted,
                "A closing page opens no champion"
            );
            Check(((Task)Invoke(view, "LoadAsync", false)!).IsCompleted, "A closing page loads nothing");
            Check(
                ((Task)Invoke(view, "RenderAsync", build, CancellationToken.None)!).IsCompleted,
                "A closing page renders no guide"
            );
            string status = Field<Label>(view, "_status").Text;
            view.Visible = false;
            Check(Field<Label>(view, "_status").Text == status, "A closing page does not pause its status line");
            view.Visible = true;
            Set(view, "_closing", false);
            MidLoadClosing();
        }
        finally
        {
            Close(view);
        }
    }

    /// <summary>Closing while a request is still in flight must drop the answer instead of publishing it.</summary>
    private static void MidLoadClosing()
    {
        var query = Query("global", "ranked", "all", "mid", 103);
        using (var handler = new GatedHandler())
        using (var client = new OpggClient(handler))
        using (var host = HiddenForm(new Size(900, 600)))
        {
            var view = CreateView(client, NoIcons);
            host.Controls.Add(view);
            host.Show();
            StopTimers(view);
            var work = (Task)Invoke(view, "OpenQueryAsync", query)!;
            PumpUntil(() => handler.TierRequests > 0);
            Set(view, "_closing", true);
            handler.Tiers.SetResult();
            PumpUntil(() => work.IsCompleted);
            Check(handler.BuildRequests == 0, "A page closed while ranking loaded never asks for the guide");
            Set(view, "_closing", false);
            Close(view);
            host.Close();
        }

        using (var handler = new GatedHandler())
        using (var client = new OpggClient(handler))
        using (var host = HiddenForm(new Size(900, 600)))
        {
            var view = CreateView(client, NoIcons);
            host.Controls.Add(view);
            host.Show();
            StopTimers(view);
            var work = (Task)Invoke(view, "OpenQueryAsync", query)!;
            PumpUntil(() => handler.TierRequests > 0);
            handler.Tiers.SetResult();
            PumpUntil(() => handler.BuildRequests > 0);
            Set(view, "_closing", true);
            handler.Build.SetResult();
            PumpUntil(() => work.IsCompleted);
            Check(
                Field<OpggBuild?>(view, "_displayed") == null,
                "A page closed while the guide loaded displays nothing"
            );
            Set(view, "_closing", false);
            Close(view);
            host.Close();
        }
    }

    private static void Retirement()
    {
        using var host = HiddenForm(new Size(1000, 700));
        using var client = new OpggClient(new Handler((request, _) => Task.FromResult(Reply(request))));
        var view = CreateView(client, NoIcons);
        host.Controls.Add(view);
        host.Show();
        StopTimers(view);
        try
        {
            LateArtwork(view);
            FaultedArtwork(view);
            FailingRow(view);
            DisposedRow(view);
            DefaultCatalog();
            TrackFaulted(view);
            Field<HashSet<Task>>(view, "_viewWork")
                .Add(Task.FromException(new InvalidOperationException("检查用遗留任务")));
        }
        finally
        {
            Close(view);
        }
    }

    /// <summary>Artwork that arrives after the row that wanted it was cancelled.</summary>
    private static void LateArtwork(Control view)
    {
        var pending = new TaskCompletionSource<(Image?, string, string)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var previous = UseLoader(view, (_, _) => pending.Task);
        using var cancellation = new CancellationTokenSource();
        var row = new Panel();
        try
        {
            var work = (Task)Invoke(view, "AddIconAsync", row, 7, "item", cancellation.Token)!;
            _ = cancellation.CancelAsync();
            Check(Cancelled(work), "Cancelling a row stops waiting for its icon");

            var image = new Bitmap(8, 8);
            pending.SetResult((image, "item 7", ""));
            Settle(view);
            Check(Disposed(image), "An icon that arrives after cancellation is disposed instead of shown");
        }
        finally
        {
            pending.TrySetResult((null, "item 7", ""));
            row.Dispose();
            UseLoader(view, previous);
        }
    }

    /// <summary>A resource read that fails after the render was cancelled is still observed.</summary>
    private static void FaultedArtwork(Control view)
    {
        var pending = new TaskCompletionSource<(Image?, string, string)>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var previous = UseLoader(view, (_, _) => pending.Task);
        using var cancellation = new CancellationTokenSource();
        try
        {
            var work = (Task)Invoke(view, "ReadResourceAsync", 12, "item", cancellation.Token)!;
            _ = cancellation.CancelAsync();
            Check(Cancelled(work), "Cancelling a render stops waiting for its resource");
            pending.SetException(new InvalidOperationException("检查用资源失败"));
            Settle(view);
            Check(true, "A resource failure after cancellation is logged rather than surfaced");
        }
        finally
        {
            pending.TrySetResult((null, "item 12", ""));
            UseLoader(view, previous);
        }
    }

    private static void FailingRow(Control view)
    {
        var previous = UseLoader(view, (_, _) => throw new InvalidOperationException("检查用资源失败"));
        try
        {
            var row = new Panel();
            Await((Task)Invoke(view, "AddIconAsync", row, 42, "item", CancellationToken.None)!);
            Check(
                row.Controls.OfType<Label>().Any(label => label.Text == "item 42"),
                "A resource failure still names the entry it could not draw"
            );
            row.Dispose();
        }
        finally
        {
            UseLoader(view, previous);
        }
    }

    private static void DisposedRow(Control view)
    {
        var previous = UseLoader(view, Icons);
        try
        {
            var row = new Panel();
            row.Dispose();
            Await((Task)Invoke(view, "AddIconAsync", row, 7, "item", CancellationToken.None)!);
            Check(row.Controls.Count == 0, "A row that is already gone accepts no icon");
        }
        finally
        {
            UseLoader(view, previous);
        }
    }

    /// <summary>The page's own resource lookup, which must survive an unloaded catalog.</summary>
    private static void DefaultCatalog()
    {
        var previous = FormMain.Globals.resLoading;
        try
        {
            FormMain.Globals.resLoading = null!;
            var missing = AwaitTuple((Task)InvokeStatic("DefaultResourceAsync", 3, "item")!);
            Check(
                missing.Image == null && missing.Name == "item 3",
                "Without a resource catalog an entry is named but not drawn"
            );
            FormMain.Globals.resLoading = new ResourceLoading();
            var augment = AwaitTuple((Task)InvokeStatic("DefaultResourceAsync", 9, "augment")!);
            Check(augment.Name == "Augment 9", "An unrecognised resource kind is read from the augment catalog");
        }
        finally
        {
            FormMain.Globals.resLoading = previous;
        }
    }

    private static void TrackFaulted(Control view)
    {
        var faulted = Task.FromException(new InvalidOperationException("检查用后台失败"));
        var track = ViewType.GetMethod("TrackWork", Any)!.MakeGenericMethod(typeof(Task));
        track.Invoke(view, new object[] { faulted });
        Check(
            !Field<HashSet<Task>>(view, "_viewWork").Contains(faulted),
            "Background work that fails is observed and dropped"
        );
    }

    private static Func<int, string, Task<(Image?, string, string)>> UseLoader(
        Control view,
        Func<int, string, Task<(Image?, string, string)>> loader
    )
    {
        var previous = Field<Func<int, string, Task<(Image?, string, string)>>>(view, "_resourceLoader");
        Set(view, "_resourceLoader", loader);
        return previous;
    }

    private static bool Cancelled(Task work)
    {
        try
        {
            Await(work);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    private static Control CreateView(OpggClient client, Func<int, string, Task<(Image?, string, string)>> loader)
    {
        var view = (Control)Activator.CreateInstance(ViewType, Any, null, new object[] { client }, null)!;
        Set(view, "_resourceLoader", loader);
        Set(view, "_selectionClient", (Func<HttpClient?>)(() => null));
        return view;
    }

    private static void Close(Control view)
    {
        view.Dispose();
        StatisticsCheckSupport.PumpUntil(() => Field<Task>(view, "_cleanupWork").IsCompleted);
        Check(Field<Task>(view, "_cleanupWork").IsCompletedSuccessfully, "Closing the page owns its cleanup");
    }

    private static void Settle(Control view) =>
        StatisticsCheckSupport.PumpUntil(() => Field<HashSet<Task>>(view, "_viewWork").Count == 0);

    /// <summary>
    /// Both of the page's timers repaint or reload it while the check pumps messages. Stopping them first keeps the
    /// checks about the page's own behaviour instead of about when the next tick happens to land.
    /// </summary>
    private static void StopTimers(Control view)
    {
        Field<System.Windows.Forms.Timer>(view, "_timer").Stop();
        Field<System.Windows.Forms.Timer>(view, "_filterTimer").Stop();
    }

    private static void Click(Control button) =>
        typeof(Control).GetMethod("OnClick", Any)!.Invoke(button, new object[] { EventArgs.Empty });

    private static void Tick(System.Windows.Forms.Timer timer) =>
        typeof(System.Windows.Forms.Timer).GetMethod("OnTick", Any)!.Invoke(timer, new object[] { EventArgs.Empty });

    private static OpggQuery Query(string region, string mode, string tier, string position, int champion) =>
        new(region, mode, tier, position, champion);

    private static string Value(Control view, string box) => (string)InvokeStatic("Value", Field<ComboBox>(view, box))!;

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

    private static JObject Fixture(string name) =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Opgg", name + ".json")));

    /// <summary>Answers a tiers request with the ranking fixture and a guide request with the mode's fixture.</summary>
    private static HttpResponseMessage Reply(HttpRequestMessage request)
    {
        var parts = request.RequestUri!.AbsolutePath.Split('/');
        if (parts.Length == 5)
        {
            return Json(Fixture("tiers"));
        }
        var data = Fixture(
            parts[4] switch
            {
                "arena" => "arena",
                "aram" => "aram",
                "urf" => "urf",
                "nexus_blitz" => "nexus_blitz",
                _ => "ranked",
            }
        );
        data["data"]!["summary"]!["id"] = int.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture);
        return Json(data);
    }

    private static HttpResponseMessage Json(JToken value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value.ToString()) };

    private static (Image? Image, string Name, string Description) AwaitTuple(Task task)
    {
        StatisticsCheckSupport.PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var type = result.GetType();
        return (
            (Image?)type.GetField("Item1")!.GetValue(result),
            (string)type.GetField("Item2")!.GetValue(result)!,
            (string)type.GetField("Item3")!.GetValue(result)!
        );
    }

    private static void Await(Task task)
    {
        StatisticsCheckSupport.PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static FieldInfo Field(object target, string name) =>
        target.GetType().GetField(name, Any) ?? throw new InvalidOperationException($"Missing field {name}.");

    private static T Field<T>(object target, string name) => (T)Field(target, name).GetValue(target)!;

    private static void Set(object target, string name, object? value) => Field(target, name).SetValue(target, value);

    private static object? Invoke(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, Any)!.Invoke(target, args);

    private static object? InvokeStatic(string method, params object?[] args) =>
        ViewType.GetMethod(method, StaticAny)!.Invoke(null, args);

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request, token);
    }

    /// <summary>Holds the first ranking and guide answer back so a closing page can be caught in between.</summary>
    private sealed class GatedHandler : HttpMessageHandler
    {
        public TaskCompletionSource Tiers { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Build { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int TierRequests { get; private set; }

        public int BuildRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            var parts = request.RequestUri!.AbsolutePath.Split('/');
            if (parts.Length == 5)
            {
                TierRequests++;
                await Tiers.Task.ConfigureAwait(false);
                return Json(Fixture("tiers"));
            }
            BuildRequests++;
            await Build.Task.ConfigureAwait(false);
            var data = Fixture("ranked");
            data["data"]!["summary"]!["id"] = 103;
            return Json(data);
        }
    }

    /// <summary>The champion-select session the page polls, with every awkward answer reachable by name.</summary>
    private sealed class SelectionStub : IDisposable
    {
        public enum Case
        {
            Normal,
            NotFound,
            Disconnected,
            BrokenJson,
            Cancelled,
        }

        private JObject _session = Session(103, "mid", true);

        public Case Scenario { get; set; }

        public HttpClient Client { get; }

        public SelectionStub()
        {
            Client = new HttpClient(
                new Handler(
                    (request, _) =>
                    {
                        if (Scenario == Case.Disconnected)
                        {
                            throw new HttpRequestException("检查用断线");
                        }
                        if (Scenario == Case.NotFound)
                        {
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                        }
                        if (Scenario == Case.Cancelled)
                        {
                            return Task.FromCanceled<HttpResponseMessage>(new CancellationToken(canceled: true));
                        }
                        if (Scenario == Case.BrokenJson)
                        {
                            return Task.FromResult(
                                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("不是 JSON") }
                            );
                        }
                        var body =
                            request.RequestUri!.AbsolutePath == "/lol-champ-select/v1/session"
                                ? _session
                                : new JObject
                                {
                                    ["gameData"] = new JObject { ["queue"] = new JObject { ["id"] = 420 } },
                                };
                        return Task.FromResult(Json(body));
                    }
                )
            )
            {
                BaseAddress = new Uri("https://localhost"),
            };
        }

        public SelectionStub Pick(int champion, string lane, bool completed)
        {
            Scenario = Case.Normal;
            _session = Session(champion, lane, completed);
            return this;
        }

        public SelectionStub Set(Case scenario)
        {
            Scenario = scenario;
            return this;
        }

        public void Dispose() => Client.Dispose();

        private static JObject Session(int champion, string lane, bool completed) =>
            new()
            {
                ["localPlayerCellId"] = 0,
                ["myTeam"] = new JArray(
                    new JObject
                    {
                        ["cellId"] = 0,
                        ["championId"] = champion,
                        ["assignedPosition"] = lane,
                    }
                ),
                ["actions"] = new JArray
                {
                    new JArray(
                        new JObject
                        {
                            ["actorCellId"] = 0,
                            ["type"] = "pick",
                            ["completed"] = completed,
                        }
                    ),
                },
            };
    }
}
