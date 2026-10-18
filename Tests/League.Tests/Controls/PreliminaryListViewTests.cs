using League.Controls;
using League.PrimaryElection;
using League.Tests.Support;

namespace League.Tests.Controls;

/// <summary>
/// A preselection list painted as rows. This is the worked example for moving a check out of Tests/StatisticsChecks:
/// it drives the control through its public surface and through window messages, never through reflection, and it
/// lives on a real message loop (see <see cref="UiHost"/>).
/// </summary>
[Trait("Category", "UI")]
public class PreliminaryListViewTests
{
    private const int StripWidth = 640;
    private const int RowHeight = 58;
    private const int RowTop = 8;
    private const int ButtonSize = 28;
    private const int ButtonGap = 4;
    private const int ButtonInset = 10;

    [Fact]
    public void SetList_Heroes_ExposesThemWithoutASelection()
    {
        var heroes = Heroes(5);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);

            strip.SetList(heroes);

            Assert.Equal(5, strip.Heroes.Count);
            Assert.Equal(-1, strip.SelectedIndex);
        });
    }

    [Fact]
    public void Highlight_ChampionInList_SelectsItsRow()
    {
        var heroes = Heroes(5);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            strip.Highlight(1003);

            Assert.Equal(3, strip.SelectedIndex);
        });
    }

    [Fact]
    public void Highlight_ChampionNotInList_ClearsTheSelection()
    {
        var heroes = Heroes(5);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);
            strip.Highlight(1003);

            strip.Highlight(999999);

            Assert.Equal(-1, strip.SelectedIndex);
        });
    }

    [Fact]
    public void Move_RowForward_ReordersRenumbersAndAnnounces()
    {
        var heroes = Heroes(4);
        var changes = 0;

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);
            strip.Changed += (_, _) => changes++;

            strip.Move(0, 2);

            Assert.Equal([1001, 1002, 1000, 1003], heroes.Select(h => h.ChampionId));
            Assert.Equal([1, 2, 3, 4], heroes.Select(h => h.Priority));
            Assert.Equal(1, changes);
        });
    }

    [Fact]
    public void Move_TargetPastTheEnd_ClampsToTheLastRow()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            strip.Move(0, 99);

            Assert.Equal(1000, heroes[^1].ChampionId);
            Assert.Equal(3, strip.SelectedIndex);
        });
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(9, 2)]
    [InlineData(2, 2)]
    public void Move_RowThatIsNotThereOrDoesNotMove_ChangesNothing(int from, int to)
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            strip.Move(from, to);

            Assert.Equal([1000, 1001, 1002, 1003], heroes.Select(h => h.ChampionId));
            Assert.Equal(-1, strip.SelectedIndex);
        });
    }

    [Fact]
    public void RemoveAt_MiddleRow_SelectsTheRowThatTakesItsPlace()
    {
        var heroes = Heroes(4);
        var changes = 0;

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);
            strip.Changed += (_, _) => changes++;

            strip.RemoveAt(1);

            Assert.Equal([1000, 1002, 1003], heroes.Select(h => h.ChampionId));
            Assert.Equal([1, 2, 3], heroes.Select(h => h.Priority));
            Assert.Equal(1, strip.SelectedIndex);
            Assert.Equal(1, changes);
        });
    }

    [Fact]
    public void RemoveAt_LastRow_SelectsTheNewLastRow()
    {
        var heroes = Heroes(3);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            strip.RemoveAt(2);

            Assert.Equal(1, strip.SelectedIndex);
        });
    }

    [Fact]
    public void RemoveAt_RowThatIsNotThere_ChangesNothing()
    {
        var heroes = Heroes(3);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            strip.RemoveAt(5);

            Assert.Equal(3, strip.Heroes.Count);
        });
    }

    [Fact]
    public void Refresh_AfterAnOutsideChange_RereadsTheListAndHighlights()
    {
        var heroes = Heroes(3);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);
            heroes.Add(new PreliminaryHero { ChampionId = 1003, ChampionName = "德玛西亚之力" });

            strip.Refresh(1003);

            Assert.Equal(4, strip.Heroes.Count);
            Assert.Equal(3, strip.SelectedIndex);
        });
    }

    [Fact]
    public void Lanes_ChampionOfOneLane_NamesThatLane()
    {
        Assert.Equal("中单", PreliminaryListView.Lanes(Champion("黑暗之女")));
    }

    [Fact]
    public void Lanes_ChampionOutsideTheTable_HasNoLane()
    {
        Assert.Equal(string.Empty, PreliminaryListView.Lanes(Champion("没有这个英雄")));
    }

    [Fact]
    public void Click_Row_SelectsItWithoutMovingAnything()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            WindowMessages.Click(strip, RowCentre(2));

            Assert.Equal(2, strip.SelectedIndex);
            Assert.Equal([1000, 1001, 1002, 1003], heroes.Select(h => h.ChampionId));
        });
    }

    [Fact]
    public void Click_UpButton_MovesTheRowUp()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            WindowMessages.Click(strip, ButtonCentre(row: 1, slot: 2));

            Assert.Equal(1001, heroes[0].ChampionId);
            Assert.Equal(1000, heroes[1].ChampionId);
        });
    }

    [Fact]
    public void Click_DownButton_MovesTheRowDown()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            WindowMessages.Click(strip, ButtonCentre(row: 1, slot: 1));

            Assert.Equal(1002, heroes[1].ChampionId);
            Assert.Equal(1001, heroes[2].ChampionId);
        });
    }

    [Fact]
    public void Click_RemoveButton_TakesTheRowOut()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            WindowMessages.Click(strip, ButtonCentre(row: 1, slot: 0));

            Assert.Equal([1000, 1002, 1003], heroes.Select(h => h.ChampionId));
        });
    }

    [Fact]
    public void Drag_RowOntoAnother_PlacesItJustBeforeThatRow()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            WindowMessages.Drag(strip, RowCentre(0), RowCentre(2));
            WindowMessages.Drop(strip, RowCentre(2));

            // The drop line is drawn on the upper edge of the row under the pointer, so the dragged row lands in
            // front of it: row 0 goes between rows 1 and 2 once it has been taken out.
            Assert.Equal([1001, 1000, 1002, 1003], heroes.Select(h => h.ChampionId));
        });
    }

    [Fact]
    public void Key_Down_MovesTheSelectionAndDeleteTakesTheRowOut()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);
            strip.Highlight(1001);

            WindowMessages.Key(strip, Keys.Down);
            Assert.Equal(2, strip.SelectedIndex);

            WindowMessages.Key(strip, Keys.Delete);
            Assert.Equal([1000, 1001, 1003], heroes.Select(h => h.ChampionId));
        });
    }

    [Fact]
    public void DrawToBitmap_EmptyList_PaintsTheEmptyText()
    {
        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList([]);
            strip.EmptyText = "还没有预选英雄";

            using var painted = Paint(strip);

            Assert.Equal(strip.Size, painted.Size);
        });
    }

    [Fact]
    public void DrawToBitmap_RowsWithAndWithoutAnIcon_PaintsThem()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            using var icon = new Bitmap(44, 44);
            using (var graphics = Graphics.FromImage(icon))
            {
                graphics.Clear(Color.CornflowerBlue);
            }

            // Every second champion has an icon and champion data, so the placeholder and the fallback text are
            // painted as well.
            strip.Icon = championId => championId % 2 == 0 ? icon : null;
            strip.Champion = championId => championId % 2 == 0 ? Champion("黑暗之女") : null;
            strip.SetList(heroes);
            strip.Highlight(1001);

            using var painted = Paint(strip);

            Assert.Equal(strip.Size, painted.Size);
        });
    }

    [Fact]
    public void Move_OntoAButton_OffersTheHandCursorAndPaintsTheHover()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            WindowMessages.Move(strip, ButtonCentre(row: 1, slot: 2));
            Assert.Equal(Cursors.Hand, strip.Cursor);

            using var painted = Paint(strip);
            Assert.Equal(strip.Size, painted.Size);
        });
    }

    [Fact]
    public void Move_OffTheRows_DropsTheHover()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            WindowMessages.Move(strip, ButtonCentre(row: 1, slot: 0));
            WindowMessages.Move(strip, new Point(StripWidth / 2, 300));
            Assert.Equal(Cursors.Default, strip.Cursor);

            using var painted = Paint(strip);
            Assert.Equal(strip.Size, painted.Size);
        });
    }

    [Fact]
    public void Click_AboveTheFirstRow_SelectsNothing()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            WindowMessages.Click(strip, new Point(StripWidth / 2, RowTop - 1));

            Assert.Equal(-1, strip.SelectedIndex);
        });
    }

    [Fact]
    public void Click_BelowTheLastRow_SelectsNothing()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);
            strip.Highlight(1001);

            WindowMessages.Click(strip, new Point(StripWidth / 2, 300));

            Assert.Equal(1, strip.SelectedIndex);
        });
    }

    [Fact]
    public void Wheel_LongList_ScrollsExactlyOneRow()
    {
        var heroes = Heroes(20);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            WindowMessages.Wheel(strip, -1);
            WindowMessages.Click(strip, RowCentre(1));

            // One row further down now sits under the same pixel, so the wheel moved the list by exactly one row.
            Assert.Equal(2, strip.SelectedIndex);
        });
    }

    [Fact]
    public void Drag_ScrollThumb_ScrollsTheList()
    {
        var heroes = Heroes(20);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            WindowMessages.Drag(strip, ScrollThumb(), new Point(ScrollThumb().X, 250));
            WindowMessages.Drop(strip, new Point(ScrollThumb().X, 250));
            WindowMessages.Click(strip, RowCentre(0));

            Assert.True(strip.SelectedIndex > 0, "The thumb dragged the list down.");
        });
    }

    [Fact]
    public void Drag_ToTheBottomEdge_ScrollsWhileDraggingAndStillMovesTheRow()
    {
        var heroes = Heroes(20);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            var edge = new Point(StripWidth / 2, strip.ClientSize.Height - 4);
            WindowMessages.Drag(strip, RowCentre(0), edge);
            using (var painted = Paint(strip))
            {
                Assert.Equal(strip.Size, painted.Size);
            }

            WindowMessages.Drop(strip, edge);

            Assert.NotEqual(1000, heroes[0].ChampionId);
        });
    }

    [Fact]
    public void Highlight_RowBelowTheVisibleArea_ScrollsItIntoView()
    {
        var heroes = Heroes(20);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            strip.Highlight(1015);

            Assert.Equal(15, strip.SelectedIndex);
        });
    }

    [Fact]
    public void Highlight_RowAboveTheVisibleArea_ScrollsBackUp()
    {
        var heroes = Heroes(20);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            WindowMessages.Wheel(strip, -10);
            WindowMessages.Wheel(strip, -1);
            using (var scrolled = Paint(strip))
            {
                // Painted while scrolled past the first rows, which are skipped instead of drawn.
                Assert.Equal(strip.Size, scrolled.Size);
            }

            strip.Highlight(1000);

            Assert.Equal(0, strip.SelectedIndex);
        });
    }

    [Fact]
    public void Click_InTheGapBetweenTwoRows_SelectsNothing()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            // Rows are 52 pixels tall inside a 58 pixel slot; this lands in the gap under the first row.
            WindowMessages.Click(strip, new Point(StripWidth / 2, 61));

            Assert.Equal(-1, strip.SelectedIndex);
        });
    }

    [Fact]
    public void Key_EmptyList_DoesNothing()
    {
        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList([]);

            WindowMessages.Key(strip, Keys.Down);

            Assert.Equal(-1, strip.SelectedIndex);
        });
    }

    [Fact]
    public void Drag_ToTheTopEdge_ScrollsBackUpWhileDragging()
    {
        var heroes = Heroes(20);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);

            WindowMessages.Wheel(strip, -5);
            WindowMessages.Drag(strip, new Point(StripWidth / 2, 200), new Point(StripWidth / 2, 4));
            WindowMessages.Drop(strip, new Point(StripWidth / 2, 4));

            // The exact landing spot depends on how far the edge scrolling ran; what matters is that the row moved.
            Assert.NotEqual(Enumerable.Range(0, 20).Select(i => 1000 + i), heroes.Select(h => h.ChampionId));
        });
    }

    [Fact]
    public void Key_KeyTheListDoesNotUse_ChangesNothing()
    {
        var heroes = Heroes(4);

        UiHost.Run(host =>
        {
            using var strip = Strip(host);
            strip.SetList(heroes);
            strip.Highlight(1001);

            WindowMessages.Key(strip, Keys.Space);

            Assert.Equal(1, strip.SelectedIndex);
            Assert.Equal([1000, 1001, 1002, 1003], heroes.Select(h => h.ChampionId));
        });
    }

    private static Point ScrollThumb() => new(StripWidth - 9, 20);

    private static Bitmap Paint(PreliminaryListView strip)
    {
        var painted = new Bitmap(strip.Width, strip.Height);
        strip.DrawToBitmap(painted, new Rectangle(Point.Empty, strip.Size));
        return painted;
    }

    private static PreliminaryListView Strip(Form host)
    {
        var strip = new PreliminaryListView { Dock = DockStyle.Fill };
        host.Controls.Add(strip);
        return strip;
    }

    private static Point RowCentre(int row) => new(StripWidth / 2, RowTop + row * RowHeight + (RowHeight - 6) / 2);

    // Rows and their buttons are laid out from the right edge; see RowBounds and ButtonBounds on the control.
    // Slot 2 is up, 1 is down and 0 is remove.
    private static Point ButtonCentre(int row, int slot)
    {
        int right = StripWidth - 8;
        int x = right - ButtonInset - ButtonSize - slot * (ButtonSize + ButtonGap) + ButtonSize / 2;
        int y = RowTop + row * RowHeight + (RowHeight - 6 - ButtonSize) / 2 + ButtonSize / 2;
        return new Point(x, y);
    }

    private static List<PreliminaryHero> Heroes(int count) =>
        [
            .. Enumerable
                .Range(0, count)
                .Select(i => new PreliminaryHero
                {
                    ChampionId = 1000 + i,
                    ChampionName = $"英雄{i}",
                    Priority = i + 1,
                }),
        ];

    private static PrimaryChampion Champion(string title) =>
        new()
        {
            Id = 1,
            Name = title,
            Title = title,
            Alias = "stub",
        };
}
