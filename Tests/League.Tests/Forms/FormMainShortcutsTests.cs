using League.Controls;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Forms;

/// <summary>
/// The parts of the real main window that can be reached without touching the machine: the settings accessor and the
/// shortcuts table on the message page.
///
/// The window is built by the same options the settings tests use - no update check, client polling, assistant, global
/// hotkeys or loading overlay, and the shutdown plan kept in memory. The shortcuts table is drawn through
/// <c>DrawToBitmap</c> so its paint handler runs; per the project's rule the test checks that the drawing happened and
/// that the layout is what it should be, and does not compare pixels.
/// </summary>
[Trait("Category", "UI")]
[Collection(EnvironmentCollection.Name)]
public class FormMainShortcutsTests : IDisposable
{
    private static readonly string ConfigPath = Path.Combine(
        AppDataPaths.LocalData,
        "LeagueHistory",
        "LeagueConfig.json"
    );
    private readonly string? _savedConfig;

    public FormMainShortcutsTests()
    {
        // The window loads the shared test config; each test puts back what was there.
        _savedConfig = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : null;
    }

    public void Dispose()
    {
        if (_savedConfig != null)
        {
            File.WriteAllText(ConfigPath, _savedConfig);
        }
        else if (File.Exists(ConfigPath))
        {
            File.Delete(ConfigPath);
        }
    }

    private static FormMain Main()
    {
        var main = new FormMain(
            new FormMainOptions
            {
                StartBackgroundServices = false,
                StartGameIntegration = false,
                ShutdownTasks = new NoShutdownTask(),
                Setup = PinnedSetup.Create(),
            }
        )
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-16000, -16000),
            ShowInTaskbar = false,
        };
        main.Show();
        return main;
    }

    private static T Find<T>(Control root, string name)
        where T : Control => Descendants(root).OfType<T>().Single(control => control.Name == name);

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Opens the message page, which is where the shortcuts table lives.</summary>
    private static TableLayoutPanel OpenShortcutTable(FormMain main)
    {
        Find<TabControl>(main, "imageTabControl1").SelectedTab = Find<TabPage>(main, "tabPage3");
        Find<TabControl>(main, "tabControl1").SelectedTab = Find<TabPage>(main, "tabPage5");
        return Find<TableLayoutPanel>(main, "ShortcutTable");
    }

    [Fact]
    public void GetAppConfig_AfterTheWindowLoaded_IsTheConfigItIsUsing()
    {
        // A public accessor rather than a private field: the settings pages and the hotkey handlers both read it.
        UiHost.Run(_ =>
        {
            using var main = Main();

            Assert.NotNull(main.GetAppConfig());
        });
    }

    [Fact]
    public void GetAppConfig_Twice_IsTheSameInstance()
    {
        // The window holds one config object; a second call must not rebuild it, or a saved change would be lost.
        UiHost.Run(_ =>
        {
            using var main = Main();

            Assert.Same(main.GetAppConfig(), main.GetAppConfig());
        });
    }

    [Fact]
    public void Load_TheShortcutTable_HasTheDocumentedColumnsAndOneRowPerShortcut()
    {
        // Three columns - key, scene, content - and a header row above the shortcuts it documents.
        UiHost.Run(_ =>
        {
            using var main = Main();

            TableLayoutPanel table = OpenShortcutTable(main);

            Assert.Equal(3, table.ColumnCount);
            Assert.True(table.RowCount > 1, $"Expected a header and at least one shortcut, got {table.RowCount} rows.");
            Assert.Equal(table.RowCount * 3, table.Controls.Count);
        });
    }

    [Fact]
    public void Load_TheShortcutTable_DrawsWithoutFailing()
    {
        // The rules under each row are painted by the table itself; drawing it runs that handler. The pixels are not
        // compared - the project's rule for interface tests - but a throw here would be a broken handler.
        UiHost.Run(_ =>
        {
            using var main = Main();
            TableLayoutPanel table = OpenShortcutTable(main);
            using var bitmap = new Bitmap(Math.Max(1, table.Width), Math.Max(1, table.Height));

            table.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

            Assert.Equal(bitmap.Width, table.Width);
        });
    }

    /// <summary>A store that keeps the plan in memory, so no Windows task is read or written.</summary>
    private sealed class NoShutdownTask : IShutdownTaskStore
    {
        public ShutdownTaskRecord? Read() => null;

        public void Register(string xml) => throw new NotSupportedException("The tests never register a plan.");

        public void Stop() => throw new NotSupportedException("The tests never stop a plan.");

        public void Delete() { }

        public void SetEnabled(bool enabled) => throw new NotSupportedException("The tests never enable a plan.");
    }
}
