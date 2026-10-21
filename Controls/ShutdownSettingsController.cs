using League.Services;

namespace League.Controls;

/// <summary>Owns the settings card's refresh state and timer; only explicit clicks mutate the plan.</summary>
internal sealed class ShutdownSettingsController : IDisposable
{
    private enum Operation
    {
        Idle,
        Reading,
        Changing,
    }

    private enum SchedulerState
    {
        Loading,
        Available,
        Failed,
    }

    private readonly Font _summaryFont;
    private readonly Form _owner;
    private readonly ScheduledShutdownService _service;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };
    private readonly Label _summary = new() { Name = "ShutdownPlanSummary", Text = "正在读取每日关机计划…" };

    // The tooltip on the button was the only place that explained the behavior; it stays visible now.
    private readonly Label _purpose = new()
    {
        Name = "ShutdownPurpose",
        Text =
            "每天到点自动关机，防止熬夜。到点时正在对局会等本局结束再关；关机前倒计时可在托盘取消本次。关闭联盟视界后计划仍然有效。",
    };

    // The next run joins the one-line summary; this label keeps the value for diagnostics and checks.
    private readonly Label _nextRun = new()
    {
        Name = "ShutdownNextRun",
        Text = "",
        Visible = false,
    };
    private readonly Label _status = new()
    {
        Name = "ShutdownStatus",
        Text = "",
        Visible = false,
    };
    private readonly ToolTip _tips = new();
    private readonly Label _result = new()
    {
        Name = "ShutdownResult",
        Text = "",
        Visible = false,
    };
    private readonly DateTimePicker _time = new()
    {
        Name = "ShutdownTime",
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "HH:mm",
        ShowUpDown = true,
        Width = 110,
        Value = DateTime.Today.AddHours(23),
    };
    private readonly NumericUpDown _countdown = new()
    {
        Name = "ShutdownCountdown",
        Minimum = 0,
        Maximum = 300,
        Increment = 5,
        Value = ScheduledShutdownService.DefaultCountdownSeconds,
        Width = 100,
    };
    private readonly Button _create = new()
    {
        Name = "CreateShutdownPlan",
        Text = "创建计划",
        Enabled = false,
    };
    private readonly Button _delete = new()
    {
        Name = "DeleteShutdownPlan",
        Text = "删除计划",
        Enabled = false,
    };
    private readonly Button _log = new() { Name = "ShutdownLog", Text = "查看日志" };
    private readonly FlowLayoutPanel _clockRow;
    private readonly FlowLayoutPanel _actions;
    private Operation _operation;
    private SchedulerState _schedulerState;
    private ShutdownTaskView? _view;
    private bool _updatingInputs;
    private bool _dirty;
    private bool _disposed;

    public ShutdownSettingsController(Form owner, Font font, ScheduledShutdownService service)
    {
        _owner = owner;
        _service = service;
        _summaryFont = new Font(font, FontStyle.Bold);
        _summary.Font = _summaryFont;
        _clockRow = ClockRow();
        _actions = ButtonRow(_create, _delete, _log);
        ButtonStyles.Primary(_create);
        _result.TextChanged += (_, _) => _result.Visible = !string.IsNullOrWhiteSpace(_result.Text);
        _time.ValueChanged += (_, _) => InputsChanged();
        _countdown.ValueChanged += (_, _) => InputsChanged();
        _create.Click += async (_, _) => await CreateAsync();
        _delete.Click += async (_, _) => await DeleteAsync();
        _log.Click += (_, _) => ShowLog();
        _tips.SetToolTip(_create, "保存后立即按新的时间与倒计时启用，替换原计划。");
        UpdateEnabledControls();
    }

    public Control[] Content => [_purpose, _summary, _nextRun, _status, _clockRow, _actions, _result];

    public void Attach()
    {
        var card = (FlowLayoutPanel)_summary.Parent!;
        card.SizeChanged += (_, _) =>
        {
            _clockRow.MaximumSize = _actions.MaximumSize = new Size(Math.Max(240, card.ClientSize.Width - 40), 0);
        };
        _timer.Tick += async (_, _) =>
        {
            if (card.Visible)
            {
                await RefreshAsync();
            }
        };
        card.Disposed += (_, _) => Dispose();
        card.HandleCreated += async (_, _) => await InitializeAsync();
        if (card.IsHandleCreated)
        {
            _ = InitializeAsync();
        }
    }

    private async Task InitializeAsync()
    {
        if (_view == null)
        {
            await RefreshAsync();
        }
        if (!_disposed)
        {
            _timer.Start();
        }
    }

    private async Task RefreshAsync()
    {
        if (_disposed || _operation != Operation.Idle)
        {
            return;
        }
        _operation = Operation.Reading;
        UpdateEnabledControls();
        try
        {
            var view = await _service.ReadAsync();
            if (!_disposed)
            {
                ShowView(view, _view == null || !_dirty);
            }
        }
        catch (Exception error)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "Shutdown settings read", error);
            if (!_disposed)
            {
                _schedulerState = SchedulerState.Failed;
                _summary.Text = "每日关机计划读取失败";
                _summary.ForeColor = Theme.Loss;
                _nextRun.Text = "";
                _status.Text = "读取计划失败：" + error.Message;
                _status.Visible = true;
            }
        }
        finally
        {
            _operation = Operation.Idle;
            if (!_disposed)
            {
                UpdateEnabledControls();
            }
        }
    }

    private void ShowView(ShutdownTaskView view, bool populate)
    {
        _view = view;
        _schedulerState = SchedulerState.Available;
        _summary.Text = view.Exists
            ? $"{(view.Plan.Enabled ? "已启用" : "已停用")} · 每天 {view.Plan.Time} · 倒计时 {view.Plan.CountdownSeconds} 秒"
                + (view.NextRun is { } upcoming ? $" · 下次 {upcoming:MM-dd HH:mm}" : "")
            : "尚未创建每日关机计划";
        _summary.ForeColor = view.Plan.Enabled ? Theme.Win : Theme.Muted;
        _nextRun.Text = view.NextRun is { } next
            ? $"下次执行：{next:yyyy-MM-dd HH:mm}（到点后检查对局状态）"
            : "下次执行：—";
        _status.Text = view.Message;
        // Routine status stays in the tooltip; only states that need the user's attention are shown.
        _status.Visible = view.Exists && (!view.Managed || view.Message.Contains("正在执行"));
        _tips.SetToolTip(_summary, view.Message);
        _create.Text = view.Exists ? "保存计划" : "创建计划";
        if (populate)
        {
            _updatingInputs = true;
            try
            {
                _time.Value = DateTime.Today.Add(TimeSpan.Parse(view.Plan.Time));
                _countdown.Value = view.Plan.CountdownSeconds;
                _dirty = false;
            }
            finally
            {
                _updatingInputs = false;
            }
        }
    }

    private void UpdateEnabledControls()
    {
        bool idle = _operation == Operation.Idle;
        bool available = _schedulerState == SchedulerState.Available && idle;
        _time.Enabled = _countdown.Enabled = _create.Enabled = available;
        _delete.Enabled = available && _view is { Exists: true };
        _log.Enabled = idle;
    }

    private void InputsChanged()
    {
        if (_view != null && !_updatingInputs)
        {
            _dirty = true;
            _result.Text = $"点击“{_create.Text}”应用当前时间与倒计时。";
        }
    }

    private Task CreateAsync()
    {
        string time = _time.Value.ToString("HH:mm");
        int seconds = (int)_countdown.Value;
        string success = _view is { Exists: true } ? "每日关机计划已更新并启用。" : "每日关机计划已创建并启用。";
        return OperateAsync(() => _service.CreateAsync(time, seconds), success);
    }

    private async Task DeleteAsync()
    {
        if (_disposed || _operation != Operation.Idle)
        {
            return;
        }
        if (
            MessageBox.Show(
                _owner,
                "删除这条每日关机计划，并停止当前等待或倒计时？\n运行日志和原任务备份会保留。",
                "删除关机计划",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2
            ) == DialogResult.Yes
        )
        {
            await OperateAsync(_service.DeleteAsync, "每日关机计划已删除；需要时可重新创建。");
        }
    }

    private async Task OperateAsync(Func<Task<ShutdownTaskView>> action, string success)
    {
        if (_disposed || _operation != Operation.Idle || _schedulerState != SchedulerState.Available)
        {
            return;
        }
        _operation = Operation.Changing;
        UpdateEnabledControls();
        try
        {
            var view = await action();
            if (!_disposed)
            {
                ShowView(view, true);
                _result.Text = success;
            }
        }
        catch (Exception error)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "Shutdown settings mutation", error);
            if (!_disposed)
            {
                _result.Text = "操作未完成：" + error.Message;
            }
        }
        finally
        {
            _operation = Operation.Idle;
            if (!_disposed)
            {
                UpdateEnabledControls();
            }
        }
    }

    private void ShowLog()
    {
        try
        {
            if (!File.Exists(_service.LogPath))
            {
                _result.Text = "暂无运行日志，计划首次执行后会生成。";
                return;
            }
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(_service.LogPath) { UseShellExecute = true }
            );
        }
        catch (Exception error)
        {
            _result.Text = "无法打开日志：" + error.Message;
        }
    }

    private FlowLayoutPanel ClockRow()
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Margin = Padding.Empty,
        };
        row.Controls.Add(
            new Label
            {
                Text = "每日时间",
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0),
            }
        );
        row.Controls.Add(_time);
        row.Controls.Add(
            new Label
            {
                Text = "关机前倒计时（秒）",
                AutoSize = true,
                Margin = new Padding(16, 6, 8, 0),
            }
        );
        row.Controls.Add(_countdown);
        return row;
    }

    private static FlowLayoutPanel ButtonRow(params Button[] buttons)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Margin = Padding.Empty,
        };
        foreach (var button in buttons)
        {
            button.Size = new Size(110, 32);
            ButtonStyles.Secondary(button);
            button.Margin = new Padding(0, 0, 8, 4);
            row.Controls.Add(button);
        }
        return row;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Dispose();
        _tips.Dispose();
        _summaryFont.Dispose();
    }
}
