using System.Drawing.Drawing2D;
using League.Controls;
using League.Models;
using League.Services;
using League.UIHelpers;

namespace League;

/// <summary>
/// The panel shown when a game ends, after WeGame's: the game's length, the champion's mastery and the K/D/A on top,
/// one row per teammate below. A win offers a thumbs-up per row and for everyone; a loss selects rows and sends one
/// line naming them. Nothing is sent without a click.
/// </summary>
public sealed class EndOfGamePanelForm : Form
{
    private const int PanelWidth = 400;
    private const int HeaderHeight = 168;
    private const int ListHeaderHeight = 30;
    private const int RowHeight = 52;
    private const int FooterHeight = 64;
    private const int IconSize = 34;
    private const int AvatarSize = 46;
    private const int HonorGlyphSize = 10;
    private const int HonorGlyphGap = 3;
    private const int NameLeft = 72;

    // The avatar, the player's name and the result badge share this horizontal centre line.
    private const int HeaderLine = 26 + AvatarSize / 2;

    // The match detail page's colours: the page behind, white rows, the selected-row tint for the local player.
    private static readonly Color Background = Theme.Canvas;
    private static readonly Color ListBackground = Theme.Surface;
    private static readonly Color ListHeaderBackground = Theme.SurfaceSubtle;
    private static readonly Color SelfRowBackground = Theme.AccentSoft;
    private static readonly Color TextColor = Theme.Ink;
    private static readonly Color MutedText = Theme.Muted;
    private static readonly Color MetricColor = Theme.Heading;

    private readonly EndOfGamePanelState _state;
    private readonly Func<string, CancellationToken, Task<PostGameSendResult>> _send;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Font _bodyFont = new("Microsoft YaHei UI", 9);
    private readonly Font _smallFont = new("Microsoft YaHei UI", 8);
    private readonly Font _metricFont = new("Microsoft YaHei UI", 11, FontStyle.Bold);
    private readonly Font _badgeFont = new("Microsoft YaHei UI", 11, FontStyle.Bold);
    private readonly Dictionary<string, Bitmap> _honorIcons = new();

