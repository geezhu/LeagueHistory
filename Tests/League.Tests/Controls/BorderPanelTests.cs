using League.Controls;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>A panel that draws a single border inset into its own edges.</summary>
[Trait("Category", "UI")]
public class BorderPanelTests
{
    [Fact]
    public void DrawToBitmap_Panel_PaintsItsBorder()
    {
        UiHost.Run(host =>
        {
            using var panel = Panel(host);

            using var painted = Repaint(panel);

            Assert.Equal(panel.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_PanelWithItsOwnColourAndWidth_PaintsThatBorder()
    {
        UiHost.Run(host =>
        {
            using var panel = Panel(host);
            panel.BorderColor = Color.FromArgb(200, 30, 40);
            panel.BorderWidth = 4;

            using var painted = Repaint(panel);

            Assert.Equal(Color.FromArgb(200, 30, 40), panel.BorderColor);
            Assert.Equal(4, panel.BorderWidth);
            Assert.Equal(panel.Size, painted.Size);
        });
    }

    private static BorderPanel Panel(Form host)
    {
        var panel = new BorderPanel { Width = 160, Height = 80 };
        host.Controls.Add(panel);
        return panel;
    }

    private static Bitmap Repaint(Control control)
    {
        var painted = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(painted, new Rectangle(Point.Empty, control.Size));
        return painted;
    }
}
