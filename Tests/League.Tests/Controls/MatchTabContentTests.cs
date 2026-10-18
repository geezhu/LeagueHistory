using League.Controls;
using League.Models;
using League.States;
using League.Tests.Support;
using Newtonsoft.Json.Linq;
using static League.FormMain;

namespace League.Tests.Controls;

/// <summary>
/// The player tabs above the history list: a tab per player, the same player not opened twice, one closed, the whole
/// set cleared (both for the close button and for the window closing), and the tab that belongs to the account
/// itself. The owner profile icon is passed as "0", so no picture is downloaded; the tab content the control builds
/// starts its own load against the host window underneath, which fails and is reported the way the application
/// reports it. <see cref="Globals.CurrentPuuid"/> and <see cref="RefreshState.ForceMatchRefresh"/> are process-wide,
/// so this class joins the collection that owns them and puts both back.
/// </summary>
[Collection(EnvironmentCollection.Name)]
[Trait("Category", "UI")]
public class MatchTabContentTests : IDisposable
{
    private const string Player = "puuid-1";

    private readonly string? _previousPuuid = Globals.CurrentPuuid;
    private readonly bool _previousForceRefresh = RefreshState.ForceMatchRefresh;

    public MatchTabContentTests()
    {
        // Pinned: with no current player, no tab is the account's own tab unless a test says otherwise.
        Globals.CurrentPuuid = null;
        RefreshState.ForceMatchRefresh = false;
    }

    public void Dispose()
    {
        Globals.CurrentPuuid = _previousPuuid;
        RefreshState.ForceMatchRefresh = _previousForceRefresh;
    }

