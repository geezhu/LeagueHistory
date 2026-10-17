using System.Reflection;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

/// <summary>
/// The history page. The constructor builds the whole surface, and the two events it asks its owner for are the
/// seam the check uses: it answers them with invented records, so no client and no network are involved.
/// </summary>
internal static class MatchTabPageContentChecks
{
    public static void Run() => WinFormsCheckHost.Run(Core);

    private static void Core()
    {
        LayoutChecks();
        MissingWiringChecks();
        PagingChecks();
        FailureChecks();
        LifecycleChecks();
        Console.WriteLine(
            "PASS: the history page's layout, its unwired guard, loading and paging cards, the empty, server and "
                + "failure messages, its argument guards and cleanup."
        );
    }

    private static void LayoutChecks()
    {
        using var host = HiddenForm(new Size(1200, 800));
        var view = new MatchTabPageContent { Dock = DockStyle.Fill };
        host.Controls.Add(view);
        host.Show();
        view.CreateControl();

        Check(view.comboPage.Items.Count > 0, "The page offers page sizes");
        Check(view.comboFilter.Items.Count > 0, "The page offers queue filters");
        Check(view.CurrentPage == 1, "The page starts on the first page");
        Check(view.CurrentFilter.Length == 0, "The page starts without a queue filter");
        Check(view.TotalPages == 0, "No pages are known before the first load");
        Check(view.flowLayoutPanelRight != null, "The page has a list to put cards in");
        Check(Children(view).OfType<Label>().Any(), "The page draws its own labels");

        view.DrawToBitmap(new Bitmap(1200, 800), view.ClientRectangle);
        host.ClientSize = new Size(820, 620);
        view.DrawToBitmap(new Bitmap(820, 620), view.ClientRectangle);
        Check(view.Width == 820, "The page follows its host's width");
        host.Close();
    }

    private static void MissingWiringChecks()
    {
        using var host = HiddenForm(new Size(1200, 800));
        var view = new MatchTabPageContent { Dock = DockStyle.Fill };
        host.Controls.Add(view);
        host.Show();

        Await((Task)UpdateMatches.Invoke(view, null)!);
        Check(Says(view, "功能未初始化"), "A page whose owner did not wire it up says so instead of failing");
        host.Close();
    }

    private static void PagingChecks()
    {
        using var host = HiddenForm(new Size(1200, 800));
        var view = new MatchTabPageContent { Dock = DockStyle.Fill };
        host.Controls.Add(view);
        host.Show();

        int requested = 0;
        var pages = new Dictionary<int, int>();
        view.LoadDataRequested += (puuid, start, count, queue) =>
        {
            requested++;
            pages[start] = count;
            return Task.FromResult(start == 0 ? Records(7) : new JArray());
        };
        view.ParsePanelRequested += (match, puuid, index) => Task.FromResult<Panel>(new MatchListPanel(Card(match)));

        Await((Task)UpdateMatches.Invoke(view, null)!);
        Check(requested == 1, "The first page is asked for once");
        Check(view.flowLayoutPanelRight.Controls.Count == 7, "Every record becomes a card");
        Check(view.CurrentPage == 1, "The page stays on the first page after loading");
        Check(
            view.flowLayoutPanelRight.Controls.OfType<MatchListPanel>().All(card => card.Compact),
            "History cards use the compact layout"
        );
        Check(view.btnNext.Enabled, "A full page means there may be more to come");

        view.btnNext.PerformClick();
        PumpUntil(() => requested == 2);
        Check(requested == 2, "The next page is asked for once");
        Check(view.CurrentPage == 2, "The next page is reached");
        PumpUntil(() => Says(view, "没有更多比赛记录"));
        Check(Says(view, "没有更多比赛记录"), "The end of the list is explained");

        view.btnPrev.PerformClick();
        PumpUntil(() => view.CurrentPage == 1 && view.flowLayoutPanelRight.Controls.Count == 7);
        Check(view.CurrentPage == 1, "The previous page is reached again");
        Check(view.flowLayoutPanelRight.Controls.Count == 7, "The cards come back with the first page");
        host.Close();
    }

    private static void FailureChecks()
    {
        using var host = HiddenForm(new Size(1200, 800));
        host.Show();

        var empty = Wired(host, () => Task.FromResult(new JArray()));
        Await((Task)UpdateMatches.Invoke(empty, null)!);
        Check(Says(empty, "没有匹配的比赛记录"), $"An empty history is explained: {Texts(empty)}");

        var unavailable = Wired(host, () => Task.FromResult<JArray>(null!));
        Await((Task)UpdateMatches.Invoke(unavailable, null)!);
        Check(
            Says(unavailable, "战绩服务器异常"),
            $"A history the server cannot answer is explained: {Texts(unavailable)}"
        );

        var broken = Wired(host, () => throw new InvalidOperationException("检查用异常"));
        Await((Task)UpdateMatches.Invoke(broken, null)!);
        Check(Says(broken, "加载失败"), $"A history read that throws is explained: {Texts(broken)}");
        host.Close();
    }

    /// <summary>Adds a page to the host with both of its events answered, so it behaves like a wired page.</summary>
    private static MatchTabPageContent Wired(Control host, Func<Task<JArray>> load)
    {
        var view = new MatchTabPageContent { Dock = DockStyle.Fill };
        host.Controls.Add(view);
        view.LoadDataRequested += (puuid, start, count, queue) => load();
        view.ParsePanelRequested += (match, puuid, index) => Task.FromResult<Panel>(new MatchListPanel(Card(match)));
        return view;
    }

    private static void LifecycleChecks()
    {
        using var host = HiddenForm(new Size(1200, 800));
        var view = new MatchTabPageContent { Dock = DockStyle.Fill };
        host.Controls.Add(view);
        host.Show();

        bool threw = false;
        try
        {
            view.Initialize("");
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        Check(threw, "A history page cannot be opened for an empty player");
        Check(!view.NeedsRefresh, "A fresh page does not ask to be refreshed");

        view.SetNeedsRefresh(true);
        Check(view.NeedsRefresh, "The page can be marked as needing a refresh");
        view.SetNeedsRefresh(false);
        Check(!view.NeedsRefresh, "The mark can be cleared again");

        view.ForceCleanup();
        view.Cleanup();
        Check(view.flowLayoutPanelRight.Controls.Count == 0, "Cleaning the page clears its cards");

        view.Dispose();
    }

    private static bool Says(Control view, string text) =>
        Children(view).OfType<Label>().Any(label => label.Text.Contains(text, StringComparison.Ordinal));

    private static string Texts(Control view) =>
        string.Join(
            " | ",
            Children(view).OfType<Label>().Select(label => label.Text).Where(text => text.Length > 0).Take(12)
        );

    private static void Await(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult(); // Completed already; this only surfaces a failure.
    }

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
                            ["participants"] = new JArray(
                                new JObject
                                {
                                    ["puuid"] = "self-puuid-0001",
                                    ["summonerId"] = 7,
                                    ["teamId"] = 100,
                                    ["championId"] = 1,
                                }
                            ),
                        }
                )
        );

    private static MatchInfo Card(JObject match) =>
        new()
        {
            GameId = match["gameId"]?.Value<long>() ?? 0,
            ResultText = "胜利",
            Mode = "极地大乱斗",
            QueueId = "q_450",
            AllParticipants = [new JObject { ["teamId"] = 100 }],
        };

    private static readonly MethodInfo UpdateMatches = typeof(MatchTabPageContent).GetMethod(
        "UpdateMatchesDisplay",
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;
}
