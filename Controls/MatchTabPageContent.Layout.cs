namespace League.Controls
{
    public partial class MatchTabPageContent
    {
        private void ConfigureHistoryLayout()
        {
            SuspendLayout();
            BackColor = Color.FromArgb(244, 247, 251);
            ForeColor = Color.FromArgb(36, 48, 65);
            Font = new Font("Microsoft YaHei UI", 9f);
            Padding = new Padding(12);

            var header = new TableLayoutPanel
            {
                Name = "ProfileHeader", Dock = DockStyle.Top, Height = 218,
                ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 0, 0, 12)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var identity = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.FromArgb(232, 241, 253) };
            identity.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 3, BackColor = Color.FromArgb(46,103,190) });
            identity.Controls.Add(new Label { Text = "玩家主页 / 点击姓名复制 Riot ID", AutoSize = true, Location = new Point(18,14), ForeColor = Color.FromArgb(89,115,151) });
            picChampionId.SetBounds(18, 36, 100, 100);
            picChampionId.SizeMode = PictureBoxSizeMode.Zoom;
            linkGameName.AutoSize = false;
            linkGameName.SetBounds(136, 52, 230, 36);
            linkGameName.Font = new Font(Font.FontFamily, 15f, FontStyle.Bold);
            linkGameName.LinkColor = Color.FromArgb(46, 103, 190);
            linkGameName.LinkBehavior = LinkBehavior.HoverUnderline;
            linkGameName.AutoEllipsis = true;
            lblLevel.Visible = false;
            lblPrivacy.Location = new Point(136, 94);
            lblLevel.ForeColor = lblPrivacy.ForeColor = Color.FromArgb(111, 123, 139);
            identity.Controls.AddRange(new Control[] { picChampionId, linkGameName, lblLevel, lblPrivacy });
            identity.Resize += (_, _) => linkGameName.Width = Math.Max(60, identity.ClientSize.Width - 148);
            header.Controls.Add(identity, 0, 0);
            header.SetColumnSpan(identity, 2);

            var solo = ConfigureRankGroup(groupBox1,
                new[] { label2, label3, label4, label5, label6, label7 },
                new[] { lblSoloTier, lblSoloGames, lblSoloWins, lblSoloLosses, lblSoloWinRate, lblSoloLeaguePoints });
            var flex = ConfigureRankGroup(groupBox2,
                new[] { label19, label18, label17, label16, label15, label14 },
                new[] { lblFlexTier, lblFlexGames, lblFlexWins, lblFlexLosses, lblFlexWinRate, lblFlexLeaguePoints });
            var ranks = new TableLayoutPanel { Height = 110, Width = 260,
                ColumnCount = 2, RowCount = 1, Padding = Padding.Empty,
                BackColor = identity.BackColor };
            ranks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            ranks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            ranks.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ranks.Controls.Add(solo, 0, 0);
            ranks.Controls.Add(flex, 1, 0);
            identity.Controls.Add(ranks);
            var peak = new Panel { Width = 150, Height = 52 };
            _highestRank = new Label { Text = "—", Dock = DockStyle.Top, Height = 26,
                Font = new Font(Font.FontFamily, 12f, FontStyle.Bold), ForeColor = Color.FromArgb(36,63,98) };
            peak.Controls.Add(new Label { Text = "赛季最高段位", Dock = DockStyle.Top, Height = 20,
                ForeColor = Color.FromArgb(89,115,151) });
            peak.Controls.Add(_highestRank);
            identity.Controls.Add(peak);
            void PositionRanks()
            {
                ranks.Width = Math.Min(260, Math.Max(160, identity.ClientSize.Width - 36));
                ranks.Location = new Point(Math.Max(18, identity.ClientSize.Width - ranks.Width - 18), identity.ClientSize.Height - ranks.Height - 12);
                peak.Location = new Point(18, identity.ClientSize.Height - peak.Height - 12);
                peak.Width = Math.Max(0, Math.Min(150, ranks.Left - 30));
            }
            identity.Resize += (_, _) => PositionRanks();
            PositionRanks();
            ranks.BringToFront();

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 40 };
            var heading = new Label { Text = "\u5386\u53f2\u6218\u7ee9", Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 12f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            var filter = new Button { Text = "\u7b5b\u9009", Dock = DockStyle.Right, Width = 60, FlatStyle = FlatStyle.Flat };
            filter.FlatAppearance.BorderSize = 0;
            toolbar.Controls.Add(heading); toolbar.Controls.Add(filter);
            var options = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Visible = false, WrapContents = false };
            comboFilter.Width = 155; comboFilter.Margin = new Padding(0,4,4,0);
            comboPage.Width = 54; comboPage.DropDownStyle = ComboBoxStyle.DropDownList;
            options.Controls.Add(comboFilter); options.Controls.Add(comboPage);
            toolbar.Controls.Add(options);
            filter.Click += (_, _) => { options.Visible = !options.Visible; toolbar.Height = options.Visible ? 74 : 40; };
            var paging = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 40, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(0,8,0,0) };
            paging.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            paging.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            int column = 0;
            foreach(var button in new[]{btnPrev,btnNext})
            {
                button.Dock = DockStyle.Fill; button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(215,223,233);
                button.BackColor = Color.White; button.ForeColor = ForeColor;
                button.Margin = new Padding(0,0,column == 0 ? 6 : 0,0);
                paging.Controls.Add(button,column++,0);
            }

            Controls.Clear();
            label8.Dispose(); // The player name already identifies the profile.
            flowLayoutPanelRight.Dock = DockStyle.Fill;
            flowLayoutPanelRight.BackColor = Color.White;
            flowLayoutPanelRight.FlowDirection = FlowDirection.TopDown;
            flowLayoutPanelRight.WrapContents = false;
            flowLayoutPanelRight.Padding = new Padding(8);
            flowLayoutPanelRight.SizeChanged += (_, _) => ResizeMatchCards();
            flowLayoutPanelRight.ControlAdded += (_, e) => ConfigureCompactMatch(e.Control);
            flowLayoutPanelRight.ControlRemoved += (_, e) => { if (e.Control == _selectedCard) ReturnToProfile(); };
            ConfigureProfileNavigation(header, toolbar);
            var listHost = new Panel { Dock = DockStyle.Fill };
            paging.Dock = DockStyle.Top;
            flowLayoutPanelRight.Dock = DockStyle.Top;
            listHost.Controls.Add(paging);
            listHost.Controls.Add(flowLayoutPanelRight);
            _historySurface.Controls.Add(listHost);
            listHost.BringToFront();
            void FitList()
            {
                flowLayoutPanelRight.Height = Math.Max(80, Math.Min(
                    flowLayoutPanelRight.Controls.Count * 66 + 20, listHost.ClientSize.Height - paging.Height));
                ResizeMatchCards();
            }
            listHost.SizeChanged += (_, _) => FitList();
            flowLayoutPanelRight.ControlAdded += (_, _) => FitList();
            flowLayoutPanelRight.ControlRemoved += (_, _) => FitList();
            FitList();
            ResumeLayout(true);
        }

        private Panel ConfigureRankGroup(GroupBox group, Label[] captions, Label[] values)
        {
            // Keep the existing data labels for rank updates, but remove the group-box presentation.
            var panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var title = new Label { Text = group.Text, Dock = DockStyle.Top, Height = 20,
                ForeColor = Color.FromArgb(89,115,151), TextAlign = ContentAlignment.MiddleCenter };
            var tier = values[0];
            tier.AutoSize = false; tier.Dock = DockStyle.Top; tier.Height = 26;
            tier.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
            tier.ForeColor = Color.FromArgb(36,63,98); tier.AutoEllipsis = true; tier.TextAlign = ContentAlignment.MiddleCenter;
            group.Visible = false;
            group.Dock = DockStyle.Fill;
            panel.Controls.Add(group);
            var history = new Label { Name = group == groupBox1 ? "SoloRankHistory" : "FlexRankHistory",
                Dock = DockStyle.Top, Height = 28, AutoEllipsis = true, Visible = false,
                Font = new Font(Font.FontFamily, 8f), ForeColor = Color.FromArgb(111,123,139) };
            components ??= new System.ComponentModel.Container();
            var tip = new ToolTip(components);
            history.TextChanged += (_, _) =>
            {
                tip.SetToolTip(tier, history.Text);
                tip.SetToolTip(title, history.Text);
            };
            group.Controls.Add(history);
            panel.Controls.Add(title);
            panel.Controls.Add(tier);
            panel.Controls.Add(new RankEmblem { Name = group == groupBox1 ? "SoloRankEmblem" : "FlexRankEmblem",
                Dock = DockStyle.Top, Height = 62 });
            return panel;
        }

        private void ResizeMatchCards()
        {
            int width = Math.Max(180, flowLayoutPanelRight.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - flowLayoutPanelRight.Padding.Horizontal - 1);
            foreach (var card in flowLayoutPanelRight.Controls.OfType<MatchListPanel>())
            {
                card.Width = width;
                card.Height = 62;
                card.Margin = new Padding(0,0,0,4);
            }
        }
    }
}