    [Fact]
    public void Constructor_Content_BuildsItsOwnTabControl()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);

            Assert.NotNull(content.MainTabControl);
            Assert.NotEmpty(content.Controls);
            Assert.Equal(0, content.MainTabControl.TabCount);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateNewTab_PlayerWithoutAPuuid_AddsNoTab(string puuid)
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);

            Open(content, puuid);

            Assert.Equal(0, content.MainTabControl.TabCount);
        });
    }

    [Fact]
    public void CreateNewTab_Player_AddsATabNamedAfterThemAndSelectsIt()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);

            Open(content, Player, "甲", "0001");

            var page = Assert.Single(content.MainTabControl.TabPages.Cast<TabPage>());
            Assert.Equal("甲#0001", page.Text);
            Assert.Equal(Player, page.Tag);
            Assert.Same(page, content.MainTabControl.SelectedTab);
            Assert.Single(page.Controls.OfType<MatchTabPageContent>());
        });
    }

    [Fact]
    public void CreateNewTab_SamePlayerAgain_SelectsTheTabThatIsAlreadyThere()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);
            Open(content, Player, "甲", "0001");
            Open(content, "puuid-2", "乙", "0002");

            Open(content, "puuid-2", "乙", "0002");

            Assert.Equal(2, content.MainTabControl.TabCount);
            Assert.Equal("乙#0002", content.MainTabControl.SelectedTab?.Text);
        });
    }

    [Fact]
    public void CloseTab_NoPage_ChangesNothing()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);
            Open(content, Player);

            content.CloseTab(null!);

            Assert.Equal(1, content.MainTabControl.TabCount);
        });
    }

    [Fact]
    public void CloseTab_PageThatIsNotInTheControl_TakesThePageItselfAway()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);
            Open(content, Player);
            var stranger = new TabPage("外来页");

            content.CloseTab(stranger);

            Assert.Equal(1, content.MainTabControl.TabCount);
            Assert.True(stranger.IsDisposed, "The page handed over is disposed even though the control never held it.");
        });
    }

    [Fact]
    public void CloseTab_PlayerTab_TakesThePageAndItsContentAway()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);
            Open(content, Player, "甲", "0001");
            Open(content, "puuid-2", "乙", "0002");
            var page = Page(content, "甲#0001");

            content.CloseTab(page);

            Assert.Equal(["乙#0002"], Titles(content));
            Assert.True(page.IsDisposed, "The tab page is disposed with its content.");
        });
    }

    [Fact]
    public void RemovePage_FromTheTabControl_CleansTheTabUpAsWell()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);
            Open(content, Player, "甲", "0001");
            Open(content, "puuid-2", "乙", "0002");
            var page = Page(content, "甲#0001");

            content.MainTabControl.TabPages.Remove(page);

            Assert.Equal(["乙#0002"], Titles(content));
        });
    }

    [Fact]
    public void CleanupAllTabs_PlayerTabs_ClosesTheOthersAndKeepsTheOwnOne()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);
            Globals.CurrentPuuid = Player;
            Open(content, Player, "甲", "0001");
            Open(content, "puuid-2", "乙", "0002");
            Open(content, "puuid-3", "丙", "0003");

            content.CleanupAllTabs();

            var remaining = Assert.Single(content.MainTabControl.TabPages.Cast<TabPage>());
            Assert.Equal(Player, remaining.Tag);
        });
    }

    [Fact]
    public void CleanupAllTabs_OnlyTheOwnTab_KeepsIt()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);
            Globals.CurrentPuuid = Player;
            Open(content, Player);

            content.CleanupAllTabs();

            Assert.Equal(["甲#0001"], Titles(content));
        });
    }

    [Fact]
    public void CleanupAllTabs_FormClosing_ClosesEveryTabIncludingTheOwnOne()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);
            Globals.CurrentPuuid = Player;
            Open(content, Player, "甲", "0001");
            Open(content, "puuid-2", "乙", "0002");

            content.CleanupAllTabs(isFormClosing: true);

            Assert.Equal(0, content.MainTabControl.TabCount);
        });
    }

    [Fact]
    public void CreateNewTab_TwentyTabsAlreadyOpen_ClosesTheOldestOneToMakeRoom()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);
            content.MainTabControl.TabPages.AddRange([
                .. Enumerable.Range(0, 20).Select(index => new TabPage($"页{index}") { Tag = $"old-{index}" }),
            ]);

            Open(content, Player, "甲", "0001");

            Assert.Equal(20, content.MainTabControl.TabCount);
            Assert.DoesNotContain("页0", Titles(content));
            Assert.Contains("甲#0001", Titles(content));
        });
    }

    [Fact]
    public void CreateNewTab_OwnPlayerWhileAForcedRefreshIsPending_ReloadsTheOpenTab()
    {
        UiHost.Run(async host =>
        {
            using var content = Content(host);
            Globals.CurrentPuuid = Player;
            RefreshState.ForceMatchRefresh = true;
            Open(content, Player, "甲", "0001");
            var tab = TabContent(Page(content, "甲#0001"));

            Open(content, Player, "甲", "0001");
            Assert.True(tab.NeedsRefresh, "The forced refresh marks the open tab for a reload.");

            await WaitFor(() => !tab.NeedsRefresh);

            Assert.False(tab.NeedsRefresh, "The reload clears the mark once it has run.");
            Assert.False(RefreshState.ForceMatchRefresh, "The reload clears the global request once it has run.");
        });
    }

    private static async Task WaitFor(Func<bool> ready)
    {
        for (int attempt = 0; attempt < 600 && !ready(); attempt++)
        {
            await Task.Delay(5);
        }
    }

    private static MatchTabContent Content(Form host)
    {
        var content = new MatchTabContent { Dock = DockStyle.Fill };
        host.Controls.Add(content);
        content.MainTabControl.Dock = DockStyle.Fill;
        return content;
    }

    private static void Open(MatchTabContent content, string puuid, string gameName = "甲", string tagLine = "0001") =>
        content.CreateNewTab("7", gameName, tagLine, puuid, "0", "30", "公开", Ranks());

    private static TabPage Page(MatchTabContent content, string title) =>
        content.MainTabControl.TabPages.Cast<TabPage>().Single(page => page.Text == title);

    private static MatchTabPageContent TabContent(TabPage page) => page.Controls.OfType<MatchTabPageContent>().Single();

    private static string[] Titles(MatchTabContent content) =>
        [.. content.MainTabControl.TabPages.Cast<TabPage>().Select(page => page.Text)];

    private static Dictionary<string, RankedStats> Ranks() =>
        RankedStats.FromJson(
            JObject.Parse(
                """
                {"queueMap":{"RANKED_SOLO_5x5":{"tier":"GOLD","division":"II","leaguePoints":45,"wins":12,"losses":8}}}
                """
            )
        );
}
