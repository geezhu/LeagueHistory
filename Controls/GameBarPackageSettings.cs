using League.Services;

namespace League.Controls;

/// <summary>Settings row that shows the Game Bar component's installed and bundled versions and installs it.</summary>
internal sealed class GameBarPackageSettings : IDisposable
{
    private readonly GameBarPackageInstaller _installer;
    private readonly Label _status = new() { Text = "正在检查组件…" };
    private readonly Button _install = new() { Text = "安装组件", Enabled = false };
    private readonly CancellationTokenSource _life = new();
    private bool _busy;

    public GameBarPackageSettings(GameBarPackageInstaller? installer = null)
    {
        _installer = installer ?? new GameBarPackageInstaller();
        Row = new SettingRow("Game Bar 组件", null, _install);
        Row.AddNote(_status);
        _install.Click += async (_, _) => await InstallAsync();
        Row.Disposed += (_, _) => Dispose();
        // Checking runs PowerShell, so it waits until the row is actually on screen.
        Row.VisibleChanged += (_, _) =>
        {
            if (Row.Visible)
            {
                Refresh();
            }
        };
    }

    public SettingRow Row { get; }

    public Task Refreshing { get; private set; } = Task.CompletedTask;

    public void Refresh()
    {
        if (!_busy)
        {
            Refreshing = RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        _busy = true;
        _install.Enabled = false;
        try
        {
            Show(await _installer.ReadStatusAsync(_life.Token));
        }
        catch (OperationCanceledException)
        {
            // The settings page closed while checking.
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            SetStatus("无法检查组件状态：" + e.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Show(GameBarPackageStatus status)
    {
        if (_install.IsDisposed)
        {
            return;
        }

        _status.Text = status.Text;
        _install.Text = status.State switch
        {
            GameBarPackageState.Outdated => "更新组件",
            GameBarPackageState.Current => "重新安装",
            _ => "安装组件",
        };
        _install.Enabled = status.State != GameBarPackageState.NotBundled;
    }

    private async Task InstallAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _install.Enabled = false;
        SetStatus("正在安装组件；首次安装需要在管理员授权中选择“是”以信任组件证书…");
        bool installed = false;
        try
        {
            await _installer.InstallAsync(_life.Token);
            installed = true;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            SetStatus(e.Message);
        }
        finally
        {
            _busy = false;
        }

        if (installed)
        {
            await RefreshAsync();
            SetStatus(_status.Text + " · 安装完成，按 Win＋G 打开「League 对局速览」。");
        }
        else if (!_install.IsDisposed)
        {
            _install.Enabled = true;
        }
    }

    private void SetStatus(string text)
    {
        if (!_status.IsDisposed)
        {
            _status.Text = text;
        }
    }

    public void Dispose()
    {
        if (!_life.IsCancellationRequested)
        {
            // Cancelling completes pending waits before the source is released.
            _life.Cancel();
            _life.Dispose();
        }
    }
}
