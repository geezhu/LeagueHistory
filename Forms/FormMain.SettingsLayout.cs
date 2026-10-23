using League.Controls;
using League.Models;

namespace League;

public partial class FormMain
{
    // Readable line length for the settings list; wider cards only add empty space on the right.
    private const int SettingsMaxWidth = 680;
    private readonly ToolTip _settingsTips = new();

    private void ConfigureSettingsLayout()
    {
        Disposed += (_, _) => _settingsTips.Dispose();
        ConfigureSettingsTabs();
        ConfigureGeneralSettings();
        ConfigureOverlayPage();
        ConfigureMessageSettings();
        MoveAdditionalSettings();
        ConfigureSetupChecklist();
    }

    private void ConfigureSettingsTabs()
    {
        tabControl1.Font = new Font("Microsoft YaHei UI", 9);
        // Flat buttons with a one-pixel header leave no visible strip or page border; the side list replaces them.
        tabControl1.Appearance = TabAppearance.FlatButtons;
        tabControl1.SizeMode = TabSizeMode.Fixed;
        tabControl1.ItemSize = new Size(0, 1);
        var navigation = new SettingsNavigation(tabControl1) { Dock = DockStyle.Left };
        tabPage3.Padding = Padding.Empty;
        tabPage3.UseVisualStyleBackColor = false;
        tabPage3.BackColor = Theme.Canvas;
        tabPage3.Controls.Add(navigation);
        tabControl1.BringToFront();
        HoldLayoutWhileSwitching(tabControl1, SettingsFlow);
        HoldLayoutWhileSwitching(
            imageTabControl1,
            page => page == tabPage3 ? SettingsFlow(tabControl1.SelectedTab) : null
        );
    }

    // Bringing a settings page into view makes every auto-sized card re-measure, and each card then re-lays out the
    // whole page: 功能辅助 took 118 page layouts (about 140 ms) per switch. The tab control only shows the page in
    // SelectedIndexChanged, after Selected, so the page is held from Selecting until then and laid out once.
    private static void HoldLayoutWhileSwitching(TabControl tabs, Func<TabPage?, Control?> content)
    {
        Control? held = null;
        tabs.Selecting += (_, e) =>
        {
            // A cancelled switch never reaches SelectedIndexChanged; let go of its page before holding the next.
            held?.ResumeLayout(true);
            held = content(e.TabPage);
            held?.SuspendLayout();
        };
        tabs.SelectedIndexChanged += (_, _) =>
        {
            var page = held;
            held = null;
            page?.ResumeLayout(true);
        };
    }

    private static FlowLayoutPanel? SettingsFlow(TabPage? page) =>
        page?.Controls.OfType<FlowLayoutPanel>().SingleOrDefault();

    private void ConfigureGeneralSettings()
    {
        var general = SettingsPage(tabPage4, "功能辅助");
        // Cards follow when they are used: champion select, the match itself, then the computer.
        ConfigureHistorySettings(general);
        ConfigureAssistantSetting(general);
        ConfigurePreselectSettings(general);
        ConfigureAutomationSettings(general);
        ConfigureChampionAlertSettings(general);
        ConfigureClientMaintenanceSettings(general);
        ConfigureShutdownSettings(general);
        ConfigureAboutSettings(general);
    }

    private void ConfigureHistorySettings(FlowLayoutPanel general)
    {
        var historyCount = new ComboBox
        {
            Name = "CardMatchHistoryCount",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 100,
        };
        historyCount.Items.AddRange(new object[] { 10, 20, 30, 50 });
        int count = _appConfig?.CardMatchHistoryCount ?? 20;
        historyCount.SelectedItem = count is 10 or 20 or 30 or 50 ? count : 20;
        _matchQueryProcessor?.SetMatchHistoryCount((int)historyCount.SelectedItem);
        historyCount.SelectedIndexChanged += (_, _) =>
        {
            if (_appConfig == null)
            {
                return;
            }

            _appConfig.CardMatchHistoryCount = (int)historyCount.SelectedItem;
            _matchQueryProcessor?.SetMatchHistoryCount(_appConfig.CardMatchHistoryCount);
            SaveAppConfig();
        };
        // The designer's checkBoxFilterMode is an explanatory label, not a switch; its text becomes the note.
        checkBoxFilterMode.Dispose();
        SettingsCard(
            general,
            "选人战绩",
            new SettingRow(
                "每位玩家查询场数",
                "排位、匹配和大乱斗类模式查询对应模式，其他模式查询全部模式",
                historyCount
            )
        );
    }

