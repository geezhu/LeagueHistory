using System.Drawing.Drawing2D;
using League.Infrastructure;

namespace League.Controls;

internal sealed class MatchHighlightCard : Control
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly ImageCacheService Cache = new(Client);
    private readonly string _title;
    private readonly string _value;
    private Image? _splash;

    public MatchHighlightCard(string title, int value, int championId, bool tied)
    {
        _title = title + (tied ? "（并列）" : "");
        _value = value.ToString("N0");
        Width = 210;
        Height = 70;
        Margin = new Padding(0, 6, 10, 6);
        DoubleBuffered = true;
        BackColor = Color.FromArgb(36, 63, 98);
        LoadSplash(championId);
    }

    private async void LoadSplash(int championId)
    {
        try
        {
            string? alias = FormMain.Globals.resLoading.GetChampionById(championId)?.Alias;
            if (string.IsNullOrEmpty(alias) || !alias.All(char.IsAsciiLetterOrDigit))
                return;
            var image = await Cache.GetOrDownloadImageAsync(
                $"https://ddragon.leagueoflegends.com/cdn/img/champion/splash/{alias}_0.jpg",
                "splash",
                $"{alias}_0.png"
            );
            if (IsDisposed || Disposing)
            {
                image?.Dispose();
                return;
            }
            _splash = image;
            Invalidate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Highlight splash: {ex.GetType().Name}");
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 0 || Height <= 0)
            return;
        if (_splash != null)
        {
            float scale = Math.Max(Width / (float)_splash.Width, Height / (float)_splash.Height);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float imageHeight = _splash.Height * scale;
            e.Graphics.DrawImage(_splash, (Width - _splash.Width * scale) / 2, 0, _splash.Width * scale, imageHeight);
        }
        using var shade = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(205, 0, 0, 0),
            Color.FromArgb(25, 0, 0, 0),
            0f
        );
        e.Graphics.FillRectangle(shade, ClientRectangle);
        using var titleFont = new Font("Microsoft YaHei UI", 9f);
        using var valueFont = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
        TextRenderer.DrawText(
            e.Graphics,
            _title,
            titleFont,
            new Rectangle(10, 8, Width - 20, 20),
            Color.Gainsboro,
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
        );
        TextRenderer.DrawText(
            e.Graphics,
            _value,
            valueFont,
            new Rectangle(10, 29, Width - 20, 30),
            Color.White,
            TextFormatFlags.SingleLine
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _splash?.Dispose();
            _splash = null;
        }
        base.Dispose(disposing);
    }
}
