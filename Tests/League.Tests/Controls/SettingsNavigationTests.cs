using League.Controls;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// The page list beside the settings: one row per page of the tab control it drives, a click on a row selects that
/// page, and the pointer turns into a hand only over a row.
/// </summary>
[Trait("Category", "UI")]
public class SettingsNavigationTests
{
    [Fact]
    public void Click_SecondRow_SelectsTheSecondPage()
    {
        UiHost.Run(host =>
        {
            var (tabs, navigation) = Navigation(host, "功能辅助", "对局速览", "消息发送");

            WindowMessages.Click(navigation, Centre(navigation.RowBounds(1)));

            Assert.Equal(1, tabs.SelectedIndex);
        });
    }

    [Fact]
    public void Click_PageAddedAfterTheList_SelectsThatPage()
    {
        UiHost.Run(host =>
        {
            var (tabs, navigation) = Navigation(host, "功能辅助");
            tabs.TabPages.Add(new TabPage("数据缓存"));

            WindowMessages.Click(navigation, Centre(navigation.RowBounds(1)));

            Assert.Equal(1, tabs.SelectedIndex);
        });
    }

    [Fact]
    public void Click_BelowTheLastRow_KeepsTheCurrentPage()
    {
        UiHost.Run(host =>
        {
            var (tabs, navigation) = Navigation(host, "功能辅助", "对局速览");
            tabs.SelectedIndex = 1;

            WindowMessages.Click(navigation, Centre(navigation.RowBounds(4)));

            Assert.Equal(1, tabs.SelectedIndex);
        });
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, false)]
    public void Move_OverOrPastTheRows_ShowsTheHandOnlyOverARow(int row, bool hand)
    {
        UiHost.Run(host =>
        {
            var (_, navigation) = Navigation(host, "功能辅助", "对局速览");

            WindowMessages.Move(navigation, Centre(navigation.RowBounds(row)));

            Assert.Equal(hand ? Cursors.Hand : Cursors.Default, navigation.Cursor);
        });
    }

    private static (TabControl Tabs, SettingsNavigation Navigation) Navigation(Form host, params string[] pages)
    {
        host.ClientSize = new Size(600, 400);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        foreach (var page in pages)
        {
            tabs.TabPages.Add(new TabPage(page));
        }

        var navigation = new SettingsNavigation(tabs) { Dock = DockStyle.Left };
        host.Controls.Add(tabs);
        host.Controls.Add(navigation);
        return (tabs, navigation);
    }

    private static Point Centre(Rectangle bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
}
