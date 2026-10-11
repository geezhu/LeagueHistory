using System.Drawing.Drawing2D;
using League.Infrastructure;

namespace League.Controls;

internal sealed class MasteryPoster : PictureBox
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };

    // The download cache lives for the application; each poster owns the image copy it receives.
    private static readonly ImageCacheService Cache = new(Client);
    private readonly int _rank;

    public MasteryPoster(int rank, Image? fallback)
    {
        _rank = rank;
        Image = fallback == null ? null : new Bitmap(fallback);
        BackColor = Color.FromArgb(29, 38, 48);
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
    }

    public async Task LoadArtworkAsync(int championId)
    {
        try
        {
            string? alias = FormMain.Globals.resLoading.GetChampionById(championId)?.Alias;
            if (string.IsNullOrEmpty(alias) || !alias.All(char.IsAsciiLetterOrDigit))
            {
                return;
            }

            var image = await Cache.GetOrDownloadImageAsync(
                $"https://ddragon.leagueoflegends.com/cdn/img/champion/loading/{alias}_0.jpg",
                "mastery-posters",
                $"{alias}_0.png"
            );
            if (IsDisposed || Disposing)
            {
                image?.Dispose();
                return;
            }
            if (image == null)
            {
                return;
            }

            var previous = Image;
            Image = image;
            previous?.Dispose();
            Invalidate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Mastery poster: {ex.GetType().Name}");
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        var g = e.Graphics;
        g.Clear(BackColor);
        if (Image != null)
        {
            float scale = Math.Max(Width / (float)Image.Width, Height / (float)Image.Height);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(Image, (Width - Image.Width * scale) / 2, 0, Image.Width * scale, Image.Height * scale);
        }
        var clear = Color.FromArgb(0, 12, 20, 30);
        using var shade = new LinearGradientBrush(ClientRectangle, clear, Color.FromArgb(245, 12, 20, 30), 90f)
        {
            WrapMode = WrapMode.TileFlipXY,
            InterpolationColors = new ColorBlend
            {
                Colors = new[] { clear, clear, Color.FromArgb(245, 12, 20, 30) },
                Positions = new[] { 0f, 1f / 3f, 1f },
            },
        };
        g.FillRectangle(shade, ClientRectangle);
        if (_rank > 3)
        {
            return;
        }

        Color accent =
            _rank == 1 ? Color.FromArgb(207, 168, 78)
            : _rank == 2 ? Color.FromArgb(145, 166, 186)
            : Color.FromArgb(185, 125, 85);
        using var badge = new SolidBrush(accent);
        g.FillPolygon(
            badge,
            new[] { new Point(10, 0), new Point(38, 0), new Point(38, 30), new Point(24, 39), new Point(10, 30) }
        );
        using var font = new Font("Segoe UI", 13f, FontStyle.Bold);
        TextRenderer.DrawText(
            g,
            _rank.ToString(),
            font,
            new Rectangle(10, 2, 28, 28),
            Color.White,
            TextFormatFlags.HorizontalCenter
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var image = Image;
            Image = null;
            image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
