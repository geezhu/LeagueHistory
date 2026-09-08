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
                Name = "ProfileHeader", Dock = DockStyle.Top, Height = 414,
                ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 0, 0, 12)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var identity = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.FromArgb(232, 241, 253) };
            identity.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 3, BackColor = Color.FromArgb(46,103,190) });
            identity.Controls.Add(new Label { Text = "玩家主页 / 点击姓名复制 Riot ID", AutoSize = true, Location = new Point(18,14), ForeColor = Color.FromArgb(89,115,151) });
            picChampionId.SetBounds(18, 40, 64, 64);
            picChampionId.SizeMode = PictureBoxSizeMode.Zoom;
            linkGameName.AutoSize = false;
            linkGameName.SetBounds(96, 43, 230, 36);
            linkGameName.Font = new Font(Font.FontFamily, 15f, FontStyle.Bold);
            linkGameName.LinkColor = Color.FromArgb(46, 103, 190);
            linkGameName.LinkBehavior = LinkBehavior.HoverUnderline;
            linkGameName.AutoEllipsis = true;
            lblLevel.Location = new Point(96, 85);
            lblPrivacy.Location = new Point(96, 110);
            lblLevel.ForeColor = lblPrivacy.ForeColor = Color.FromArgb(111, 123, 139);
            identity.Controls.AddRange(new Control[] { picChampionId, linkGameName, lblLevel, lblPrivacy });
            identity.Resize += (_, _) => linkGameName.Width = Math.Max(60, identity.ClientSize.Width - 108);
            header.Controls.Add(identity, 0, 0);
            header.SetColumnSpan(identity, 2);

            ConfigureRankGroup(groupBox1,
                new[] { label2, label3, label4, label5, label6, label7 },
                new[] { lblSoloTier, lblSoloGames, lblSoloWins, lblSoloLosses, lblSoloWinRate, lblSoloLeaguePoints });
            ConfigureRankGroup(groupBox2,
                new[] { label19, label18, label17, label16, label15, label14 },
                new[] { lblFlexTier, lblFlexGames, lblFlexWins, lblFlexLosses, lblFlexWinRate, lblFlexLeaguePoints });
            header.Controls.Add(groupBox1, 0, 1);
            header.Controls.Add(groupBox2, 1, 1);

            var toolbar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 122, ColumnCount = 2, RowCount = 4, Padding = new Padding(0,0,0,8) };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            for(int i=0;i<4;i++) toolbar.RowStyles.Add(new RowStyle(SizeType.Percent,25));
            var heading = new Label { Text = "\u5386\u53f2\u6218\u7ee9", Dock = DockStyle.Fill, Font = new Font(Font,FontStyle.Bold) };
            toolbar.Controls.Add(heading,0,0); toolbar.SetColumnSpan(heading,2);
            comboFilter.Dock = DockStyle.Fill; comboFilter.Margin = new Padding(0,0,0,3);
            toolbar.Controls.Add(comboFilter,0,1); toolbar.SetColumnSpan(comboFilter,2);
            var paging = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            comboPage.Width = 54; comboPage.DropDownStyle = ComboBoxStyle.DropDownList; comboPage.Margin = Padding.Empty;
            paging.Controls.Add(new Label { Text = "\u6bcf\u9875", Width = 40, Margin = new Padding(0,4,0,0) });
            paging.Controls.Add(comboPage);
            paging.Controls.Add(new Label { Text = "\u573a", Width = 30, Margin = new Padding(4,4,0,0) });
            toolbar.Controls.Add(paging,0,2); toolbar.SetColumnSpan(paging,2);
            int column = 0;
            foreach(var button in new[]{btnPrev,btnNext})
            {
                button.Dock = DockStyle.Fill; button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(215,223,233);
                button.BackColor = Color.White; button.ForeColor = ForeColor;
                button.Margin = new Padding(0,0,column == 0 ? 6 : 0,0);
                toolbar.Controls.Add(button,column++,3);
            }

            Controls.Clear();
            label8.Dispose(); // The player name already identifies the profile.
            flowLayoutPanelRight.Dock = DockStyle.Fill;
            flowLayoutPanelRight.BackColor = BackColor;
            flowLayoutPanelRight.FlowDirection = FlowDirection.TopDown;
            flowLayoutPanelRight.WrapContents = false;
            flowLayoutPanelRight.Padding = Padding.Empty;
            flowLayoutPanelRight.SizeChanged += (_, _) => ResizeMatchCards();
            flowLayoutPanelRight.ControlAdded += (_, e) => ConfigureCompactMatch(e.Control);
            flowLayoutPanelRight.ControlRemoved += (_, e) => { if (e.Control == _selectedCard) ReturnToProfile(); };
            ConfigureProfileNavigation(header, toolbar);
            ResumeLayout(true);
        }

        private void ConfigureRankGroup(GroupBox group, Label[] captions, Label[] values)
        {
            group.Dock = DockStyle.Fill;
            group.Margin = new Padding(0, 12, 8, 0);
            group.Padding = new Padding(14, 8, 14, 10);
            group.BackColor = Color.White;
            group.ForeColor = Color.FromArgb(46,103,190);
            var title = new Label { Text = group.Text, Dock = DockStyle.Top, Height = 20, ForeColor = group.ForeColor };
            group.Text = "";
            var tier = values[0];
            tier.AutoSize = false; tier.Dock = DockStyle.Top; tier.Height = 32;
            tier.Font = new Font(Font.FontFamily, 16f, FontStyle.Bold);
            tier.ForeColor = Color.FromArgb(36,63,98); tier.AutoEllipsis = true;
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int row = 0; row < 3; row++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));
            string[] names = { "段位", "场次", "胜场", "负场", "胜率", "胜点" };
            for (int i = 1; i < 6; i++)
            {
                var cell = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
                captions[i].Text = names[i];
                captions[i].ForeColor = Color.FromArgb(111, 123, 139);
                values[i].ForeColor = Color.FromArgb(36,48,65);
                values[i].Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
                foreach (var label in new[] { captions[i], values[i] })
                {
                    label.AutoSize = false;
                    label.AutoEllipsis = true;
                    label.Dock = DockStyle.Fill;
                    label.TextAlign = ContentAlignment.MiddleLeft;
                    label.Margin = Padding.Empty;
                }
                captions[i].Dock = DockStyle.Left; captions[i].Width = 34;
                cell.Controls.Add(values[i]); cell.Controls.Add(captions[i]);
                grid.Controls.Add(cell, (i-1)%2, (i-1)/2);
            }
            captions[0].Visible = false;
            group.Controls.Add(grid);
            group.Controls.Add(tier);
            group.Controls.Add(title);
            var history = new Label { Name = group == groupBox1 ? "SoloRankHistory" : "FlexRankHistory", Dock = DockStyle.Bottom,
                Height = 54, Text = "赛季最高  —\n上赛季结算  —", ForeColor = Color.FromArgb(111,123,139), Padding = new Padding(0,8,0,0) };
            group.Controls.Add(history);
            history.SendToBack();
        }

        private void ResizeMatchCards()
        {
            int width = Math.Max(180, flowLayoutPanelRight.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            foreach (var card in flowLayoutPanelRight.Controls.OfType<MatchListPanel>())
            {
                card.Width = width;
                card.Height = 82;
            }
        }
    }
}