    // Everything sits inside this panel, one pixel in from the form's edge, so the form's own colour is the border
    // that keeps a light window apart from a light desktop.
    private readonly BufferedPanel _content = new() { Name = "endOfGameContent" };
    private readonly BufferedPanel _header = new() { Name = "endOfGameHeader" };
    private readonly BufferedPanel _selfRow = new() { Name = "endOfGameSelfRow" };
    private readonly BufferedPanel[] _rows;
    private readonly PillButton[] _rowButtons;
    private readonly PillButton _honorAll = PanelButton("一键全赞", "endOfGameHonorAll", filled: true);
    private readonly PillButton _footer = PanelButton("关闭", "endOfGameFooter", filled: true);
    private readonly Label _status = new()
    {
        Name = "endOfGameStatus",
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Image?[] _icons;
    private Image? _ownIcon;
    private string _mastery = "--";
    private bool _sending;

    public EndOfGamePanelForm(
        EndOfGamePanelState state,
        Func<string, CancellationToken, Task<PostGameSendResult>> send,
        Func<int, Task<Image?>>? championIcon = null
    )
    {
        _state = state;
        _send = send;
        int count = state.Summary.Teammates.Count;
        _rows = new BufferedPanel[count];
        _rowButtons = new PillButton[count];
        _icons = new Image?[count];
        ConfigureSurface();
        BuildRows();
        BindActions();
        UpdateActions();
        if (championIcon != null)
        {
            _ = LoadIconsAsync(championIcon);
        }
    }

    public string MasteryText => _mastery;

    /// <summary>The mastery cell arrives after the panel, once the refreshed mastery list is in.</summary>
    public void ShowMastery(string text)
    {
        _mastery = text;
        _header.Invalidate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // A send still in flight must not report into a closed panel.
        _lifetime.Cancel();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        // Closing a modeless form disposes it, so an owner's own Dispose comes second and must find nothing to do.
        if (disposing && !IsDisposed)
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
            _bodyFont.Dispose();
            _smallFont.Dispose();
            _metricFont.Dispose();
            _badgeFont.Dispose();
            foreach (var icon in _honorIcons.Values)
            {
                icon.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private void ConfigureSurface()
    {
        Name = "EndOfGamePanel";
        Text = "本局总结";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.DividerStrong;
        ForeColor = TextColor;
        Font = _bodyFont;
        int listTop = HeaderHeight + ListHeaderHeight;
        // The local player's row comes first, then one per teammate.
        int footerTop = listTop + RowHeight * (_rows.Length + 1);
        ClientSize = new Size(PanelWidth + 2, footerTop + FooterHeight + 2);
        _content.SetBounds(1, 1, PanelWidth, footerTop + FooterHeight);
        _content.BackColor = Background;
        Controls.Add(_content);

        _header.SetBounds(0, 0, PanelWidth, listTop);
        _header.BackColor = Background;
        _header.Paint += (_, e) => PaintHeader(e.Graphics);
        _content.Controls.Add(_header);

        var close = new Label
        {
            Name = "endOfGameClose",
            Text = "✕",
            ForeColor = MutedText,
            BackColor = Background,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        close.SetBounds(PanelWidth - 36, 8, 28, 28);
        close.Click += (_, _) => Close();
        _header.Controls.Add(close);

        _status.SetBounds(24, footerTop + (FooterHeight - 32) / 2, PanelWidth - 156, 32);
        _status.ForeColor = MutedText;
        _footer.SetBounds(PanelWidth - 116, footerTop + (FooterHeight - 32) / 2, 96, 32);
        _content.Controls.AddRange([_status, _footer]);
    }

    private void BuildRows()
    {
        int top = HeaderHeight + ListHeaderHeight;
        var summary = _state.Summary;
        _selfRow.BackColor = SelfRowBackground;
        _selfRow.SetBounds(0, top, PanelWidth, RowHeight);
        _selfRow.Paint += (_, e) =>
            PaintPlayer(e.Graphics, _ownIcon, summary.PlayerName, summary.Score, summary.Honors);
        // As in WeGame, "honor all" sits on the local player's own row; nobody honors themselves.
        _honorAll.SetBounds(PanelWidth - 100, (RowHeight - 28) / 2, 76, 28);
        _selfRow.Controls.Add(_honorAll);
        _content.Controls.Add(_selfRow);
        for (int i = 0; i < _rows.Length; i++)
        {
            int index = i;
            var row = new BufferedPanel { Name = $"endOfGameRow{i}", BackColor = ListBackground };
            row.SetBounds(0, top + RowHeight * (i + 1), PanelWidth, RowHeight);
            row.Paint += (_, e) => PaintRow(e.Graphics, index);
            var button = PanelButton("", $"endOfGameRowButton{i}", filled: false);
            button.SetBounds(PanelWidth - 100, (RowHeight - 28) / 2, 76, 28);
            row.Controls.Add(button);
            _rows[i] = row;
            _rowButtons[i] = button;
            _content.Controls.Add(row);
        }
    }

    private void BindActions()
    {
        for (int i = 0; i < _rowButtons.Length; i++)
        {
            int index = i;
            _rowButtons[i].Click += async (_, _) => await RowClickedAsync(index);
        }

        _honorAll.Click += async (_, _) => await SendAsync(_state.Unhonored, closeAfter: false);
        _footer.Click += async (_, _) =>
        {
            if (_state.Footer == EndOfGameFooterAction.Send)
            {
                await SendAsync(_state.Selected, closeAfter: true);
            }
            else
            {
                Close();
            }
        };
    }

    private async Task RowClickedAsync(int index)
    {
        if (_state.Mode == EndOfGamePanelMode.Honor)
        {
            await SendAsync([index], closeAfter: false);
            return;
        }

        _state.Toggle(index);
        UpdateActions();
    }

    private async Task SendAsync(IReadOnlyList<int> rows, bool closeAfter)
    {
        if (_sending || rows.Count == 0)
        {
            return;
        }

        _sending = true;
        // The line waits for the post-game chat, which opens only after the honor screen.
        _status.Text = "等结算聊天打开后发出…";
        UpdateActions();
        try
        {
            var result = await _send(_state.Message(rows), _lifetime.Token);
            if (result == PostGameSendResult.Sent)
            {
                _state.MarkSent(rows);
                _status.Text = "";
                if (closeAfter)
                {
                    Close();
                }
            }
            else
            {
                AppLog.Shared.Info(LogCategory.Automation, $"结算面板消息未发送：{result}");
                _status.Text =
                    result == PostGameSendResult.NoConversation ? "结算聊天已关闭，未发送" : "发送失败，可再试一次";
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The panel closed while sending; there is nobody left to tell.
        }
        catch (Exception ex)
        {
            AppLog.Shared.Warn(LogCategory.Automation, "结算面板消息发送异常", ex);
            _status.Text = "发送失败，可再试一次";
        }
        finally
        {
            _sending = false;
            if (!IsDisposed)
            {
                UpdateActions();
            }
        }
    }

    private void UpdateActions()
    {
        bool honor = _state.Mode == EndOfGamePanelMode.Honor;
        for (int i = 0; i < _rowButtons.Length; i++)
        {
            var rowState = _state.Row(i);
            var button = _rowButtons[i];
            button.Text = (honor, rowState) switch
            {
                (true, EndOfGameRowState.Sent) => "已赞",
                (true, _) => "点赞",
                (false, EndOfGameRowState.Selected) => "已选",
                (false, EndOfGameRowState.Sent) => "已发送",
                (false, _) => "拉黑",
            };
            button.Enabled = !_sending && _state.CanAct(i);
            button.Filled = rowState == EndOfGameRowState.Selected;
        }

        // Once every teammate is thanked the button reads 已全赞 and stays gold, as WeGame's does; clicking it then
        // has nothing left to send.
        bool allHonored =
            _state.Unhonored.Count == 0
            && Enumerable.Range(0, _rowButtons.Length).Any(i => _state.Row(i) == EndOfGameRowState.Sent);
        _honorAll.Visible = honor;
        _honorAll.Text = allHonored ? "已全赞" : "一键全赞";
        _honorAll.Enabled = !_sending && (_state.Unhonored.Count > 0 || allHonored);
        _footer.Text = _state.Footer == EndOfGameFooterAction.Send ? "发送" : "关闭";
        _footer.Enabled = !_sending;
    }

    private async Task LoadIconsAsync(Func<int, Task<Image?>> championIcon)
    {
        try
        {
            _ownIcon = await championIcon(_state.Summary.ChampionId);
            for (int i = 0; i < _icons.Length; i++)
            {
                _icons[i] = await championIcon(_state.Summary.Teammates[i].ChampionId);
            }
        }
        catch (Exception ex)
        {
            // Icons are decoration: the panel stays usable with empty circles.
            AppLog.Shared.Warn(LogCategory.Automation, "结算面板英雄头像加载失败", ex);
        }

        if (!IsDisposed)
        {
            _header.Invalidate();
            _selfRow.Invalidate();
            foreach (var row in _rows)
            {
                row.Invalidate();
            }
        }
    }

    private void PaintHeader(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var summary = _state.Summary;
        var avatar = new Rectangle(24, HeaderLine - AvatarSize / 2, AvatarSize, AvatarSize);
        // No MVP or SVP mark here: the local player's own row in the list already carries it.
        PaintIcon(g, _ownIcon, avatar, Theme.Accent, 2f);

        var nameFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        var nameBounds = new Rectangle(
            avatar.Right + 12,
            avatar.Y,
            PanelWidth / 2 - 22 - avatar.Right - 20,
            AvatarSize
        );
        TextRenderer.DrawText(
            g,
            summary.PlayerName,
            _bodyFont,
            nameBounds,
            TextColor,
            nameFlags | TextFormatFlags.NoPrefix
        );

        PaintResultBadge(g, new Point(PanelWidth / 2, HeaderLine), summary.Win);

        string[] values = [$"{(int)Math.Round(summary.Duration.TotalMinutes)}min", _mastery, summary.Kda.ToString()];
        string[] labels = ["本局时长", "熟练度", "K/D/A"];
        int column = (PanelWidth - 48) / values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            var cell = new Rectangle(24 + column * i, HeaderLine + AvatarSize / 2 + 20, column, 24);
            TextRenderer.DrawText(g, values[i], _metricFont, cell, MetricColor, TextFormatFlags.HorizontalCenter);
            cell.Offset(0, 24);
            TextRenderer.DrawText(g, labels[i], _smallFont, cell, MutedText, TextFormatFlags.HorizontalCenter);
        }

        using var list = new SolidBrush(ListHeaderBackground);
        g.FillRectangle(list, 0, HeaderHeight, PanelWidth, ListHeaderHeight);
        var listHeader = new Rectangle(0, HeaderHeight, PanelWidth, ListHeaderHeight);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
        TextRenderer.DrawText(g, "玩家", _smallFont, listHeader with { X = NameLeft }, MutedText, flags);
        TextRenderer.DrawText(g, "评价", _smallFont, listHeader with { X = 236 }, MutedText, flags);
    }

    private void PaintRow(Graphics g, int index)
    {
        var teammate = _state.Summary.Teammates[index];
        string name = teammate.CanBeNamed ? teammate.Name : "名字未公开";
        PaintPlayer(g, _icons[index], name, teammate.Score, teammate.Honors);
    }

    private void PaintPlayer(
        Graphics g,
        Image? image,
        string name,
        OwnMatchScore? playerScore,
        IReadOnlyList<string> honors
    )
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var icon = new Rectangle(24, (RowHeight - IconSize) / 2, IconSize, IconSize);
        PaintIcon(g, image, icon, Theme.DividerStrong, 1.5f);
        if (playerScore?.Honour is MatchHonour honour && WeGameHonorIcons.Badge(honour) is Bitmap badge)
        {
            // Centred on the bottom edge of the icon, where WeGame puts it.
            var size = MatchScoreMark.BadgeSize;
            var bounds = new Rectangle(
                icon.X + (icon.Width - size.Width) / 2,
                icon.Bottom - 8,
                size.Width,
                size.Height
            );
            g.DrawImage(badge, bounds);
        }

        var flags =
            TextFormatFlags.Left
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix;
        // The name sits on the row's centre line alone, or just above it with a line of honor icons below. Drawn
        // without GDI's side padding so its first character starts exactly where the icons do.
        var nameBounds =
            honors.Count == 0
                ? new Rectangle(NameLeft, 0, 156, RowHeight)
                : new Rectangle(NameLeft, RowHeight / 2 - 18, 156, 18);
        TextRenderer.DrawText(g, name, _bodyFont, nameBounds, TextColor, flags | TextFormatFlags.NoPadding);
        for (int i = 0; i < honors.Count; i++)
        {
            // Each icon is a 10 px glyph centred in a 16 px slot; only the glyph is drawn, packed as WeGame does.
            var sprite = HonorIcon(honors[i]);
            int inset = (sprite.Width - HonorGlyphSize) / 2;
            var glyph = new Rectangle(
                NameLeft + (HonorGlyphSize + HonorGlyphGap) * i,
                RowHeight / 2 + 3,
                HonorGlyphSize,
                HonorGlyphSize
            );
            g.DrawImage(sprite, glyph, new Rectangle(inset, inset, HonorGlyphSize, HonorGlyphSize), GraphicsUnit.Pixel);
        }

        string score = playerScore is OwnMatchScore value ? $"{value.Estimate:0.0}" : "--";
        TextRenderer.DrawText(
            g,
            score,
            _bodyFont,
            new Rectangle(236, 0, 56, RowHeight),
            TextColor,
            flags | TextFormatFlags.VerticalCenter
        );
    }

    // A round champion icon inside a ring, as WeGame draws them; the local player's ring takes the accent, the way
    // the player cards ring their avatars.
    private static void PaintIcon(Graphics g, Image? image, Rectangle bounds, Color ring, float ringWidth)
    {
        if (image != null)
        {
            RoundImage.Draw(g, image, bounds);
        }
        else
        {
            using var placeholder = new SolidBrush(Theme.Divider);
            g.FillEllipse(placeholder, bounds);
        }

        // Centred on the icon's edge, so half the ring covers the outermost pixels of the round image.
        var edge = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        edge.Inflate(ringWidth / 2 - 0.5f, ringWidth / 2 - 0.5f);
        using var pen = new Pen(ring, ringWidth);
        g.DrawEllipse(pen, edge);
    }

    // WeGame's honor icons are 16 x 16 bitmaps built per key; the panel keeps one of each and disposes them itself.
    private Bitmap HonorIcon(string key)
    {
        if (!_honorIcons.TryGetValue(key, out var icon))
        {
            _honorIcons[key] = icon = WeGameHonorIcons.Get(key);
        }

        return icon;
    }

    // A pointy-top hexagon with 胜 or 败 in the match detail page's win and loss colours, in place of WeGame's artwork.
    private void PaintResultBadge(Graphics g, Point centre, bool win)
    {
        const float Radius = 17f;
        var points = Enumerable
            .Range(0, 6)
            .Select(i => Math.PI / 3 * i + Math.PI / 6)
            .Select(angle => new PointF(
                centre.X + Radius * (float)Math.Cos(angle),
                centre.Y + Radius * (float)Math.Sin(angle)
            ))
            .ToArray();
        Color tint = win ? Theme.Win : Theme.Loss;
        using var pen = new Pen(tint, 1.5f);
        g.DrawPolygon(pen, points);
        // Measured without GDI padding, so the glyph's own box is what gets centred.
        string text = win ? "胜" : "败";
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var size = TextRenderer.MeasureText(g, text, _badgeFont, Size.Empty, flags);
        var origin = new Point(centre.X - size.Width / 2, centre.Y - size.Height / 2);
        TextRenderer.DrawText(g, text, _badgeFont, new Rectangle(origin, size), tint, flags);
    }

    private static PillButton PanelButton(string text, string name, bool filled) =>
        new()
        {
            Text = text,
            Name = name,
            Filled = filled,
        };
}
