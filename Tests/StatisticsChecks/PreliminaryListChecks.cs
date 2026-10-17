using System.Reflection;
using League.Controls;
using League.PrimaryElection;
using static StatisticsCheckSupport;

/// <summary>
/// The preselection list control: reordering and removing, hit testing a row and its three buttons, dragging a row to
/// a new place, the scroll bar it forwards mouse events to, the keyboard shortcuts and every painted row state. All
/// input is synthetic and the window stays off screen, so no real mouse or keyboard is used.
/// </summary>
internal static class PreliminaryListChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        Lanes();
        Editing();
        Hovering();
        Dragging();
        Buttons();
        Scrolling();
        Keyboard();
        Painting();
        Console.WriteLine(
            "PASS: the preselection list reorders and removes rows, hit tests rows and buttons, drags a row with "
                + "automatic scrolling, forwards the wheel and the scroll bar, answers the keyboard and paints every "
                + "row state from stub art."
        );
    }

    private static void Lanes()
    {
        var positions =
            (Dictionary<string, List<ChampionPosition>>)
                typeof(ChampionPositionMapper)
                    .GetField("_positionMap", BindingFlags.Static | BindingFlags.NonPublic)!
                    .GetValue(null)!;
        var lanes = new HashSet<string>();
        foreach (string name in positions.Keys)
        {
            lanes.UnionWith(
                PreliminaryListView
                    .Lanes(new PrimaryChampion { Name = name, Title = "称号" })
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
            );
        }

        Check(
            lanes.IsSupersetOf(new[] { "上单", "打野", "中单", "下路", "辅助" }),
            "Every lane in the position table has a label: " + string.Join("/", lanes.Order())
        );
        Check(
            PreliminaryListView.Lanes(new PrimaryChampion { Name = "不在表里的英雄" }) == "",
            "A champion outside the position table shows no lane"
        );
    }

    private static void Editing()
    {
        using var fixture = new Fixture();
        var heroes = fixture.Heroes(6);
        int[] ids = heroes.Select(h => h.ChampionId).ToArray();
        fixture.View.SetList(heroes);
        Check(fixture.View.Heroes.Count == 6 && fixture.View.SelectedIndex == -1, "A stored list starts unselected");

        fixture.View.Move(-1, 2);
        fixture.View.Move(6, 2);
        fixture.View.Move(2, 2);
        Check(
            fixture.View.Heroes.Select(h => h.ChampionId).SequenceEqual(ids),
            "Moving from outside the list or onto the same row changes nothing"
        );
        fixture.View.RemoveAt(-1);
        fixture.View.RemoveAt(6);
        Check(fixture.View.Heroes.Count == 6, "Removing outside the list changes nothing");

        fixture.View.Highlight(999999);
        Check(fixture.View.SelectedIndex == -1, "Highlighting an absent champion selects nothing");
        fixture.View.Highlight(heroes[^1].ChampionId);
        Check(
            fixture.View.SelectedIndex == 5 && ScrollValue(fixture.View) > 0,
            "Highlighting a row below the view scrolls it in"
        );
        fixture.View.Highlight(heroes[0].ChampionId);
        Check(ScrollValue(fixture.View) == 0, "Highlighting a row above the view scrolls back up");
        fixture.View.Refresh(heroes[2].ChampionId);
        Check(fixture.View.SelectedIndex == 2, "Refreshing re-reads the list and selects the champion");
    }

    private static void Hovering()
    {
        using var fixture = new Fixture();
        var list = fixture.View;
        list.SetList(fixture.Heroes(6));

        var row = Row(list, 1);
        Hover(list, Centre(row));
        Check(
            Private<int>(list, "_hover") == 1 && Private<object>(list, "_hoverButton")!.ToString() == "None",
            "Hovering a row body highlights the row"
        );
        Check(list.Cursor == Cursors.Default, "A row body offers no special cursor");

        Hover(list, Centre(Button(list, row, "Up")));
        Check(
            Private<object>(list, "_hoverButton")!.ToString() == "Up" && list.Cursor == Cursors.Hand,
            "Hovering a row button offers the hand cursor"
        );
        Hover(list, new Point(2, 4));
        Check(Private<int>(list, "_hover") == -1 && list.Cursor == Cursors.Default, "Hovering the padding clears it");

        Hover(list, Centre(Button(list, row, "Down")));
        Invoke(list, "OnMouseLeave", EventArgs.Empty);
        Check(
            Private<int>(list, "_hover") == -1 && Private<object>(list, "_hoverButton")!.ToString() == "None",
            "Leaving the list drops the hover"
        );
    }

    private static void Dragging()
    {
        using var fixture = new Fixture();
        var list = fixture.View;
        var heroes = fixture.Heroes(6);
        list.SetList(heroes);

        var pressed = Centre(Row(list, 1));
        var ids = heroes.Select(h => h.ChampionId).ToArray();
        Press(list, pressed, down: true);
        Check(
            list.SelectedIndex == 1 && Private<int>(list, "_pressed") == 1 && !Private<bool>(list, "_dragging"),
            "Pressing a row selects it and remembers the press"
        );
        Hover(list, new Point(pressed.X + 2, pressed.Y + 2));
        Check(!Private<bool>(list, "_dragging"), "A tiny movement is still a press");

        Hover(list, new Point(pressed.X + 40, pressed.Y + 40));
        Check(
            Private<bool>(list, "_dragging") && list.Cursor == Cursors.SizeNS,
            "A longer movement starts dragging the row"
        );
        Hover(list, new Point(pressed.X + 40, 4));
        Check(ScrollValue(list) == 0, "Dragging above the list does not scroll past the start");
        Hover(list, new Point(pressed.X + 40, list.ClientSize.Height - 4));
        Check(ScrollValue(list) > 0, "Dragging below the list scrolls toward the end");
        Check(Private<int>(list, "_dropIndex") >= 0, "The drag knows where the row would land");
        Paint(list);

        Hover(list, new Point(pressed.X + 40, pressed.Y + 80));
        Press(list, pressed, down: false);
        Check(!Private<bool>(list, "_dragging") && list.Cursor == Cursors.Default, "Releasing ends the drag");
        Check(
            list.Heroes[2].ChampionId == ids[1],
            "The row lands where the drop line was drawn: " + string.Join(",", list.Heroes.Select(h => h.ChampionId))
        );
        Check(
            list.Heroes.Select(h => h.Priority).SequenceEqual(new[] { 1, 2, 3, 4, 5, 6 }),
            "A dropped row renumbers the priorities"
        );
    }

    private static void Buttons()
    {
        using var fixture = new Fixture();
        var list = fixture.View;
        var heroes = fixture.Heroes(6);
        int[] ids = heroes.Select(h => h.ChampionId).ToArray();
        list.SetList(heroes);

        Press(list, Centre(Button(list, Row(list, 2), "Up")), down: true);
        Check(
            list.Heroes[1].ChampionId == ids[2] && list.Heroes[2].ChampionId == ids[1],
            "The up button moves the row one place"
        );
        Press(list, Centre(Button(list, Row(list, 0), "Down")), down: true);
        Check(list.Heroes[1].ChampionId == ids[0], "The down button moves the row one place");
        Press(list, Centre(Button(list, Row(list, 0), "Remove")), down: true);
        Check(list.Heroes.Count == 5 && list.Heroes[0].ChampionId == ids[0], "The remove button drops the row");

        int selected = list.SelectedIndex;
        Press(list, new Point(4, 4), down: true);
        Press(list, new Point(Row(list, 0).Left + 4, Row(list, 0).Bottom + 2), down: true);
        Check(
            list.SelectedIndex == selected && list.Heroes.Count == 5,
            "A press above the first row or in the gap between rows is ignored"
        );
    }

    private static void Scrolling()
    {
        using var fixture = new Fixture();
        var list = fixture.View;
        list.SetList(fixture.Heroes(6));
        var track = Private<Rectangle>(list, "Track");
        Check(
            ScrollValue(list) == 0 && ScrollNeeded(list),
            "Six rows do not fit the view, so the list starts at the top"
        );

        Press(list, new Point(track.Left + 2, track.Bottom - 2), down: true);
        Check(ScrollValue(list) > 0, "Clicking the track below the thumb pages toward the click");

        var grip = Centre(Thumb(list, track));
        Press(list, grip, down: true);
        Check(ScrollDragging(list), "Pressing the thumb starts a scroll drag");
        Hover(list, new Point(grip.X, track.Top));
        Check(ScrollValue(list) == 0, "Moving with the thumb held drags the list to the top");
        Press(list, new Point(grip.X, track.Top), down: false);
        Check(!ScrollDragging(list), "Releasing the mouse ends the scroll drag");

        Wheel(list, -120);
        Wheel(list, -120);
        Check(ScrollValue(list) > 0, "The wheel scrolls the list down");
        Wheel(list, 120);
        Wheel(list, 120);
        Wheel(list, 120);
        Check(ScrollValue(list) == 0, "The wheel scrolls back and stops at the top");
    }

    private static void Keyboard()
    {
        using var fixture = new Fixture();
        var list = fixture.View;
        var heroes = fixture.Heroes(6);
        int[] ids = heroes.Select(h => h.ChampionId).ToArray();
        list.SetList(heroes);
        Check(
            Private<bool>(list, "IsInputKey", Keys.Up) && Private<bool>(list, "IsInputKey", Keys.Down),
            "The arrows reach the list as input keys"
        );
        Check(!Private<bool>(list, "IsInputKey", Keys.A), "A letter key stays with the dialog");

        var down = new KeyEventArgs(Keys.Down);
        Invoke(list, "OnKeyDown", down);
        Check(list.SelectedIndex == 1 && down.Handled, "The down arrow moves the selection and marks the key used");
        Invoke(list, "OnKeyDown", new KeyEventArgs(Keys.Up));
        Invoke(list, "OnKeyDown", new KeyEventArgs(Keys.Up));
        Check(list.SelectedIndex == 0, "The up arrow stops at the first row");
        Invoke(list, "OnKeyDown", new KeyEventArgs(Keys.A));
        Check(list.SelectedIndex == 0, "A key the list does not use leaves the selection alone");

        Invoke(list, "OnKeyDown", new KeyEventArgs(Keys.Control | Keys.Up));
        Check(list.Heroes[0].ChampionId == ids[0], "Moving the first row further up changes nothing");
        Invoke(list, "OnKeyDown", new KeyEventArgs(Keys.Control | Keys.Down));
        Check(list.Heroes[0].ChampionId == ids[1], "Control and down moves the selected row");
        Invoke(list, "OnKeyDown", new KeyEventArgs(Keys.Delete));
        Check(
            list.Heroes.Count == 5 && !list.Heroes.Any(h => h.ChampionId == ids[0]),
            "Delete removes the selected row"
        );

        list.SetList(new List<PreliminaryHero>());
        Invoke(list, "OnKeyDown", new KeyEventArgs(Keys.Delete));
        Check(list.Heroes.Count == 0, "An empty list ignores the keyboard");
    }

    private static void Painting()
    {
        using (var empty = new Fixture())
        {
            empty.View.EmptyText = "还没有英雄";
            empty.View.SetList(new List<PreliminaryHero>());
            Paint(empty.View);
            Check(empty.View.Heroes.Count == 0, "An empty list paints its placeholder text");
        }

        // The default icon and champion lookups make every row fall back to the placeholder art and the
        // "not in the catalog" detail line, which is what a missing champion folder shows.
        using var fixture = new Fixture();
        var list = fixture.View;
        list.SetList(fixture.Heroes(6));
        Paint(list);
        Check(list.Heroes.Count == 6, "A row without an icon or champion data still paints");

        Hover(list, Centre(Row(list, 1)));
        Paint(list);
        Hover(list, Centre(Button(list, Row(list, 1), "Up")));
        Paint(list);
        Hover(list, Centre(Button(list, Row(list, 1), "Remove")));
        Paint(list);
        Press(list, Centre(Row(list, 0)), down: true);
        Paint(list);
        Press(list, Centre(Row(list, 0)), down: false);

        Private<bool>(Fixture.Scroll(list), "ScrollTo", 100);
        Paint(list);
        Check(ScrollValue(list) == 100, "A scrolled list paints only the rows still inside the view");

        // With an icon and champion data every row draws real art and the lane summary of the champion folder.
        using (var art = new Fixture())
        {
            using var icon = new Bitmap(44, 44);
            using (var graphics = Graphics.FromImage(icon))
            {
                graphics.Clear(Color.CornflowerBlue);
            }

            art.View.Icon = _ => icon;
            art.View.Champion = id => new PrimaryChampion
            {
                Id = id,
                Name = "腕豪",
                Title = "瑟提",
            };
            art.View.SetList(art.Heroes(3));
            Paint(art.View);
            Check(art.View.Heroes.Count == 3, "A row with an icon and champion data paints the lane summary");
        }
    }

    private static int ScrollValue(PreliminaryListView list) => Private<int>(Fixture.Scroll(list), "Value");

    private static bool ScrollNeeded(PreliminaryListView list) => Private<bool>(Fixture.Scroll(list), "Needed");

    private static bool ScrollDragging(PreliminaryListView list) => Private<bool>(Fixture.Scroll(list), "Dragging");

    private static Rectangle Thumb(PreliminaryListView list, Rectangle track) =>
        Private<Rectangle>(Fixture.Scroll(list), "Thumb", track);

    private static Rectangle Row(PreliminaryListView list, int index) => Private<Rectangle>(list, "RowBounds", index);

    private static Rectangle Button(PreliminaryListView list, Rectangle row, string name) =>
        Private<Rectangle>(list, "ButtonBounds", row, Enum.Parse(RowButton(list), name));

    private static Type RowButton(PreliminaryListView list) =>
        list.GetType().GetNestedType("RowButton", BindingFlags.NonPublic)!;

    private static Point Centre(Rectangle rectangle) =>
        new(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);

    private static void Hover(Control control, Point point) =>
        Invoke(control, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0));

    private static void Press(Control control, Point point, bool down) =>
        Invoke(
            control,
            down ? "OnMouseDown" : "OnMouseUp",
            new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)
        );

    private static void Wheel(Control control, int delta) =>
        Invoke(control, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, delta));

    private static void Paint(Control control)
    {
        using var bitmap = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height));
        using var graphics = Graphics.FromImage(bitmap);
        using var args = new PaintEventArgs(graphics, new Rectangle(Point.Empty, control.ClientSize));
        Invoke(control, "OnPaint", args);
    }

    private static void Invoke(object owner, string name, params object[] args) =>
        owner.GetType().GetMethod(name, Flags)!.Invoke(owner, args);

    private static T Private<T>(object owner, string name, params object[] args)
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = owner.GetType();
        if (args.Length > 0)
        {
            return (T)type.GetMethod(name, Any)!.Invoke(owner, args)!;
        }

        var property = type.GetProperty(name, Any);
        return property != null ? (T)property.GetValue(owner)! : (T)type.GetField(name, Any)!.GetValue(owner)!;
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Host = HiddenForm(new Size(300, 200));
            View = new PreliminaryListView { Dock = DockStyle.Fill };
            Host.Controls.Add(View);
            Host.Show();
            Application.DoEvents();
            Check(View.ClientSize.Height == 200, "The list fills its host: " + View.ClientSize);
        }

        public Form Host { get; }
        public PreliminaryListView View { get; }

        public static object Scroll(PreliminaryListView list) => Private<object>(list, "_scroll")!;

        public List<PreliminaryHero> Heroes(int count) =>
            Enumerable
                .Range(0, count)
                .Select(i => new PreliminaryHero { ChampionId = 100 + i, Priority = i + 1 })
                .ToList();

        public void Dispose() => Host.Dispose();
    }
}
