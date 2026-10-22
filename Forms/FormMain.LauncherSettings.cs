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
        AddAugmentTextStatus(layout);
        page.Controls.Add(layout);
        tabControl1.TabPages.Add(page);
    }

    // The game's augment descriptions follow the patch rather than a lifetime: they are read once per patch and kept
    // until the client updates. The row says where they are and offers a fresh download.
    private void AddAugmentTextStatus(FlowLayoutPanel layout)
    {
        var status = Services.AugmentTextStatus.Shared;
        layout.Controls.Add(
            new Label
            {
                Text = "海克斯说明 · CommunityDragon",
                AutoSize = true,
                Margin = new Padding(0, 18, 0, 10),
            }
        );
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        var state = new Label
        {
            Name = "AugmentTextState",
            AutoSize = true,
            Margin = new Padding(0, 6, 12, 0),
        };
        var reload = new Button { Text = "重新下载", AutoSize = true };
        ButtonStyles.Secondary(reload);
        row.Controls.AddRange(new Control[] { state, reload });
        layout.Controls.Add(row);
        layout.Controls.Add(
            new Label
            {
                Text =
                    "游戏内的强化说明与数值，按游戏版本保存；客户端更新后自动重新下载，无需设置有效期。\n首次下载约 45MB，期间显示内置说明。说明有误时可重新下载。",
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 0),
            }
        );
        void Show()
        {
            state.Text = status.Describe();
            reload.Enabled = status.State != Services.AugmentTextState.Downloading;
        }
        // Raised on the download's thread.
        void Changed()
        {
            if (state.IsHandleCreated && !state.IsDisposed)
            {
                state.BeginInvoke(Show);
            }
        }
        status.Changed += Changed;
        state.Disposed += (_, _) => status.Changed -= Changed;
        state.HandleCreated += (_, _) => Show();
        reload.Click += async (_, _) =>
        {
            reload.Enabled = false;
            await Globals.resLoading.ReloadAugmentTextsAsync();
        };
        Show();
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
