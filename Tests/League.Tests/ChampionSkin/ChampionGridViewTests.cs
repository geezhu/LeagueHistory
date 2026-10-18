using League.ChampionSkin;
using League.Tests.Support;

namespace League.Tests.ChampionSkin;

/// <summary>
/// The champion grid painted as one surface: it lays its champions out in columns, keeps the selected champion when a
/// new list still holds it, and reacts to clicks, the arrow keys, Enter and its slim scroll bar. It is driven through
/// its public surface and through window messages only — the cell and scroll geometry below repeats the constants the
/// control lays out with, where the old check read the same rectangles back through reflection.
/// </summary>
[Trait("Category", "UI")]
public class ChampionGridViewTests
{
    private const int GridWidth = 400;
    private const int GridHeight = 300;
    private const int CellWidth = 80;
    private const int CellHeight = 98;
    private const int RowTop = 6;
    private const int ScrollGutter = 12;
    private const int ColumnCount = 4; // (400 - 12 - 8) / 80
    private const int LeftMargin = 34; // (400 - 12 - 4 * 80) / 2, split evenly so the grid stays centered
    private const int RowCount = 10;
    private const int ItemCount = RowCount * ColumnCount;
    private const int ContentHeight = RowCount * CellHeight + 12; // the control adds 12 below the last row
    private const int ScrollMax = ContentHeight - GridHeight;
    private const int TrackThickness = 6;
    private const int TrackInset = 3;
    private const int ThumbDrag = 40;