    private void ConfigureAutomationSettings(FlowLayoutPanel general)
    {
        var acceptDelay = new ComboBox
        {
            Name = "AutoAcceptDelaySeconds",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 110,
        };
        acceptDelay.Items.AddRange(new object[] { "立即接受", "延迟 5 秒", "延迟 10 秒" });
        acceptDelay.SelectedIndex = _appConfig?.AutoAcceptDelaySeconds switch
        {
            5 => 1,
            10 => 2,
            _ => 0,
        };
        // The delay only matters while auto accept is on, so it appears beside the switch then.
        void ShowDelay() => acceptDelay.Visible = acceptDelay.Enabled = chkAutoAccept.Checked;
        ShowDelay();
        chkAutoAccept.CheckedChanged += (_, _) => ShowDelay();
        acceptDelay.SelectedIndexChanged += (_, _) =>
        {
            if (_appConfig == null)
            {
                return;
            }

            _appConfig.AutoAcceptDelaySeconds = acceptDelay.SelectedIndex switch
            {
                1 => 5,
                2 => 10,
                _ => 0,
            };
            SaveAppConfig();
        };
        foreach (
            var (check, name) in new[]
            {
                (chkAutoAccept, "自动接受对局"),
                (chkSkipHonor, "自动跳过点赞界面"),
                (chkSkipEndOfGameStats, "自动跳过结算统计界面"),
            }
        )
        {
            check.Text = "";
            check.AccessibleName = name;
        }

        SettingsCard(
            general,
            "对局自动化",
            new SettingRow("自动接受对局", null, acceptDelay, chkAutoAccept),
            new SettingRow("自动跳过点赞界面", null, chkSkipHonor),
            new SettingRow("自动跳过结算统计界面", null, chkSkipEndOfGameStats)
        );
    }

    private void ConfigureAssistantSetting(FlowLayoutPanel general)
    {
        var assistant = new CheckBox
        {
            AccessibleName = "选人时显示选人助手",
            Checked = _appConfig?.EnableChampionSelectAssistant ?? true,
        };
        assistant.CheckedChanged += (_, _) =>
        {
            if (_appConfig != null)
            {
                _appConfig.EnableChampionSelectAssistant = assistant.Checked;
                SaveAppConfig();
            }
        };
        SettingsCard(
            general,
            "选人助手",
            new SettingRow(
                "选人时显示选人助手",
                "贴靠客户端显示英雄推荐，可从客户端旁的标签开关；符文仅在点击「应用方案」时修改。",
                assistant
            )
        );
    }

    private void ConfigureOverlayPage()
    {
        var page = new TabPage("对局速览");
        tabControl1.TabPages.Insert(tabControl1.TabPages.IndexOf(tabPage4) + 1, page);
        var layout = SettingsPage(page, "对局速览");
        ConfigureOverlaySettings(layout);
    }

