namespace League.Controls;

/// <summary>
/// One statistics cell for every recommendation on the OP.GG guide: the win rate, coloured by which side of 50% it
/// falls, with the games under it, and the pick rate with a thin bar beside them. The guide used to print these three
/// numbers several ways, some of them without saying which number was which.
/// </summary>
internal sealed class OpggStatCell : Control
{
    public const int CellWidth = 196;
    public const int CellHeight = 40;

    private readonly double? _win;
    private readonly long? _games;
    private readonly double? _pick;

    public OpggStatCell(double? win, long? games, double? pick)
    {
        _win = win;
        _games = games;
        _pick = pick;
        DoubleBuffered = true;
        Size = new Size(CellWidth, CellHeight);
        Margin = Padding.Empty;
        AccessibleName = $"胜率 {Percent(win)}，{Games(games)}，选用率 {Percent(pick)}";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Draw(e.Graphics, ClientRectangle, Font, _win, _games, _pick);
    }

    internal static string Percent(double? value) => value.HasValue ? $"{value:P1}" : "—";

    private static string Games(long? games) => games.HasValue ? $"{games:N0} 场" : "— 场";

    // Shared with the rune scheme buttons, which draw the same cell inside themselves.
    internal static void Draw(Graphics g, Rectangle bounds, Font font, double? win, long? games, double? pick)
    {
        var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
        int half = bounds.Height / 2;
        int left = bounds.Width * 52 / 100;
        var winColor =
            win is not double rate ? Theme.Muted
            : rate >= 0.5 ? Theme.Win
            : Theme.Loss;
        using var strong = new Font(font, FontStyle.Bold);
        int caption = TextRenderer.MeasureText(g, "胜率 ", font, Size.Empty, flags).Width;
        TextRenderer.DrawText(g, "胜率", font, new Rectangle(bounds.X, bounds.Y, caption, half), Theme.Muted, flags);
        TextRenderer.DrawText(
            g,
            Percent(win),
            strong,
            new Rectangle(bounds.X + caption, bounds.Y, Math.Max(1, left - caption), half),
            winColor,
            flags
        );
        TextRenderer.DrawText(
            g,
            Games(games),
            font,
            new Rectangle(bounds.X, bounds.Y + half, left, half),
            Theme.Muted,
            flags
        );

        int right = bounds.X + left;
        int width = Math.Max(1, bounds.Right - right);
        TextRenderer.DrawText(g, "选用率", font, new Rectangle(right, bounds.Y, width, half), Theme.Muted, flags);
        int value = TextRenderer.MeasureText(g, "选用率 ", font, Size.Empty, flags).Width;
        TextRenderer.DrawText(
            g,
            Percent(pick),
            strong,
            new Rectangle(right + value, bounds.Y, Math.Max(1, width - value), half),
            Theme.Heading,
            flags
        );
        var track = new Rectangle(right, bounds.Y + half + half / 2 - 2, width - 4, 4);
        using var trackBrush = new SolidBrush(Theme.ControlFill);
        g.FillRectangle(trackBrush, track);
        if (pick is double share)
        {
            using var fill = new SolidBrush(Theme.Accent);
            g.FillRectangle(fill, track.X, track.Y, (int)(track.Width * Math.Clamp(share, 0, 1)), track.Height);
        }
    }
}
