namespace League.Controls;

public partial class PlayerCardControl
{
    public Panel SummaryPanel { get; private set; } = null!;
    private Font _historyStrong = null!;
    private Font _historyFigure = null!;

    private void ConfigureCardLayout()
    {
        SuspendLayout();
        try
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Padding = new Padding(6);
            Font = new Font("Microsoft YaHei UI", 9f);
            ForeColor = Theme.Heading;
            ConfigureSummaryPanel();
            ConfigureHistoryList();
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private void ConfigureSummaryPanel()
    {
        SummaryPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 112,
            BackColor = Theme.Surface,
        };
        Disposed += (_, _) => SummaryPanel.Dispose();
        ConfigureSummaryIdentity();
        ConfigureRecentStatistics();
        lblSoloRank.TextChanged += (_, _) => UpdateRanks();
        lblFlexRank.TextChanged += (_, _) => UpdateRanks();
        SummaryPanel.Controls.AddRange(
            new Control[] { picHero, lblPlayerName, lblPrivacyStatus, lblSoloRank, lblFlexRank }
        );
        SummaryPanel.Controls.Add(_summonerAvatar);
        SummaryPanel.Resize += (_, _) => ReflowSummaryPanel();
        ConfigureChampionBar();
        Controls.Add(SummaryPanel);
        UpdateRanks();
    }

    private void ConfigureSummaryIdentity()
    {
        picHero.SetBounds(10, 16, 52, 52);
        picHero.Visible = false;
        _summonerAvatar.Location = new Point(4, 5);
        lblPlayerName.AutoSize = false;
        lblPlayerName.SetBounds(72, 6, 150, 27);
        lblPlayerName.Font = new Font(Font.FontFamily, 11, FontStyle.Bold);
        lblPlayerName.AutoEllipsis = true;
        lblPlayerName.TextAlign = ContentAlignment.MiddleLeft;
        lblPlayerName.LinkBehavior = LinkBehavior.HoverUnderline;
        lblPlayerName.LinkClicked += async (_, _) => await OpenProfileAsync();
        _summonerAvatar.Cursor = Cursors.Hand;
        _summonerAvatar.Click += async (_, _) => await OpenProfileAsync();
        lblPrivacyStatus.AutoSize = false;
        lblPrivacyStatus.SetBounds(72, 40, 150, 20);
        lblPrivacyStatus.Visible = false;
        lblPrivacyStatus.ForeColor = Theme.Muted;
        lblSoloRank.AutoSize = lblFlexRank.AutoSize = false;
        lblSoloRank.SetBounds(10, 74, 110, 26);
        lblFlexRank.SetBounds(125, 74, 110, 26);
        lblSoloRank.AutoEllipsis = lblFlexRank.AutoEllipsis = true;
    }

    private void ConfigureRecentStatistics()
    {
        components ??= new System.ComponentModel.Container();
        var tips = new ToolTip(components);
        _statisticsTips = tips;
        _recentKda.SetBounds(72, 34, 150, 19);
        _recentWinRate.SetBounds(72, 54, 150, 19);
        _recentKda.ForeColor = Theme.Ink;
        _recentWinRate.ForeColor = Theme.Ink;
        SummaryPanel.Controls.AddRange(new Control[] { _recentKda, _recentWinRate });
        lblSoloRank.TextChanged += (_, _) => tips.SetToolTip(lblSoloRank, "单双排：" + lblSoloRank.Text);
        lblFlexRank.TextChanged += (_, _) => tips.SetToolTip(lblFlexRank, "灵活排位：" + lblFlexRank.Text);
    }

    private void UpdateRanks()
    {
        bool Show(Label label) =>
            !string.IsNullOrWhiteSpace(label.Text)
            && !label.Text.Contains("未定级")
            && !label.Text.Contains("未评级")
            && !label.Text.Contains("未知");
        lblSoloRank.Visible = Show(lblSoloRank);
        lblFlexRank.Visible = Show(lblFlexRank);
        SummaryPanel.Height = Show(lblSoloRank) || Show(lblFlexRank) ? 154 : 120;
    }

    private void ReflowSummaryPanel()
    {
        lblPlayerName.Width = lblPrivacyStatus.Width = Math.Max(20, SummaryPanel.Width - 82);
        _recentKda.Width = _recentWinRate.Width = lblPlayerName.Width;
        int half = Math.Max(20, (SummaryPanel.Width - 25) / 2);
        lblSoloRank.Width = lblFlexRank.Width = half;
        lblFlexRank.Left = half + 15;
    }

    private void ConfigureHistoryList()
    {
        listViewGames.Dock = DockStyle.Fill;
        listViewGames.BringToFront();
        listViewGames.HeaderStyle = ColumnHeaderStyle.None;
        listViewGames.Columns.Clear();
        listViewGames.Columns.Add(columnHeader1);
        listViewGames.OwnerDraw = true;
        listViewGames.HideSelection = false;
        listViewGames.Cursor = Cursors.Hand;
        ConfigureHistoryNavigation();
        listViewGames.BackColor = Theme.Surface;
        _historyStrong = new Font(Font, FontStyle.Bold);
        Disposed += (_, _) => _historyStrong.Dispose();
        _historyFigure = Theme.NumberFont(Font.SizeInPoints + 1);
        Disposed += (_, _) => _historyFigure.Dispose();
        var rowHeight = new ImageList(components) { ImageSize = new Size(1, 48), ColorDepth = ColorDepth.Depth32Bit };
        using (var blank = new Bitmap(1, 48))
        {
            rowHeight.Images.Add(blank);
            _ = rowHeight.Handle;
        }
        listViewGames.StateImageList = rowHeight;
        var historyHost = new CompactHistoryHost(listViewGames) { Dock = DockStyle.Fill };
        Controls.Add(historyHost);
        historyHost.BringToFront();
        listViewGames.DrawColumnHeader += (_, e) => e.DrawDefault = false;
        listViewGames.DrawSubItem += (_, e) => { };
        listViewGames.DrawItem += (_, e) => DrawHistoryItem(e);
    }