    private void ConfigureOverlaySettings(FlowLayoutPanel general)
    {
        var overlay = new CheckBox
        {
            AccessibleName = "启用对局速览",
            Checked = _appConfig?.EnableLoadingOverlay ?? true,
        };
        var overlayPreview = new Button
        {
            Name = "PreviewLoadingOverlay",
            Text = "测试桌面显示",
            Enabled = overlay.Checked,
        };
        overlayPreview.Click += (_, _) => _loadingOverlay?.ShowPreview();
        var overlayBackend = new ComboBox
        {
            Name = "LoadingOverlayBackend",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
        };
        overlayBackend.Items.AddRange(new object[] { "桌面窗口", "Game Bar（支持全屏）" });
        overlayBackend.SelectedIndex = _appConfig?.LoadingOverlayBackend == "GameBar" ? 1 : 0;
        var gameBarStatus = new Label { Text = _loadingOverlay?.GameBarStatus ?? "" };
        TrackGameBarSettingsStatus(gameBarStatus);
        var enableRow = new SettingRow("启用对局速览", "在加载界面和游戏中查看十位玩家的段位、战绩和英雄推荐", overlay);
        var backendRow = new SettingRow("显示方式", null, overlayBackend);
        var previewRow = new SettingRow("测试显示", "不需要进入对局，检查桌面窗口是否正常", overlayPreview);
        var gameBarPackage = new Controls.GameBarPackageSettings();
        var waveOpacityRow = CreateWaveOpacityRow();
        BindOverlayBackendHelp(overlayBackend, backendRow, previewRow, gameBarStatus);
        void SaveOverlay()
        {
            if (_appConfig == null)
            {
                return;
            }

            _appConfig.EnableLoadingOverlay = overlay.Checked;
            _appConfig.LoadingOverlayBackend = overlayBackend.SelectedIndex == 1 ? "GameBar" : "Desktop";
            SaveAppConfig();
            _loadingOverlay?.RefreshDisplaySettings();
        }

        // Options under a disabled overlay are hidden rather than greyed out.
        void ShowEnabledOptions()
        {
            overlayBackend.Enabled = overlayPreview.Enabled = overlay.Checked;
            backendRow.Visible = overlay.Checked;
            previewRow.Visible = overlay.Checked && overlayBackend.SelectedIndex == 0;
            gameBarPackage.Row.Visible = waveOpacityRow.Visible = overlay.Checked && overlayBackend.SelectedIndex == 1;
        }
        overlay.CheckedChanged += (_, _) => SaveOverlay();
        overlay.CheckedChanged += (_, _) => ShowEnabledOptions();
        overlayBackend.SelectedIndexChanged += (_, _) => SaveOverlay();
        overlayBackend.SelectedIndexChanged += (_, _) => ShowEnabledOptions();
        ShowEnabledOptions();
        SettingsCard(general, "显示设置", enableRow, backendRow, gameBarPackage.Row, waveOpacityRow, previewRow);
    }

    // The mini wave widget reads the value from the next snapshot, so a change shows without reopening it.
    private SettingRow CreateWaveOpacityRow()
    {
        var opacity = new NumericUpDown
        {
            Name = "GameBarWaveOpacity",
            AccessibleName = "兵线组件背景不透明度",
            Minimum = Models.LeagueConfig.MinGameBarWaveOpacity,
            Maximum = Models.LeagueConfig.MaxGameBarWaveOpacity,
            Increment = 5,
            Width = 80,
            Value = Math.Clamp(
                _appConfig?.GameBarWaveOpacity ?? Models.LeagueConfig.DefaultGameBarWaveOpacity,
                Models.LeagueConfig.MinGameBarWaveOpacity,
                Models.LeagueConfig.MaxGameBarWaveOpacity
            ),
        };
        opacity.ValueChanged += (_, _) =>
        {
            if (_appConfig == null)
            {
                return;
            }

            _appConfig.GameBarWaveOpacity = (int)opacity.Value;
            SaveAppConfig();
            _loadingOverlay?.RefreshDisplaySettings();
        };
        return new SettingRow(
            "兵线组件不透明度（%）",
            "Win＋G 打开「League 兵线」并点图钉固定，可悬浮在游戏上方；只调整背景，文字保持不透明",
            opacity
        );
    }

