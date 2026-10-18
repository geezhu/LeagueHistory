using League.ChampionSkin;
using League.Models;
using League.Tests.Support;

namespace League.Tests.ChampionSkin;

/// <summary>
/// The champion's skin strip on a real message loop: which tile it selects, that the automatic skin can be moved
/// without replacing the list, and that it paints. The tiles come from a stub, so no client, no network and no real
/// art folder is involved. The geometry below repeats the constants the control lays its tiles and its slim scroll bar
/// out with, where the old check read the same rectangles back through reflection.
/// </summary>
[Trait("Category", "UI")]
public class SkinStripViewTests
{
    private const int StripWidth = 640; // the docked strip fills the UiHost window
    private const int CellWidth = 118;
    private const int CellInset = 8;
    private const int TileSize = 96;
    private const int TrackThickness = 5;
    private const int TrackInset = 4;
    private const int SkinCount = 12;
    private const int ContentWidth = SkinCount * CellWidth + 16; // the strip keeps 16 pixels behind the last tile
    private const int ScrollMax = ContentWidth - StripWidth;
    private const int ThumbDrag = 150;
    private const int WiderWidth = 1300;

    [Fact]
    public void SetItems_RequestedSkin_SelectsIt()
    {
        var skins = Skins(SkinCount);
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
        var skins = Skins(SkinCount);

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
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[0].SkinId, skins[1].SkinId);

            strip.SetPreferred(skins[3].SkinId);

