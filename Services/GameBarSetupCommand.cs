namespace League.Services;

/// <summary>
/// <c>League.exe --install-gamebar</c>: the installer's last step, run as the signed-in user so the component installs
/// for them. It uses the same code as the settings button; a failure is shown once and can be retried there.
/// </summary>
internal static class GameBarSetupCommand
{
    public const string Argument = "--install-gamebar";

    public static int Run()
    {
        var installer = new GameBarPackageInstaller();
        try
        {
            // Called from Main before any window or message loop exists, so blocking here is safe.
            var status = installer.ReadStatusAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (status.State == GameBarPackageState.Current)
            {
                AppLog.Shared.Info(LogCategory.App, "安装程序：Game Bar 组件已是 " + status.Installed);
                return 0;
            }

            installer.InstallAsync(CancellationToken.None).GetAwaiter().GetResult();
            AppLog.Shared.Info(LogCategory.App, "安装程序：Game Bar 组件已安装 " + status.Bundled);
            return 0;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            AppLog.Shared.Error(LogCategory.App, "安装程序：Game Bar 组件安装失败", e);
            MessageBox.Show(
                e.Message + "\n\nLeague 本身已装好，可以稍后在「设置 → Game Bar 组件」重新安装。",
                "Game Bar 组件未安装",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return 1;
        }
        finally
        {
            AppLog.Shared.FlushAsync().GetAwaiter().GetResult();
        }
    }
}