    // Only the selected backend's instructions and status are shown under the display choice.
    private static void BindOverlayBackendHelp(
        ComboBox backend,
        SettingRow backendRow,
        SettingRow previewRow,
        Label gameBarStatus
    )
    {
        var gameBarHelp = new Label
        {
            Text =
                "按 Win＋G 打开「League 对局速览」，点图钉固定在游戏上方。固定后每局进入加载界面会自动展开；未固定时只在 Win＋G 中显示。",
        };
        var desktopHelp = new Label { Text = "进入加载界面时自动弹出，适用于窗口 / 无边框模式，独占全屏时不弹出。" };
        backendRow.AddNote(desktopHelp);
        backendRow.AddNote(gameBarHelp);
        backendRow.AddNote(gameBarStatus);
        var preview = previewRow.Controls.OfType<FlowLayoutPanel>().Last().Controls[0];
        void Show()
        {
            bool gameBar = backend.SelectedIndex == 1;
            gameBarStatus.Visible = gameBarHelp.Visible = gameBar;
            desktopHelp.Visible = preview.Visible = !gameBar;
        }
        backend.SelectedIndexChanged += (_, _) => Show();
        Show();
    }

    private void TrackGameBarSettingsStatus(Label gameBarStatus)
    {
        var overlayStatusTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        overlayStatusTimer.Tick += (_, _) =>
        {
            if (gameBarStatus.Visible)
            {
                gameBarStatus.Text = _loadingOverlay?.GameBarStatus ?? "";
            }
        };
        gameBarStatus.Disposed += (_, _) => overlayStatusTimer.Dispose();
        overlayStatusTimer.Start();
    }

    private void ConfigurePreselectSettings(FlowLayoutPanel general)
    {
        lkbPreliminary.Text = "配置预选英雄 ›";
        foreach (
            var (check, name) in new[]
            {
                (chkNormal, "匹配"),
                (chkRanked, "排位"),
                (chkAram, "大乱斗"),
                (chkNexus, "海克斯"),
                (chkUrf, "无限乱斗"),
                (chkPickUrf, "无限火力"),
            }
        )
        {
            check.Text = name;
        }

        var classic = PreselectSwitch(
            "chkClassic",
            "经典峡谷",
            _appConfig?.EnablePreliminaryInClassic ?? false,
            enabled => _appConfig!.EnablePreliminaryInClassic = enabled
        );
        var bot = PreselectSwitch(
            "chkBot",
            "人机",
            _appConfig?.EnablePreliminaryInBot ?? false,
            enabled => _appConfig!.EnablePreliminaryInBot = enabled
        );
        labChampionSkinBrowser.Text = "浏览英雄与默认皮肤 ›";
        chkAutoSkinSelect.Text = "";
        chkAutoSkinSelect.AccessibleName = "选人时自动切换默认皮肤";
        SettingsCard(
            general,
            "英雄预选",
            new SettingRow("预选英雄", "分路信息来自英雄联盟攻略中心", lkbPreliminary),
            new SettingRow("峡谷模式", "经典峡谷含经典人机", chkNormal, chkRanked, classic, bot),
            new SettingRow("娱乐模式", null, chkAram, chkNexus, chkUrf, chkPickUrf),
            new SettingRow("默认皮肤", "为每个已拥有的英雄设置默认皮肤", labChampionSkinBrowser),
            new SettingRow("选人时自动切换默认皮肤", "仅限已拥有的皮肤", chkAutoSkinSelect)
        );
        groupBox1.Dispose();
    }

    // Modes upstream added after the designer's six; each saves its own setting as it changes.
    private CheckBox PreselectSwitch(string name, string text, bool enabled, Action<bool> save)
    {
        var check = new CheckBox
        {
            Name = name,
            Text = text,
            Checked = enabled,
        };
        check.CheckedChanged += (_, _) =>
        {
            if (_appConfig != null)
            {
                save(check.Checked);
                SaveAppConfig();
            }
        };
        return check;
    }

