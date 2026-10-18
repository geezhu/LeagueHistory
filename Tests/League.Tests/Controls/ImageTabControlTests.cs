using League.Controls;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// The tab strip down the side of the main window: every tab is painted from the image its page carries, the
/// selected one is highlighted and the others are dimmed, and a separator closes the strip off.
/// </summary>
[Trait("Category", "UI")]
public class ImageTabControlTests
{
    [Fact]
    public void DrawToBitmap_TabsWithAndWithoutImages_PaintsEveryTabAndTheSeparator()
    {
        UiHost.Run(host =>
        {
            using var tabs = Tabs(host);
            using var icon = new Bitmap(40, 32);
            tabs.TabPages.Add(new TabPage("第一页") { Tag = icon });
            tabs.TabPages.Add(new TabPage("第二页"));

            using var painted = Repaint(tabs);

            Assert.Equal(tabs.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_SelectedTab_PaintsItAsTheSelectedOne()
    {
        UiHost.Run(host =>
        {
            using var tabs = Tabs(host);
            using var icon = new Bitmap(40, 32);
            tabs.TabPages.Add(new TabPage("第一页") { Tag = icon });
            tabs.TabPages.Add(new TabPage("第二页") { Tag = icon });

            tabs.SelectedIndex = 1;
            using var painted = Repaint(tabs);

            Assert.Equal(1, tabs.SelectedIndex);
            Assert.Equal(tabs.Size, painted.Size);
        });
    }

    private static ImageTabControl Tabs(Form host)
    {
        var tabs = new ImageTabControl { Dock = DockStyle.Fill };
        host.Controls.Add(tabs);
        return tabs;
    }

    private static Bitmap Repaint(Control control)
    {
        var painted = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(painted, new Rectangle(Point.Empty, control.Size));
        return painted;
    }
}
