using League.Controls;
using League.Services;

namespace League;

public partial class FormMain
{
    private bool _exportingDiagnostics;
    private UiHangMonitor? _hangMonitor;

    private void StartHangMonitor()
    {
        _hangMonitor = new UiHangMonitor(
            action => BeginInvoke(action),
            () =>
                $"游戏阶段 {_gameFlowWatcher?.CurrentPhase ?? "未知"}，客户端{(_uiManager?.LcuReady == true ? "已连接" : "未连接")}"
        );
        _hangMonitor.Start();
    }

    private void StopHangMonitor()
    {
        _hangMonitor?.Dispose();
        _hangMonitor = null;
    }

    private SettingRow CreateDiagnosticsRow()
    {
        var export = new Button { Name = "ExportDiagnostics", Text = "导出诊断报告…" };
        export.Click += async (_, _) => await ExportDiagnosticReportAsync(export);
        return new SettingRow(
            "诊断报告",
            "包含版本、系统环境、程序状态和最近 7 天的日志，已去掉令牌、玩家 ID 和自定义发送内容；只保存到本机，不会上传。",
            export
        );
    }

    private async Task ExportDiagnosticReportAsync(Button? button = null)
    {
        if (_exportingDiagnostics)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "导出诊断报告",
            Filter = "诊断报告 (*.zip)|*.zip",
            FileName = $"联盟视界诊断-{DateTime.Now:yyyyMMdd-HHmm}.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _exportingDiagnostics = true;
        if (button != null)
        {
            button.Enabled = false;
        }
        bool connected = _uiManager?.LcuReady == true;
        var state = new DiagnosticAppState(
            connected,
            _uiManager?.IsGame == true,
            _loadingOverlay?.GameBarStatus ?? "",
            _appConfig,
            connected ? Globals.lcuClient?.Client : null,
            _gameFlowWatcher?.CurrentPhase ?? "未知"
        );
        try
        {
            // Reading logs and checking the voice model files must stay off the UI thread.
            int count = await Task.Run(() =>
                DiagnosticReportExporter.CreateDefault().ExportAsync(dialog.FileName, state, CancellationToken.None)
            );
            ShowExportedReport(dialog.FileName, count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Shared.Warn(LogCategory.App, "导出诊断报告失败", ex);
            MessageBox.Show(
                this,
                $"导出失败：{ex.Message}\n请换一个保存位置再试。",
                "诊断报告",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }
        finally
        {
            _exportingDiagnostics = false;
            if (button is { IsDisposed: false })
            {
                button.Enabled = true;
            }
        }
    }

    private void ShowExportedReport(string path, int count)
    {
        MessageBox.Show(
            this,
            $"已导出诊断报告（含 {count} 个日志文件）：\n{path}\n\n反馈问题时把这个文件发给开发者即可。",
            "诊断报告",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information
        );
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 打不开资源管理器时，路径已在提示框里给出。
        }
    }

    /// <summary>上次后台异常导致退出时，窗口显示后询问是否导出报告；只问一次。</summary>
    private void ShowCrashPromptIfNeeded()
    {
        var crash = CrashReporter.TakeLastCrash();
        if (crash == null || IsDisposed)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"联盟视界上次在 {crash.Time:MM-dd HH:mm} 异常退出（{crash.Error}）。\n\n要现在导出诊断报告，方便反馈问题吗？",
            "诊断报告",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question
        );
        if (answer == DialogResult.Yes)
        {
            _ = ExportDiagnosticReportAsync();
        }
    }
}
