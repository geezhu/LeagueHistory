namespace League.Controls;

public partial class MatchTabPageContent
{
    private readonly Label _pageNumber = new()
    {
        Name = "CurrentHistoryPage",
        Text = "1",
        Size = new Size(32, 32),
        TextAlign = ContentAlignment.MiddleCenter,
        BackColor = Theme.Accent,
        ForeColor = Color.White,
    };

    private void ConfigureHistoryLayout()
    {
        SuspendLayout();
        try
        {
            BackColor = Theme.Canvas;
            ForeColor = Theme.Ink;
            ApplyOwnedFont(this, new Font("Microsoft YaHei UI", 9f));
            Padding = new Padding(12);
            var header = CreateProfileHeader();
            var toolbar = CreateHistoryToolbar();
            var paging = CreateHistoryPaging();
            Controls.Clear();
            label8.Dispose(); // The player name already identifies the profile.
            ConfigureHistoryFlow();
            ConfigureProfileNavigation(header, toolbar);
            ConfigureHistoryListHost(paging);
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private static void ApplyOwnedFont(Control control, Font font)
    {
        control.Font = font;
        control.Disposed += (_, _) => font.Dispose();
    }

    private TableLayoutPanel CreateProfileHeader()
    {
        var header = new TableLayoutPanel
        {
            Name = "ProfileHeader",
            Dock = DockStyle.Top,
            Height = 218,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 0, 0, 12),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var identity = _profileBackground = new ProfileBackgroundPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Theme.AccentSoft,
        };
        identity.Controls.Add(
            new Panel
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = Theme.Accent,
            }
        );
        ConfigureProfileIdentity(identity);
        ConfigureIdentityCopy(identity);
        ConfigureProfileRanks(identity);
        header.Controls.Add(identity, 0, 0);
        header.SetColumnSpan(identity, 2);
        return header;
    }

    private void ConfigureProfileIdentity(Panel identity)
    {
        picChampionId.SetBounds(18, 36, 100, 100);
        picChampionId.SizeMode = PictureBoxSizeMode.Zoom;
        linkGameName.AutoSize = false;
        linkGameName.SetBounds(136, 52, 230, 36);
        linkGameName.TextAlign = ContentAlignment.MiddleLeft;
        ApplyOwnedFont(linkGameName, new Font(Font.FontFamily, 15f, FontStyle.Bold));
        linkGameName.LinkColor = Theme.Accent;
        linkGameName.LinkBehavior = LinkBehavior.HoverUnderline;
        linkGameName.AutoEllipsis = true;
        lblLevel.Visible = false;
        lblPrivacy.Location = new Point(136, 94);
        lblPrivacy.Visible = false;
        lblLevel.ForeColor = lblPrivacy.ForeColor = Theme.Muted;
        identity.Controls.AddRange(new Control[] { picChampionId, linkGameName, lblLevel, lblPrivacy });
    }

    private void ConfigureIdentityCopy(Panel identity)
    {
        var copy = new IdentityIcon
        {
            IsCopy = true,
            Size = new Size(28, 36),
            Font = linkGameName.Font,
            ForeColor = Theme.Accent,
            Cursor = Cursors.Hand,
            AccessibleName = "复制 Riot ID",
            AccessibleRole = AccessibleRole.PushButton,
            TabStop = true,
        };
        components ??= new System.ComponentModel.Container();
        var copyTip = new ToolTip(components);
        copyTip.SetToolTip(copy, "复制 Riot ID");
        copy.Click += (_, _) =>
        {
            string value = linkGameName.Tag as string ?? linkGameName.Text;
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            Clipboard.SetText(value);
            copyTip.Show("已复制 Riot ID", copy, 0, copy.Height, 1500);
        };
        identity.Controls.Add(copy);
        void PositionIdentity()
        {
            int available = Math.Max(30, identity.ClientSize.Width - linkGameName.Left - copy.Width - 16);
            int desired =
                TextRenderer.MeasureText(linkGameName.Text, linkGameName.Font).Width + linkGameName.Padding.Right + 4;
            linkGameName.Width = Math.Min(available, desired);
            copy.Location = new Point(linkGameName.Right + 3, linkGameName.Top);
        }
        identity.Resize += (_, _) => PositionIdentity();
        linkGameName.TextChanged += (_, _) => PositionIdentity();
        linkGameName.PaddingChanged += (_, _) => PositionIdentity();
        PositionIdentity();
    }

    private void ConfigureProfileRanks(Panel identity)
    {
        var solo = ConfigureRankGroup(groupBox1, lblSoloTier);
        var flex = ConfigureRankGroup(groupBox2, lblFlexTier);
        var ranks = new TableLayoutPanel
        {
            Height = 110,
            Width = 260,
            ColumnCount = 2,
            RowCount = 1,
            Padding = Padding.Empty,
            BackColor = identity.BackColor,
        };
        ranks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        ranks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        ranks.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        ranks.Controls.Add(solo, 0, 0);
        ranks.Controls.Add(flex, 1, 0);
        identity.Controls.Add(ranks);
        ConfigureHighestRank(identity, ranks);
    }