    private void ConfigureClientMaintenanceSettings(FlowLayoutPanel general)
    {
        SettingsCard(
            general,
            "客户端维护",
            new SettingRow("重启客户端界面", "大厅、选人或读秒界面卡顿时使用，无需重新登录", btnRestartUx),
            new SettingRow("退出残留客户端", "退出游戏后清理仍占用资源的客户端进程", btnCloseClients)
        );
        groupBox2.Dispose();
    }

    private void ConfigureAboutSettings(FlowLayoutPanel general)
    {
        SettingsCard(
            general,
            "关于与交流",
            CreateVersionRow(),
            CreateDiagnosticsRow(),
            new SettingRow(label4.Text, null, lk_QQGroup),
            new SettingRow(label5.Text, null, lk_GitPro)
        );
        groupBox3.Dispose();
    }

    // 反馈问题时附上完整 commit，开发者可以对到确切的源码。
    private static SettingRow CreateVersionRow()
    {
        var build = Infrastructure.BuildInfo.Current;
        var copy = new Button { Name = "CopyVersion", Text = "复制版本信息" };
        copy.Click += (_, _) =>
        {
            string builtAt = build.BuiltAtUtc is { } time ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "未知";
            try
            {
                Clipboard.SetText(
                    $"联盟视界 {build.Describe()}{Environment.NewLine}提交：{build.Commit}{Environment.NewLine}构建时间：{builtAt}"
                );
                copy.Text = "已复制";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // 剪贴板被其他程序占用时只提示，用户可以再点一次。
                copy.Text = "复制失败，请重试";
            }
        };
        return new SettingRow("版本", build.Describe(), copy);
    }

    private void ConfigureMessageSettings()
    {
        var messages = SettingsPage(tabPage5, "消息发送");
        label2.Text = "⚠ 游戏内快捷键需要以管理员身份运行本程序，否则游戏不会响应。";
        label3.Text =
            "自定义内容每行一条，使用相同快捷键发送。游戏内发送前请关闭聊天输入框和游戏菜单，按快捷键后松开 Ctrl。";
        var hotkeyStatus = new Label
        {
            Name = "HotkeyStatus",
            Text = "点击快捷键后按下新的组合即可修改；Backspace 恢复默认。需要 F1–F24，或带 Ctrl / Alt 的组合。",
        };
        SettingsCard(
            messages,
            "发送内容",
            label2,
            rbModeMatch,
            rbModeCustom,
            CreateShortcutTable(hotkeyStatus),
            hotkeyStatus,
            label3,
            txtCustomContent
        );
        // SettingsCard applies the muted note color; the admin requirement must stand out.
        label2.ForeColor = Color.FromArgb(178, 92, 20);
        label2.Font = new Font(tabControl1.Font, FontStyle.Bold);
        chkHideSelf.Text = "";
        chkHideSelf.AccessibleName = "发送战绩时隐藏自己";
        chkUseChampionName.Text = "";
        chkUseChampionName.AccessibleName = "发送时使用英雄名称";
        SettingsCard(
            messages,
            "战绩显示",
            new SettingRow("发送战绩时隐藏自己", null, chkHideSelf),
            new SettingRow("发送时使用英雄名称", "可避免玩家名称被屏蔽", chkUseChampionName)
        );
        ConfigureVoiceSettings(messages);
        ConfigureMessageDiagnostics(messages);
    }

    private static readonly (GameHotkeyAction Action, string Scene, string Content)[] ShortcutRows =
    [
        (GameHotkeyAction.ChampSelect, "选人界面", "我方战绩"),
        (GameHotkeyAction.MyTeam, "游戏内", "我方战绩"),
        (GameHotkeyAction.FullTeam, "游戏内", "双方战绩"),
    ];

