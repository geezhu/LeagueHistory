using League.Controls;

namespace League;

public partial class FormMain
{
    private void ConfigureLauncherSettings()
    {
        var page = new TabPage("客户端目录") { Padding = new Padding(16), UseVisualStyleBackColor = true };
        var group = new GroupBox { Text = "英雄联盟安装目录", Dock = DockStyle.Top, Height = 190, Padding = new Padding(12,24,12,12) };
        var launcher = new LolLauncherPanel { Dock = DockStyle.Fill };
        group.Controls.Add(launcher);
        page.Controls.Add(group);
        tabControl1.TabPages.Add(page);
    }
}
