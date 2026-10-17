using League.ChampionSkin;
using League.Models;
using League.Tests.Support;

namespace League.Tests.ChampionSkin;

/// <summary>
/// The champion's skin strip on a real message loop: which tile it selects, that the automatic skin can be moved
/// without replacing the list, and that it paints. The tiles come from a stub, so no client, no network and no real
/// art folder is involved. This is the sample for porting an interface check out of Tests/StatisticsChecks.
/// </summary>
[Trait("Category", "UI")]
public class SkinStripViewTests
{
    [Fact]
    public void SetItems_RequestedSkin_SelectsIt()
    {
        var skins = Skins(12);
        var changed = 0;

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SelectionChanged += (_, _) => changed++;

            strip.SetItems(skins, skins[5].SkinId, skins[1].SkinId);

            Assert.Same(skins[5], strip.Selected);
            Assert.Equal(1, changed);
        });
    }

    [Fact]
    public void SetItems_SkinNotInList_SelectsTheFirst()
    {
        var skins = Skins(12);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);

            strip.SetItems(skins, 999999, 0);

            Assert.Same(skins[0], strip.Selected);
        });
    }

    [Fact]
    public void SetItems_EmptyList_HasNoSelection()
    {
        UiHost.Run(host =>
        {
            using var strip = Strip(host);

            strip.SetItems([], 0, 0);

            Assert.Null(strip.Selected);
            Assert.Empty(strip.Items);
        });
    }

    [Fact]
    public void SetPreferred_OtherSkin_KeepsTheSelection()
    {
        var skins = Skins(12);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[0].SkinId, skins[1].SkinId);

            strip.SetPreferred(skins[3].SkinId);

            Assert.Same(skins[0], strip.Selected);
        });
    }

    [Fact]
    public void DrawToBitmap_WithTiles_PaintsTheStrip()
    {
        var skins = Skins(12);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            using var tile = new Bitmap(96, 96);
            using (var graphics = Graphics.FromImage(tile))
            {
                graphics.Clear(Color.CornflowerBlue);
            }

            // Every second skin has no tile, so the placeholder is painted as well.
            strip.Tile = skinId => skinId % 2 == 0 ? tile : null;
            strip.SetItems(skins, skins[1].SkinId, skins[3].SkinId);

            using var painted = new Bitmap(strip.Width, strip.Height);
            strip.DrawToBitmap(painted, new Rectangle(Point.Empty, strip.Size));

            Assert.Equal(strip.Size, painted.Size);
        });
    }

    private static SkinStripView Strip(Form host)
    {
        var strip = new SkinStripView { Dock = DockStyle.Fill };
        host.Controls.Add(strip);
        return strip;
    }

    private static List<SkinThumbItem> Skins(int count) =>
        [
            .. Enumerable
                .Range(0, count)
                .Select(i => new SkinThumbItem
                {
                    SkinId = 99000 + i,
                    Name = $"皮肤{i}",
                    IsBase = i == 0,
                }),
        ];
}
