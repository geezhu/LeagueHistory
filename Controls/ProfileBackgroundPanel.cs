using League.Clients;
using League.Infrastructure;
using Newtonsoft.Json.Linq;
using System.Drawing.Drawing2D;

namespace League.Controls;

internal sealed class ProfileBackgroundPanel : Panel
{
    private Image? _artwork;
    private CancellationTokenSource? _request;
    private readonly Dictionary<Control, (Color background, Color foreground, Color? link)> _colors = new();

    public ProfileBackgroundPanel() { DoubleBuffered = true; }
    internal void SetChampionArtwork(Image? image)=>SetArtwork(image);

    public static int SelectSkin(JToken? profile, JToken? mastery)
    {
        if (int.TryParse(profile?["backgroundSkinId"]?.ToString(), out int skinId) && skinId > 0) return skinId;
        var champion = ProfileMasteryView.SelectTop(mastery).FirstOrDefault();
        int championId = (int?)champion?["championId"] ?? 0;
        return championId > 0 && championId <= int.MaxValue / 1000 ? championId * 1000 : 0;
    }

    public static string? FindSplash(JToken? champion, int skinId)
    {
        var skins = champion?["skins"] as JArray;
        var skin = skins?.OfType<JObject>().FirstOrDefault(s => (int?)s["id"] == skinId);
        skin ??= skins?.OfType<JObject>().SelectMany(s => (s["questSkinInfo"]?["tiers"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            .FirstOrDefault(s => (int?)s["id"] == skinId);
        var path = (string?)skin?["uncenteredSplashPath"];
        if (string.IsNullOrWhiteSpace(path)) path = (string?)skin?["splashPath"];
        return path?.StartsWith("/lol-game-data/assets/", StringComparison.OrdinalIgnoreCase) == true ? path : null;
    }

    public async void LoadPlayer(string puuid)
    {
        _request?.Cancel(); _request?.Dispose();
        var request = _request = new CancellationTokenSource();
        SetArtwork(null);
        try
        {
            var client = FormMain.Globals.lcuClient.Client;
            if (client == null || string.IsNullOrWhiteSpace(puuid)) return;
            var service = new PlayerStatisticsService(() => client);
            var profile = await service.GetAsync("/lol-summoner/v1/summoner-profile?puuid=" + Uri.EscapeDataString(puuid), request.Token);
            if (IsDisposed || request.IsCancellationRequested) return;
            int skinId = SelectSkin(profile.Data, null);
            if (skinId == 0)
            {
                var mastery = await ChampionMasteryService.Shared.GetAsync(puuid, request.Token);
                if (IsDisposed || request.IsCancellationRequested) return;
                skinId = SelectSkin(profile.Data, mastery.Data);
            }
            if (skinId == 0) return;
            var champion = await service.GetAsync($"/lol-game-data/assets/v1/champions/{skinId / 1000}.json", request.Token);
            if (IsDisposed || request.IsCancellationRequested) return;
            string? path = FindSplash(champion.Data, skinId);
            if (path == null) return;
            var image = await new ImageCacheService(client).GetOrDownloadImageAsync(path, "profile-backgrounds", $"{skinId}.png");
            if (IsDisposed || request.IsCancellationRequested) { image?.Dispose(); return; }
            SetArtwork(image);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Profile background: {ex.GetType().Name}"); }
    }

    private void SetArtwork(Image? image)
    {
        var old = _artwork; _artwork = image; old?.Dispose();
        void UpdateColors(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is Label or Panel or PictureBox or RankEmblem)
                {
                    if (!_colors.ContainsKey(child)) _colors[child] = (child.BackColor, child.ForeColor, (child as LinkLabel)?.LinkColor);
                    var colors = _colors[child];
                    child.BackColor = image == null ? colors.background : Color.Transparent;
                    child.ForeColor = image == null ? colors.foreground : Color.WhiteSmoke;
                    if (child is LinkLabel link) link.LinkColor = image == null ? colors.link!.Value : Color.White;
                }
                UpdateColors(child);
            }
        }
        UpdateColors(this);
        Invalidate(true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (_artwork == null || Width <= 0 || Height <= 0) return;
        float scale = Math.Max(Width / (float)_artwork.Width, Height / (float)_artwork.Height);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_artwork, (Width - _artwork.Width * scale) / 2, 0, _artwork.Width * scale, _artwork.Height * scale);
        using var shade = new LinearGradientBrush(ClientRectangle, Color.FromArgb(205, 10, 19, 30), Color.FromArgb(105, 10, 19, 30), 0f);
        e.Graphics.FillRectangle(shade, ClientRectangle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _request?.Cancel(); _request?.Dispose(); _artwork?.Dispose(); _artwork = null; _colors.Clear(); }
        base.Dispose(disposing);
    }
}
