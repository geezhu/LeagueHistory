using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed class AssistantEquipmentEditor : Form
{
    private Func<int, Task<(Image? image, string name, string description)>> _itemLoader = id =>
        FormMain.Globals.resLoading.GetItemInfoAsync(id);
    private readonly OpggBuild _build;
    private readonly JObject[] _cores,
        _starters,
        _boots;
    private readonly string _source;
    private readonly ToolTip _tips;
    private readonly Func<JObject, string> _itemOption;
    private readonly Action<int, int, int> _changed;
    private readonly Action<Form> _position;
    private readonly CancellationTokenSource _lifetime;
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 250 };
    private readonly HashSet<Task> _work = new();
    private Task _cleanupWork = Task.CompletedTask;
    private CancellationTokenSource? _rendering;
    private bool _saving,
        _closing;
    private TableLayoutPanel _filters = null!;
    private FlowLayoutPanel _preview = null!;
    private Button _save = null!;
    private Label _feedback = null!;
    private ComboBox _coreBox = null!,
        _starterBox = null!,
        _bootsBox = null!;

    public AssistantEquipmentEditor(
        OpggBuild build,
        JObject[] cores,
        int coreIndex,
        int starterIndex,
        int bootsIndex,
        CancellationToken token,
        Action<int, int, int> changed,
        string source,
        Control owner,
        ToolTip tips,
        Func<JObject, string> itemOption,
        Action<Form> position
    )
    {
        _build = build;
        _cores = cores;
        _starters = OpggItemSetService.Options(build.Data, "starter_items");
        _boots = OpggItemSetService.Options(build.Data, "boots");
        _source = source;
        _changed = changed;
        _tips = tips;
        _itemOption = itemOption;
        _position = position;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Text = source + " 出装方案";
        ClientSize = new Size(660, 620);
        MinimumSize = new Size(530, 460);
        StartPosition = FormStartPosition.Manual;
        BackColor = owner.BackColor;
        ForeColor = owner.ForeColor;
        Font = owner.Font;
        ShowInTaskbar = false;
        CreateFilters(coreIndex, starterIndex, bootsIndex);
        CreatePreview();
        CreateFooter();
        ConnectEvents();
    }

    private void CreateFilters(int coreIndex, int starterIndex, int bootsIndex)
    {
        _filters = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 152,
            ColumnCount = 2,
            Padding = new Padding(10),
            RowCount = 3,
        };
        _filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
        _filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _coreBox = Choice("核心方案", _cores, coreIndex, 0);
        _starterBox = Choice("出门装", _starters, starterIndex, 1);
        _bootsBox = Choice("鞋子", _boots, bootsIndex, 2);
        if (_source == "arammeta")
        {
            _starterBox.Visible = false;
            _filters.GetControlFromPosition(0, 1)!.Visible = false;
            _filters.RowStyles.Clear();
            _filters.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            _filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            _filters.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            _filters.Height = 108;
        }
    }

    private void CreatePreview()
    {
        _preview = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 4, 12, 4),
        };
    }

    private void CreateFooter()
    {
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            Padding = new Padding(10),
        };
        _save = new Button
        {
            Text = "应用出装",
            Dock = DockStyle.Right,
            Width = 110,
            BackColor = AccentColors.Fill,
            FlatStyle = FlatStyle.Flat,
        };
        _save.FlatAppearance.BorderColor = AccentColors.Fill;
        _save.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 145, 158);
        _save.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 105, 118);
        _feedback = new Label
        {
            Dock = DockStyle.Fill,
            Text = "保存所选出门装、鞋子、核心与后续装备。\n进入游戏后，在商店的物品套装中查看。",
        };
        if (_source == "arammeta")
        {
            _feedback.Text = "保存核心、鞋子与后续装备 · 数据来自 arammeta。\n进入游戏后，在商店的物品套装中查看。";
        }

        footer.Controls.Add(_feedback);
        footer.Controls.Add(_save);
        this.Controls.Add(_preview);
        this.Controls.Add(_filters);
        this.Controls.Add(footer);
    }

    private void ConnectEvents()
    {
        foreach (var box in new[] { _coreBox, _starterBox, _bootsBox })
        {
            box.SelectedIndexChanged += (_, _) => _ = Track(RenderAsync());
        }

        _preview.SizeChanged += (_, _) =>
        {
            foreach (var grid in _preview.Controls.OfType<FlowLayoutPanel>())
            {
                grid.MaximumSize = new Size(
                    Math.Max(124, _preview.ClientSize.Width - 24 - SystemInformation.VerticalScrollBarWidth),
                    0
                );
            }
        };
        _save.Click += async (_, _) => await Track(SaveAsync());
        Shown += (_, _) =>
        {
            _position(this);
            _ = Track(RenderAsync());
        };
        _watch.Tick += (_, _) =>
        {
            if (_lifetime.IsCancellationRequested)
            {
                Close();
            }
        };
        _watch.Start();
        FormClosed += (_, _) => _ = Track(_lifetime.CancelAsync());
    }

    private async Task AddItemTileAsync(int id, FlowLayoutPanel grid, CancellationToken renderToken)
    {
        renderToken.ThrowIfCancellationRequested();
        var info = await _itemLoader(id);
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
        var option = (_build.Data["last_items"] as JArray ?? new())
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
            + (option != null ? "\n" + _itemOption(option) : "");
        foreach (Control child in tile.Controls)
        {
            _tips.SetToolTip(child, details);
        }
    }

    private async Task SaveAsync()
    {
        if (_closing || _saving || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _saving = true;
        _save.Enabled = false;
        _filters.Enabled = false;
        _feedback.Text = "正在保存并校验物品套装…";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var payload = Payload();
            var client = FormMain.Globals.lcuClient?.Client ?? throw new InvalidOperationException("客户端未连接。");
            await OpggItemSetService.SaveAsync(client, payload, timeout.Token);
            if (!IsDisposed)
            {
                _feedback.Text = "出装已保存并校验成功。\n进入游戏后，在商店的物品套装中查看。";
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _feedback.Text =
                    ex is OperationCanceledException ? "保存中断或超时，请检查客户端物品套装。" : ex.Message;
            }
        }
        finally
        {
            _saving = false;
            if (!IsDisposed)
            {
                _save.Enabled = !_lifetime.IsCancellationRequested;
                _filters.Enabled = !_lifetime.IsCancellationRequested;
            }
        }
    }

    private ComboBox Choice(string title, JObject[] options, int index, int row)
    {
        var box = new Controls.EquipmentComboBox(options, _lifetime.Token) { Dock = DockStyle.Fill };
        box.SelectedIndex = Math.Clamp(index, 0, box.Items.Count - 1);
        void UpdateTip()
        {
            if (options.Length > 0)
            {
                _tips.SetToolTip(box, _itemOption(options[box.SelectedIndex]));
            }
        }
        box.SelectedIndexChanged += (_, _) => UpdateTip();
        UpdateTip();
        _filters.Controls.Add(
            new Label
            {
                Text = title,
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0),
            },
            0,
            row
        );
        _filters.Controls.Add(box, 1, row);
        return box;
    }

    private JObject Payload() =>
        OpggItemSetService.Create(
            _build,
            FormMain.Globals.resLoading.GetChampionById(_build.Query.ChampionId)?.Name
                ?? _build.Query.ChampionId.ToString(),
            _cores[_coreBox.SelectedIndex],
            _starters.ElementAtOrDefault(_starterBox.SelectedIndex),
            _boots.ElementAtOrDefault(_bootsBox.SelectedIndex),
            _source
        );

    private async Task RenderAsync()
    {
        if (_closing || _lifetime.IsCancellationRequested)
        {
            return;
        }

        Retire(_rendering);
        _rendering = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var renderToken = _rendering.Token;
        foreach (Control child in _preview.Controls.Cast<Control>().ToArray())
        {
            child.Dispose();
        }

        _changed(_coreBox.SelectedIndex, _starterBox.SelectedIndex, _bootsBox.SelectedIndex);
        try
        {
            var later = OpggItemSetService.Later(_build.Data, _cores[_coreBox.SelectedIndex]);
            _preview.Controls.Add(
                new Label
                {
                    Text = $"后续可选装备 · {later.Length} 件",
                    AutoSize = true,
                    ForeColor = Color.FromArgb(208, 185, 130),
                    Margin = new Padding(0, 6, 0, 2),
                }
            );
            _preview.Controls.Add(
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
                    Math.Max(124, _preview.ClientSize.Width - 24 - SystemInformation.VerticalScrollBarWidth),
                    0
                ),
                Margin = Padding.Empty,
            };
            _preview.Controls.Add(grid);
            if (later.Length == 0)
            {
                grid.Controls.Add(new Label { Text = "当前方案暂无其他可选装备", AutoSize = true });
            }

            foreach (int id in later)
            {
                await AddItemTileAsync(id, grid, renderToken);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer option selection owns the replacement preview.
        }
        catch (Exception)
        {
            if (!_lifetime.IsCancellationRequested && !IsDisposed)
            {
                _feedback.Text = "部分预览图标暂不可用，仍可按所选装备保存。";
            }
        }
    }

    private Task Track(Task task)
    {
        lock (_work)
        {
            _work.Add(task);
        }
        _ = task.ContinueWith(
            completed =>
            {
                lock (_work)
                {
                    _work.Remove(completed);
                }
                if (completed.Exception is { } error)
                {
                    System.Diagnostics.Debug.WriteLine("Equipment editor: " + error.GetBaseException().GetType().Name);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        return task;
    }

    private Task[] Pending()
    {
        lock (_work)
        {
            return _work.ToArray();
        }
    }

    private void Retire(CancellationTokenSource? source)
    {
        if (source == null)
        {
            return;
        }
        var pending = Pending();
        var cancellation = source.CancelAsync();
        Track(
            Task.WhenAll(pending.Append(cancellation))
                .ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        source.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                )
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            _watch.Stop();
            _watch.Dispose();
            Retire(_rendering);
            _rendering = null;
            Retire(_lifetime);
            _cleanupWork = Task.WhenAll(Pending());
        }
        base.Dispose(disposing);
    }
}
