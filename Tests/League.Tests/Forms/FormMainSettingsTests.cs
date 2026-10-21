using League.Controls;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Forms;

/// <summary>
/// The settings pages of the real main window, built by its own <c>Load</c> with everything that reaches outside the
/// process switched off: no update check, client polling, assistant, global hotkeys or loading overlay, and the daily
/// shutdown plan kept in memory instead of Task Scheduler. Controls are found by name through the control tree. These
/// checks take over the binding, radio-group and navigation parts of Tests/StatisticsChecks/SettingsChecks.cs.
/// </summary>
[Trait("Category", "UI")]
[Collection(EnvironmentCollection.Name)]
public class FormMainSettingsTests : IDisposable
{
    private static readonly string ConfigPath = Path.Combine(
        AppDataPaths.LocalData,
        "LeagueHistory",
        "LeagueConfig.json"
    );
    private readonly string? _savedConfig;

    public FormMainSettingsTests()
    {
        // Toggling a setting saves the shared test config; each test starts from, and puts back, what was there.
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

    [Theory]
    [InlineData("chkAutoAccept")]
    [InlineData("chkSkipHonor")]
    [InlineData("chkSkipEndOfGameStats")]
    [InlineData("chkHideSelf")]
    [InlineData("chkUseChampionName")]
    public void Load_DesignerSwitch_StaysLiveInsideTheSettings(string name)
    {
        UiHost.Run(_ =>
        {
            using var main = Main();

            var check = Find<CheckBox>(main, name);

            Assert.False(check.IsDisposed);
            Assert.Contains(check, Descendants(Find<TabControl>(main, "tabControl1")));
        });
    }

    [Theory]
    [InlineData("chkAutoAccept")]
    [InlineData("chkHideSelf")]
    public void Load_LoneSwitch_IsDrawnAsAToggle(string name)
    {
        UiHost.Run(_ =>
        {
            using var main = Main();

            var check = Find<CheckBox>(main, name);

            Assert.Equal(ToggleSwitch.SwitchSize, check.Size);
        });
    }

    [Fact]
    public void SendMode_CustomChosen_ClearsTheMatchMode()
    {
        UiHost.Run(_ =>
        {
            using var main = Main();
            var match = Find<RadioButton>(main, "rbModeMatch");
            var custom = Find<RadioButton>(main, "rbModeCustom");
            match.Checked = true;

            custom.Checked = true;

            Assert.False(match.Checked);
        });
    }

    [Theory]
    [InlineData(1, "对局速览")]
    [InlineData(2, "消息发送")]
    public void Navigation_RowClicked_OpensThatSettingsPage(int row, string page)
    {
        UiHost.Run(_ =>
        {
            using var main = Main();
            Find<TabControl>(main, "imageTabControl1").SelectedTab = Find<TabPage>(main, "tabPage3"); // 功能配置
            var navigation = Descendants(main).OfType<SettingsNavigation>().Single();
            var bounds = navigation.RowBounds(row);

            WindowMessages.Click(navigation, new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));

            Assert.Equal(page, Find<TabControl>(main, "tabControl1").SelectedTab!.Text);
        });
    }

    [Fact]
    public void Load_SettingsPages_FollowTheSideList()
    {
        UiHost.Run(_ =>
        {
            using var main = Main();

            var pages = Find<TabControl>(main, "tabControl1").TabPages.Cast<TabPage>().Select(page => page.Text);

            Assert.Equal(["功能辅助", "对局速览", "消息发送", "客户端目录", "数据缓存"], pages);
        });
    }

    [Theory]
    [InlineData("ShutdownPlanSummary")]
    [InlineData("ShutdownNextRun")]
    [InlineData("CreateShutdownPlan")]
    [InlineData("DeleteShutdownPlan")]
    [InlineData("ShutdownLog")]
    [InlineData("VoiceMicrophone")]
    [InlineData("VoiceMicrophoneRefresh")]
    public void Load_ManagementControl_IsOnTheSettingsPages(string name)
    {
        UiHost.Run(_ =>
        {
            using var main = Main();

            var control = Descendants(main).Where(control => control.Name == name);

            Assert.Single(control);
        });
    }

    [Fact]
    public void Load_MasteryCacheLifetime_StartsAtLeastOneMinute()
    {
        UiHost.Run(_ =>
        {
            using var main = Main();

            var lifetime = Find<NumericUpDown>(main, "ChampionMasteryCacheMinutes");

            Assert.True(lifetime.Value >= 1, $"Mastery cache lifetime was {lifetime.Value}.");
        });
    }

    private static FormMain Main()
    {
        var main = new FormMain(
            new FormMainOptions
            {
                StartBackgroundServices = false,
                StartGameIntegration = false,
                ShutdownTasks = new NoShutdownTask(),
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
