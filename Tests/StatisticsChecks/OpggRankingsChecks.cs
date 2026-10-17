using System.Reflection;
using League.Controls;
using League.Models;
using static StatisticsCheckSupport;

/// <summary>
/// The OP.GG ranking table. Its events are raised through the grid's own protected handlers and its artwork through
/// a stubbed loader, so the table paints, sorts, sorts back and reports failures without any network or hero data.
/// </summary>
internal static class OpggRankingsChecks
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type RankingsType = typeof(PlayerCardControl).Assembly.GetType(
        "League.Controls.OpggRankingsView"
    )!;

    public static void Run()
    {
        using var host = HiddenForm(new Size(1400, 800));
        var view = (Control)Activator.CreateInstance(RankingsType)!;
        host.Controls.Add(view);
        Set(view, "_iconLoader", Loader());
        host.Show();
        try
        {
            SetRows(view);
            Check(Property<bool>(view, "HasRows"), "A table with rows reports them");
            Paint(host, view);
            Pump(() => Field<Dictionary<int, Image?>>(view, "_icons").Count >= 3, "ranking artwork");
            Paint(host, view);
            Check(Property<bool>(view, "HasRows"), "The table keeps its rows while it is redrawn");

            Selection(view);
            Sort(view);
            Tooltips(view);
            Failures(view);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
        Console.WriteLine(
            "PASS: the OP.GG ranking table's rows, sorting, tooltips, artwork and its failure and closing paths."
        );
    }

    private static Func<int, Task<Image?>> Loader() =>
        id =>
        {
            if (id == 4242)
            {
                throw new InvalidOperationException("检查用图标失败");
            }
            return Task.FromResult<Image?>(new Bitmap(24, 24));
        };

    private static void SetRows(Control view)
    {
        var rows = new[]
        {
            new OpggTier(103, "MID", 0.52, 0.09, 0.03, 1, 1) { Counters = new[] { 50, 950, 115 } },
            new OpggTier(1, "MID", 0.5, 0.02, 0.004, 2, 27) { Counters = new[] { 63 } },
            new OpggTier(67, "MID", null, 0.01, 0.1, 3, 12),
            new OpggTier(99, "MID", 0.45, null, null, null, null),
        };
        Invoke(view, "SetRows", new object[] { rows });
        Check(Grid(view).Rows.Count == 4, "Every ranking row reaches the table");
        Check(Grid(view).Columns.Count == 7, "The table keeps its seven columns");
    }

    private static void Selection(Control view)
    {
        var grid = Grid(view);
        OpggTier? requested = null;
        var subscribed = Field<Action<OpggTier>?>(view, "ChampionRequested");
        Set(
            view,
            "ChampionRequested",
            (Action<OpggTier>)Delegate.Combine(subscribed, (Action<OpggTier>)(row => requested = row))
        );

        Raise(grid, "OnCellClick", new DataGridViewCellEventArgs(1, 0));
        Check(requested?.ChampionId == 103, "Clicking a champion cell asks for that champion");
        requested = null;
        Raise(grid, "OnCellClick", new DataGridViewCellEventArgs(1, -1));
        Check(requested == null, "Clicking the header asks for nothing");

        Raise(grid, "OnKeyDown", new KeyEventArgs(Keys.Enter));
        Check(requested?.ChampionId == 103, "Enter opens the selected champion");
        requested = null;
        Raise(grid, "OnKeyDown", new KeyEventArgs(Keys.Space));
        Check(requested == null, "Other keys leave the selection alone");

        Invoke(view, "ClearRows");
        Check(!Property<bool>(view, "HasRows"), "An emptied table reports no rows");
        Raise(grid, "OnKeyDown", new KeyEventArgs(Keys.Enter));
        Check(requested == null, "An emptied table opens nothing");
        SetRows(view);
    }

    private static void Sort(Control view)
    {
        var grid = Grid(view);
        Sort(grid, 1, "英雄", SortOrder.Ascending);
        Sort(grid, 1, "英雄", SortOrder.Descending);
        Sort(grid, 2, "梯队", SortOrder.Ascending);
        Sort(grid, 3, "胜率", SortOrder.Descending);
        Sort(grid, 4, "登场率", SortOrder.Descending);
        Sort(grid, 5, "禁用率", SortOrder.Descending);
        Sort(grid, 0, "排名", SortOrder.Ascending);
        RaiseHeader(grid, 6);
        Check(
            grid.Columns[0].HeaderCell.SortGlyphDirection == SortOrder.Ascending
                && grid.Columns[6].HeaderCell.SortGlyphDirection == SortOrder.None,
            "The matchup column cannot be sorted"
        );
    }

    private static void Sort(DataGridView grid, int index, string title, SortOrder expected)
    {
        RaiseHeader(grid, index);
        Check(
            grid.Columns[index].HeaderCell.SortGlyphDirection == expected,
            $"The {title} column sorts {expected}, but it is {grid.Columns[index].HeaderCell.SortGlyphDirection}"
        );
        Check(
            grid.Columns.Cast<DataGridViewColumn>().Count(c => c.HeaderCell.SortGlyphDirection != SortOrder.None) == 1,
            $"Sorting by {title} leaves the mark on that column alone"
        );
    }

    private static void Tooltips(Control view)
    {
        var grid = Grid(view);
        Check(Tooltip(grid, 6, 0).Contains("劣势对局"), "The matchup tooltip explains the column");
        Check(Tooltip(grid, 1, 0).StartsWith("点击查看", StringComparison.Ordinal), "A hero tooltip offers the guide");
        int plain = Enumerable
            .Range(0, grid.Rows.Count)
            .First(index => ((OpggTier)grid.Rows[index].Tag!).Counters.Length == 0);
        Check(Tooltip(grid, 6, plain).Contains("暂无数据"), "A row without matchups says so");
        Check(Tooltip(grid, 1, -1).Length == 0, "A header cell has no tooltip");
    }

    private static string Tooltip(DataGridView grid, int column, int row)
    {
        var args = (DataGridViewCellToolTipTextNeededEventArgs)
            Activator.CreateInstance(
                typeof(DataGridViewCellToolTipTextNeededEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object[] { column, row, "" },
                null
            )!;
        Raise(grid, "OnCellToolTipTextNeeded", args);
        return args.ToolTipText;
    }

    private static void Failures(Control view)
    {
        Set(view, "_closing", true);
        Check(((Task)Invoke(view, "LoadIconAsync", 55)!).IsCompleted, "A closing table refuses new artwork");
        Set(view, "_closing", false);

        var failed = (Task)Invoke(view, "LoadIconAsync", 4242)!;
        Pump(() => failed.IsCompleted, "failed artwork");
        Check(failed.IsCompletedSuccessfully, "A failed icon does not fail the table");
        Pump(() => !Field<HashSet<int>>(view, "_pending").Contains(4242), "failed artwork bookkeeping");
        Check(
            Field<Dictionary<int, Image?>>(view, "_icons")[4242] == null,
            "A hero whose icon failed is remembered so it is not requested again"
        );

        var images = Field<Dictionary<int, Image?>>(view, "_icons").Values.Where(image => image != null).ToArray();
        Check(images.Length > 0, "Some artwork was loaded before the table closed");
        view.Dispose();
        Check(images.All(Disposed), "Closing the table disposes its artwork");
        Pump(() => Field<Task>(view, "_cleanupWork").IsCompleted, "ranking cleanup");
        Check(Field<Task>(view, "_cleanupWork").IsCompletedSuccessfully, "The table's cleanup owns its gate");
    }

    private static DataGridView Grid(Control view) => view.Controls.OfType<DataGridView>().Single();

    private static void RaiseHeader(DataGridView grid, int column) =>
        Raise(
            grid,
            "OnColumnHeaderMouseClick",
            new DataGridViewCellMouseEventArgs(column, -1, 1, 1, new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0))
        );

    private static void Raise(DataGridView grid, string method, EventArgs args) =>
        typeof(DataGridView)
            .GetMethod(method, Any, null, new[] { args.GetType() }, null)!
            .Invoke(grid, new object[] { args });

    private static void Paint(Form host, Control view)
    {
        using var image = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
        host.DrawToBitmap(image, new Rectangle(Point.Empty, host.ClientSize));
        Application.DoEvents();
        Check(!view.IsDisposed, "Drawing does not dispose the table");
    }

    private static bool Disposed(Image? image)
    {
        if (image == null)
        {
            return false;
        }
        try
        {
            _ = image.Width;
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void Pump(Func<bool> ready, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Check(ready(), $"The simulated ranking work finished: {what}");
    }

    private static FieldInfo Field(object target, string name) =>
        target.GetType().GetField(name, Any) ?? throw new InvalidOperationException($"Missing field {name}.");

    private static T Field<T>(object target, string name) => (T)Field(target, name).GetValue(target)!;

    private static T Property<T>(object target, string name) =>
        (T)
            (
                target.GetType().GetProperty(name, Any)
                ?? throw new InvalidOperationException($"Missing property {name}.")
            ).GetValue(target)!;

    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);

    private static object? Invoke(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, Any)!.Invoke(target, args);
}
