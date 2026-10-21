namespace League.Controls;

/// <summary>What the main window is waiting for while it has nothing to show.</summary>
public enum ClientStatus
{
    /// <summary>No League client is connected; the card also offers to start one.</summary>
    NotConnected,

    /// <summary>The client is connected but no match has started yet.</summary>
    WaitingForGame,
}

/// <summary>
/// The card shown in the middle of an empty page while the client is missing or no match has started: an icon, a
/// title, a reassuring note and a loading bar, plus the game launchers when there is no client yet. Its parts are
/// direct children, so the window's status handling can recognise the card by its text and its loading bar.
/// </summary>
public sealed class ClientStatusCard : Panel
{
    private const int CardWidth = 520;
    private const int Inset = 32;
    private readonly Label _icon = new() { TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Accent };
    private readonly Label _title = new() { TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Heading };
    private readonly Label _note = new() { TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted };
    private readonly LoadingBar _loading = new();
    private readonly Label _launchHeading = new() { Text = "快速启动", ForeColor = Theme.Heading };
    private readonly Font _iconFont = new(Theme.IconFontName, 26f);
    private readonly Font _titleFont = new(Theme.FontName, 14f, FontStyle.Bold);
    private readonly Font _headingFont = new(Theme.FontName, 9f, FontStyle.Bold);
    private readonly Font _bodyFont = new(Theme.FontName, 9f);
    private readonly Control? _launcher;

    public ClientStatusCard(ClientStatus status)
    {
        Status = status;
        DoubleBuffered = true;
        BackColor = Theme.Surface;
        Font = _bodyFont;
        Width = CardWidth;
        _icon.Font = _iconFont;
        _title.Font = _titleFont;
        _launchHeading.Font = _headingFont;
        bool notConnected = status == ClientStatus.NotConnected;
        // Glyphs from Segoe Fluent Icons / MDL2 Assets: Connect, Game.
        _icon.Text = notConnected ? "" : "";
        _title.Text = notConnected ? "未连接到英雄联盟客户端" : "等待进入对局";
        _note.Text = notConnected
            ? "登录游戏后会自动连接，无需其他操作。"
            : "正在等待加入游戏，进入选人后会自动显示双方战绩。";
        Controls.AddRange(new Control[] { _icon, _title, _note, _loading });
        if (notConnected)
        {
            _launcher = new LolLauncherPanel();
            Controls.Add(_launchHeading);
            Controls.Add(_launcher);
            _launcher.SizeChanged += (_, _) => Arrange();
        }

        Arrange();
    }

    public ClientStatus Status { get; }

    /// <summary>The launch controls, present only while no client is connected.</summary>
    public bool OffersLaunch => _launcher != null;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Theme.Divider);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        if (_launcher != null)
        {
            int y = _launchHeading.Top - 16;
            e.Graphics.DrawLine(border, Inset, y, Width - Inset, y);
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Arrange();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconFont.Dispose();
            _titleFont.Dispose();
            _headingFont.Dispose();
            _bodyFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Arrange()
    {
        int inner = Math.Max(120, Width - Inset * 2);
        _icon.SetBounds(Inset, 28, inner, 44);
        _title.SetBounds(Inset, _icon.Bottom + 8, inner, 30);
        _note.SetBounds(Inset, _title.Bottom + 2, inner, 22);
        int barWidth = Math.Min(200, inner);
        _loading.SetBounds((Width - barWidth) / 2, _note.Bottom + 14, barWidth, 4);
        int bottom = _loading.Bottom + 28;
        if (_launcher != null)
        {
            _launchHeading.SetBounds(Inset, _loading.Bottom + 44, inner, 22);
            _launcher.SetBounds(Inset, _launchHeading.Bottom + 6, inner, _launcher.Height);
            bottom = _launcher.Bottom + 24;
        }

        if (Height != bottom)
        {
            Height = bottom;
        }
    }
}
