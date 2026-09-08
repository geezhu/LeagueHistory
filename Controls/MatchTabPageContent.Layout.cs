namespace League.Controls
{
    public partial class MatchTabPageContent
    {
        private readonly Label _pageNumber = new() { Name = "CurrentHistoryPage", Text = "1", Size = new Size(32,32),
            TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(46,103,190), ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI",10f,FontStyle.Bold) };
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
            var identity = _profileBackground = new ProfileBackgroundPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.FromArgb(232, 241, 253) };
            identity.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 3, BackColor = Color.FromArgb(46,103,190) });
            picChampionId.SetBounds(18, 36, 100, 100);
            picChampionId.SizeMode = PictureBoxSizeMode.Zoom;
            linkGameName.AutoSize = false;
            linkGameName.SetBounds(136, 52, 230, 36);
            linkGameName.TextAlign = ContentAlignment.MiddleLeft;
            linkGameName.Font = new Font(Font.FontFamily, 15f, FontStyle.Bold);
            linkGameName.LinkColor = Color.FromArgb(46, 103, 190);
            linkGameName.LinkBehavior = LinkBehavior.HoverUnderline;
            linkGameName.AutoEllipsis = true;
            lblLevel.Visible = false;
            lblPrivacy.Location = new Point(136, 94);
            lblPrivacy.Visible = false;
            lblLevel.ForeColor = lblPrivacy.ForeColor = Color.FromArgb(111, 123, 139);
            identity.Controls.AddRange(new Control[] { picChampionId, linkGameName, lblLevel, lblPrivacy });
            var copy = new IdentityIcon { IsCopy = true, Size = new Size(28,36),
                Font = linkGameName.Font, ForeColor = Color.FromArgb(46,103,190), Cursor = Cursors.Hand,
                AccessibleName = "复制 Riot ID", AccessibleRole = AccessibleRole.PushButton, TabStop = true };
            components ??= new System.ComponentModel.Container();
            var copyTip = new ToolTip(components);
            copyTip.SetToolTip(copy, "复制 Riot ID");
            copy.Click += (_, _) =>
            {
                string value = linkGameName.Tag as string ?? linkGameName.Text;
                if (string.IsNullOrWhiteSpace(value)) return;
                Clipboard.SetText(value);
                copyTip.Show("已复制 Riot ID", copy, 0, copy.Height, 1500);
            };
            identity.Controls.Add(copy);
            void PositionIdentity()
            {
                int available = Math.Max(30, identity.ClientSize.Width - linkGameName.Left - copy.Width - 16);
                int desired = TextRenderer.MeasureText(linkGameName.Text, linkGameName.Font).Width + linkGameName.Padding.Right + 4;
                linkGameName.Width = Math.Min(available, desired);
                copy.Location = new Point(linkGameName.Right + 3, linkGameName.Top);
            }
            identity.Resize += (_, _) => PositionIdentity();
            linkGameName.TextChanged += (_, _) => PositionIdentity();
            linkGameName.PaddingChanged += (_, _) => PositionIdentity();
            PositionIdentity();
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
            var options = new TableLayoutPanel { Size = new Size(300,152),
                ColumnCount = 2, RowCount = 4, Padding = new Padding(8), BackColor = Color.White };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,64));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            for (int row = 0; row < 3; row++) options.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
            options.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            comboFilter.Dock = DockStyle.Fill; comboFilter.Margin = new Padding(0,2,0,2);
            comboPage.Width = 54; comboPage.DropDownStyle = ComboBoxStyle.DropDownList;
            _outcomeFilter.Items.AddRange(new object[] { "全部", "只看胜利", "只看失败" });
            _outcomeFilter.SelectedIndex = 0; _outcomeFilter.Dock = DockStyle.Fill;
            _outcomeFilter.SelectedIndexChanged += async (_, _) =>
            {
                _selectedWin = _outcomeFilter.SelectedIndex switch { 1 => true, 2 => false, _ => null };
                _currentPage = 1;
                if (!string.IsNullOrEmpty(_puuid)) await UpdateMatchesDisplay();
            };
            string[] filterLabels = { "比赛模式", "比赛结果", "每页场数" };
            Control[] filterControls = { comboFilter, _outcomeFilter, comboPage };
            for (int row = 0; row < 3; row++)
            {
                options.Controls.Add(new Label { Text = filterLabels[row], Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft },0,row);
                options.Controls.Add(filterControls[row],1,row);
            }
            _filterScope.Text = "按需查找，满页即停；下一页继续加载";
            _filterScope.ForeColor = Color.FromArgb(111,123,139);
            options.Controls.Add(_filterScope,0,3); options.SetColumnSpan(_filterScope,2);
            var filterHost = new ToolStripControlHost(options) { AutoSize = false, Size = options.Size, Margin = Padding.Empty, Padding = Padding.Empty };
            var filterPopup = new ToolStripDropDown { AutoSize = true, Padding = new Padding(1), BackColor = Color.FromArgb(213,223,237), AutoClose = true };
            filterPopup.Items.Add(filterHost);
            components ??= new System.ComponentModel.Container();
            components.Add(filterPopup);
            filter.Click += (_, _) =>
            {
                if (filterPopup.Visible) { filterPopup.Close(); return; }
                filterPopup.Show(filter, new Point(filter.Width-filterPopup.PreferredSize.Width,filter.Height));
            };
            var paging = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 48, ColumnCount = 3, RowCount = 1,
                Padding = new Padding(0,8,0,0) };
            paging.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,32));
            paging.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            paging.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,32));
            int column = 0;
            foreach(var button in new[]{btnPrev,btnNext})
            {
                button.Dock = DockStyle.None; button.Size = new Size(32,32); button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(225,237,253);
                button.BackColor = Color.FromArgb(232,239,248); button.ForeColor = ForeColor;
                button.Text = "";
                bool previous = column == 0;
                button.Paint += (_, e) =>
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    float x = button.ClientSize.Width / 2f, y = button.ClientSize.Height / 2f;
                    float size = Math.Min(button.ClientSize.Width,button.ClientSize.Height) * .17f;
                    float direction = previous ? -1 : 1;
                    using var pen = new Pen(button.Enabled ? button.ForeColor : SystemColors.GrayText, Math.Max(1.5f,button.DeviceDpi/64f))
                        { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
                    e.Graphics.DrawLines(pen,new[] { new PointF(x-direction*size/2,y-size), new PointF(x+direction*size/2,y), new PointF(x-direction*size/2,y+size) });
                };
                button.AccessibleName = column == 0 ? "上一页" : "下一页";
                button.Font = new Font("Segoe UI",18f);
                button.Margin = Padding.Empty;
                paging.Controls.Add(button,column == 0 ? 0 : 2,0); column++;
            }
            _pageNumber.Anchor = AnchorStyles.Top;
            _pageNumber.Margin = Padding.Empty;
            paging.Controls.Add(_pageNumber,1,0);

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
