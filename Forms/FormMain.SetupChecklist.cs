using League.Controls;

namespace League;

public partial class FormMain
{
    private TabPage? _setupPage;

    // First in the settings list, so a new user lands on what still needs doing.
    private void ConfigureSetupChecklist()
    {
        _setupPage = new TabPage("准备情况");
        tabControl1.TabPages.Insert(0, _setupPage);
        tabControl1.SelectedTab = _setupPage;
        var layout = SettingsPage(_setupPage, "准备情况");
        var controller = new SetupChecklistController(
            _options.Setup ?? new Services.SetupEnvironment(),
            () => _appConfig is { EnableLoadingOverlay: true, LoadingOverlayBackend: "GameBar" },
            OpenSettingsPage,
            Close
        );
        SettingsCard(layout, "本机状态", controller.Rows);
        // Read on selection rather than VisibleChanged: hiding a tab page does not reach its rows, and showing the
        // settings raises it twice, so the rows cannot tell when the page comes back into view.
        void RefreshWhenShown()
        {
            if (imageTabControl1.SelectedTab == tabPage3 && tabControl1.SelectedTab == _setupPage)
            {
                controller.Refresh();
            }
        }
        imageTabControl1.SelectedIndexChanged += (_, _) => RefreshWhenShown();
        tabControl1.SelectedIndexChanged += (_, _) => RefreshWhenShown();
        layout.Controls.Add(
            new Label
            {
                Text = "打开本页时重新检测；在其他设置页安装或下载后，回到这里即可看到结果。",
                AutoSize = true,
                ForeColor = Theme.Muted,
            }
        );
    }

    private void OpenSettingsPage(string title)
    {
        imageTabControl1.SelectedTab = tabPage3;
        tabControl1.SelectedTab = tabControl1.TabPages.Cast<TabPage>().Single(page => page.Text == title);
    }

    // Shown once, on the first start with this page; afterwards it stays in the settings list.
    private void ShowSetupChecklistOnFirstRun()
    {
        if (_appConfig == null || _appConfig.SetupChecklistShown || _setupPage == null)
        {
            return;
        }

        _appConfig.SetupChecklistShown = true;
        SaveAppConfig();
        OpenSettingsPage(_setupPage.Text);
    }
}
