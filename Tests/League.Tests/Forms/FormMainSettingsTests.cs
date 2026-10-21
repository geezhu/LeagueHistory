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

    [Fact]
    public void Load_GeneralPage_ListsTheCardsInTheOrderTheyAreUsed()
    {
        UiHost.Run(_ =>
        {
            using var main = Main();

            var titles = CardTitles(Page(main, "功能辅助"));

            // Champion select first, then the match, then the computer, then about.
            Assert.Equal(
                [
                    "选人战绩",
                    "选人助手",
                    "英雄预选",
                    "对局自动化",
                    "英雄提醒",
                    "客户端维护",
                    "到点关机 · 防熬夜",
                    "关于与交流",
                ],
                titles
            );
        });
    }

    [Fact]
    public void Load_ShutdownCard_OffersCreateDeleteAndLogOnly()
    {
        UiHost.Run(_ =>
        {
            using var main = Main();

            var buttons = ShutdownCard(main).Controls.Cast<Control>().SelectMany(Descendants).OfType<Button>();

            Assert.Equal(["CreateShutdownPlan", "DeleteShutdownPlan", "ShutdownLog"], buttons.Select(b => b.Name));
        });
    }

    [Fact]
    public void Load_ShutdownCard_ExplainsThatARunningMatchFinishesFirst()
    {
        UiHost.Run(_ =>
        {
            using var main = Main();

            var purpose = Find<Label>(main, "ShutdownPurpose");

            Assert.Contains("本局结束", purpose.Text);
        });
    }

    [Fact]
    public void Load_NoShutdownTaskScheduled_SummarySaysNoneWasCreated()
    {
        UiHost.Run(async _ =>
        {
            using var main = Main();
            var summary = Find<Label>(main, "ShutdownPlanSummary");
            var read = new TaskCompletionSource();
            summary.TextChanged += (_, _) => read.TrySetResult();
            // The card reads the plan once its page is opened, and publishes the result through the label.
            Open(main, "功能辅助");
            var published = summary.Text == "正在读取每日关机计划…" ? read.Task : Task.CompletedTask;

            // The delay only bounds a broken read.
            await Task.WhenAny(published, Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.Equal("尚未创建每日关机计划", summary.Text);
        });
    }

    [Fact]
    public void Load_OverlayPage_HoldsTheOverlaySettingsAndNoShortcutSetting()
    {
        UiHost.Run(_ =>
        {
            using var main = Main();

            Assert.Contains(Find<Button>(main, "PreviewLoadingOverlay"), Descendants(Page(main, "对局速览")));
            Assert.DoesNotContain(Descendants(Page(main, "功能辅助")), c => c.Name == "LoadingOverlayBackend");
            Assert.DoesNotContain(Descendants(main), c => c.Name == "LoadingOverlayHotkey");
        });
    }

    [Fact]
    public void Load_OverlayPage_KeepsTheGameBarInstructions()
    {
        UiHost.Run(_ =>
        {
            using var main = Main();

            var instructions = Descendants(Page(main, "对局速览")).Where(c => c.Text.Contains("Win＋G"));

            Assert.NotEmpty(instructions);
        });
    }

    [Fact]
    public void Load_DefaultConfig_ShowsTheOverlayAutomaticallyAndOffersThePreview()
    {
        UiHost.Run(_ =>
        {
            using var main = Main();

            Assert.True(Find<CheckBox>(main, "AutoShowLoadingOverlay").Checked);
            Assert.True(Find<Button>(main, "PreviewLoadingOverlay").Enabled);
        });
    }

    [Fact]
    public void Load_DesktopOverlay_ShowsThePreviewAndHidesTheGameBarHelp()
    {
        UiHost.Run(_ =>
        {
            using var main = Main();
            Open(main, "对局速览");

            var backend = Find<ComboBox>(main, "LoadingOverlayBackend");
            var help = Descendants(main).OfType<Label>().Single(c => c.Text.StartsWith("按 Win＋G"));

            // The Game Bar choice is not exercised: showing it checks the installed package through powershell.exe.
            Assert.Equal(0, backend.SelectedIndex);
            Assert.True(Find<Button>(main, "PreviewLoadingOverlay").Visible);
            Assert.False(help.Visible);
        });
    }

    [Fact]
    public void Load_ShortcutTable_ListsAHeaderAndThreeShortcuts()
    {
        UiHost.Run(_ =>
        {
            using var main = Main();

            var table = Find<TableLayoutPanel>(main, "ShortcutTable");

            Assert.Equal(4, table.RowCount);
        });
    }

    [Fact]
    public void Load_HistoryCount_Offers10To50AndStartsAt20()
    {
        UiHost.Run(_ =>
        {
            using var main = Main();

            var count = Find<ComboBox>(main, "CardMatchHistoryCount");

            Assert.Equal([10, 20, 30, 50], count.Items.Cast<int>());
            Assert.Equal(20, count.SelectedItem);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AutoAccept_Switched_OffersTheDelayOnlyWhileOn(bool on)
    {
        UiHost.Run(_ =>
        {
            using var main = Main();
            var delay = Find<ComboBox>(main, "AutoAcceptDelaySeconds");

            Find<CheckBox>(main, "chkAutoAccept").Checked = on;

            Assert.Equal(on, delay.Enabled);
            Assert.Equal(3, delay.Items.Count);
        });
    }

    [Theory]
    [InlineData(650)]
    [InlineData(1235)]
    public void Resize_MainWindowWidth_KeepsEveryGeneralCardInsideThePage(int width)
    {
        UiHost.Run(_ =>
        {
            using var main = Main();
            Open(main, "功能辅助");

            main.ClientSize = new Size(width, 720);
            var page = Page(main, "功能辅助").Controls.OfType<FlowLayoutPanel>().Single();

            Assert.All(
                Cards(page),
                card => Assert.True(card.Right <= page.ClientSize.Width, $"{card.Right} > {page.ClientSize.Width}")
            );
        });
    }

    private static TabPage Page(Control main, string title) =>
        Find<TabControl>(main, "tabControl1").TabPages.Cast<TabPage>().Single(page => page.Text == title);

    private static void Open(Control main, string title)
    {
        Find<TabControl>(main, "imageTabControl1").SelectedTab = Find<TabPage>(main, "tabPage3"); // 功能配置
        Find<TabControl>(main, "tabControl1").SelectedTab = Page(main, title);
    }

    // A settings card is a flow panel directly inside the page's scrolling flow; its first child is the title.
    private static IEnumerable<FlowLayoutPanel> Cards(FlowLayoutPanel page) => page.Controls.OfType<FlowLayoutPanel>();

    private static IEnumerable<string> CardTitles(TabPage page) =>
        Cards(page.Controls.OfType<FlowLayoutPanel>().Single()).Select(card => card.Controls[0].Text);

    private static Control ShutdownCard(Control main)
    {
        Control card = Find<Label>(main, "ShutdownPlanSummary");
        while (card.Parent is not FlowLayoutPanel { AutoScroll: true })
        {
            card = card.Parent!;
        }

        return card;
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