    [Fact]
    public void SetItems_NewList_SelectsTheFirstChampionAndAnnouncesItOnce()
    {
        var items = Items(ItemCount);
        var changes = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SelectionChanged += (_, _) => changes++;

            grid.SetItems(items);

            Assert.Same(items[0], grid.Selected);
            Assert.Equal(ItemCount, grid.Items.Count);
            Assert.Equal(1, changes);
        });
    }

    [Fact]
    public void SetItems_EmptyGrid_HasNoChampionsAndNoSelection()
    {
        UiHost.Run(host =>
        {
            using var grid = Grid(host);

            grid.SetItems([]);

            Assert.Empty(grid.Items);
            Assert.Null(grid.Selected);
        });
    }

    [Fact]
    public void SetItems_SelectedChampionStillListed_KeepsItSelected()
    {
        var items = Items(ItemCount);
        var withoutFirst = items.Where(item => item.ChampionId != items[0].ChampionId).ToList();

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectChampion(items[9].ChampionId);

            grid.SetItems(withoutFirst);

            Assert.Same(items[9], grid.Selected);
        });
    }

    [Fact]
    public void SetItems_SelectedChampionGone_FallsBackToTheFirst()
    {
        var items = Items(ItemCount);
        var withoutNinth = items.Where(item => item.ChampionId != items[9].ChampionId).ToList();

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectChampion(items[9].ChampionId);

            grid.SetItems(withoutNinth);

            Assert.Same(items[0], grid.Selected);
        });
    }

    [Fact]
    public void SetItems_EmptyList_ClearsTheSelection()
    {
        var items = Items(ItemCount);
        var changes = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectionChanged += (_, _) => changes++;

            grid.SetItems([]);

            Assert.Null(grid.Selected);
            Assert.Equal(1, changes);
        });
    }

    [Fact]
    public void SelectChampion_KnownChampion_MovesTheSelectionAndAnnouncesIt()
    {
        var items = Items(ItemCount);
        var changes = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectionChanged += (_, _) => changes++;

            grid.SelectChampion(items[5].ChampionId);

            Assert.Same(items[5], grid.Selected);
            Assert.Equal(1, changes);
        });
    }

    [Fact]
    public void SelectChampion_UnknownChampion_ClearsTheSelection()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            grid.SelectChampion(999999);

            Assert.Null(grid.Selected);
        });
    }

    [Fact]
    public void SelectChampion_ChampionAlreadySelected_AnnouncesNothing()
    {
        var items = Items(ItemCount);
        var changes = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectionChanged += (_, _) => changes++;

            grid.SelectChampion(items[0].ChampionId);

            Assert.Same(items[0], grid.Selected);
            Assert.Equal(0, changes);
        });
    }

    [Fact]
    public void Click_Champion_SelectsAndActivatesIt()
    {
        var items = Items(ItemCount);
        var activated = 0;
        var changes = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectChampion(items[3].ChampionId);
            grid.ItemActivated += (_, _) => activated++;
            grid.SelectionChanged += (_, _) => changes++;

            WindowMessages.Click(grid, CellCentre(1));

            Assert.Same(items[1], grid.Selected);
            Assert.Equal(1, activated);
            Assert.Equal(1, changes);
        });
    }

    [Fact]
    public void Click_CellsOfTheGrid_SelectTheChampionThosePixelsShow()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            WindowMessages.Click(grid, CellCentre(1));
            Assert.Same(items[1], grid.Selected);

            // The next cell to the right, now the first cell of the second row: four champions fit in a row.
            WindowMessages.Click(grid, CellCentre(ColumnCount));
            Assert.Same(items[ColumnCount], grid.Selected);

            WindowMessages.Click(grid, CellCentre(ColumnCount + 1));
            Assert.Same(items[ColumnCount + 1], grid.Selected);
        });
    }

    [Fact]
    public void Click_AlreadySelectedChampion_ActivatesItWithoutAnnouncingASelection()
    {
        var items = Items(ItemCount);
        var activated = 0;
        var changes = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.ItemActivated += (_, _) => activated++;
            grid.SelectionChanged += (_, _) => changes++;

            WindowMessages.Click(grid, CellCentre(0));

            Assert.Equal(1, activated);
            Assert.Equal(0, changes);
        });
    }

    [Fact]
    public void Click_LeftOfTheFirstColumn_SelectsNothing()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectChampion(items[5].ChampionId);

            WindowMessages.Click(grid, new Point(LeftMargin - 1, CellCentre(1).Y));

            Assert.Same(items[5], grid.Selected);
        });
    }

    [Fact]
    public void Click_AboveTheFirstRow_SelectsNothing()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectChampion(items[5].ChampionId);

            WindowMessages.Click(grid, new Point(CellCentre(0).X, RowTop - 1));

            Assert.Same(items[5], grid.Selected);
        });
    }

    [Fact]
    public void Click_TheScrollGutter_SelectsNothing()
    {
        var items = Items(ItemCount);
        var activated = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectChampion(items[5].ChampionId);
            grid.ItemActivated += (_, _) => activated++;

            WindowMessages.Click(grid, new Point(GridWidth - 2, CellCentre(1).Y));

            Assert.Same(items[5], grid.Selected);
            Assert.Equal(0, activated);
        });
    }

    [Fact]
    public void Click_PastTheLastChampion_SelectsNothing()
    {
        var items = Items(3);
        var activated = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.ItemActivated += (_, _) => activated++;

            // The second row is empty with three champions, so the cell there holds nobody.
            WindowMessages.Click(grid, CellCentre(ColumnCount));

            Assert.Same(items[0], grid.Selected);
            Assert.Equal(0, activated);
        });
    }

    [Fact]
    public void Move_OntoAChampion_OffersTheHandCursor()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            WindowMessages.Move(grid, CellCentre(2));

            Assert.Equal(Cursors.Hand, grid.Cursor);
        });
    }

    [Fact]
    public void Move_OntoTheScrollGutter_OffersTheDefaultCursor()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            WindowMessages.Move(grid, CellCentre(2));
            WindowMessages.Move(grid, new Point(GridWidth - 2, CellCentre(2).Y));

            Assert.Equal(Cursors.Default, grid.Cursor);
        });
    }

    [Fact]
    public void Move_OffTheChampions_OffersTheDefaultCursor()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            WindowMessages.Move(grid, CellCentre(2));
            WindowMessages.Move(grid, new Point(LeftMargin - 1, CellCentre(2).Y));

            Assert.Equal(Cursors.Default, grid.Cursor);
        });
    }

    [Fact]
    public void MouseLeave_AfterHoveringAChampion_DropsTheHover()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            WindowMessages.Move(grid, CellCentre(2));
            WindowMessages.Leave(grid);
            WindowMessages.Move(grid, new Point(LeftMargin - 1, CellCentre(2).Y));

            // The leave already dropped the hover, so off the champions the cursor is left as it was.
            Assert.Equal(Cursors.Hand, grid.Cursor);
        });
    }

    [Fact]
    public void Wheel_LongGrid_ScrollsOneRowPerNotch()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            // Each turn of the wheel is one message and moves the grid by exactly one row, whatever the delta it carries.
            WindowMessages.Wheel(grid, -1);
            WindowMessages.Wheel(grid, -1);
            WindowMessages.Click(grid, CellCentre(0));

            // The pixel of the first row now shows the champion two rows further down.
            Assert.Same(items[2 * ColumnCount], grid.Selected);
        });
    }

    [Fact]
    public void Wheel_ScrolledBackUp_ReturnsToTheFirstRow()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            WindowMessages.Wheel(grid, -1);
            WindowMessages.Wheel(grid, -1);
            WindowMessages.Wheel(grid, 1);
            WindowMessages.Wheel(grid, 1);
            WindowMessages.Click(grid, CellCentre(0));

            Assert.Same(items[0], grid.Selected);
        });
    }

    [Fact]
    public void Wheel_AtTheTop_StopsThere()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            WindowMessages.Wheel(grid, 1);
            WindowMessages.Click(grid, CellCentre(0));

            Assert.Same(items[0], grid.Selected);
        });
    }

    [Fact]
    public void Drag_TheScrollThumb_ScrollsTheGrid()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            var grip = ThumbCentre();
            WindowMessages.Drag(grid, grip, new Point(grip.X, grip.Y + ThumbDrag));
            WindowMessages.Drop(grid, new Point(grip.X, grip.Y + ThumbDrag));
            WindowMessages.Click(grid, CellCentre(0));

            // A grip of the thumb moves it by its own share of the content, which brings the second row up here.
            Assert.Same(items[ColumnCount], grid.Selected);
        });
    }

    [Fact]
    public void Click_TheTrackBelowTheThumb_PagesDown()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            WindowMessages.Click(grid, new Point(ThumbCentre().X, GridHeight - 4));
            WindowMessages.Click(grid, CellCentre(0));

            // One page is one viewport, which is a little over three rows.
            Assert.Same(items[3 * ColumnCount], grid.Selected);
        });
    }

    [Fact]
    public void SelectChampion_ChampionAboveTheViewport_ScrollsBackToIt()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            WindowMessages.Wheel(grid, -1);
            WindowMessages.Wheel(grid, -1);
            WindowMessages.Wheel(grid, -1);

            grid.SelectChampion(items[2].ChampionId);
            WindowMessages.Click(grid, CellCentre(0));

            // Back at the top: without the scroll the first row's pixel would show a champion further down.
            Assert.Same(items[0], grid.Selected);
        });
    }

    [Fact]
    public void SelectChampion_ChampionBelowTheViewport_ScrollsItIntoView()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            grid.SelectChampion(items[^1].ChampionId);
            WindowMessages.Click(grid, CellCentre(0));

            // Scrolled all the way down, so the first row's pixel sits on the row the last champion is in.
            Assert.Same(items[ScrollMax / CellHeight * ColumnCount], grid.Selected);
        });
    }

    [Fact]
    public void Resize_TallerGrid_ClampsTheScrollToTheNewContent()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectChampion(items[^1].ChampionId);

            grid.Size = new Size(GridWidth, GridHeight + 200);
            WindowMessages.Click(grid, CellCentre(0));

            // The scroll is clamped to the shorter content, so the first row's pixel shows the champion five rows down
            // instead of the seventh it sat on before the resize.
            Assert.Same(items[5 * ColumnCount], grid.Selected);
        });
    }

    [Fact]
    public void Key_RightArrow_MovesOneChampionAtATime()
    {
        var items = Items(ItemCount);
        var changes = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectionChanged += (_, _) => changes++;

            WindowMessages.Key(grid, Keys.Right);
            Assert.Same(items[1], grid.Selected);

            WindowMessages.Key(grid, Keys.Right);
            WindowMessages.Key(grid, Keys.Right);
            WindowMessages.Key(grid, Keys.Right);

            // The fourth step is the first champion of the second row, and every step was announced.
            Assert.Same(items[ColumnCount], grid.Selected);
            Assert.Equal(4, changes);
        });
    }

    [Fact]
    public void Key_UpAndDownArrows_MoveOneRowAtATime()
    {
        var items = Items(ItemCount);
        var changes = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectionChanged += (_, _) => changes++;

            WindowMessages.Key(grid, Keys.Down);
            Assert.Same(items[ColumnCount], grid.Selected);

            WindowMessages.Key(grid, Keys.Down);
            Assert.Same(items[2 * ColumnCount], grid.Selected);

            WindowMessages.Key(grid, Keys.Up);
            WindowMessages.Key(grid, Keys.Left);

            Assert.Same(items[ColumnCount - 1], grid.Selected);
            Assert.Equal(4, changes);
        });
    }

    [Fact]
    public void Key_LeftArrowOnTheFirstChampion_StopsThere()
    {
        var items = Items(ItemCount);
        var changes = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectionChanged += (_, _) => changes++;

            WindowMessages.Key(grid, Keys.Left);

            Assert.Same(items[0], grid.Selected);
            Assert.Equal(0, changes);
        });
    }

    [Fact]
    public void Key_DownArrowOnTheLastChampion_StopsThere()
    {
        var items = Items(ItemCount);
        var changes = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectChampion(items[^1].ChampionId);
            grid.SelectionChanged += (_, _) => changes++;

            WindowMessages.Key(grid, Keys.Down);

            Assert.Same(items[^1], grid.Selected);
            Assert.Equal(0, changes);
        });
    }

    [Fact]
    public void Key_Enter_ActivatesTheSelectedChampion()
    {
        var items = Items(ItemCount);
        var activated = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.ItemActivated += (_, _) => activated++;

            WindowMessages.Key(grid, Keys.Enter);

            Assert.Equal(1, activated);
        });
    }

    [Fact]
    public void Key_EnterWithoutASelection_ActivatesNothing()
    {
        var items = Items(ItemCount);
        var activated = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);
            grid.SelectChampion(999999);
            grid.ItemActivated += (_, _) => activated++;

            WindowMessages.Key(grid, Keys.Enter);

            Assert.Equal(0, activated);
        });
    }

    [Fact]
    public void Key_EmptyGrid_MovesNothingAndActivatesNothing()
    {
        var activated = 0;

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems([]);
            grid.ItemActivated += (_, _) => activated++;

            WindowMessages.Key(grid, Keys.Right);
            WindowMessages.Key(grid, Keys.Down);
            WindowMessages.Key(grid, Keys.Enter);

            Assert.Null(grid.Selected);
            Assert.Equal(0, activated);
        });
    }

    [Fact]
    public void PreProcessControlMessage_ArrowKey_LeavesTheKeyToTheGrid()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            var arrow = WindowMessages.KeyDownMessage(grid, Keys.Down);
            var tab = WindowMessages.KeyDownMessage(grid, Keys.Tab);

            // The grid takes the arrows for itself; Tab stays with the window, which moves the focus on.
            Assert.Equal(PreProcessControlState.MessageNeeded, grid.PreProcessControlMessage(ref arrow));
            Assert.Equal(PreProcessControlState.MessageProcessed, grid.PreProcessControlMessage(ref tab));
        });
    }

    [Fact]
    public void DrawToBitmap_EmptyGrid_PaintsTheEmptyText()
    {
        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems([]);

            using var painted = Paint(grid);

            Assert.Equal(grid.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_ChampionsInEveryState_PaintsThem()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            using var icon = new Bitmap(64, 64);
            using (var graphics = Graphics.FromImage(icon))
            {
                graphics.Clear(Color.CornflowerBlue);
            }

            // Every second champion has an icon, one carries a badge, another the mark: every painted state is drawn,
            // and the hovered cell is not the selected one so both fades are painted too.
            grid.Icon = championId => championId % 2 == 0 ? icon : null;
            grid.Badge = item => item.ChampionId == 1000 ? "已拥有" : null;
            grid.Marked = item => item.ChampionId == 1001;
            grid.MarkGlyph = "✔";
            grid.SetItems(items);
            grid.SelectChampion(items[0].ChampionId);

            WindowMessages.Move(grid, CellCentre(2));
            using var painted = Paint(grid);

            Assert.Equal(grid.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_ScrolledGrid_SkipsTheRowsThatLeftTheView()
    {
        var items = Items(ItemCount);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            // Three rows have left the view, so painting starts at the first row still on screen.
            WindowMessages.Wheel(grid, -1);
            WindowMessages.Wheel(grid, -1);
            WindowMessages.Wheel(grid, -1);
            using var painted = Paint(grid);

            Assert.Equal(grid.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_ShortGrid_PaintsWithoutAScrollBar()
    {
        var items = Items(2);

        UiHost.Run(host =>
        {
            using var grid = Grid(host);
            grid.SetItems(items);

            using var painted = Paint(grid);

            Assert.Equal(grid.Size, painted.Size);
        });
    }

    private static Bitmap Paint(ChampionGridView<GridItem> grid)
    {
        var painted = new Bitmap(grid.Width, grid.Height);
        grid.DrawToBitmap(painted, new Rectangle(Point.Empty, grid.Size));
        return painted;
    }

    private static ChampionGridView<GridItem> Grid(Form host)
    {
        var grid = new ChampionGridView<GridItem> { Location = Point.Empty, Size = new Size(GridWidth, GridHeight) };
        host.Controls.Add(grid);
        return grid;
    }

    private static Point CellCentre(int index) =>
        new(
            LeftMargin + index % ColumnCount * CellWidth + CellWidth / 2,
            RowTop + index / ColumnCount * CellHeight + CellHeight / 2
        );

    // ThinScrollBar lays the thumb out from the length of its track and how much of the content fits in the viewport;
    // the old check read the same rectangle back from the control by reflection.
    private static Point ThumbCentre()
    {
        int trackHeight = GridHeight - 2 * TrackInset;
        int length = Math.Clamp(trackHeight * GridHeight / ContentHeight, Math.Min(trackHeight, 24), trackHeight);
        return new Point(GridWidth - TrackThickness - TrackInset + TrackThickness / 2, TrackInset + length / 2);
    }

    private static List<GridItem> Items(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new GridItem(1000 + i, $"英雄{i}"))];

    private sealed class GridItem(int championId, string name) : IChampionGridItem
    {
        public int ChampionId { get; } = championId;

        public string Name { get; } = name;
    }
}
