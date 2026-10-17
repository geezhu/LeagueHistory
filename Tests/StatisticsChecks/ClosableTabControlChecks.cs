using System.Reflection;
using League.Controls;
using static StatisticsCheckSupport;

/// <summary>
/// The closable tab control: the close button it paints on every tab, what a click on it closes (through the tab
/// content, or by removing the page itself when there is no tab content) and its owner-drawn tab.
/// The two paths that show a message box are deliberately never reached: the account's own tab and the last tab.
/// </summary>
internal static class ClosableTabControlChecks
{
    public static void Run()
    {
        using var host = HiddenForm(new Size(520, 340));
        using var content = new MatchTabContent { Dock = DockStyle.Fill };
        host.Controls.Add(content);
        host.Show();
        Application.DoEvents();

        var tabs = content.MainTabControl;
        tabs.Dock = DockStyle.Fill;
        var second = new TabPage("第二页");
        tabs.TabPages.Add(new TabPage("第一页"));
        tabs.TabPages.Add(second);
        tabs.TabPages.Add(new TabPage("第三页"));
        Application.DoEvents();

        Check(tabs.TabCount == 3, "The tab control holds the pages it was given");
        Check(tabs.ProtectedTab == null, "No tab is protected by default");

        Paint(tabs);
        Check(
            Private<Dictionary<int, Rectangle>>(tabs, "_closeButtonBounds").Count == 3,
            "Painting puts a close button on every tab"
        );
        Paint(tabs);
        Check(
            Private<Dictionary<int, Rectangle>>(tabs, "_closeButtonBounds").Count == 3,
            "Painting again replaces the close buttons instead of piling them up"
        );

        var rect = Private<Dictionary<int, Rectangle>>(tabs, "_closeButtonBounds")[1];
        Check(rect.Width == 12 && rect.Height == 12, "The close button keeps its fixed size");
        Check(
            Private<Rectangle>(tabs, "GetCloseButtonRect", tabs.GetTabRect(1)).Width == 12,
            "The owner-draw close button is the same size"
        );

        Click(tabs, new Point(tabs.GetTabRect(0).Left + 3, tabs.GetTabRect(0).Top + 3));
        Check(tabs.TabCount == 3, "Clicking a tab away from its close button closes nothing");

        Click(tabs, Centre(rect));
        Check(
            tabs.TabCount == 2 && !tabs.TabPages.Contains(second),
            "Clicking a close button closes that page through the tab content"
        );

        Draws(tabs);
        WithoutTabContent();
        Console.WriteLine(
            "PASS: the closable tab control paints a close button per tab, closes the clicked page through the tab "
                + "content, removes it directly without one, and draws its own tab."
        );
    }

    /// <summary>A control with no MatchTabContent above it removes the page itself instead of asking the content.</summary>
    private static void WithoutTabContent()
    {
        using var host = HiddenForm(new Size(400, 260));
        using var tabs = new ClosableTabControl { Dock = DockStyle.Fill };
        host.Controls.Add(tabs);
        host.Show();
        Application.DoEvents();

        var orphan = new TabPage("孤立页");
        tabs.TabPages.Add(orphan);
        tabs.TabPages.Add(new TabPage("保留页"));
        Application.DoEvents();
        Paint(tabs);

        var rect = Private<Dictionary<int, Rectangle>>(tabs, "_closeButtonBounds")[0];
        Click(tabs, Centre(rect));
        Check(
            tabs.TabCount == 1 && !tabs.TabPages.Contains(orphan),
            "Without a tab content above it the clicked page is removed directly"
        );
        Check(tabs.TabCount == 1, "The last page is never removed, so the control is never empty");
    }

    private static void Draws(ClosableTabControl tabs)
    {
        using var bitmap = new Bitmap(Math.Max(1, tabs.Width), Math.Max(1, tabs.Height));
        using var graphics = Graphics.FromImage(bitmap);
        int drawn = 0;
        for (int index = 0; index < tabs.TabCount; index++)
        {
            using var args = new DrawItemEventArgs(
                graphics,
                SystemFonts.DefaultFont,
                tabs.GetTabRect(index),
                index,
                index == 0 ? DrawItemState.Selected : DrawItemState.None
            );
            Invoke(tabs, "OnDrawItem", args);
            drawn++;
        }

        Check(drawn == 2, "Owner-drawing every tab paints it without closing anything");
    }

    private static void Paint(ClosableTabControl tabs)
    {
        using var bitmap = new Bitmap(Math.Max(1, tabs.Width), Math.Max(1, tabs.Height));
        using var graphics = Graphics.FromImage(bitmap);
        using var args = new PaintEventArgs(graphics, new Rectangle(Point.Empty, tabs.ClientSize));
        Invoke(tabs, "OnPaint", args);
    }

    private static void Click(ClosableTabControl tabs, Point point) =>
        Invoke(tabs, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));

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
}