    private TableLayoutPanel CreateShortcutTable(Label status)
    {
        var table = new TableLayoutPanel
        {
            Name = "ShortcutTable",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = ShortcutRows.Length + 1,
            BackColor = Theme.Surface,
        };
        var keyFont = new Font(tabControl1.Font, FontStyle.Bold);
        table.Disposed += (_, _) => keyFont.Dispose();
        string[] header = { "快捷键", "场景", "发送内容" };
        for (int column = 0; column < header.Length; column++)
        {
            table.Controls.Add(ShortcutCell(header[column], header: true), column, 0);
        }

        var current = GameHotkeys.From(_appConfig);
        for (int row = 0; row < ShortcutRows.Length; row++)
        {
            var (action, scene, content) = ShortcutRows[row];
            var box = new HotkeyBox
            {
                Name = "Hotkey" + action,
                Text = GameHotkeys.Format(current[action]),
                Font = keyFont,
                BackColor = Theme.ControlFill,
                Margin = new Padding(0, 4, 28, 4),
            };
            box.Chosen += keys => ChooseHotkey(action, keys, box, status);
            box.ResetRequested += () => ChooseHotkey(action, GameHotkeys.Defaults[action], box, status);
            table.Controls.Add(box, 0, row + 1);
            table.Controls.Add(ShortcutCell(scene, header: false), 1, row + 1);
            table.Controls.Add(ShortcutCell(content, header: false), 2, row + 1);
        }

        // Thin rules under each row instead of a full grid.
        table.Paint += (_, e) =>
        {
            using var rule = new Pen(Theme.Divider);
            int y = 0;
            foreach (int height in table.GetRowHeights())
            {
                y += height;
                e.Graphics.DrawLine(rule, 0, y - 1, table.Width, y - 1);
            }
        };
        return table;
    }

