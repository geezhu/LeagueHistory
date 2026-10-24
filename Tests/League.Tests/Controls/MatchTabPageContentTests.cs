using League.Controls;
using League.Models;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Controls;

/// <summary>
/// The history page while a page loads. The history request is answered by a task the test completes, so the page
/// is caught mid-load: the sweeping bar shows, and an empty list says it is loading rather than looking broken.
///
/// Coming back to the history tab asks for the newest page again; the list is redrawn only when its top game changed.
/// </summary>
[Trait("Category", "UI")]
public class MatchTabPageContentTests
{
    private static (MatchTabPageContent View, TaskCompletionSource<JArray> Page) Loading(Form host)
    {
        var view = new MatchTabPageContent { Dock = DockStyle.Fill };
        host.Controls.Add(view);
        var page = new TaskCompletionSource<JArray>();
        view.LoadDataRequested += (_, _, _, _) => page.Task;
        view.ParsePanelRequested += (_, _, index) =>
            Task.FromResult<Panel>(new MatchListPanel(new MatchInfo { Index = index, ResultText = "胜利" }));
        view.Initialize("puuid");
        return (view, page);
    }

    private static JArray Records(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new JObject { ["gameId"] = i + 1 })];

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static LoadingBar Bar(Control view) => Descendants(view).OfType<LoadingBar>().Single();

    /// <summary>Waits for the page to land, with a deadline, while the message loop keeps pumping.</summary>
    private static async Task WaitForAsync(Func<bool> ready)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(ready(), "The page finished loading within five seconds.");
    }

    [Fact]
    public void Initialize_WhileThePageLoads_ShowsTheLoadingBar()
    {
        UiHost.Run(host =>
        {
            var (view, _) = Loading(host);

            Assert.True(Bar(view).Visible);
        });
    }

    [Fact]
    public void Initialize_AnEmptyListWhileLoading_SaysWhichPageItIsLoading()
    {
        UiHost.Run(host =>
        {
            var (view, _) = Loading(host);

            Assert.Contains(Descendants(view).OfType<Label>(), label => label.Text == "正在加载第 1 页战绩…");
        });
    }

    [Fact]
    public void Initialize_OnceThePageArrives_HidesTheLoadingBar()
    {
        UiHost.Run(async host =>
        {
            var (view, page) = Loading(host);
            page.SetResult(Records(3));

            await WaitForAsync(() => Descendants(view).OfType<MatchListPanel>().Count() == 3);

            Assert.False(Bar(view).Visible);
        });
    }

    /// <summary>How often the page asked for a refresh and parsed a row.</summary>
    private sealed class Calls
    {
        public int Refreshed { get; set; }

        public int Parsed { get; set; }
    }

    /// <summary>A page showing games 1 to 3, whose refresh answers with the given games.</summary>
    private static async Task<(MatchTabPageContent View, Calls Calls)> ShowingThreeAsync(Form host, JArray newest)
    {
        var calls = new Calls();
        var view = new MatchTabPageContent { Dock = DockStyle.Fill };
        host.Controls.Add(view);
        view.LoadDataRequested += (_, _, _, _) => Task.FromResult(Records(3));
        view.RefreshDataRequested += (_, _, _, _) =>
        {
            calls.Refreshed++;
            return Task.FromResult(newest);
        };
        view.ParsePanelRequested += (_, _, index) =>
        {
            calls.Parsed++;
            return Task.FromResult<Panel>(new MatchListPanel(new MatchInfo { Index = index, ResultText = "胜利" }));
        };
        view.Initialize("puuid");
        await WaitForAsync(() => Descendants(view).OfType<MatchListPanel>().Count() == 3);
        return (view, calls);
    }

    private static JArray WithNewGameOnTop() => [new JObject { ["gameId"] = 99 }, .. Records(3)];

    [Fact]
    public void RefreshNewestAsync_ANewGameOnTop_RedrawsTheList()
    {
        UiHost.Run(async host =>
        {
            var (view, _) = await ShowingThreeAsync(host, WithNewGameOnTop());

            await view.RefreshNewestAsync();

            await WaitForAsync(() => Descendants(view).OfType<MatchListPanel>().Count() == 4);
        });
    }

    [Fact]
    public void RefreshNewestAsync_TheSameGameOnTop_KeepsTheList()
    {
        UiHost.Run(async host =>
        {
            var (view, calls) = await ShowingThreeAsync(host, Records(3));

            await view.RefreshNewestAsync();

            Assert.Equal(1, calls.Refreshed);
            Assert.Equal(3, calls.Parsed);
        });
    }

    [Fact]
    public void RefreshNewestAsync_WhileThePageStillLoads_AsksNothing()
    {
        // The page being loaded is already the newest one.
        UiHost.Run(async host =>
        {
            var (view, _) = Loading(host);
            int refreshed = 0;
            view.RefreshDataRequested += (_, _, _, _) =>
            {
                refreshed++;
                return Task.FromResult(WithNewGameOnTop());
            };

            await view.RefreshNewestAsync();

            Assert.Equal(0, refreshed);
        });
    }
}
