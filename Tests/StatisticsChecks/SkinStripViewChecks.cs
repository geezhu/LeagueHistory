using System.Reflection;
using League.ChampionSkin;
using League.Models;
using static StatisticsCheckSupport;

/// <summary>
/// The champion's skin strip: which tile it selects, the mark on the automatic skin, hit testing, mouse and keyboard
/// activation, sideways scrolling and the painted tile in each state. The tiles come from a stub, so no client, no
/// network and no real art folder is involved.
/// </summary>
internal static class SkinStripViewChecks
{
    public static void Run()
    {
        var items = Skins(12);
        using var host = new Form
        {
            ClientSize = new Size(420, 200),
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-30000, -30000),
        };
        using var strip = new SkinStripView { Dock = DockStyle.Fill };
        host.Controls.Add(strip);
        host.Show();
        Application.DoEvents();

        var changes = new Counter();
        var activated = new Counter();
        strip.SelectionChanged += (_, _) => changes.Value++;
        strip.Activated += (_, _) => activated.Value++;

        Check(strip.Items.Count == 0 && strip.Selected == null, "An empty strip has no selection");
        Paint(strip);

        Selecting(strip, items, changes);
        Input(strip, items, activated, changes);
        Scrolling(host, strip, items);
        Painting(strip, items);
        Console.WriteLine(
            "PASS: the skin strip selects, marks and activates its tiles, hit tests them, scrolls sideways and paints "
                + "every tile state."
        );
    }

    private static void Selecting(SkinStripView strip, List<SkinThumbItem> items, Counter changes)
    {
        strip.SetItems(items, items[5].SkinId, items[1].SkinId);
        Check(
            ReferenceEquals(strip.Selected, items[5]) && changes.Value == 1,
            "The strip selects the skin it was asked for and announces it"
        );
        Check(Private<int>(strip, "_preferred") == items[1].SkinId, "The automatic skin is remembered");

        strip.SetItems(items, 999999, items[1].SkinId);
        Check(ReferenceEquals(strip.Selected, items[0]), "A skin that is not in the list falls back to the first");

        strip.SetItems([], 0, 0);
        Check(strip.Selected == null, "An empty list clears the selection");

        strip.SetItems(items, items[0].SkinId, items[1].SkinId);
        strip.SetPreferred(items[2].SkinId);
        Check(
            Private<int>(strip, "_preferred") == items[2].SkinId,
            "The automatic skin can be moved without replacing the list"
        );
        strip.SetPreferred(0);
    }

    private static void Input(SkinStripView strip, List<SkinThumbItem> items, Counter activated, Counter changes)
    {
        strip.SetItems(items, items[0].SkinId, 0);
        var first = Private<Rectangle>(strip, "CellBounds", 0);
        var second = Private<Rectangle>(strip, "CellBounds", 1);
        Check(second.Left == first.Left + first.Width, "Tiles are laid out side by side");

        Check(Private<int>(strip, "HitTest", Centre(second)) == 1, "A point inside a tile hits that skin");
        Check(
            Private<int>(strip, "HitTest", new Point(first.Left - 4, second.Top + 2)) == -1,
            "The left edge is not a skin"
        );
        Check(
            Private<int>(strip, "HitTest", new Point(second.Left + 2, strip.ClientSize.Height - 2)) == -1,
            "The strip under the names is not a skin"
        );
        Check(
            Private<int>(strip, "HitTest", new Point(2000, second.Top + 2)) == -1,
            "A point past the last tile is not a skin"
        );

        Move(strip, Centre(second));
        Check(
            Private<int>(strip, "_hover") == 1 && strip.Cursor == Cursors.Hand,
            "Hovering a tile highlights it and offers the hand cursor"
        );
        Move(strip, new Point(first.Left - 4, second.Top + 2));
        Check(
            Private<int>(strip, "_hover") == -1 && strip.Cursor == Cursors.Default,
            "Leaving the tiles drops the highlight"
        );
        Invoke(strip, "OnMouseLeave", EventArgs.Empty);
        Check(Private<int>(strip, "_hover") == -1, "Leaving the strip drops the highlight");

        int before = changes.Value;
        Press(strip, Centre(second), down: true);
        Check(
            ReferenceEquals(strip.Selected, items[1]) && changes.Value == before + 1 && activated.Value == 0,
            "Clicking a tile selects it without making it automatic"
        );
        Press(strip, Centre(second), down: false);

        Invoke(
            strip,
            "OnMouseDoubleClick",
            new MouseEventArgs(MouseButtons.Left, 2, second.Left + 2, second.Top + 2, 0)
        );
        Check(activated.Value == 1, "Double clicking the selected tile activates it");
        Invoke(strip, "OnMouseDoubleClick", new MouseEventArgs(MouseButtons.Left, 2, first.Left + 2, first.Top + 2, 0));
        Check(activated.Value == 1, "Double clicking another tile only opens it");

        Check(
            Private<bool>(strip, "IsInputKey", Keys.Right) && Private<bool>(strip, "IsInputKey", Keys.Left),
            "The sideways arrows reach the strip as input keys"
        );
        strip.SetItems(items, items[0].SkinId, 0);
        Invoke(strip, "OnKeyDown", new KeyEventArgs(Keys.Right));
        Check(ReferenceEquals(strip.Selected, items[1]), "The right arrow moves one tile right");
        Invoke(strip, "OnKeyDown", new KeyEventArgs(Keys.Enter));
        Check(activated.Value == 2, "Enter activates the selected tile");
        Invoke(strip, "OnKeyDown", new KeyEventArgs(Keys.Left));
        Invoke(strip, "OnKeyDown", new KeyEventArgs(Keys.Left));
        Check(ReferenceEquals(strip.Selected, items[0]), "The left arrow stops at the first tile");
        Invoke(strip, "OnKeyDown", new KeyEventArgs(Keys.Space));
        Check(ReferenceEquals(strip.Selected, items[0]), "A key the strip does not use leaves the selection alone");
    }

    private static void Scrolling(Form host, SkinStripView strip, List<SkinThumbItem> items)
    {
        strip.SetItems(items, items[0].SkinId, 0);
        var scroll = Private<object>(strip, "_scroll");
        Check(
            Private<int>(scroll, "Value") == 0 && Private<bool>(scroll, "Needed"),
            "A long strip starts at its left end"
        );

        Wheel(strip, -120);
        Check(Private<int>(scroll, "Value") > 0, "The wheel scrolls the strip right");
        Wheel(strip, 120);
        Check(Private<int>(scroll, "Value") == 0, "The wheel scrolls the strip back to the left end");
        Wheel(strip, 120);
        Check(Private<int>(scroll, "Value") == 0, "The wheel stops at the left end");

        // Selecting a tile that is off screen brings it into view.
        strip.SetItems(items, items[^1].SkinId, 0);
        Check(Private<int>(scroll, "Value") > 0, "Selecting an off-screen tile scrolls it into view");

        strip.SetItems(items, items[0].SkinId, 0);
        var track = Private<Rectangle>(strip, "Track");
        var thumb = Private<Rectangle>(scroll, "Thumb", track);
        var grip = Centre(thumb);
        Press(strip, grip, down: true);
        Check(Private<bool>(scroll, "Dragging"), "Pressing the scroll thumb starts a drag");
        Invoke(strip, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, grip.X + 40, grip.Y, 0));
        Check(Private<int>(scroll, "Value") > 0, "Dragging the thumb scrolls the strip");
        Press(strip, new Point(grip.X + 40, grip.Y), down: false);
        Check(!Private<bool>(scroll, "Dragging"), "Releasing the mouse ends the drag");

        int viewport = Private<int>(scroll, "Viewport");
        host.ClientSize = new Size(host.ClientSize.Width + 160, host.ClientSize.Height);
        Application.DoEvents();
        Check(
            Private<int>(scroll, "Viewport") == strip.ClientSize.Width && strip.ClientSize.Width > viewport,
            "Widening the strip updates the scroll viewport"
        );
    }

    private static void Painting(SkinStripView strip, List<SkinThumbItem> items)
    {
        using var tile = new Bitmap(96, 96);
        using (var graphics = Graphics.FromImage(tile))
        {
            graphics.Clear(Color.CornflowerBlue);
        }

        strip.Tile = skinId => skinId / 2 % 2 == 0 ? tile : null;
        strip.SetItems(items, items[1].SkinId, items[3].SkinId);
        Paint(strip);

        Move(strip, Centre(Private<Rectangle>(strip, "CellBounds", 2)));
        Paint(strip);

        Invoke(strip, "OnMouseLeave", EventArgs.Empty);
        Paint(strip);

        // Scrolled to the right, the tiles that left the view are skipped instead of drawn.
        Wheel(strip, -120);
        Wheel(strip, -120);
        Paint(strip);
        Check(strip.Items.Count == items.Count, "A scrolled strip paints without failing");

        strip.SetItems(items, items[0].SkinId, 0);
        Check(Private<int>(strip, "_preferred") == 0, "No skin is marked automatic unless one was chosen");
    }

    private static List<SkinThumbItem> Skins(int count) =>
        Enumerable
            .Range(0, count)
            .Select(i => new SkinThumbItem
            {
                SkinId = 99000 + i * 2,
                Name = "皮肤" + i,
                IsBase = i == 0,
            })
            .ToList();

    private static void Move(SkinStripView strip, Point point) =>
        Invoke(strip, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0));

    private static void Wheel(SkinStripView strip, int delta) =>
        Invoke(strip, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, delta));

    private static void Press(SkinStripView strip, Point point, bool down) =>
        Invoke(
            strip,
            down ? "OnMouseDown" : "OnMouseUp",
            new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)
        );

    private static void Paint(SkinStripView strip)
    {
        using var bitmap = new Bitmap(Math.Max(1, strip.Width), Math.Max(1, strip.Height));
        using var graphics = Graphics.FromImage(bitmap);
        using var args = new PaintEventArgs(graphics, new Rectangle(Point.Empty, strip.ClientSize));
        Invoke(strip, "OnPaint", args);
    }

    private static Point Centre(Rectangle rectangle) =>
        new(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);

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

    private static void Invoke(object owner, string name, params object[] args) =>
        owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);

    private sealed class Counter
    {
        public int Value { get; set; }
    }
}
