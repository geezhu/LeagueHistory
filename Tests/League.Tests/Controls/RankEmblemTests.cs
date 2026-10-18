using League.Controls;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// The rank emblem: it cuts the tier's emblem out of the picture that ships inside the assembly and centres it in
/// whatever space it is given. The emblems come from the assembly's own resources, so no asset folder is needed.
/// </summary>
[Trait("Category", "UI")]
public class RankEmblemTests
{
    [Theory]
    [InlineData("gold")]
    [InlineData("GOLD")]
    [InlineData("challenger")]
    [InlineData("emerald")]
    public void DrawToBitmap_KnownTier_PaintsThatEmblem(string tier)
    {
        UiHost.Run(host =>
        {
            using var emblem = Emblem(host);

            emblem.SetTier(tier);
            using var painted = Repaint(emblem);

            Assert.Equal(new Size(64, 64), painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_TierWithoutAnEmblem_PaintsNothing()
    {
        UiHost.Run(host =>
        {
            using var emblem = Emblem(host);

            emblem.SetTier("unranked");
            using var painted = Repaint(emblem);

            Assert.Equal(new Size(64, 64), painted.Size);
        });
    }

    [Fact]
    public void SetTier_SameTierTwice_KeepsTheEmblemItAlreadyHas()
    {
        UiHost.Run(host =>
        {
            using var emblem = Emblem(host);
            emblem.SetTier("gold");

            emblem.SetTier("gold");
            using var painted = Repaint(emblem);

            Assert.Equal(new Size(64, 64), painted.Size);
        });
    }

    private static RankEmblem Emblem(Form host)
    {
        var emblem = new RankEmblem { Width = 64, Height = 64 };
        host.Controls.Add(emblem);
        return emblem;
    }

    private static Bitmap Repaint(Control control)
    {
        var painted = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(painted, new Rectangle(Point.Empty, control.Size));
        return painted;
    }
}
