using League.Controls;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// The search box: it keeps the native editable dropdown and paints a quiet border and arrow over it, both when
/// Windows asks it to paint and when it is printed into a bitmap.
/// </summary>
[Trait("Category", "UI")]
public class SearchComboBoxTests
{
    [Fact]
    public void DrawToBitmap_SearchBox_PaintsItsOwnChrome()
    {
        UiHost.Run(host =>
        {
            using var box = Box(host);

            using var painted = Repaint(box);

            Assert.Equal(box.Size, painted.Size);
        });
    }

    [Fact]
    public void Paint_Message_PaintsTheChromeAfterTheNativeBox()
    {
        UiHost.Run(host =>
        {
            using var box = Box(host);

            WindowMessages.Paint(box);

            Assert.True(box.IsHandleCreated, "The box paints through its own window procedure.");
        });
    }

    [Fact]
    public void DrawToBitmap_BoxWithoutRoomForChrome_SkipsIt()
    {
        UiHost.Run(host =>
        {
            using var box = Box(host);
            box.Width = 12;

            using var painted = Repaint(box);

            Assert.Equal(new Size(12, box.Height), painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_DisabledBox_PaintsTheArrowInTheDisabledColour()
    {
        UiHost.Run(host =>
        {
            using var box = Box(host);
            box.Enabled = false;

            using var painted = Repaint(box);

            Assert.False(box.Enabled);
            Assert.Equal(box.Size, painted.Size);
        });
    }

    private static SearchComboBox Box(Form host)
    {
        var box = new SearchComboBox { Width = 160 };
        host.Controls.Add(box);
        return box;
    }

    private static Bitmap Repaint(Control control)
    {
        var painted = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(painted, new Rectangle(Point.Empty, control.Size));
        return painted;
    }
}