    private void ConfigureHighestRank(Panel identity, Control ranks)
    {
        var peak = new Panel { Width = 150, Height = 52 };
        _highestRank = new Label
        {
            Text = "—",
            Dock = DockStyle.Top,
            Height = 26,
            ForeColor = Theme.Heading,
        };
        ApplyOwnedFont(_highestRank, new Font(Font.FontFamily, 12f, FontStyle.Bold));
        peak.Controls.Add(
            new Label
            {
                Text = "赛季最高段位",
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.FromArgb(89, 115, 151),
            }
        );
        peak.Controls.Add(_highestRank);
        identity.Controls.Add(peak);
        void PositionRanks()
        {
            ranks.Width = Math.Min(260, Math.Max(160, identity.ClientSize.Width - 36));
            ranks.Location = new Point(
                Math.Max(18, identity.ClientSize.Width - ranks.Width - 18),
                identity.ClientSize.Height - ranks.Height - 12
            );
            peak.Location = new Point(18, identity.ClientSize.Height - peak.Height - 12);
            peak.Width = Math.Max(0, Math.Min(150, ranks.Left - 30));
        }
        identity.Resize += (_, _) => PositionRanks();
        PositionRanks();
        ranks.BringToFront();
    }

    private Panel CreateHistoryToolbar()
    {
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 40 };
        var heading = new Label
        {
            Text = "历史战绩",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var filter = new Button
        {
            Text = "筛选",
            Dock = DockStyle.Right,
            Width = 60,
            FlatStyle = FlatStyle.Flat,
        };
        filter.FlatAppearance.BorderSize = 0;
        ApplyOwnedFont(heading, new Font(Font.FontFamily, 12f, FontStyle.Bold));
        toolbar.Controls.Add(heading);
        toolbar.Controls.Add(filter);
        ConfigureFilterPopup(filter, CreateFilterOptions());
        return toolbar;
    }

    private TableLayoutPanel CreateFilterOptions()
    {
        var options = new TableLayoutPanel
        {
            Size = new Size(300, 152),
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(8),
            BackColor = Color.White,
        };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 3; row++)
        {
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        }

