using System.Drawing.Drawing2D;
using League.Clients;
using League.Infrastructure;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed class ProfileBackgroundPanel : Panel
{
    private Image? _artwork;
    private CancellationTokenSource? _request;
    private bool _closing;
    private readonly HashSet<Task> _work = new();
    private Task _cleanupWork = Task.CompletedTask;
    private Func<HttpClient?> _clientProvider = () => FormMain.Globals.lcuClient?.Client;
    private Func<string, CancellationToken, Task<JToken?>> _readMastery = async (puuid, token) =>
        (await ChampionMasteryService.Shared.GetAsync(puuid, token)).Data;
    private Func<HttpClient, string, int, Task<Image?>> _readArtwork = (client, path, skinId) =>
        Task.Run(
            () => new ImageCacheService(client).GetOrDownloadImageAsync(path, "profile-backgrounds", $"{skinId}.png"),
            CancellationToken.None
        );
    private readonly Dictionary<Control, (Color background, Color foreground, Color? link)> _colors = new();

    public ProfileBackgroundPanel()
    {
        DoubleBuffered = true;
    }

    internal void SetChampionArtwork(Image? image) => SetArtwork(image);

    public static int SelectSkin(JToken? profile, JToken? mastery)
    {
        if (int.TryParse(profile?["backgroundSkinId"]?.ToString(), out int skinId) && skinId > 0)
        {
            return skinId;
        }

        var champion = ProfileMasteryView.SelectTop(mastery).FirstOrDefault();
        int championId = (int?)champion?["championId"] ?? 0;
        return championId > 0 && championId <= int.MaxValue / 1000 ? championId * 1000 : 0;
    }

    public static string? FindSplash(JToken? champion, int skinId)
    {
        var skins = champion?["skins"] as JArray;
        var skin = skins?.OfType<JObject>().FirstOrDefault(s => (int?)s["id"] == skinId);
        skin ??= skins
            ?.OfType<JObject>()
            .SelectMany(s => (s["questSkinInfo"]?["tiers"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            .FirstOrDefault(s => (int?)s["id"] == skinId);
        var path = (string?)skin?["uncenteredSplashPath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            path = (string?)skin?["splashPath"];
        }

        return path?.StartsWith("/lol-game-data/assets/", StringComparison.OrdinalIgnoreCase) == true ? path : null;
    }

    public void LoadPlayer(string puuid)
    {
        if (_closing)
        {
            return;
        }
        RetireRequest();
        _request = new CancellationTokenSource();
        var token = _request.Token;
        SetArtwork(null);
        if (!_closing && !token.IsCancellationRequested)
        {
            _ = TrackWork(LoadPlayerAsync(puuid, token));
        }
    }

    private async Task LoadPlayerAsync(string puuid, CancellationToken token)
    {
        // Register work before a synchronous profile or cache result can update the parent controls.
        await Task.Yield();
        try
        {
            token.ThrowIfCancellationRequested();
            var client = _clientProvider();
            if (client == null || string.IsNullOrWhiteSpace(puuid))
            {
                return;
            }
            var service = new PlayerStatisticsService(() => client);
            int skinId = await ReadSkinAsync(service, puuid, token);
            if (skinId == 0 || _closing || token.IsCancellationRequested)
            {
                return;
            }
            var champion = await service.GetAsync($"/lol-game-data/assets/v1/champions/{skinId / 1000}.json", token);
            if (_closing || token.IsCancellationRequested)
            {
                return;
            }
            string? path = FindSplash(champion.Data, skinId);
            if (path == null)
            {
                return;
            }
            var image = await _readArtwork(client, path, skinId);
            if (_closing || token.IsCancellationRequested)
            {
                image?.Dispose();
                return;
            }
            SetArtwork(image);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A replaced player or closed background no longer owns this response.
        }
        catch (Exception ex)
        {
            // Background artwork is optional; the profile remains usable without it.
            AppLog.Shared.Warn(LogCategory.Ui, "Profile background", ex);
        }
    }

    private async Task<int> ReadSkinAsync(PlayerStatisticsService service, string puuid, CancellationToken token)
    {
        var profile = await service.GetAsync(
            "/lol-summoner/v1/summoner-profile?puuid=" + Uri.EscapeDataString(puuid),
            token
        );
        token.ThrowIfCancellationRequested();
        int skin = SelectSkin(profile.Data, null);
        if (skin == 0 && !_closing)
        {
            skin = SelectSkin(profile.Data, await _readMastery(puuid, token));
            token.ThrowIfCancellationRequested();
        }
        return skin;
    }

    private Task TrackWork(Task task)
    {
        lock (_work)
        {
            _work.Add(task);
        }
        _ = task.ContinueWith(
            completed =>
            {
                lock (_work)
                {
                    _work.Remove(completed);
                }
                _ = completed.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        return task;
    }

    private Task[] PendingWork()
    {
        lock (_work)
        {
            return _work.ToArray();
        }
    }

    private void RetireRequest()
    {
        var source = _request;
        _request = null;
        if (source == null)
        {
            return;
        }
        var pending = PendingWork();
        var cancellation = source.CancelAsync();
        TrackWork(
            Task.WhenAll(pending.Append(cancellation))
                .ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        source.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                )
        );
    }

    private void SetArtwork(Image? image)
    {
        if (_closing)
        {
            image?.Dispose();
            return;
        }
        var old = _artwork;
        _artwork = image;
        if (!ReferenceEquals(old, image))
        {
            old?.Dispose();
        }
        UpdateChildColors(this, image != null);
        Invalidate(true);
    }

    private void UpdateChildColors(Control parent, bool artworkVisible)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Label or Panel or PictureBox or RankEmblem)
            {
                if (!_colors.ContainsKey(child))
                {
                    _colors[child] = (child.BackColor, child.ForeColor, (child as LinkLabel)?.LinkColor);
                    // Profile sections may be rebuilt repeatedly; do not retain their disposed controls.
                    child.Disposed += ForgetColors;
                }
                var colors = _colors[child];
                child.BackColor = artworkVisible ? Color.Transparent : colors.background;
                child.ForeColor = artworkVisible ? Color.WhiteSmoke : colors.foreground;
                if (child is LinkLabel link)
                {
                    link.LinkColor = artworkVisible ? Color.White : colors.link!.Value;
                }
            }
            UpdateChildColors(child, artworkVisible);
        }
    }

    private void ForgetColors(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            _colors.Remove(control);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (_artwork == null || Width <= 0 || Height <= 0)
        {
            return;
        }

        float scale = Math.Max(Width / (float)_artwork.Width, Height / (float)_artwork.Height);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(
            _artwork,
            (Width - _artwork.Width * scale) / 2,
            0,
            _artwork.Width * scale,
            _artwork.Height * scale
        );
        using var shade = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(205, 10, 19, 30),
            Color.FromArgb(105, 10, 19, 30),
            0f
        );
        e.Graphics.FillRectangle(shade, ClientRectangle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            RetireRequest();
            _cleanupWork = Task.WhenAll(PendingWork());
            _artwork?.Dispose();
            _artwork = null;
            foreach (var control in _colors.Keys)
            {
                control.Disposed -= ForgetColors;
            }
            _colors.Clear();
        }
        base.Dispose(disposing);
    }
}
