using League.Controls;

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
    }

    private void ConfigureSettingsTabs()
    {
        tabControl1.Font = new Font("Microsoft YaHei UI", 9);
        tabControl1.SizeMode = TabSizeMode.Fixed;
        tabControl1.ItemSize = new Size(116, 36);
        tabControl1.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabControl1.DrawItem += (_, e) =>
        {
            bool selected = e.Index == tabControl1.SelectedIndex;
            using var background = new SolidBrush(selected ? Color.White : Color.FromArgb(239, 244, 248));
            e.Graphics.FillRectangle(background, e.Bounds);
            TextRenderer.DrawText(
                e.Graphics,
                tabControl1.TabPages[e.Index].Text,
                tabControl1.Font,
                e.Bounds,
                selected ? AccentColors.Accent : Color.FromArgb(95, 113, 130),
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter
            );
            if (selected)
            {
                using var line = new SolidBrush(AccentColors.Accent);
                e.Graphics.FillRectangle(line, e.Bounds.X + 18, e.Bounds.Bottom - 3, e.Bounds.Width - 36, 3);
            }
        };
    }

    private void ConfigureGeneralSettings()
    {
        var general = SettingsPage(tabPage4, "功能辅助");
        ConfigureHistorySettings(general);
        ConfigureAutomationSettings(general);
        ConfigureShutdownSettings(general);
        ConfigureAssistantSetting(general);
        ConfigureClientMaintenanceSettings(general);
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
        var overlayAuto = new CheckBox
        {
            Name = "AutoShowLoadingOverlay",
            AccessibleName = "进入加载界面后自动展示",
            Checked = _appConfig?.AutoShowLoadingOverlay ?? true,
            Enabled = overlay.Checked,
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
        overlayBackend.Items.AddRange(new object[] { "桌面窗口（窗口 / 无边框）", "Game Bar（全屏验证版）" });
        overlayBackend.SelectedIndex = _appConfig?.LoadingOverlayBackend == "GameBar" ? 1 : 0;
        var gameBarStatus = new Label { Text = _loadingOverlay?.GameBarStatus ?? "" };
        TrackGameBarSettingsStatus(gameBarStatus);
        var enableRow = new SettingRow("启用对局速览", "在加载界面和游戏中查看十位玩家的段位、战绩和英雄推荐", overlay);
        var backendRow = new SettingRow("显示方式", null, overlayBackend);
        var autoRow = new SettingRow("进入加载界面后自动展示", null, overlayAuto);
        var previewRow = new SettingRow("测试显示", "不需要进入对局，检查桌面窗口是否正常", overlayPreview);
        BindOverlayBackendHelp(overlayBackend, backendRow, previewRow, gameBarStatus);
        void SaveOverlay()
        {
            if (_appConfig == null)
            {
                return;
            }

            _appConfig.EnableLoadingOverlay = overlay.Checked;
            _appConfig.AutoShowLoadingOverlay = overlayAuto.Checked;
            _appConfig.LoadingOverlayBackend = overlayBackend.SelectedIndex == 1 ? "GameBar" : "Desktop";
            SaveAppConfig();
            _loadingOverlay?.RefreshDisplaySettings();
        }

        // Options under a disabled overlay are hidden rather than greyed out.
        void ShowEnabledOptions()
        {
            overlayBackend.Enabled = overlayAuto.Enabled = overlayPreview.Enabled = overlay.Checked;
            backendRow.Visible = autoRow.Visible = overlay.Checked;
            previewRow.Visible = overlay.Checked && overlayBackend.SelectedIndex == 0;
        }
        overlay.CheckedChanged += (_, _) => SaveOverlay();
        overlay.CheckedChanged += (_, _) => ShowEnabledOptions();
        overlayAuto.CheckedChanged += (_, _) => SaveOverlay();
        overlayBackend.SelectedIndexChanged += (_, _) => SaveOverlay();
        overlayBackend.SelectedIndexChanged += (_, _) => ShowEnabledOptions();
        ShowEnabledOptions();
        SettingsCard(general, "显示设置", enableRow, backendRow, autoRow, previewRow);
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
                "按 Win＋G，从小组件列表打开「League 对局速览」；点击组件右上角图钉可保留在游戏上方。保持 League 主程序运行以同步对局数据。",
        };
        var desktopHelp = new Label { Text = "支持窗口 / 无边框模式，检测到独占全屏时暂停弹窗。" };
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

    private void ConfigureClientMaintenanceSettings(FlowLayoutPanel general)
    {
        lkbPreliminary.Text = "配置预选英雄 ›";
        foreach (
            var (check, name) in new[]
            {
                (chkNormal, "匹配"),
                (chkRanked, "排位"),
                (chkAram, "大乱斗"),
                (chkNexus, "海克斯"),
            }
        )
        {
            check.Text = name;
        }

        SettingsCard(
            general,
            "英雄预选",
            new SettingRow("预选英雄", "分路信息来自英雄联盟攻略中心", lkbPreliminary),
            new SettingRow("按模式启用", null, chkNormal, chkRanked, chkAram, chkNexus)
        );
        SettingsCard(
            general,
            "客户端维护",
            new SettingRow("重启客户端界面", "大厅、选人或读秒界面卡顿时使用，无需重新登录", btnRestartUx),
            new SettingRow("退出残留客户端", "退出游戏后清理仍占用资源的客户端进程", btnCloseClients)
        );
        SettingsCard(
            general,
            "关于与交流",
            new SettingRow(label4.Text, null, lk_QQGroup),
            new SettingRow(label5.Text, null, lk_GitPro)
        );
        groupBox1.Dispose();
        groupBox2.Dispose();
        groupBox3.Dispose();
    }

    private void ConfigureMessageSettings()
    {
        var messages = SettingsPage(tabPage5, "消息发送");
        label2.Text = "⚠ 游戏内快捷键需要以管理员身份运行本程序，否则游戏不会响应。";
        label3.Text =
            "自定义内容每行一条，使用相同快捷键发送。游戏内发送前请关闭聊天输入框和游戏菜单，按快捷键后松开 Ctrl。";
        SettingsCard(
            messages,
            "发送内容",
            label2,
            rbModeMatch,
            rbModeCustom,
            CreateShortcutTable(),
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

    private TableLayoutPanel CreateShortcutTable()
    {
        var rows = new (string Key, string Scene, string Content)[]
        {
            ("快捷键", "场景", "发送内容"),
            ("Ctrl + F7", "选人界面", "我方战绩"),
            ("F9", "游戏内", "我方战绩"),
            ("F11", "游戏内", "双方战绩"),
            ("Ctrl + F9", "大乱斗 / 海克斯大乱斗", "我方梯队与胜率"),
            ("Ctrl + F11", "大乱斗 / 海克斯大乱斗", "对方梯队与胜率"),
        };
        var table = new TableLayoutPanel
        {
            Name = "ShortcutTable",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = rows.Length,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            BackColor = Color.FromArgb(247, 250, 252),
        };
        var header = new Font(tabControl1.Font, FontStyle.Bold);
        table.Disposed += (_, _) => header.Dispose();
        for (int row = 0; row < rows.Length; row++)
        {
            string[] cells = { rows[row].Key, rows[row].Scene, rows[row].Content };
            for (int column = 0; column < cells.Length; column++)
            {
                var cell = new Label
                {
                    Text = cells[column],
                    AutoSize = true,
                    Margin = new Padding(10, 6, 16, 6),
                    ForeColor = Color.FromArgb(59, 81, 99),
                };
                if (row == 0 || column == 0)
                {
                    cell.Font = header;
                }

                table.Controls.Add(cell, column, row);
            }
        }

        return table;
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
                SettingsCard(layout, "CommunityDragon · 阵容分析", children.Skip(6).Take(3).ToArray());
                SettingsCard(layout, "英雄联盟客户端 · 玩家英雄成就", children.Skip(9).ToArray());
                original.Dispose();
            }
        }
    }

    private FlowLayoutPanel SettingsPage(TabPage page, string title)
    {
        page.Controls.Clear();
        page.Padding = Padding.Empty;
        page.UseVisualStyleBackColor = false;
        page.BackColor = Color.FromArgb(244, 247, 251);
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
                Font = new Font(tabControl1.Font.FontFamily, 17, FontStyle.Bold),
                ForeColor = Color.FromArgb(35, 58, 76),
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
                Font = new Font(tabControl1.Font, FontStyle.Bold),
                ForeColor = Color.FromArgb(39, 62, 79),
                Margin = new Padding(0, 0, 0, rows ? 4 : 14),
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
                    box.BorderStyle = BorderStyle.FixedSingle;
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
