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
                Dock = DockStyle.Top, Height = 124, BackColor = Color.White,
                ColumnCount = 3, RowCount = 1, Padding = new Padding(12, 8, 12, 8)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var identity = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            picChampionId.SetBounds(0, 16, 56, 56);
            linkGameName.AutoSize = false;
            linkGameName.SetBounds(68, 12, 230, 28);
            linkGameName.Font = new Font(Font, FontStyle.Bold);
            linkGameName.LinkColor = Color.FromArgb(46, 103, 190);
            linkGameName.LinkBehavior = LinkBehavior.HoverUnderline;
            linkGameName.AutoEllipsis = true;
            lblLevel.Location = new Point(68, 43);
            lblPrivacy.Location = new Point(68, 68);
            lblLevel.ForeColor = lblPrivacy.ForeColor = Color.FromArgb(111, 123, 139);
            identity.Controls.AddRange(new Control[] { picChampionId, linkGameName, lblLevel, lblPrivacy });
            identity.Resize += (_, _) => linkGameName.Width = Math.Max(60, identity.ClientSize.Width - 72);
            header.Controls.Add(identity, 0, 0);

            ConfigureRankGroup(groupBox1,
                new[] { label2, label3, label4, label5, label6, label7 },
                new[] { lblSoloTier, lblSoloGames, lblSoloWins, lblSoloLosses, lblSoloWinRate, lblSoloLeaguePoints });
            ConfigureRankGroup(groupBox2,
                new[] { label19, label18, label17, label16, label15, label14 },
                new[] { lblFlexTier, lblFlexGames, lblFlexWins, lblFlexLosses, lblFlexWinRate, lblFlexLeaguePoints });
            header.Controls.Add(groupBox1, 1, 0);
            header.Controls.Add(groupBox2, 2, 0);

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 52, WrapContents = false,
                Padding = new Padding(0, 12, 0, 8), BackColor = BackColor
            };
            Label Caption(string text) => new Label
            {
                Text = text, AutoSize = true, Margin = new Padding(0, 5, 8, 0), ForeColor = ForeColor
            };
            comboFilter.Width = 140;
            comboFilter.Margin = new Padding(0, 0, 24, 0);
            comboPage.Width = 64;
            comboPage.DropDownStyle = ComboBoxStyle.DropDownList;
            comboPage.Margin = new Padding(0, 0, 8, 0);
            foreach (var button in new[] { btnPrev, btnNext })
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(215, 223, 233);
                button.BackColor = Color.White;
                button.ForeColor = ForeColor;
                button.Size = new Size(80, 28);
                button.Margin = new Padding(8, 0, 0, 0);
                button.Cursor = Cursors.Hand;
            }
            toolbar.Controls.AddRange(new Control[]
            {
                Caption("比赛模式"), comboFilter, Caption("每页"), comboPage, Caption("场"), btnPrev, btnNext
            });

            Controls.Clear();
            label8.Dispose(); // The player name already identifies the profile.
            flowLayoutPanelRight.Dock = DockStyle.Fill;
            flowLayoutPanelRight.BackColor = BackColor;
            flowLayoutPanelRight.FlowDirection = FlowDirection.TopDown;
            flowLayoutPanelRight.WrapContents = false;
            flowLayoutPanelRight.Padding = Padding.Empty;
            flowLayoutPanelRight.SizeChanged += (_, _) => ResizeMatchCards();
            flowLayoutPanelRight.ControlAdded += (_, _) => ResizeMatchCards();
            Controls.Add(flowLayoutPanelRight);
            Controls.Add(toolbar);
            Controls.Add(header);
            ResumeLayout(true);
        }

        private void ConfigureRankGroup(GroupBox group, Label[] captions, Label[] values)
        {
            group.Dock = DockStyle.Fill;
            group.Margin = new Padding(12, 0, 0, 0);
            group.Padding = new Padding(8, 5, 8, 5);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int row = 0; row < 3; row++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));
            string[] names = { "段位", "场次", "胜场", "负场", "胜率", "胜点" };
            for (int i = 0; i < 6; i++)
            {
                int column = i < 3 ? 0 : 2, row = i % 3;
                captions[i].Text = names[i];
                captions[i].ForeColor = Color.FromArgb(111, 123, 139);
                foreach (var label in new[] { captions[i], values[i] })
                {
                    label.AutoSize = false;
                    label.AutoEllipsis = true;
                    label.Dock = DockStyle.Fill;
                    label.TextAlign = ContentAlignment.MiddleLeft;
                    label.Margin = Padding.Empty;
                }
                grid.Controls.Add(captions[i], column, row);
                grid.Controls.Add(values[i], column + 1, row);
            }
            group.Controls.Add(grid);
        }

        private void ResizeMatchCards()
        {
            int width = Math.Max((int)(850 * DeviceDpi / 96f),
                flowLayoutPanelRight.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            foreach (var card in flowLayoutPanelRight.Controls.OfType<MatchListPanel>())
            {
                card.Width = width;
                card.Height = (int)(112 * card.DeviceDpi / 96f);
            }
        }
    }
}
