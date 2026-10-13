using League.Controls;

namespace League;

public partial class FormMain
{
    private void ConfigureAramMetaSettings()
    {
        var page = new TabPage("数据缓存") { Padding = new Padding(16), UseVisualStyleBackColor = true };
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
        };
        AddCacheLifetimeSetting(
            layout,
            "arammeta 统计数据缓存有效期",
            _appConfig?.AramMetaCacheMinutes ?? 1440,
            30,
            "单位：分钟，默认 1440 分钟（1 天），最多 7 天。修改后自动保存，下次加载时生效。\n未过期的数据可在重启后直接使用；过期后检查更新。\n点击页面“刷新”可立即检查更新；更新失败时显示旧缓存并注明状态。",
            SaveAramMetaCacheLifetime
        );
        AddCacheLifetimeSetting(
            layout,
            "普通大乱斗平衡缓存有效期 · aramonly",
            _appConfig?.AramBalanceCacheMinutes ?? 1440,
            30,
            "单位：分钟，默认 1440（1 天）。缓存保存到本地，过期后在查看平衡提示时更新；失败时保留旧数据。",
            SaveAramBalanceCacheLifetime
        );
        var mastery = AddCacheLifetimeSetting(
            layout,
            "玩家英雄成就缓存有效期 · 英雄联盟客户端",
            _appConfig?.ChampionMasteryCacheMinutes ?? 30,
            10,
            "单位：分钟，默认 30。助手、个人资料和对局速览共用本地缓存。\n过期后后台更新，失败时保留旧数据；自己的对局结束后自动检查更新。",
            SaveMasteryCacheLifetime
        );
        mastery.Name = "ChampionMasteryCacheMinutes";
        Clients.ChampionMasteryService.Shared.CacheLifetime = TimeSpan.FromMinutes((int)mastery.Value);
        page.Controls.Add(layout);
        tabControl1.TabPages.Add(page);
    }

    private static NumericUpDown AddCacheLifetimeSetting(
        FlowLayoutPanel layout,
        string title,
        int value,
        int increment,
        string description,
        Action<int> save
    )
    {
        layout.Controls.Add(
            new Label
            {
                Text = title,
                AutoSize = true,
                Margin = new Padding(0, layout.Controls.Count == 0 ? 0 : 18, 0, 10),
            }
        );
        var minutes = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 10080,
            Increment = increment,
            Width = 150,
            Value = Math.Clamp(value, 1, 10080),
        };
        layout.Controls.Add(minutes);
        layout.Controls.Add(
            new Label
            {
                Text = description,
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 0),
            }
        );
        minutes.ValueChanged += (_, _) => save((int)minutes.Value);
        return minutes;
    }

    private void SaveMasteryCacheLifetime(int minutes)
    {
        if (_appConfig == null)
        {
            return;
        }
        _appConfig.ChampionMasteryCacheMinutes = minutes;
        Clients.ChampionMasteryService.Shared.CacheLifetime = TimeSpan.FromMinutes(minutes);
        SaveAppConfig();
    }

    private void SaveAramMetaCacheLifetime(int minutes)
    {
        if (_appConfig != null)
        {
            _appConfig.AramMetaCacheMinutes = minutes;
            SaveAppConfig();
        }
    }

    private void SaveAramBalanceCacheLifetime(int minutes)
    {
        if (_appConfig != null)
        {
            _appConfig.AramBalanceCacheMinutes = minutes;
            SaveAppConfig();
        }
    }

    private void ConfigureLauncherSettings()
    {
        var page = new TabPage("客户端目录") { Padding = new Padding(16), UseVisualStyleBackColor = true };
        var group = new GroupBox
        {
            Text = "英雄联盟安装目录",
            Dock = DockStyle.Top,
            Height = 190,
            Padding = new Padding(12, 24, 12, 12),
        };
        var launcher = new LolLauncherPanel { Dock = DockStyle.Fill };
        group.Controls.Add(launcher);
        page.Controls.Add(group);
        tabControl1.TabPages.Add(page);
    }
}
