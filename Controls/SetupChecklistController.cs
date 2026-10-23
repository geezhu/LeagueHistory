using League.Services;

namespace League.Controls;

/// <summary>
/// The 准备情况 rows: each shows what was detected and, while it is not ready, one button that fixes it or opens the
/// settings page that does. The owner calls <see cref="Refresh"/> each time the page comes into view, so returning
/// from an install or a download shows the result.
/// </summary>
internal sealed class SetupChecklistController : IDisposable
{
    private static readonly Color AttentionColor = Color.FromArgb(178, 92, 20);
    private readonly SetupEnvironment _environment;
    private readonly Func<bool> _gameBarChosen;
    private readonly Action _exitForRestart;
    private readonly Item _administrator;
    private readonly Item _gameBar;
    private readonly Item _voice;
    private CancellationTokenSource? _reading;
    private bool _disposed;

    public SetupChecklistController(
        SetupEnvironment environment,
        Func<bool> gameBarChosen,
        Action<string> openSettingsPage,
        Action exitForRestart
    )
    {
        _environment = environment;
        _gameBarChosen = gameBarChosen;
        _exitForRestart = exitForRestart;
        _administrator = new Item("SetupAdministrator", "管理员权限", "以管理员身份重启");
        _gameBar = new Item("SetupGameBar", "Game Bar 组件", "前往设置");
        _voice = new Item("SetupVoiceModel", "中文语音模型", "前往下载");
        _administrator.Action.Click += (_, _) => RestartElevated();
        _gameBar.Action.Click += (_, _) => openSettingsPage("对局速览");
        _voice.Action.Click += (_, _) => openSettingsPage("消息发送");
        _administrator.Row.Disposed += (_, _) => Dispose();
    }

    public Control[] Rows => [_administrator.Row, _gameBar.Row, _voice.Row];

    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        _administrator.Show(SetupChecklist.Administrator(_environment.IsAdministrator()));
        _voice.Show(SetupChecklist.VoiceModel(_environment.VoiceModelAvailable()));
        _gameBar.Show(SetupChecklist.GameBar(_gameBarChosen(), null));
        _reading?.Cancel();
        _reading?.Dispose();
        _reading = new CancellationTokenSource();
        _ = ReadGameBarAsync(_reading.Token);
    }

    private async Task ReadGameBarAsync(CancellationToken token)
    {
        try
        {
            var status = await _environment.ReadGameBarPackage(token);
            // A newer refresh or closing the page retires this result.
            if (!token.IsCancellationRequested)
            {
                _gameBar.Show(SetupChecklist.GameBar(_gameBarChosen(), status));
            }
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer refresh, or the page closed.
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "准备情况：无法检查 Game Bar 组件", e);
            if (!token.IsCancellationRequested)
            {
                _gameBar.Show(new SetupItem(SetupItemState.Optional, "无法检查组件状态：" + e.Message));
            }
        }
    }

    private void RestartElevated()
    {
        if (_environment.StartElevatedCopy())
        {
            _exitForRestart();
            return;
        }

        _administrator.Status.Text = "⚠ 已取消管理员授权，继续以当前权限运行";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reading?.Cancel();
        _reading?.Dispose();
    }

    private sealed class Item
    {
        public Item(string name, string title, string action)
        {
            Status = new Label { Name = name + "Status" };
            Action = new Button
            {
                Name = name + "Action",
                Text = action,
                Visible = false,
            };
            Row = new SettingRow(title, null, Action);
            Row.AddNote(Status);
        }

        public SettingRow Row { get; }

        public Label Status { get; }

        public Button Action { get; }

        public void Show(SetupItem item)
        {
            if (Status.IsDisposed)
            {
                return;
            }

            (string mark, Color color) = item.State switch
            {
                SetupItemState.Ready => ("✓ ", Theme.Win),
                SetupItemState.NeedsAction => ("⚠ ", AttentionColor),
                SetupItemState.Optional => ("○ ", Theme.Muted),
                _ => ("", Theme.Muted),
            };
            Status.Text = mark + item.Detail;
            Status.ForeColor = color;
            Action.Visible = item.State is SetupItemState.NeedsAction or SetupItemState.Optional;
        }
    }
}
