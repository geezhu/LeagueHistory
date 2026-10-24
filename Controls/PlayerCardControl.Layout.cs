using League.Models;

namespace League.Controls;

public partial class PlayerCardControl
{
    public Panel SummaryPanel { get; private set; } = null!;
    private Font _historyFigure = null!;
    private readonly Label _soloCaption = new() { Text = "单双" };
    private readonly Label _flexCaption = new() { Text = "灵活" };
    private const int HistoryRowHeight = 34;
    private const int RankTop = 74;
    private const int RankHeight = 26;

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
        SummaryPanel.Controls.AddRange(
            new Control[]
            {
                picHero,
                lblPlayerName,
                lblPrivacyStatus,
                _soloCaption,
                lblSoloRank,
                _flexCaption,
                lblFlexRank,
            }
        );
        SummaryPanel.Controls.Add(_summonerAvatar);
        SummaryPanel.Controls.Add(_masteryStrip);
        SummaryPanel.Resize += (_, _) => ReflowSummaryPanel();
        ConfigureChampionBar();
        Controls.Add(SummaryPanel);
        ShowRanks("", "");
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
        foreach (var label in new[] { _soloCaption, lblSoloRank, _flexCaption, lblFlexRank })
        {
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
        }
        _soloCaption.ForeColor = _flexCaption.ForeColor = Theme.Muted;
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
    }

    // Each queue carries its own caption, so a player ranked only in flex no longer reads as a solo rank.
    private void ShowRanks(string solo, string flex)
    {
        lblSoloRank.Text = RankText.Compact(solo);
        lblFlexRank.Text = RankText.Compact(flex);
        foreach (var label in new[] { _soloCaption, lblSoloRank })
        {
            _statisticsTips?.SetToolTip(label, "单双排：" + solo);
        }
        foreach (var label in new[] { _flexCaption, lblFlexRank })
        {
            _statisticsTips?.SetToolTip(label, "灵活排位：" + flex);
        }
        _soloCaption.Visible = lblSoloRank.Visible = lblSoloRank.Text.Length > 0;
        _flexCaption.Visible = lblFlexRank.Visible = lblFlexRank.Text.Length > 0;
        ReflowSummaryHeight();
        ReflowRanks();
    }

    // Identity on top, then the ranks and the mastery row when they have something to show, then the champion bar.
    // Decided from the content, not from Visible: a card filled before its window is shown reads every child as hidden.
    private void ReflowSummaryHeight()
    {
        bool ranks = HasRank(lblSoloRank) || HasRank(lblFlexRank);
        int masteryTop = ranks ? RankTop + RankHeight + 4 : RankTop + 2;
        _masteryStrip.SetBounds(10, masteryTop, Math.Max(20, SummaryPanel.Width - 20), _masteryStrip.Height);
        SummaryPanel.Height =
            _masteryStrip.Count > 0 ? _masteryStrip.Bottom + 10 + ChampionBarHeight
            : ranks ? 154
            : 120;
    }

    private static bool HasRank(Label rank) => rank.Text.Length > 0;

    private void ReflowSummaryPanel()
    {
        lblPlayerName.Width = lblPrivacyStatus.Width = Math.Max(20, SummaryPanel.Width - 82);
        _recentKda.Width = _recentWinRate.Width = lblPlayerName.Width;
        _masteryStrip.Width = Math.Max(20, SummaryPanel.Width - 20);
        ReflowRanks();
    }

    // The shown queues sit one after the other from the left, each as wide as its text, and the last one gives way
    // with an ellipsis when the card is too narrow for both.
    private void ReflowRanks()
    {
        const int gap = 8;
        int left = 10,
            right = SummaryPanel.Width - 8;
        foreach (var (caption, value) in new[] { (_soloCaption, lblSoloRank), (_flexCaption, lblFlexRank) })
        {
            if (!HasRank(value))
            {
                continue;
            }

            int captionWidth = TextRenderer.MeasureText(caption.Text, caption.Font).Width;
            int width = TextRenderer.MeasureText(value.Text, value.Font).Width;
            caption.SetBounds(left, RankTop, captionWidth, RankHeight);
            value.SetBounds(caption.Right, RankTop, Math.Max(20, Math.Min(width, right - caption.Right)), RankHeight);
            left = value.Right + gap;
        }
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
        listViewGames.ShowItemToolTips = true;
        listViewGames.Cursor = Cursors.Hand;
        ConfigureHistoryNavigation();
        listViewGames.BackColor = Theme.Surface;
        _historyFigure = Theme.NumberFont(Font.SizeInPoints + 1);
        Disposed += (_, _) => _historyFigure.Dispose();
        var rowHeight = new ImageList(components)
        {
            ImageSize = new Size(1, HistoryRowHeight),
            ColorDepth = ColorDepth.Depth32Bit,
        };
        using (var blank = new Bitmap(1, HistoryRowHeight))
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
        var rect = new Rectangle(e.Bounds.X, e.Bounds.Y, Math.Max(1, columnHeader1.Width - 1), e.Bounds.Height - 1);
        using var fill = new SolidBrush(e.Item.Selected ? Theme.AccentSoft : Theme.Surface);
        e.Graphics.FillRectangle(fill, e.Bounds);
        if (win || loss)
        {
            // The outcome washes in from the stripe and fades out before the score, so a column of games reads as
            // wins and losses at a glance while the mode and score stay on a light ground. Remakes stay plain.
            var wash = new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width * 2 / 3), rect.Height);
            using var gradient = new System.Drawing.Drawing2D.LinearGradientBrush(
                wash,
                Color.FromArgb(90, accent),
                Color.FromArgb(0, accent),
                System.Drawing.Drawing2D.LinearGradientMode.Horizontal
            );
            e.Graphics.FillRectangle(gradient, wash);
        }
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
        var bounds = new Rectangle(rect.X + 8, rect.Y + (rect.Height - 24) / 2, 24, 24);
        if (icon != null)
        {
            e.Graphics.DrawImage(icon, bounds);
        }
        if (e.Item.Tag is League.Models.MatchMetadata { Score: League.Models.OwnMatchScore score })
        {
            League.UIHelpers.MatchScoreMark.Paint(e.Graphics, League.UIHelpers.MatchScoreMark.Bounds(bounds), score);
        }
    }

    // One game per line: icon, mode and score. The stripe already carries the result, so the row spends its width on
    // the mode instead of repeating 胜利 or 失败; a remake names itself in place of the mode. The day is left to the
    // tooltip: in a room the recent form matters, not when each game was played.
    private void DrawHistoryText(DrawListViewItemEventArgs e, Rectangle rect, Color accent)
    {
        const int textLeft = 38,
            gap = 8;
        // Measured and drawn with the same padding, or the measured score is a few pixels short and loses its tail.
        var measure = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var flags = measure | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        var row = new Rectangle(rect.X, rect.Y, rect.Width - 6, rect.Height);
        // Sized for the widest score rather than this row's, so the scores line up down the card.
        int scoreWidth = TextRenderer.MeasureText(e.Graphics, "10/10/10", _historyFigure, Size.Empty, measure).Width;
        var score = new Rectangle(row.Right - scoreWidth, row.Y, scoreWidth, row.Height);
        var mode = new Rectangle(row.X + textLeft, row.Y, Math.Max(1, score.Left - row.X - textLeft - gap), row.Height);
        bool remake = e.Item.ForeColor == MatchResult.RemakeColor;
        TextRenderer.DrawText(
            e.Graphics,
            remake ? "重开" : HistoryField(e.Item, 1),
            Font,
            mode,
            remake ? accent : Theme.Ink,
            flags
        );
        TextRenderer.DrawText(
            e.Graphics,
            HistoryField(e.Item, 2),
            _historyFigure,
            score,
            Theme.Ink,
            flags | TextFormatFlags.Right
        );
    }

    private static string HistoryField(ListViewItem item, int index) =>
        item.SubItems.Count > index ? item.SubItems[index].Text : "";

    // The row leaves out the day and shortens the mode first, so the whole game is spelled out on hover.
    private static string HistoryToolTip(ListViewItem item)
    {
        string result =
            item.ForeColor == Color.Green ? "胜利"
            : item.ForeColor == Color.Red ? "失败"
            : item.ForeColor == MatchResult.RemakeColor ? "重开"
            : "";
        var parts = new[] { HistoryField(item, 1), result, HistoryField(item, 2), HistoryField(item, 3) };
        string line = string.Join(" · ", parts.Where(part => part.Length > 0));
        return item.Tag is League.Models.MatchMetadata { Score: League.Models.OwnMatchScore score }
            ? line + "\n" + League.UIHelpers.MatchScoreMark.Hint(score)
            : line;
    }
}