    private static Label ShortcutCell(string text, bool header) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 6, 28, 6),
            ForeColor = header ? Theme.Muted : Theme.Ink,
        };

    // A rejected combination leaves the shortcut as it was and says why under the table.
    private void ChooseHotkey(GameHotkeyAction action, Keys keys, HotkeyBox box, Label status)
    {
        if (_appConfig == null)
        {
            return;
        }

        string name = GameHotkeys.Format(keys);
        if (!GameHotkeys.IsAllowed(keys))
        {
            status.Text =
                $"{name} 不能用作快捷键：需要 F1–F24，或带 Ctrl / Alt 的组合，单独的字母会让所有程序都打不出这个字。";
            return;
        }

        var current = GameHotkeys.From(_appConfig);
        var taken = ShortcutRows.FirstOrDefault(row => row.Action != action && current[row.Action] == keys);
        if (taken != default)
        {
            status.Text = $"{name} 已用于「{taken.Scene} · {taken.Content}」，请换一个。";
            return;
        }

        GameHotkeys.Store(_appConfig, action, keys);
        SaveAppConfig();
        _hotkeyManager?.ApplyConfig(_appConfig);
        box.Text = name;
        status.Text = $"已改为 {name}，立即生效。";
    }

    private void ConfigureMessageDiagnostics(FlowLayoutPanel messages)
    {
        var logButton = new Button { Text = "打开日志" };
        logButton.Click += (_, _) =>
        {
            try
            {
                var path = Services.ChatSendDiagnostics.Shared.LogPath;
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = System.IO.File.Exists(path) ? path : System.IO.Path.GetDirectoryName(path)!,
                        UseShellExecute = true,
                    }
                );
            }
            catch
            {
                Services.ChatSendDiagnostics.Shared.Report("log-error", "无法打开日志文件，请检查本地文件权限。");
            }
        };
        var row = new SettingRow("最近一次发送", null, logButton);
        row.AddNote(new ChatSendStatusLabel());
        SettingsCard(messages, "快捷发送状态", row);
        _settingsTips.SetToolTip(
            logButton,
            "记录触发、数据读取和输入结果，不记录聊天正文。\n" + Services.ChatSendDiagnostics.Shared.LogPath
        );
    }

    private void MoveAdditionalSettings()
    {
        foreach (TabPage page in tabControl1.TabPages)
        {
            if (page == tabPage4 || page == tabPage5)
            {
                continue;
            }

            if (page.Text == "客户端目录")
            {
                var group = page.Controls.OfType<GroupBox>().First();
                var launcher = group.Controls[0];
                group.Controls.Remove(launcher);
                var layout = SettingsPage(page, "客户端目录");
                launcher.Dock = DockStyle.None;
                SettingsCard(layout, "英雄联盟安装目录", launcher);
                group.Dispose();
            }
            else if (page.Text == "数据缓存")
            {
                var original = page.Controls.OfType<FlowLayoutPanel>().First();
                var children = original.Controls.Cast<Control>().ToArray();
                foreach (var child in children)
                {
                    original.Controls.Remove(child);
                }

                var layout = SettingsPage(page, "数据缓存");
                SettingsCard(layout, "arammeta · 海克斯大乱斗", children.Take(3).ToArray());
                SettingsCard(layout, "aramonly · 普通大乱斗平衡", children.Skip(3).Take(3).ToArray());
                SettingsCard(layout, "英雄联盟客户端 · 玩家英雄成就", children.Skip(6).ToArray());
                original.Dispose();
            }
        }
    }

    private FlowLayoutPanel SettingsPage(TabPage page, string title)
    {
        page.Controls.Clear();
        page.Padding = Padding.Empty;
        page.UseVisualStyleBackColor = false;
        page.BackColor = Theme.Canvas;
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(24, 20, 24, 20),
        };
        page.Controls.Add(flow);
        flow.Controls.Add(
            new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font(tabControl1.Font.FontFamily, 16, FontStyle.Bold),
                ForeColor = Theme.Heading,
                Margin = new Padding(0, 0, 0, 18),
            }
        );
        flow.SizeChanged += (_, _) => ResizeSettings(flow);
        return flow;
    }

    private void SettingsCard(FlowLayoutPanel parent, string title, params Control[] controls)
    {
        bool rows = controls.Length > 0 && controls.All(control => control is SettingRow);
        var card = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Padding = new Padding(20, 16, 20, rows ? 8 : 16),
            BackColor = Color.White,
            Margin = new Padding(0, 0, 0, 14),
        };
        card.Controls.Add(
            new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font(tabControl1.Font.FontFamily, 10.5f, FontStyle.Bold),
                ForeColor = Theme.Heading,
                Margin = new Padding(0, 0, 0, rows ? 6 : 14),
            }
        );
        foreach (var control in controls)
        {
            if (control is not SettingRow)
            {
                SettingRow.Style(control);
                control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                control.Margin = new Padding(0, 0, 0, 10);
                if (control is TextBox box && box.Multiline)
                {
                    box.Height = 132;
                    box.BorderStyle = BorderStyle.Fixed3D; // Themed border: light grey, accent on focus.
                }
            }

            card.Controls.Add(control);
        }
        parent.Controls.Add(card);
        ResizeSettings(parent);
    }

    private void ResizeSettings(FlowLayoutPanel flow)
    {
        int width = Math.Max(300, Math.Min(SettingsMaxWidth, flow.ClientSize.Width - 64));
        foreach (var label in flow.Controls.OfType<Label>())
        {
            label.MaximumSize = new Size(width, 0);
        }

        foreach (var card in flow.Controls.OfType<FlowLayoutPanel>())
        {
            card.MinimumSize = new Size(width, 0);
            card.MaximumSize = new Size(width, 0);
            card.Width = width;
            foreach (Control control in card.Controls)
            {
                if (control is SettingRow row)
                {
                    row.FitWidth(width - 40);
                }
                else if (control is Label or CheckBox or RadioButton)
                {
                    control.MaximumSize = new Size(width - 40, 0);
                }
                else if (control is TextBox || control is LolLauncherPanel)
                {
                    control.Width = width - 40;
                }
            }
        }
    }
}
