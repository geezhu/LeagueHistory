using League.Services;

namespace League;

public partial class FormMain
{
    private void ConfigureShutdownSettings(FlowLayoutPanel parent, ScheduledShutdownService? schedulerService = null)
    {
        var service = schedulerService ?? new ScheduledShutdownService();
        var summary = new Label
        {
            Name = "ShutdownPlanSummary",
            Text = "正在读取每日关机计划…",
            Font = new Font(tabControl1.Font, FontStyle.Bold),
        };
        var nextRun = new Label { Name = "ShutdownNextRun", Text = "" };
        var status = new Label { Name = "ShutdownStatus", Text = "" };
        var time = new DateTimePicker
        {
            Name = "ShutdownTime",
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm",
            ShowUpDown = true,
            Width = 110,
            Value = DateTime.Today.AddHours(23),
        };
        var countdown = new NumericUpDown
        {
            Name = "ShutdownCountdown",
            Minimum = 0,
            Maximum = 300,
            Increment = 15,
            Value = 30,
            Width = 100,
        };
        var create = new Button
        {
            Name = "CreateShutdownPlan",
            Text = "创建计划",
            Enabled = false,
        };
        var delete = new Button
        {
            Name = "DeleteShutdownPlan",
            Text = "删除计划",
            Enabled = false,
        };
        var log = new Button { Name = "ShutdownLog", Text = "查看日志" };
        var result = new Label
        {
            Name = "ShutdownResult",
            Text = "",
            Visible = false,
        };
        result.TextChanged += (_, _) => result.Visible = !string.IsNullOrWhiteSpace(result.Text);
        var clockRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Margin = Padding.Empty,
        };
        clockRow.Controls.Add(
            new Label
            {
                Text = "每日时间",
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0),
            }
        );
        clockRow.Controls.Add(time);
        clockRow.Controls.Add(
            new Label
            {
                Text = "关机前倒计时（秒）",
                AutoSize = true,
                Margin = new Padding(16, 6, 8, 0),
            }
        );
        clockRow.Controls.Add(countdown);
        FlowLayoutPanel ButtonRow(params Button[] items)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = true,
                Margin = Padding.Empty,
            };
            foreach (var button in items)
            {
                button.Size = new Size(110, 32);
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(207, 220, 230);
                button.BackColor = Color.FromArgb(247, 250, 252);
                button.Margin = new Padding(0, 0, 8, 4);
                row.Controls.Add(button);
            }
            return row;
        }
        var actions = ButtonRow(create, delete, log);
        create.BackColor = Color.FromArgb(0, 145, 155);
        create.ForeColor = Color.White;
        SettingsCard(
            parent,
            "定时关机 · 每日计划",
            summary,
            nextRun,
            status,
            clockRow,
            actions,
            result,
            new Label
            {
                Text =
                    "创建后立即启用每日计划；重复创建会替换原计划。到点仍在游戏则等待本局结束，退出联盟视界后仍有效。",
            }
        );
        var card = (FlowLayoutPanel)summary.Parent!;
        card.SizeChanged += (_, _) =>
        {
            clockRow.MaximumSize = actions.MaximumSize = new Size(Math.Max(240, card.ClientSize.Width - 40), 0);
        };
        bool busy = false,
            loaded = false,
            available = false,
            reading = false,
            populating = false,
            dirty = false;
        ShutdownTaskView? view = null;
        void ControlsReady()
        {
            time.Enabled = countdown.Enabled = create.Enabled = available && !busy;
            delete.Enabled = available && !busy && view is { Exists: true };
            log.Enabled = !busy;
        }
        void Show(ShutdownTaskView value, bool populate)
        {
            view = value;
            summary.Text = value.Exists
                ? $"{(value.Plan.Enabled ? "已启用" : "已停用")} · 每天 {value.Plan.Time} · 倒计时 {value.Plan.CountdownSeconds} 秒"
                : "尚未创建每日关机计划";
            summary.ForeColor = value.Plan.Enabled ? Color.FromArgb(0, 145, 155) : Color.FromArgb(70, 92, 112);
            nextRun.Text = value.NextRun is { } next
                ? $"下次执行：{next:yyyy-MM-dd HH:mm}（到点后检查对局状态）"
                : "下次执行：—";
            status.Text = value.Message;
            if (populate)
            {
                populating = true;
                time.Value = DateTime.Today.Add(TimeSpan.Parse(value.Plan.Time));
                countdown.Value = value.Plan.CountdownSeconds;
                populating = false;
                dirty = false;
            }
            loaded = available = true;
            ControlsReady();
        }
        async Task Refresh()
        {
            if (reading || busy || status.IsDisposed)
            {
                return;
            }

            reading = true;
            try
            {
                var value = await service.ReadAsync();
                if (!status.IsDisposed && !busy)
                {
                    Show(value, !loaded || !dirty);
                }
            }
            catch (Exception ex)
            {
                if (!status.IsDisposed)
                {
                    available = false;
                    view = null;
                    summary.Text = "每日关机计划读取失败";
                    summary.ForeColor = Color.FromArgb(176, 70, 70);
                    nextRun.Text = "";
                    status.Text = "读取计划失败：" + ex.Message;
                    ControlsReady();
                }
            }
            finally
            {
                reading = false;
            }
        }
        void Changed()
        {
            if (!loaded || populating)
            {
                return;
            }

            dirty = true;
            result.Text = "点击“创建计划”应用当前时间与倒计时。";
        }
        time.ValueChanged += (_, _) => Changed();
        countdown.ValueChanged += (_, _) => Changed();
        async Task Operate(Func<Task> action)
        {
            if (busy)
            {
                return;
            }

            busy = true;
            ControlsReady();
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                if (!result.IsDisposed)
                {
                    result.Text = "操作未完成：" + ex.Message;
                }
            }
            finally
            {
                busy = false;
                if (!status.IsDisposed)
                {
                    ControlsReady();
                }
            }
        }
        create.Click += async (_, _) =>
            await Operate(async () =>
            {
                var value = await service.CreateAsync(time.Value.ToString("HH:mm"), (int)countdown.Value);
                if (!status.IsDisposed)
                {
                    Show(value, true);
                    result.Text = "每日关机计划已创建并启用。";
                }
            });
        delete.Click += async (_, _) =>
        {
            if (
                MessageBox.Show(
                    this,
                    "删除这条每日关机计划，并停止当前等待或倒计时？\n运行日志和原任务备份会保留。",
                    "删除关机计划",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2
                ) != DialogResult.Yes
            )
            {
                return;
            }

            await Operate(async () =>
            {
                var value = await service.DeleteAsync();
                if (!status.IsDisposed)
                {
                    Show(value, true);
                    result.Text = "每日关机计划已删除；需要时可重新创建。";
                }
            });
        };
        log.Click += (_, _) =>
        {
            try
            {
                if (!File.Exists(service.LogPath))
                {
                    result.Text = "暂无运行日志，计划首次执行后会生成。";
                    return;
                }
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(service.LogPath) { UseShellExecute = true }
                );
            }
            catch (Exception ex)
            {
                result.Text = "无法打开日志：" + ex.Message;
            }
        };
        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += async (_, _) =>
        {
            if (card.Visible)
            {
                await Refresh();
            }
        };
        card.Disposed += (_, _) => timer.Dispose();
        card.HandleCreated += async (_, _) =>
        {
            if (!loaded)
            {
                await Refresh();
                if (!card.IsDisposed)
                {
                    timer.Start();
                }
            }
        };
        ControlsReady();
        // Opening settings only reads the existing task; all changes require a button click.
    }
}
