using System.Reflection;
using static StatisticsCheckSupport;

internal static class SettingsChecks
{
    public static void Render() => WinFormsCheckHost.Run(RenderCore);

    private static void RenderCore()
    {
        using var owner = new League.FormMain();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = owner.GetType();
        var tabs = (TabControl)type.GetField("tabControl1", flags)!.GetValue(owner)!;
        var original = (Control)type.GetField("chkAutoAccept", flags)!.GetValue(owner)!;
        type.GetMethod("ConfigureAramMetaSettings", flags)!.Invoke(owner, null);
        type.GetMethod("ConfigureSettingsLayout", flags)!.Invoke(owner, null);
        ExistingBindings(owner, tabs, original);
        OptionalControls(tabs);
        ShutdownControls(tabs);
        OverlayControls(tabs);
        AutoAcceptControls(tabs, original);
        RenderLayouts(tabs);
        Console.WriteLine("PASS: settings retain bound controls, radio grouping, and responsive pages.");
    }

    private static IEnumerable<Control> All(Control root) => Children(root);

    private static void ExistingBindings(League.FormMain owner, TabControl tabs, Control original)
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        Control Field(string name) => (Control)owner.GetType().GetField(name, flags)!.GetValue(owner)!;
        if (original.IsDisposed || original.Parent is not FlowLayoutPanel)
        {
            throw new Exception("Settings must retain existing bound controls");
        }

        if (Field("rbModeMatch").Parent != Field("rbModeCustom").Parent)
        {
            throw new Exception("Send modes must remain mutually exclusive");
        }

        if (All(tabs).OfType<NumericUpDown>().Single(c => c.Name == "ChampionMasteryCacheMinutes").Value < 1)
        {
            throw new Exception("Mastery cache lifetime is configurable and initialized");
        }
    }

    private static void OptionalControls(TabControl tabs)
    {
        if (
            !All(tabs).OfType<ComboBox>().Any(c => c.Name == "VoiceMicrophone")
            || !All(tabs).OfType<Button>().Any(c => c.Name == "VoiceMicrophoneRefresh")
        )
        {
            throw new Exception("Voice settings expose input selection and refresh without starting a microphone");
        }
    }

    private static void ShutdownControls(TabControl tabs)
    {
        foreach (
            var name in new[]
            {
                "ShutdownPlanSummary",
                "ShutdownNextRun",
                "CreateShutdownPlan",
                "DeleteShutdownPlan",
                "ShutdownLog",
            }
        )
        {
            if (!All(tabs).Any(c => c.Name == name))
            {
                throw new Exception("Missing daily shutdown management control: " + name);
            }
        }

        var shutdownCard = All(tabs).Single(c => c.Name == "ShutdownPlanSummary").Parent!;
        if (
            !All(shutdownCard)
                .OfType<Button>()
                .Select(c => c.Name)
                .SequenceEqual(new[] { "CreateShutdownPlan", "DeleteShutdownPlan", "ShutdownLog" })
        )
        {
            throw new Exception("Shutdown settings only expose create, delete and logs");
        }
    }

    private static void OverlayControls(TabControl tabs)
    {
        if (All(tabs).Any(c => c.Name == "LoadingOverlayHotkey"))
        {
            throw new Exception("Removed overlay shortcut must not remain configurable");
        }

        if (!All(tabs).Any(c => c.Text.Contains("Win＋G")))
        {
            throw new Exception("Game Bar management instructions remain available");
        }

        if (!All(tabs).OfType<CheckBox>().Single(c => c.Name == "AutoShowLoadingOverlay").Checked)
        {
            throw new Exception("Automatic loading overlay defaults on");
        }

        if (!All(tabs).OfType<Button>().Single(c => c.Name == "PreviewLoadingOverlay").Enabled)
        {
            throw new Exception("Desktop overlay preview is available without a match");
        }
    }

    private static void AutoAcceptControls(TabControl tabs, Control original)
    {
        var count = All(tabs).OfType<ComboBox>().Single(c => c.Name == "CardMatchHistoryCount");
        if (!count.Items.Cast<int>().SequenceEqual(new[] { 10, 20, 30, 50 }) || !Equals(count.SelectedItem, 20))
        {
            throw new Exception("Upstream history count choices default to 20");
        }

        var delay = All(tabs).OfType<ComboBox>().Single(c => c.Name == "AutoAcceptDelaySeconds");
        ((CheckBox)original).Checked = true;
        if (!delay.Enabled || delay.Items.Count != 3)
        {
            throw new Exception("Auto-accept delay follows enable switch");
        }
        ((CheckBox)original).Checked = false;
        if (delay.Enabled)
        {
            throw new Exception("Disabled auto accept also disables delay");
        }
    }

    private static void RenderLayouts(TabControl tabs)
    {
        using var preview = new Form { ClientSize = new Size(1050, 800) };
        preview.Controls.Add(tabs);
        preview.Show();
        Application.DoEvents();
        Save(preview, "settings-general");
        var shutdown = All(tabs).Single(c => c.Name == "ShutdownPlanSummary");
        ((FlowLayoutPanel)shutdown.Parent!.Parent!).AutoScrollPosition = new Point(
            0,
            Math.Max(0, shutdown.Parent.Top - 16)
        );
        Application.DoEvents();
        Save(preview, "settings-shutdown");
        preview.ClientSize = new Size(650, 720);
        Application.DoEvents();
        Save(preview, "settings-shutdown-narrow");
        preview.ClientSize = new Size(1050, 800);
        tabs.SelectedIndex = 1;
        Application.DoEvents();
        Save(preview, "settings-messages");
        var voice = All(tabs).Single(c => c.Name == "VoiceMicrophone");
        ((FlowLayoutPanel)voice.Parent!.Parent!).AutoScrollPosition = new Point(0, Math.Max(0, voice.Parent.Top - 16));
        var microphoneDeadline = DateTime.UtcNow.AddSeconds(3);
        while (!voice.Enabled && DateTime.UtcNow < microphoneDeadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Save(preview, "settings-voice");
        preview.ClientSize = new Size(650, 720);
        Application.DoEvents();
        Save(preview, "settings-voice-narrow");
        preview.ClientSize = new Size(1050, 800);
        tabs.SelectedIndex = 2;
        Application.DoEvents();
        Save(preview, "settings-launcher");
        preview.ClientSize = new Size(650, 720);
        tabs.SelectedIndex = 3;
        Application.DoEvents();
        Save(preview, "settings-cache-narrow");
        WaitForShutdownRead(shutdown);
    }

    private static void WaitForShutdownRead(Control shutdown)
    {
        // Keep the shared message loop alive until the initial scheduler query publishes its result.
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (shutdown.Text == "正在读取每日关机计划…" && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Check(shutdown.Text != "正在读取每日关机计划…", "The initial shutdown-plan read completed.");
    }

    private static void Save(Form preview, string name)
    {
        using var bitmap = new Bitmap(preview.Width, preview.Height);
        preview.DrawToBitmap(bitmap, preview.ClientRectangle);
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, name + ".png"));
    }
}
