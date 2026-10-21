using League.UIHelpers;

namespace League.Controls;

internal sealed class LolLauncherPanel : UserControl
{
    private readonly Label _status = new()
    {
        AutoEllipsis = true,
        Text = "正在查找安装目录…",
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Panel _pathFrame = new() { BackColor = Theme.Canvas };
    private readonly TextBox _path = new()
    {
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Canvas,
        ForeColor = Color.FromArgb(39, 62, 79),
        Text = "尚未识别安装位置",
        AccessibleName = "英雄联盟安装路径",
    };
    private readonly FlowLayoutPanel _buttons = new() { WrapContents = true, Margin = Padding.Empty };
    private readonly Label _hint = new()
    {
        Text = "也可手动启动游戏，登录后会自动连接。",
        ForeColor = Color.FromArgb(112, 129, 145),
    };
    private readonly Button _official = new()
    {
        Text = "启动官方客户端",
        AutoSize = true,
        Enabled = false,
    };
    private readonly Button _wegame = new()
    {
        Text = "启动 WeGame 版",
        AutoSize = true,
        Enabled = false,
    };
    private readonly ToolTip _tips = new();
    private LolInstallation? _installation;
    private bool _selected;
    private bool _closing;
    private readonly Font _panelFont = new("Microsoft YaHei UI", 9);
    private Func<Task<LolInstallation?>> _detectInstallation = LOLHelper.Shared.DetectAsync;
    private Func<string, Task<LolInstallation>> _saveInstallation = path =>
        Task.Run(() => LOLHelper.Shared.SaveInstallation(path), CancellationToken.None);
    private Func<string, Task> _startLauncher = path =>
        Task.Run(() => LOLHelper.Shared.StartValidatedLauncher(path), CancellationToken.None);

    public LolLauncherPanel()
    {
        Font = _panelFont;
        Size = new Size(580, 156);
        var choose = new Button { Text = "选择安装目录", AutoSize = true };
        foreach (var button in new[] { choose, _official, _wegame })
        {
            button.AutoSize = false;
            button.Size = new Size(button == _wegame ? 132 : 124, 34);
            button.FlatStyle = FlatStyle.Flat;
            button.Margin = new Padding(0, 0, 8, 6);
            button.BackColor = button == choose ? AccentColors.Fill : Theme.SurfaceSubtle;
            button.ForeColor = button == choose ? Color.White : Color.FromArgb(59, 81, 99);
            button.FlatAppearance.BorderColor = button == choose ? button.BackColor : Color.FromArgb(207, 220, 230);
        }
        _buttons.Controls.AddRange(new Control[] { choose, _official, _wegame });
        _pathFrame.Controls.Add(_path);
        Controls.AddRange(new Control[] { _status, _pathFrame, _buttons, _hint });
        Arrange();
        _official.Click += async (_, _) => await StartAsync(_installation?.OfficialPath);
        _wegame.Click += async (_, _) => await StartAsync(_installation?.WeGamePath);
        choose.Click += async (_, _) => await ChooseInstallationAsync(choose);
    }

    private async Task ChooseInstallationAsync(Button choose)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择英雄联盟安装目录（包含 LeagueClient 文件夹）",
            UseDescriptionForTitle = true,
            SelectedPath = _installation?.Root ?? "",
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK || _closing)
        {
            return;
        }
        await SaveInstallationAsync(dialog.SelectedPath, choose);
    }

    private async Task SaveInstallationAsync(string path, Button choose)
    {
        if (_closing)
        {
            return;
        }
        _selected = true;
        choose.Enabled = false;
        try
        {
            var installation = await _saveInstallation(path);
            if (!_closing)
            {
                ShowInstallation(installation);
            }
        }
        catch (Exception ex)
        {
            if (!_closing)
            {
                ShowInstallation(_installation);
                MessageBox.Show(FindForm(), ex.Message, "无法保存安装目录");
            }
        }
        finally
        {
            if (!_closing)
            {
                choose.Enabled = true;
            }
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _ = DetectInstallationAsync();
    }

    private async Task DetectInstallationAsync()
    {
        if (_closing)
        {
            return;
        }
        var detection = _detectInstallation();
        try
        {
            var installation = await detection.WaitAsync(TimeSpan.FromSeconds(8));
            if (!_closing && !_selected)
            {
                ShowInstallation(installation);
            }
        }
        catch (Exception ex)
        {
            // Detection is shared by launch panels. It may continue after our timeout; observe a later failure too.
            _ = detection.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
            System.Diagnostics.Debug.WriteLine($"LOL path detection: {ex.GetType().Name}");
            if (!_closing && !_selected)
            {
                ShowInstallation(null);
                _status.Text = "识别暂不可用 · 请手动选择目录";
            }
        }
    }

    private void ShowInstallation(LolInstallation? installation)
    {
        _installation = installation;
        _status.Text = installation != null ? "已识别安装目录" : "未找到安装目录 · 请手动选择";
        _status.ForeColor = installation != null ? AccentColors.Accent : Color.FromArgb(166, 113, 44);
        _path.Text = installation?.Root ?? "尚未设置英雄联盟安装路径";
        _tips.SetToolTip(_path, installation?.Root);
        _path.Select(0, 0);
        _official.Enabled = installation?.OfficialPath != null;
        _wegame.Enabled = installation?.WeGamePath != null;
        _tips.SetToolTip(_official, installation?.OfficialPath);
        _tips.SetToolTip(_wegame, installation?.WeGamePath);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Arrange();
    }

    private void Arrange()
    {
        if (_closing || _buttons == null || _buttons.Controls.Count == 0)
        {
            return;
        }

        int width = Math.Max(180, ClientSize.Width),
            line = Math.Max(22, Font.Height + 5);
        _status.SetBounds(0, 0, width, line);
        int pathHeight = Math.Max(38, _path.PreferredHeight + 16);
        _pathFrame.SetBounds(0, _status.Bottom + 6, width, pathHeight);
        _path.SetBounds(12, (pathHeight - _path.PreferredHeight) / 2, width - 24, _path.PreferredHeight);
        int rows = 1,
            used = 0;
        foreach (Control button in _buttons.Controls)
        {
            int space = button.Width + button.Margin.Horizontal;
            if (used > 0 && used + space > width)
            {
                rows++;
                used = 0;
            }
            used += space;
        }
        _buttons.SetBounds(0, _pathFrame.Bottom + 10, width, rows * 40);
        int hintHeight =
            TextRenderer.MeasureText(_hint.Text, Font, new Size(width, 0), TextFormatFlags.WordBreak).Height + 4;
        _hint.SetBounds(0, _buttons.Bottom + 2, width, hintHeight);
        int height = _hint.Bottom + 2;
        if (Height != height)
        {
            Height = height;
        }
    }

    private async Task StartAsync(string? path)
    {
        if (path == null || _closing)
        {
            return;
        }

        _official.Enabled = _wegame.Enabled = false;
        try
        {
            await _startLauncher(path);
        }
        catch (Exception ex)
        {
            if (!_closing)
            {
                MessageBox.Show(FindForm(), ex.Message, "启动失败");
            }
        }
        finally
        {
            if (!_closing)
            {
                ShowInstallation(_installation);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            _tips.Dispose();
            _panelFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