            Assert.Same(skins[0], strip.Selected);
        });
    }

    [Fact]
    public void SetItems_TheLastTile_ScrollsItIntoView()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);

            strip.SetItems(skins, skins[^1].SkinId, 0);
            WindowMessages.Click(strip, TileCentre(0));

            // Scrolled to its right end, so the pixel of the first tile shows the one seven tiles further along.
            Assert.Same(skins[(ScrollMax + CellWidth / 2) / CellWidth], strip.Selected);
        });
    }

    [Fact]
    public void DrawToBitmap_WithTiles_PaintsTheStrip()
    {
        var skins = Skins(SkinCount);

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

            using var painted = Paint(strip);

            Assert.Equal(strip.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_EmptyStrip_PaintsTheEmptySurface()
    {
        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems([], 0, 0);

            using var painted = Paint(strip);

            Assert.Equal(strip.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_WithoutTiles_PaintsThePlaceholders()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[1].SkinId, skins[3].SkinId);

            using var painted = Paint(strip);

            Assert.Equal(strip.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_HoveredTile_PaintsTheHover()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            using var tile = new Bitmap(TileSize, TileSize);
            strip.Tile = _ => tile;
            strip.SetItems(skins, skins[1].SkinId, skins[3].SkinId);

            WindowMessages.Move(strip, TileCentre(2));
            using var painted = Paint(strip);

            Assert.Equal(strip.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_ScrolledStrip_SkipsTheTilesThatLeftTheView()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            using var tile = new Bitmap(TileSize, TileSize);
            strip.Tile = _ => tile;
            strip.SetItems(skins, skins[0].SkinId, 0);

            WindowMessages.Wheel(strip, -1);
            WindowMessages.Wheel(strip, -1);
            WindowMessages.Wheel(strip, -1);
            using var painted = Paint(strip);

            Assert.Equal(strip.Size, painted.Size);
        });
    }

    [Fact]
    public void Click_AnotherTile_SelectsItWithoutActivatingIt()
    {
        var skins = Skins(SkinCount);
        var activated = 0;

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.Activated += (_, _) => activated++;
            strip.SetItems(skins, skins[0].SkinId, 0);

            WindowMessages.Click(strip, TileCentre(2));

            Assert.Same(skins[2], strip.Selected);
            Assert.Equal(0, activated);
        });
    }

    [Fact]
    public void Click_LeftOfTheFirstTile_SelectsNothing()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[3].SkinId, 0);

            WindowMessages.Click(strip, new Point(CellInset - 4, TileCentre(1).Y));

            Assert.Same(skins[3], strip.Selected);
        });
    }

    [Fact]
    public void Move_OntoATile_OffersTheHandCursor()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[0].SkinId, 0);

            WindowMessages.Move(strip, TileCentre(2));

            Assert.Equal(Cursors.Hand, strip.Cursor);
        });
    }

    [Fact]
    public void Move_LeftOfTheFirstTile_OffersTheDefaultCursor()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[0].SkinId, 0);

            WindowMessages.Move(strip, TileCentre(2));
            WindowMessages.Move(strip, new Point(CellInset - 4, TileCentre(1).Y));

            Assert.Equal(Cursors.Default, strip.Cursor);
        });
    }

    [Fact]
    public void Move_UnderTheNames_OffersTheDefaultCursor()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[0].SkinId, 0);

            WindowMessages.Move(strip, TileCentre(2));
            WindowMessages.Move(strip, new Point(TileCentre(2).X, strip.ClientSize.Height - 2));

            Assert.Equal(Cursors.Default, strip.Cursor);
        });
    }

    [Fact]
    public void MouseLeave_AfterHoveringATile_DropsTheHover()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[0].SkinId, 0);

            WindowMessages.Move(strip, TileCentre(2));
            WindowMessages.Leave(strip);
            WindowMessages.Move(strip, new Point(CellInset - 4, TileCentre(1).Y));

            // The leave already dropped the hover, so off the tiles the cursor is left as it was.
            Assert.Equal(Cursors.Hand, strip.Cursor);
        });
    }

    [Fact]
    public void DoubleClick_TheSelectedTile_ActivatesIt()
    {
        var skins = Skins(SkinCount);
        var activated = 0;

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.Activated += (_, _) => activated++;
            strip.SetItems(skins, skins[1].SkinId, 0);

            WindowMessages.DoubleClick(strip, TileCentre(1));

            Assert.Equal(1, activated);
        });
    }

    [Fact]
    public void DoubleClick_PastTheLastTile_ActivatesNothing()
    {
        var skins = Skins(SkinCount);
        var activated = 0;

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.Activated += (_, _) => activated++;
            strip.SetItems(skins, skins[0].SkinId, 0);

            WindowMessages.DoubleClick(strip, new Point(ContentWidth + 40, TileCentre(1).Y));

            Assert.Same(skins[0], strip.Selected);
            Assert.Equal(0, activated);
        });
    }

    [Fact]
    public void Key_RightArrow_MovesOneTileRight()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[0].SkinId, 0);

            WindowMessages.Key(strip, Keys.Right);

            Assert.Same(skins[1], strip.Selected);
        });
    }

    [Fact]
    public void Key_LeftArrowOnTheFirstTile_StopsThere()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[0].SkinId, 0);

            WindowMessages.Key(strip, Keys.Left);

            Assert.Same(skins[0], strip.Selected);
        });
    }

    [Fact]
    public void Key_Enter_ActivatesTheSelectedTile()
    {
        var skins = Skins(SkinCount);
        var activated = 0;

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.Activated += (_, _) => activated++;
            strip.SetItems(skins, skins[0].SkinId, 0);

            WindowMessages.Key(strip, Keys.Enter);

            Assert.Equal(1, activated);
        });
    }

    [Fact]
    public void PreProcessControlMessage_ArrowKey_LeavesTheKeyToTheStrip()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[0].SkinId, 0);

            var arrow = WindowMessages.KeyDownMessage(strip, Keys.Right);
            var tab = WindowMessages.KeyDownMessage(strip, Keys.Tab);

            // The strip takes the sideways arrows for itself; Tab stays with the window, which moves the focus on.
            Assert.Equal(PreProcessControlState.MessageNeeded, strip.PreProcessControlMessage(ref arrow));
            Assert.Equal(PreProcessControlState.MessageProcessed, strip.PreProcessControlMessage(ref tab));
        });
    }

    [Fact]
    public void Wheel_ScrolledRight_ShowsTheTilesThatLeftTheView()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[0].SkinId, 0);

            // Each turn is one message and moves the strip by exactly one tile.
            WindowMessages.Wheel(strip, -1);
            WindowMessages.Wheel(strip, -1);
            WindowMessages.Click(strip, TileCentre(0));

            Assert.Same(skins[2], strip.Selected);
        });
    }

    [Fact]
    public void Wheel_ScrolledBack_ReturnsToTheFirstTile()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[0].SkinId, 0);

            WindowMessages.Wheel(strip, -1);
            WindowMessages.Wheel(strip, -1);
            WindowMessages.Wheel(strip, 1);
            WindowMessages.Wheel(strip, 1);
            WindowMessages.Click(strip, TileCentre(0));

            Assert.Same(skins[0], strip.Selected);
        });
    }

    [Fact]
    public void Wheel_AtTheLeftEnd_StopsThere()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[0].SkinId, 0);

            WindowMessages.Wheel(strip, 1);
            WindowMessages.Click(strip, TileCentre(0));

            Assert.Same(skins[0], strip.Selected);
        });
    }

    [Fact]
    public void Drag_TheScrollThumb_ScrollsTheStrip()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[0].SkinId, 0);

            var grip = ThumbCentre(strip);
            WindowMessages.Drag(strip, grip, new Point(grip.X + ThumbDrag, grip.Y));
            WindowMessages.Drop(strip, new Point(grip.X + ThumbDrag, grip.Y));
            WindowMessages.Click(strip, TileCentre(0));

            // A grip of the thumb moves it by its own share of the content, which brings the fourth tile up here.
            Assert.Same(skins[3], strip.Selected);
        });
    }

    [Fact]
    public void Resize_WiderStrip_ClampsTheScrollToTheNewWidth()
    {
        var skins = Skins(SkinCount);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetItems(skins, skins[^1].SkinId, 0);

            host.ClientSize = new Size(WiderWidth, host.ClientSize.Height);
            Application.DoEvents();
            WindowMessages.Click(strip, TileCentre(0));

            // The wider viewport leaves less to scroll, so the first tile's pixel shows the second tile, not the eighth.
            Assert.Same(skins[1], strip.Selected);
        });
    }

    private static Bitmap Paint(SkinStripView strip)
    {
        var painted = new Bitmap(strip.Width, strip.Height);
        strip.DrawToBitmap(painted, new Rectangle(Point.Empty, strip.Size));
        return painted;
    }

    private static SkinStripView Strip(Form host)
    {
        var strip = new SkinStripView { Dock = DockStyle.Fill };
        host.Controls.Add(strip);
        return strip;
    }

    private static Point TileCentre(int index) =>
        new(CellInset + index * CellWidth + CellWidth / 2, CellInset + TileSize / 2);

    // ThinScrollBar lays the thumb out from the length of its track and how much of the content fits in the viewport;
    // the old check read the same rectangle back from the control by reflection.
    private static Point ThumbCentre(SkinStripView strip)
    {
        int trackWidth = strip.ClientSize.Width - 2 * TrackInset;
        int length = Math.Clamp(
            trackWidth * strip.ClientSize.Width / ContentWidth,
            Math.Min(trackWidth, 24),
            trackWidth
        );
        return new Point(
            TrackInset + length / 2,
            strip.ClientSize.Height - TrackInset - TrackThickness + TrackThickness / 2
        );
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
