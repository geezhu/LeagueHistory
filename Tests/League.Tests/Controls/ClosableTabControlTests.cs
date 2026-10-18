using League.Controls;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// The closable tab control: the close button it paints on every tab, what a click on that button closes (through the
/// tab content, or by removing the page itself when there is no tab content above it) and its owner-drawn tab. The two
/// paths that end in a message box are deliberately never reached: the account's own tab and the last tab.
/// </summary>
[Trait("Category", "UI")]
public class ClosableTabControlTests
{
    private const int CloseButtonSize = 12;

    // PaintCloseButton keeps 4 pixels between the button and the right edge of the tab.
    private const int CloseButtonInset = 4;

    [Fact]
    public void TabPages_Added_HoldsThemAndProtectsNone()
    {
        UiHost.Run(host =>
        {
            using var tabs = Tabs(host);

            AddPages(tabs, "第一页", "第二页", "第三页");

            Assert.Equal(3, tabs.TabCount);
            Assert.Equal(["第一页", "第二页", "第三页"], Titles(tabs));
            Assert.Null(tabs.ProtectedTab);
        });
    }

    [Fact]
    public void DrawToBitmap_Tabs_PaintsEveryTab()
    {
        UiHost.Run(host =>
        {
            using var tabs = Tabs(host);
            AddPages(tabs, "第一页", "第二页", "第三页");

            using var painted = Paint(tabs);

            Assert.Equal(tabs.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_Tabs_PaintsThemItselfInsteadOfOwnerDrawingThem()
    {
        UiHost.Run(host =>
        {
            using var tabs = new RecordingTabControl { Dock = DockStyle.Fill };
            host.Controls.Add(tabs);
            AddPages(tabs, "第一页", "第二页");

            using var painted = Paint(tabs);

            // The control paints every tab in its own OnPaint, so the framework never asks it to owner-draw one.
            Assert.Equal(0, tabs.DrawnTabs);
            Assert.Equal(tabs.Size, painted.Size);
        });
    }

    [Fact]
    public void Click_AwayFromTheCloseButton_ClosesNothing()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);
            var tabs = content.MainTabControl;
            AddPages(tabs, "第一页", "第二页", "第三页");
            var corner = tabs.GetTabRect(0);

            WindowMessages.Click(tabs, new Point(corner.Left + 3, corner.Top + 3));

            Assert.Equal(3, tabs.TabCount);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Click_TheCloseButtonOfATab_ClosesExactlyThatPage(int index)
    {
        var titles = new[] { "第一页", "第二页", "第三页" };

        UiHost.Run(host =>
        {
            using var content = Content(host);
            var tabs = content.MainTabControl;
            AddPages(tabs, titles);
            using var painted = Paint(tabs);

            WindowMessages.Click(tabs, CloseButtonCentre(tabs, index));

            Assert.Equal(2, tabs.TabCount);
            Assert.DoesNotContain(titles[index], Titles(tabs));
        });
    }

    [Fact]
    public void Click_TheCloseButtonOfTheNextTabAfterClosingOne_ClosesTheTabThatMovedThere()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);
            var tabs = content.MainTabControl;
            AddPages(tabs, "第一页", "第二页", "第三页");
            using var painted = Paint(tabs);

            WindowMessages.Click(tabs, CloseButtonCentre(tabs, 1));
            using var repainted = Paint(tabs);
            WindowMessages.Click(tabs, CloseButtonCentre(tabs, 1));

            // Painting again puts the close buttons on the pages as they are now, so the third page closes next.
            Assert.Equal(["第一页"], Titles(tabs));
        });
    }

    [Fact]
    public void Click_TheCloseButtonAfterPaintingTwice_ClosesThePageUnderIt()
    {
        UiHost.Run(host =>
        {
            using var content = Content(host);
            var tabs = content.MainTabControl;
            AddPages(tabs, "第一页", "第二页", "第三页");

            using (var painted = Paint(tabs))
            {
                Assert.Equal(tabs.Size, painted.Size);
            }

            using (var painted = Paint(tabs))
            {
                Assert.Equal(tabs.Size, painted.Size);
            }

            WindowMessages.Click(tabs, CloseButtonCentre(tabs, 1));

            Assert.Equal(["第一页", "第三页"], Titles(tabs));
        });
    }

    [Fact]
    public void Click_TheCloseButtonWithoutATabContent_RemovesThatPageDirectly()
    {
        UiHost.Run(host =>
        {
            using var tabs = Tabs(host);
            AddPages(tabs, "孤立页", "保留页");
            using var painted = Paint(tabs);

            WindowMessages.Click(tabs, CloseButtonCentre(tabs, 0));

            Assert.Equal(["保留页"], Titles(tabs));
        });
    }

    private static ClosableTabControl Tabs(Form host)
    {
        var tabs = new ClosableTabControl { Dock = DockStyle.Fill };
        host.Controls.Add(tabs);
        return tabs;
    }

    private static MatchTabContent Content(Form host)
    {
        var content = new MatchTabContent { Dock = DockStyle.Fill };
        host.Controls.Add(content);
        content.MainTabControl.Dock = DockStyle.Fill;
        return content;
    }

    private static void AddPages(ClosableTabControl tabs, params string[] titles) =>
        tabs.TabPages.AddRange([.. titles.Select(title => new TabPage(title))]);

    private static string[] Titles(ClosableTabControl tabs) =>
        [.. tabs.TabPages.Cast<TabPage>().Select(page => page.Text)];

    // PaintCloseButton puts the 12x12 button 4 pixels from the right edge of the tab, vertically centred in it.
    private static Point CloseButtonCentre(ClosableTabControl tabs, int index)
    {
        var tab = tabs.GetTabRect(index);
        return new Point(
            tab.Right - CloseButtonSize - CloseButtonInset + CloseButtonSize / 2,
            tab.Top + (tab.Height - CloseButtonSize) / 2 + CloseButtonSize / 2
        );
    }

    private static Bitmap Paint(ClosableTabControl tabs)
    {
        var painted = new Bitmap(tabs.Width, tabs.Height);
        tabs.DrawToBitmap(painted, new Rectangle(Point.Empty, tabs.Size));
        return painted;
    }

    /// <summary>Counts the tabs the framework owner-draws, which is the only way to reach that override.</summary>
    private sealed class RecordingTabControl : ClosableTabControl
    {
        public int DrawnTabs { get; private set; }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            base.OnDrawItem(e);
            DrawnTabs++;
        }
    }
}
