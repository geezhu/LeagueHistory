using League.Controls;
using League.UIHelpers;
using Newtonsoft.Json.Linq;
using static StatisticsCheckSupport;

internal static class LauncherInstallationChecks
{
    public static void Run()
    {
        string rootPath = Path.Combine(AppContext.BaseDirectory, "launcher-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        try
        {
            string FixtureFile(string relative)
            {
                var path = Path.Combine(rootPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "fixture");
                return path;
            }
            var delta = FixtureFile(@"Delta Force\Launcher\startup_runner.exe");
            var deltaWeGame = FixtureFile(@"Delta Force\Launcher\WeGameLauncher\launcher.exe");
            Check(
                League.UIHelpers.LOLHelper.ResolveInstallation(delta) == null
                    && League.UIHelpers.LOLHelper.ResolveInstallation(deltaWeGame) == null,
                "Generic Delta Force launchers are not recognized as League"
            );
            var official = FixtureFile(@"League\Launcher\startup_runner.exe");
            var wegame = FixtureFile(@"League\WeGameLauncher\launcher.exe");
            FixtureFile(@"League\LeagueClient\LeagueClient.exe");
            var ux = FixtureFile(@"League\LeagueClient\LeagueClientUx.exe");
            var installation = League.UIHelpers.LOLHelper.ResolveInstallation(ux);
            Check(
                installation?.OfficialPath == official && installation.WeGamePath == wegame,
                "Verified League install resolves both launchers despite other games"
            );
            LauncherSurface(installation);
            Configuration(rootPath, delta, official, wegame, ux);
        }
        finally
        {
            if (
                Path.GetFullPath(rootPath)
                    .StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase)
            )
            {
                Directory.Delete(rootPath, true);
            }
        }
    }

    private static void LauncherSurface(LolInstallation? installation)
    {
        var launcherPanelType = typeof(PlayerCardControl).Assembly.GetType("League.Controls.LolLauncherPanel")!;
        using (var launcherPanel = (Control)Activator.CreateInstance(launcherPanelType)!)
        {
            launcherPanelType
                .GetField(
                    "_detectInstallation",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                )!
                .SetValue(launcherPanel, (Func<Task<LolInstallation?>>)(() => Task.FromResult(installation)));
            launcherPanelType
                .GetMethod(
                    "ShowInstallation",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                )!
                .Invoke(launcherPanel, new object?[] { installation });
            Check(
                Children(launcherPanel).OfType<Button>().Count(b => b.Enabled) == 3,
                "Verified launchers enable launch and manual selection controls"
            );
            using var screenshot = new Bitmap(580, 150);
            launcherPanel.DrawToBitmap(screenshot, new Rectangle(0, 0, 580, 150));
            screenshot.Save(Path.Combine(AppContext.BaseDirectory, "launcher-panel.png"));
            launcherPanel.Width = 300;
            launcherPanel.PerformLayout();
            var buttons = Children(launcherPanel).OfType<Button>().ToArray();
            Check(
                buttons.All(b => b.Right <= b.Parent!.ClientSize.Width && b.Bottom <= b.Parent.ClientSize.Height),
                "Launcher buttons wrap without clipping in narrow panels"
            );
            Check(
                Children(launcherPanel).OfType<TextBox>().Single().ReadOnly,
                "Installation paths can be selected and copied without bypassing validation"
            );
            using var narrow = new Bitmap(launcherPanel.Width, launcherPanel.Height);
            launcherPanel.DrawToBitmap(narrow, new Rectangle(Point.Empty, narrow.Size));
            narrow.Save(Path.Combine(AppContext.BaseDirectory, "launcher-panel-narrow.png"));
        }
    }

    private static void Configuration(string rootPath, string delta, string official, string wegame, string ux)
    {
        var configPath = Path.Combine(rootPath, "LOLPathConfig.json");
        var helper = new League.UIHelpers.LOLHelper(configPath);
        File.WriteAllText(configPath, new JObject { ["LastLOLPath"] = delta }.ToString());
        Check(helper.ReadSavedInstallation() == null, "Legacy wrong-game cache is ignored");
        bool rejected = false;
        try
        {
            helper.StartValidatedLauncher(delta);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Check(rejected, "Wrong-game launcher is rejected before process start");
        helper.SaveInstallation(Path.GetDirectoryName(Path.GetDirectoryName(ux)!)!);
        var saved = JObject.Parse(File.ReadAllText(configPath));
        Check(
            saved["OfficialPath"]?.ToString() == official
                && saved["WeGamePath"]?.ToString() == wegame
                && saved["LastLOLPath"] == null,
            "Official and WeGame paths persist separately"
        );
        Check(helper.ReadSavedInstallation()?.OfficialPath == official, "Validated settings reload successfully");
        File.Delete(ux);
        Check(helper.ReadSavedInstallation() == null, "Moved or damaged install invalidates saved paths");
        File.WriteAllText(configPath, "broken json");
        Check(helper.ReadSavedInstallation() == null, "Damaged launcher config does not prevent startup");
    }
}
