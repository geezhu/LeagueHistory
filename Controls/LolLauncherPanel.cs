using League.UIHelpers;

namespace League.Controls;

internal sealed class LolLauncherPanel : UserControl
{
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 44, TextAlign = ContentAlignment.MiddleCenter,
        AutoEllipsis = true, Text = "正在查找英雄联盟安装目录…" };
    private readonly Button _official = new() { Text = "启动官方客户端", AutoSize = true, Enabled = false };
    private readonly Button _wegame = new() { Text = "启动 WeGame 版", AutoSize = true, Enabled = false };
    private readonly ToolTip _tips = new();
    private LolInstallation? _installation;
    private bool _selected;

    public LolLauncherPanel()
    {
        Size = new Size(580,150);
        var choose = new Button { Text = "选择安装目录", AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, WrapContents = false, Padding = new Padding(50,4,0,0) };
        buttons.Controls.AddRange(new Control[] { _official, _wegame, choose });
        var hint = new Label { Dock = DockStyle.Top, Height = 35, TextAlign = ContentAlignment.MiddleCenter,
            Text = "也可以手动启动英雄联盟，登录后会自动连接。", ForeColor = Color.DimGray };
        Controls.Add(hint); Controls.Add(buttons); Controls.Add(_status);
        _official.Click += (_,_) => Start(_installation?.OfficialPath);
        _wegame.Click += (_,_) => Start(_installation?.WeGamePath);
        choose.Click += async (_,_) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "选择英雄联盟安装目录（包含 LeagueClient 文件夹）", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
            _selected = true;
            choose.Enabled = false;
            try
            {
                var installation = await Task.Run(() => LOLHelper.Shared.SaveInstallation(dialog.SelectedPath));
                if (!IsDisposed) ShowInstallation(installation);
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    ShowInstallation(_installation);
                    MessageBox.Show(FindForm(), ex.Message, "无法保存安装目录");
                }
            }
            finally { if (!IsDisposed) choose.Enabled = true; }
        };
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            var installation = await LOLHelper.Shared.DetectAsync().WaitAsync(TimeSpan.FromSeconds(8));
            if (!IsDisposed && !_selected) ShowInstallation(installation);
        }
        catch (Exception)
        {
            if (!IsDisposed && !_selected) _status.Text = "暂时无法识别安装目录，请手动选择。";
        }
    }

    private void ShowInstallation(LolInstallation? installation)
    {
        _installation = installation;
        _status.Text = installation?.Root ?? "未找到有效的英雄联盟安装目录，请手动选择。";
        _tips.SetToolTip(_status, installation?.Root);
        _official.Enabled = installation?.OfficialPath != null;
        _wegame.Enabled = installation?.WeGamePath != null;
        _tips.SetToolTip(_official, installation?.OfficialPath);
        _tips.SetToolTip(_wegame, installation?.WeGamePath);
    }

    private async void Start(string? path)
    {
        if (path == null) return;
        _official.Enabled = _wegame.Enabled = false;
        try { await Task.Run(() => LOLHelper.Shared.StartValidatedLauncher(path)); }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(FindForm(), ex.Message, "启动失败"); }
        finally { if (!IsDisposed) ShowInstallation(_installation); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}
