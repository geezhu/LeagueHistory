using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    internal static Rectangle EquipmentEditorBounds(Rectangle client, Rectangle work, Size size)
    {
        int width = Math.Min(size.Width, work.Width),
            height = Math.Min(size.Height, work.Height);
        return new Rectangle(
            Math.Clamp(client.Left + (client.Width - width) / 2, work.Left, work.Right - width),
            Math.Clamp(client.Top + (client.Height - height) / 2, work.Top, work.Bottom - height),
            width,
            height
        );
    }

    private void PositionEquipmentEditor(Form editor, Size? minimum = null)
    {
        Rectangle work = Screen.FromControl(this).WorkingArea,
            client = work;
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("LeagueClientUx"))
        {
            using (process)
            {
                try
                {
                    var handle = process.MainWindowHandle;
                    if (
                        handle == IntPtr.Zero
                        || !IsWindowVisible(handle)
                        || IsIconic(handle)
                        || !GetWindowRect(handle, out var rect)
                    )
                        continue;
                    client = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                    work = Screen.FromHandle(handle).WorkingArea;
                    break;
                }
                catch
                { /* Fall back to the current monitor if the client exits. */
                }
            }
        }
        var minimumSize = minimum ?? new Size(530, 460);
        editor.MinimumSize = new Size(
            Math.Min(minimumSize.Width, work.Width),
            Math.Min(minimumSize.Height, work.Height)
        );
        editor.Bounds = EquipmentEditorBounds(client, work, editor.Size);
    }

    private string ItemOption(JObject option)
    {
        string names = string.Join(
            " → ",
            OpggItemSetService
                .Ids(option)
                .Select(id => FormMain.Globals.resLoading.GetItemById(id)?.Name ?? id.ToString())
        );
        double? win = OpggBuild.Ratio(option["win"], option["play"]),
            pick = (double?)option["pick_rate"];
        return $"{names} · 胜率 {(win.HasValue ? win.Value.ToString("P2") : "—")} / 使用率 {(pick.HasValue ? pick.Value.ToString("P2") : "—")} · {(long?)option["play"] ?? 0:N0} 场";
    }

    private void AddEquipmentChoices(OpggBuild build, CancellationToken token)
    {
        var cores = OpggItemSetService.Options(build.Data, "core_items");
        if (cores.Length == 0)
        {
            Line("暂无核心装备方案");
            return;
        }
        var section = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MaximumSize = new Size(282, 0),
            Margin = Padding.Empty,
        };
        var select = new Controls.EquipmentComboBox(cores, token) { Width = 190, Margin = new Padding(0, 0, 4, 0) };
        var configure = new Button
        {
            Text = "配置出装",
            Width = 82,
            Height = select.Height,
            FlatStyle = FlatStyle.Flat,
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(16, 45, 57),
            ForeColor = ForeColor,
        };
        configure.FlatAppearance.BorderColor = Color.FromArgb(42, 72, 83);
        configure.FlatAppearance.MouseOverBackColor = Color.FromArgb(23, 65, 78);
        configure.FlatAppearance.MouseDownBackColor = Color.FromArgb(23, 82, 95);
        section.Controls.AddRange(new Control[] { select, configure });
        section.SetFlowBreak(configure, true);
        _body.Controls.Add(section);
        int starterIndex = 0,
            bootsIndex = 0;
        void Refresh() => _tips.SetToolTip(select, ItemOption(cores[select.SelectedIndex]));
        select.SelectedIndexChanged += (_, _) =>
        {
            if (!token.IsCancellationRequested)
                Refresh();
        };
        Refresh();
        configure.Click += (_, _) =>
        {
            if (token.IsCancellationRequested)
                return;
            ShowEquipmentEditor(
                build,
                cores,
                select.SelectedIndex,
                starterIndex,
                bootsIndex,
                token,
                (core, starter, boots) =>
                {
                    if (token.IsCancellationRequested || select.IsDisposed)
                        return;
                    starterIndex = starter;
                    bootsIndex = boots;
                    select.SelectedIndex = core;
                }
            );
        };
    }

    private void ShowEquipmentEditor(
        OpggBuild build,
        JObject[] cores,
        int coreIndex,
        int starterIndex,
        int bootsIndex,
        CancellationToken token,
        Action<int, int, int> changed,
        string source = "OP.GG"
    )
    {
        using var dialog = new Form
        {
            Text = source + " 出装方案",
            ClientSize = new Size(660, 620),
            MinimumSize = new Size(530, 460),
            StartPosition = FormStartPosition.Manual,
            BackColor = BackColor,
            ForeColor = ForeColor,
            Font = Font,
            ShowInTaskbar = false,
        };
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var starters = OpggItemSetService.Options(build.Data, "starter_items");
        var boots = OpggItemSetService.Options(build.Data, "boots");
        var filters = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 152,
            ColumnCount = 2,
            Padding = new Padding(10),
            RowCount = 3,
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ComboBox Choice(string title, JObject[] options, int index, int row)
        {
            var box = new Controls.EquipmentComboBox(options, lifetime.Token) { Dock = DockStyle.Fill };
            box.SelectedIndex = Math.Clamp(index, 0, box.Items.Count - 1);
            void UpdateTip()
            {
                if (options.Length > 0)
                    _tips.SetToolTip(box, ItemOption(options[box.SelectedIndex]));
            }
            box.SelectedIndexChanged += (_, _) => UpdateTip();
            UpdateTip();
            filters.Controls.Add(
                new Label
                {
                    Text = title,
                    AutoSize = true,
                    Margin = new Padding(0, 6, 0, 0),
                },
                0,
                row
            );
            filters.Controls.Add(box, 1, row);
            return box;
        }
        var coreBox = Choice("核心方案", cores, coreIndex, 0);
        var starterBox = Choice("出门装", starters, starterIndex, 1);
        var bootsBox = Choice("鞋子", boots, bootsIndex, 2);
        if (source == "arammeta")
        {
            starterBox.Visible = false;
            filters.GetControlFromPosition(0, 1)!.Visible = false;
            filters.RowStyles.Clear();
            filters.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            filters.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            filters.Height = 108;
        }
        var preview = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 4, 12, 4),
        };
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            Padding = new Padding(10),
        };
        var save = new Button
        {
            Text = "应用出装",
            Dock = DockStyle.Right,
            Width = 110,
            BackColor = Color.FromArgb(0, 126, 139),
            FlatStyle = FlatStyle.Flat,
        };
        save.FlatAppearance.BorderColor = Color.FromArgb(0, 126, 139);
        save.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 145, 158);
        save.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 105, 118);
        var feedback = new Label
        {
            Dock = DockStyle.Fill,
            Text = "保存所选出门装、鞋子、核心与后续装备。\n进入游戏后，在商店的物品套装中查看。",
        };
        if (source == "arammeta")
            feedback.Text = "保存核心、鞋子与后续装备 · 数据来自 arammeta。\n进入游戏后，在商店的物品套装中查看。";
        footer.Controls.Add(feedback);
        footer.Controls.Add(save);
        dialog.Controls.Add(preview);
        dialog.Controls.Add(filters);
        dialog.Controls.Add(footer);
        CancellationTokenSource? rendering = null;
        JObject Payload() =>
            OpggItemSetService.Create(
                build,
                FormMain.Globals.resLoading.GetChampionById(build.Query.ChampionId)?.Name
                    ?? build.Query.ChampionId.ToString(),
                cores[coreBox.SelectedIndex],
                starters.ElementAtOrDefault(starterBox.SelectedIndex),
                boots.ElementAtOrDefault(bootsBox.SelectedIndex),
                source
            );
        async Task Render()
        {
            if (lifetime.IsCancellationRequested)
                return;
            rendering?.Cancel();
            rendering?.Dispose();
            rendering = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var renderToken = rendering.Token;
            foreach (Control child in preview.Controls.Cast<Control>().ToArray())
                child.Dispose();
            changed(coreBox.SelectedIndex, starterBox.SelectedIndex, bootsBox.SelectedIndex);
            try
            {
                var later = OpggItemSetService.Later(build.Data, cores[coreBox.SelectedIndex]);
                preview.Controls.Add(
                    new Label
                    {
                        Text = $"后续可选装备 · {later.Length} 件",
                        AutoSize = true,
                        ForeColor = Color.FromArgb(208, 185, 130),
                        Margin = new Padding(0, 6, 0, 2),
                    }
                );
                preview.Controls.Add(
                    new Label
                    {
                        Text = "已排除当前核心装备 · 悬浮查看详情",
                        AutoSize = true,
                        ForeColor = Color.FromArgb(135, 162, 173),
                        Margin = new Padding(0, 0, 0, 8),
                    }
                );
                var grid = new FlowLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    MaximumSize = new Size(
                        Math.Max(124, preview.ClientSize.Width - 24 - SystemInformation.VerticalScrollBarWidth),
                        0
                    ),
                    Margin = Padding.Empty,
                };
                preview.Controls.Add(grid);
                if (later.Length == 0)
                    grid.Controls.Add(new Label { Text = "当前方案暂无其他可选装备", AutoSize = true });
                foreach (int id in later)
                {
                    renderToken.ThrowIfCancellationRequested();
                    var info = await FormMain.Globals.resLoading.GetItemInfoAsync(id);
                    if (renderToken.IsCancellationRequested || grid.IsDisposed)
                    {
                        info.image?.Dispose();
                        renderToken.ThrowIfCancellationRequested();
                        return;
                    }
                    var tile = new Panel
                    {
                        Size = new Size(116, 52),
                        Margin = new Padding(0, 0, 4, 4),
                        BackColor = Color.FromArgb(16, 45, 57),
                    };
                    var icon = new PictureBox
                    {
                        Image = info.image,
                        Size = new Size(32, 32),
                        Location = new Point(5, 10),
                        SizeMode = PictureBoxSizeMode.Zoom,
                    };
                    icon.Disposed += (_, _) => icon.Image?.Dispose();
                    var name = new Label
                    {
                        Text = info.name,
                        AutoEllipsis = true,
                        Size = new Size(73, 20),
                        Location = new Point(41, 5),
                    };
                    var option = (build.Data["last_items"] as JArray ?? new())
                        .OfType<JObject>()
                        .FirstOrDefault(o => OpggItemSetService.Ids(o).Contains(id));
                    var rate = OpggBuild.Ratio(option?["win"], option?["play"]);
                    var statistics = new Label
                    {
                        Text = rate.HasValue ? $"胜率 {rate:P1}" : "胜率 —",
                        Size = new Size(73, 20),
                        Location = new Point(41, 27),
                        ForeColor = Color.FromArgb(51, 193, 199),
                    };
                    tile.Controls.AddRange(new Control[] { icon, name, statistics });
                    grid.Controls.Add(tile);
                    string details =
                        League.Controls.TooltipText.Format(info.name, info.description)
                        + (option != null ? "\n" + ItemOption(option) : "");
                    foreach (Control child in tile.Controls)
                        _tips.SetToolTip(child, details);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!lifetime.IsCancellationRequested)
                    feedback.Text = "部分预览图标暂不可用，仍可按所选装备保存。";
            }
        }
        foreach (var box in new[] { coreBox, starterBox, bootsBox })
            box.SelectedIndexChanged += (_, _) => _ = Render();
        preview.SizeChanged += (_, _) =>
        {
            foreach (var grid in preview.Controls.OfType<FlowLayoutPanel>())
                grid.MaximumSize = new Size(
                    Math.Max(124, preview.ClientSize.Width - 24 - SystemInformation.VerticalScrollBarWidth),
                    0
                );
        };
        bool saving = false;
        save.Click += async (_, _) =>
        {
            if (saving || lifetime.IsCancellationRequested)
                return;
            saving = true;
            save.Enabled = false;
            filters.Enabled = false;
            feedback.Text = "正在保存并校验物品套装…";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                var payload = Payload();
                var client =
                    FormMain.Globals.lcuClient?.Client ?? throw new InvalidOperationException("客户端未连接。");
                await OpggItemSetService.SaveAsync(client, payload, timeout.Token);
                if (!dialog.IsDisposed)
                    feedback.Text = "出装已保存并校验成功。\n进入游戏后，在商店的物品套装中查看。";
            }
            catch (Exception ex)
            {
                if (!dialog.IsDisposed)
                    feedback.Text =
                        ex is OperationCanceledException ? "保存中断或超时，请检查客户端物品套装。" : ex.Message;
            }
            finally
            {
                saving = false;
                if (!dialog.IsDisposed)
                {
                    save.Enabled = !lifetime.IsCancellationRequested;
                    filters.Enabled = !lifetime.IsCancellationRequested;
                }
            }
        };
        dialog.Shown += (_, _) =>
        {
            PositionEquipmentEditor(dialog);
            _ = Render();
        };
        using var watch = new System.Windows.Forms.Timer { Interval = 250 };
        watch.Tick += (_, _) =>
        {
            if (token.IsCancellationRequested)
            {
                lifetime.Cancel();
                dialog.Close();
            }
        };
        watch.Start();
        dialog.FormClosed += (_, _) =>
        {
            lifetime.Cancel();
            rendering?.Cancel();
        };
        dialog.ShowDialog(this);
        rendering?.Dispose();
    }
}