    private void ConfigureHistoryNavigation()
    {
        listViewGames.MouseEnter += (_, _) =>
        {
            if (FindForm()?.ContainsFocus == true)
            {
                listViewGames.Focus();
            }
        };
        listViewGames.MouseClick += async (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (
                listViewGames.HitTest(e.Location).Item?.Tag is League.Models.MatchMetadata metadata
                && FindForm() is FormMain main
            )
            {
                await main.OpenRoomMatchAsync(metadata, CurrentPuuId);
            }
        };
        listViewGames.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || listViewGames.SelectedItems.Count == 0)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            if (
                listViewGames.SelectedItems[0].Tag is League.Models.MatchMetadata metadata
                && FindForm() is FormMain main
            )
            {
                await main.OpenRoomMatchAsync(metadata, CurrentPuuId);
            }
        };
    }

    private void DrawHistoryItem(DrawListViewItemEventArgs e)
    {
        if (e.Item == null)
        {
            return;
        }
        var (rect, accent) = DrawHistoryBackground(e);
        DrawHistoryIcon(e, rect);
        DrawHistoryText(e, rect, accent);
    }

    private (Rectangle Rect, Color Accent) DrawHistoryBackground(DrawListViewItemEventArgs e)
    {
        bool win = e.Item.ForeColor == Color.Green;
        bool loss = e.Item.ForeColor == Color.Red;
        var accent =
            win ? Theme.Win
            : loss ? Theme.Loss
            : Theme.Muted;
        // Rows stay white so ten results in a column read as a calm list; the stripe and the result word carry the outcome.
        var rect = new Rectangle(e.Bounds.X, e.Bounds.Y, Math.Max(1, columnHeader1.Width - 1), e.Bounds.Height - 1);
        using var fill = new SolidBrush(e.Item.Selected ? Theme.AccentSoft : Theme.Surface);
        e.Graphics.FillRectangle(fill, e.Bounds);
        using var divider = new SolidBrush(Theme.Divider);
        e.Graphics.FillRectangle(divider, rect.X, rect.Bottom, rect.Width, 1);
        using var stripe = new SolidBrush(accent);
        e.Graphics.FillRectangle(stripe, rect.X, rect.Y + 6, 3, rect.Height - 12);
        return (rect, accent);
    }

    private void DrawHistoryIcon(DrawListViewItemEventArgs e, Rectangle rect)
    {
        Image? icon = null;
        if (listViewGames.SmallImageList is { } images)
        {
            if (!string.IsNullOrEmpty(e.Item.ImageKey) && images.Images.ContainsKey(e.Item.ImageKey))
            {
                icon = images.Images[e.Item.ImageKey];
            }
            else if (e.Item.ImageIndex >= 0 && e.Item.ImageIndex < images.Images.Count)
            {
                icon = images.Images[e.Item.ImageIndex];
            }
        }
        if (icon != null)
        {
            e.Graphics.DrawImage(icon, rect.X + 7, rect.Y + (rect.Height - 32) / 2, 32, 32);
        }
    }

    private void DrawHistoryText(DrawListViewItemEventArgs e, Rectangle rect, Color accent)
    {
        string Field(int index) => e.Item.SubItems.Count > index ? e.Item.SubItems[index].Text : "";
        bool win = e.Item.ForeColor == Color.Green;
        bool loss = e.Item.ForeColor == Color.Red;
        string date = DateTime.TryParse(Field(3), out var parsed) ? parsed.ToString("MM-dd") : Field(3);
        var flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(
            e.Graphics,
            Field(1),
            Font,
            new Rectangle(rect.X + 44, rect.Y + 3, Math.Max(1, rect.Width - 128), 21),
            Theme.Ink,
            flags
        );
        TextRenderer.DrawText(
            e.Graphics,
            win ? "胜利"
                : loss ? "失败"
                : e.Item.ForeColor == League.Models.MatchResult.RemakeColor ? "重开"
                : "—",
            _historyStrong,
            new Rectangle(rect.Right - 46, rect.Y + 3, 42, 21),
            accent,
            flags | TextFormatFlags.Right
        );
        TextRenderer.DrawText(
            e.Graphics,
            date,
            Font,
            new Rectangle(rect.X + 44, rect.Y + 24, Math.Max(1, rect.Width - 128), 20),
            Theme.Muted,
            flags
        );
        TextRenderer.DrawText(
            e.Graphics,
            Field(2),
            _historyFigure,
            new Rectangle(rect.Right - 126, rect.Y + 24, 122, 20),
            Theme.Ink,
            flags | TextFormatFlags.Right
        );
    }
}
