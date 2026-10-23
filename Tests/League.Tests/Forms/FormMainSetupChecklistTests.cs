using League.Services;
using League.Tests.Support;

namespace League.Tests.Forms;

/// <summary>
/// The 准备情况 settings page of the real main window. Every fact it detects is pinned through
/// <see cref="FormMainOptions.Setup"/>: whether this process is elevated, whether the voice model and the Game Bar
/// component are present, and what restarting elevated does. The component read is a hand-completed task, so a test
/// decides when it finishes and which of two overlapping reads comes back last.
/// </summary>
[Trait("Category", "UI")]
[Collection(EnvironmentCollection.Name)]
public class FormMainSetupChecklistTests : IDisposable
{
    private static readonly string ConfigPath = Path.Combine(
        AppDataPaths.LocalData,
        "LeagueHistory",
        "LeagueConfig.json"
    );
    private static readonly GameBarPackageStatus NotInstalled = new(new Version(1, 0, 0, 102), null);
    private readonly string? _savedConfig;

    public FormMainSetupChecklistTests()
    {
        // Choosing Game Bar saves the shared test config; each test starts from, and puts back, what was there.
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

    [Fact]
    public void Rail_IntoTheSettings_OpensTheChecklistFirst()
    {
        UiHost.Run(_ =>
        {
            using var main = Launch(PinnedSetup.Create());

            OpenSettings(main);

            Assert.Equal("准备情况", Find<TabControl>(main, "tabControl1").SelectedTab!.Text);
        });
    }

    [Fact]
    public void Open_NotElevated_OffersTheRestart()
    {
        UiHost.Run(_ =>
        {
            using var main = Launch(PinnedSetup.Create(administrator: false));

            OpenSettings(main);

            Assert.True(Find<Button>(main, "SetupAdministratorAction").Visible);
            Assert.StartsWith("⚠", Find<Label>(main, "SetupAdministratorStatus").Text);
        });
    }

    [Fact]
    public void Open_Elevated_ShowsReadyWithoutAButton()
    {
        UiHost.Run(_ =>
        {
            using var main = Launch(PinnedSetup.Create(administrator: true));

            OpenSettings(main);

            Assert.False(Find<Button>(main, "SetupAdministratorAction").Visible);
            Assert.StartsWith("✓", Find<Label>(main, "SetupAdministratorStatus").Text);
        });
    }

    [Fact]
    public void Open_VoiceModelMissing_OffersTheDownloadPage()
    {
        UiHost.Run(_ =>
        {
            using var main = Launch(PinnedSetup.Create(voiceModel: false));
            OpenSettings(main);
            var action = Find<Button>(main, "SetupVoiceModelAction");

            Click(action);

            Assert.Equal("消息发送", Find<TabControl>(main, "tabControl1").SelectedTab!.Text);
        });
    }

    [Fact]
    public void Open_PackageStillReading_ShowsChecking()
    {
        var read = new TaskCompletionSource<GameBarPackageStatus>();
        UiHost.Run(_ =>
        {
            using var main = Launch(PinnedSetup.Create(package: _ => read.Task));

            OpenSettings(main);

            Assert.Equal("正在检查组件…", Find<Label>(main, "SetupGameBarStatus").Text);
        });
    }

    [Fact]
    public void Open_PackageCurrent_ShowsTheInstalledVersion()
    {
        var read = new TaskCompletionSource<GameBarPackageStatus>();
        UiHost.Run(async _ =>
        {
            using var main = Launch(PinnedSetup.Create(package: _ => read.Task));
            OpenSettings(main);

            read.SetResult(PinnedSetup.CurrentPackage);
            await AfterPostedResults();

            Assert.Equal("✓ 已安装 1.0.0.1", Find<Label>(main, "SetupGameBarStatus").Text);
        });
    }

    [Fact]
    public void Open_GameBarChosenAndNotInstalled_NeedsTheComponent()
    {
        UiHost.Run(async _ =>
        {
            using var main = Launch(PinnedSetup.Create(package: _ => Task.FromResult(NotInstalled)));
            OpenSettings(main);
            // Changed while its page is hidden: on screen, the overlay page's own component row would start PowerShell.
            Find<ComboBox>(main, "LoadingOverlayBackend").SelectedIndex = 1;
            Find<TabControl>(main, "tabControl1").SelectedTab = Page(main, "功能辅助");

            Find<TabControl>(main, "tabControl1").SelectedTab = Page(main, "准备情况");
            await AfterPostedResults();

            Assert.StartsWith("⚠", Find<Label>(main, "SetupGameBarStatus").Text);
            Assert.True(Find<Button>(main, "SetupGameBarAction").Visible);
        });
    }

    [Fact]
    public void Reopen_EarlierReadFinishesLast_KeepsTheNewerResult()
    {
        var first = new TaskCompletionSource<GameBarPackageStatus>();
        var second = new TaskCompletionSource<GameBarPackageStatus>();
        var reads = new Queue<TaskCompletionSource<GameBarPackageStatus>>([first, second]);
        UiHost.Run(async _ =>
        {
            using var main = Launch(PinnedSetup.Create(package: _ => reads.Dequeue().Task));
            OpenSettings(main);
            Find<TabControl>(main, "tabControl1").SelectedTab = Page(main, "功能辅助");
            Find<TabControl>(main, "tabControl1").SelectedTab = Page(main, "准备情况");
            var status = Find<Label>(main, "SetupGameBarStatus");

            second.SetResult(PinnedSetup.CurrentPackage);
            await AfterPostedResults();
            string shownBeforeTheLateRead = status.Text;
            first.SetResult(NotInstalled);
            await AfterPostedResults();

            Assert.Equal("✓ 已安装 1.0.0.1", shownBeforeTheLateRead);
            Assert.Equal("✓ 已安装 1.0.0.1", status.Text);
        });
    }

    [Fact]
    public void Restart_Declined_KeepsTheWindowAndSaysSo()
    {
        UiHost.Run(_ =>
        {
            using var main = Launch(PinnedSetup.Create(administrator: false, startElevated: () => false));
            OpenSettings(main);

            Click(Find<Button>(main, "SetupAdministratorAction"));

            Assert.False(main.IsDisposed);
            Assert.Equal("⚠ 已取消管理员授权，继续以当前权限运行", Find<Label>(main, "SetupAdministratorStatus").Text);
        });
    }

    [Fact]
    public void Restart_Accepted_ClosesThisWindow()
    {
        UiHost.Run(_ =>
        {
            using var main = Launch(PinnedSetup.Create(administrator: false, startElevated: () => true));
            OpenSettings(main);

            Click(Find<Button>(main, "SetupAdministratorAction"));

            Assert.True(main.IsDisposed);
        });
    }

    // A finished read resumes the page through a message posted to this thread; yielding posts behind it, and the
    // queue is first in, first out, so the result has been handled when this returns.
    private static async Task AfterPostedResults() => await Task.Yield();

    private static void OpenSettings(FormMain main) =>
        Find<TabControl>(main, "imageTabControl1").SelectedTab = Find<TabPage>(main, "tabPage3"); // 功能配置

    private static TabPage Page(Control main, string title) =>
        Find<TabControl>(main, "tabControl1").TabPages.Cast<TabPage>().Single(page => page.Text == title);

    private static void Click(Control control) =>
        WindowMessages.Click(control, new Point(control.Width / 2, control.Height / 2));

    private static FormMain Launch(SetupEnvironment setup)
    {
        var main = new FormMain(
            new FormMainOptions
            {
                StartBackgroundServices = false,
                StartGameIntegration = false,
                ShutdownTasks = new NoShutdownTask(),
                Setup = setup,
            }
        )
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-16000, -16000),
            ShowInTaskbar = false,
        };
        main.Show(); // Load builds the settings pages.
        return main;
    }

    private static T Find<T>(Control root, string name)
        where T : Control => Descendants(root).OfType<T>().Single(control => control.Name == name);

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

    /// <summary>A Task Scheduler with no shutdown task, which refuses to change anything.</summary>
    private sealed class NoShutdownTask : IShutdownTaskStore
    {
        public ShutdownTaskRecord? Read() => null;

        public void Register(string xml) => throw new InvalidOperationException("Tests never register a task.");

        public void Stop() => throw new InvalidOperationException("Tests never stop a task.");

        public void Delete() => throw new InvalidOperationException("Tests never delete a task.");

        public void SetEnabled(bool enabled) => throw new InvalidOperationException("Tests never change a task.");
    }
}