        options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        comboFilter.Dock = DockStyle.Fill;
        comboFilter.Margin = new Padding(0, 2, 0, 2);
        comboPage.Width = 54;
        comboPage.DropDownStyle = ComboBoxStyle.DropDownList;
        _outcomeFilter.Items.AddRange(new object[] { "全部", "只看胜利", "只看失败" });
        _outcomeFilter.SelectedIndex = 0;
        _outcomeFilter.Dock = DockStyle.Fill;
        _outcomeFilter.SelectedIndexChanged += OnOutcomeFilterChanged;
        string[] filterLabels = { "比赛模式", "比赛结果", "每页场数" };
        Control[] filterControls = { comboFilter, _outcomeFilter, comboPage };
        for (int row = 0; row < 3; row++)
        {
            options.Controls.Add(
                new Label
                {
                    Text = filterLabels[row],
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                },
                0,
                row
            );
            options.Controls.Add(filterControls[row], 1, row);
        }
        _filterScope.Text = "按需查找，满页即停；下一页继续加载";
        _filterScope.ForeColor = Theme.Muted;
        options.Controls.Add(_filterScope, 0, 3);
        options.SetColumnSpan(_filterScope, 2);
        return options;
    }

    private void ConfigureFilterPopup(Control filter, Control options)
    {
        var filterHost = new ToolStripControlHost(options)
        {
            AutoSize = false,
            Size = options.Size,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        var filterPopup = new ToolStripDropDown
        {
            AutoSize = true,
            Padding = new Padding(1),
            BackColor = Color.FromArgb(213, 223, 237),
            AutoClose = true,
        };
        filterPopup.Items.Add(filterHost);
        components ??= new System.ComponentModel.Container();
        components.Add(filterPopup);
        filter.Click += (_, _) =>
        {
            if (filterPopup.Visible)
            {
                filterPopup.Close();
                return;
            }
            filterPopup.Show(filter, new Point(filter.Width - filterPopup.PreferredSize.Width, filter.Height));
        };
    }

    private TableLayoutPanel CreateHistoryPaging()
    {
        var paging = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(0, 8, 0, 0),
        };
        paging.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        paging.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        paging.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        ConfigurePagingButtons(paging);
        ApplyOwnedFont(_pageNumber, new Font("Microsoft YaHei UI", 10f, FontStyle.Bold));
        _pageNumber.Anchor = AnchorStyles.Top;
        _pageNumber.Margin = Padding.Empty;
        paging.Controls.Add(_pageNumber, 1, 0);

        return paging;
    }

    private void ConfigurePagingButtons(TableLayoutPanel paging)
    {
        int column = 0;
        foreach (var button in new[] { btnPrev, btnNext })
        {
            button.Dock = DockStyle.None;
            button.Size = new Size(32, 32);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Theme.AccentSoftHover;
            button.BackColor = Color.FromArgb(232, 239, 248);
            button.ForeColor = ForeColor;
            button.Text = "";
            bool previous = column == 0;
            button.Paint += (_, e) => PaintPagingArrow(button, e.Graphics, previous);
            button.AccessibleName = column == 0 ? "上一页" : "下一页";
            button.Margin = Padding.Empty;
            paging.Controls.Add(button, column == 0 ? 0 : 2, 0);
            column++;
        }
    }

    private static void PaintPagingArrow(Button button, Graphics graphics, bool previous)
    {
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float x = button.ClientSize.Width / 2f,
            y = button.ClientSize.Height / 2f;
        float size = Math.Min(button.ClientSize.Width, button.ClientSize.Height) * .17f;
        float direction = previous ? -1 : 1;
        using var pen = new Pen(
            button.Enabled ? button.ForeColor : SystemColors.GrayText,
            Math.Max(1.5f, button.DeviceDpi / 64f)
        )
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };
        graphics.DrawLines(
            pen,
            new[]
            {
                new PointF(x - direction * size / 2, y - size),
                new PointF(x + direction * size / 2, y),
                new PointF(x - direction * size / 2, y + size),
            }
        );
    }

    private void ConfigureHistoryFlow()
    {
        flowLayoutPanelRight.Dock = DockStyle.Fill;
        flowLayoutPanelRight.BackColor = Color.White;
        flowLayoutPanelRight.FlowDirection = FlowDirection.TopDown;
        flowLayoutPanelRight.WrapContents = false;
        flowLayoutPanelRight.Padding = new Padding(8);
        flowLayoutPanelRight.SizeChanged += (_, _) => ResizeMatchCards();
        flowLayoutPanelRight.ControlAdded += (_, e) => ConfigureCompactMatch(e.Control);
        flowLayoutPanelRight.ControlRemoved += (_, e) =>
        {
            if (e.Control == _selectedCard)
            {
                ReturnToProfile();
            }
        };
    }

    private void ConfigureHistoryListHost(Control paging)
    {
        var listHost = new Panel { Dock = DockStyle.Fill };
        paging.Dock = DockStyle.Top;
        flowLayoutPanelRight.Dock = DockStyle.Top;
        listHost.Controls.Add(paging);
        listHost.Controls.Add(flowLayoutPanelRight);
        _historySurface.Controls.Add(listHost);
        listHost.BringToFront();
        void FitList()
        {
            if (_navigationClosing || flowLayoutPanelRight.IsDisposed)
            {
                return;
            }
            flowLayoutPanelRight.Height = Math.Max(
                80,
                Math.Min(flowLayoutPanelRight.Controls.Count * 66 + 20, listHost.ClientSize.Height - paging.Height)
            );
            ResizeMatchCards();
        }
        listHost.SizeChanged += (_, _) => FitList();
        flowLayoutPanelRight.ControlAdded += (_, _) => FitList();
        flowLayoutPanelRight.ControlRemoved += (_, _) => FitList();
        FitList();
    }

    private Panel ConfigureRankGroup(GroupBox group, Label tier)
    {
        // Keep the existing data labels for rank updates, but remove the group-box presentation.
        var panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var title = new Label
        {
            Text = group.Text,
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = Color.FromArgb(89, 115, 151),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        tier.AutoSize = false;
        tier.Dock = DockStyle.Top;
        tier.Height = 26;
        ApplyOwnedFont(tier, new Font(Font.FontFamily, 12f, FontStyle.Bold));
        tier.ForeColor = Theme.Heading;
        tier.AutoEllipsis = true;
        tier.TextAlign = ContentAlignment.MiddleCenter;
        group.Visible = false;
        group.Dock = DockStyle.Fill;
        panel.Controls.Add(group);
        var history = new Label
        {
            Name = group == groupBox1 ? "SoloRankHistory" : "FlexRankHistory",
            Dock = DockStyle.Top,
            Height = 28,
            AutoEllipsis = true,
            Visible = false,
            ForeColor = Theme.Muted,
        };
        components ??= new System.ComponentModel.Container();
        var tip = new ToolTip(components);
        ApplyOwnedFont(history, new Font(Font.FontFamily, 8f));
        history.TextChanged += (_, _) =>
        {
            tip.SetToolTip(tier, history.Text);
            tip.SetToolTip(title, history.Text);
        };
        group.Controls.Add(history);
        panel.Controls.Add(title);
        panel.Controls.Add(tier);
        panel.Controls.Add(
            new RankEmblem
            {
                Name = group == groupBox1 ? "SoloRankEmblem" : "FlexRankEmblem",
                Dock = DockStyle.Top,
                Height = 62,
            }
        );
        return panel;
    }

    private void ResizeMatchCards()
    {
        if (_navigationClosing || flowLayoutPanelRight.IsDisposed)
        {
            return;
        }
        int width = Math.Max(
            180,
            flowLayoutPanelRight.ClientSize.Width
                - SystemInformation.VerticalScrollBarWidth
                - flowLayoutPanelRight.Padding.Horizontal
                - 1
        );
        foreach (var card in flowLayoutPanelRight.Controls.OfType<MatchListPanel>())
        {
            card.Width = width;
            card.Height = 62;
            card.Margin = new Padding(0, 0, 0, 4);
        }
    }
}
