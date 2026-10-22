using System.Diagnostics;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Infrastructure
{
    public class AugmentResourceLoader
    {
        // The client ships the large art for only a few dozen augments, so the rest is read from CommunityDragon's
        // copy of the game files. That request goes through its own client: the LCU one carries the client's
        // credentials, which must not reach another host.
        private static readonly Lazy<HttpClient> PublicHttp = new(() =>
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            // CommunityDragon refuses requests without a user agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LeagueHistory");
            return client;
        });

        private readonly HttpClient _client;
        private readonly ImageCacheService _imageCache;
        private readonly ImageCacheService _publicArt;
        private readonly string _publicHost;

        // Augments with no art anywhere are remembered, so drawing their icon again does not ask twice more.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> _missingArt = new();

        private List<AugmentInfo> _augments = new();

        public AugmentResourceLoader(
            HttpClient client,
            ImageCacheService imageCache,
            ImageCacheService? publicArt = null,
            string publicHost = "https://raw.communitydragon.org/latest"
        )
        {
            _client = client;
            _imageCache = imageCache;
            _publicArt = publicArt ?? new ImageCacheService(PublicHttp.Value);
            _publicHost = publicHost.TrimEnd('/');
        }

        public async Task LoadAsync()
        {
            try
            {
                var response = await _client.GetAsync("/lol-game-data/assets/v1/cherry-augments.json");
                if (!response.IsSuccessStatusCode)
                {
                    _augments = new();
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                var jsonArray = JArray.Parse(json);
                _augments = ParseAugmentsFromJsonArray(jsonArray);
            }
            catch (Exception ex)
            {
                AppLog.Shared.Warn(LogCategory.Data, "海克斯符文加载异常", ex);
                _augments = new();
            }
        }

        private List<AugmentInfo> ParseAugmentsFromJsonArray(JArray jsonArray)
        {
            var augments = new List<AugmentInfo>();
            var descriptionMap = AugmentDescriptionRepository.GetAll();

            // 优先处理 ID >= 1000 的海克斯符文
            foreach (var item in jsonArray.Where(i => (i["id"]?.Value<int>() ?? 0) >= 1000))
            {
                ProcessAugmentItem(item, augments, descriptionMap);
            }

            foreach (var item in jsonArray.Where(i => (i["id"]?.Value<int>() ?? 0) < 1000))
            {
                ProcessAugmentItem(item, augments, descriptionMap);
            }

            return augments;
        }

        private void ProcessAugmentItem(JToken item, List<AugmentInfo> augments, Dictionary<string, string> descMap)
        {
            int id = item["id"]?.Value<int>() ?? 0;
            if (id <= 0)
            {
                return;
            }

            string name = item["nameTRA"]?.ToString() ?? item["name"]?.ToString() ?? $"Augment {id}";
            string description = item["description"]?.ToString() ?? item["desc"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(description) && descMap.TryGetValue(name, out var mapped))
            {
                description = mapped;
            }

            string iconPath = item["augmentSmallIconPath"]?.ToString() ?? "";

            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(iconPath))
            {
                augments.Add(
                    new AugmentInfo
                    {
                        Id = id,
                        Name = name,
                        Description = description,
                        IconPath = iconPath,
                        Rarity = item["rarity"]?.ToString() ?? "Unknown",
                    }
                );
            }
        }

        public AugmentInfo? GetById(int id) => _augments.FirstOrDefault(a => a.Id == id);

        /// <summary>
        /// Replaces the hand-written descriptions with the game's own, numbers filled, for every augment the game data
        /// covers; the others keep theirs. Called after <see cref="LoadAsync"/>, in the background: until it finishes,
        /// and if it fails, the hand-written ones stand.
        /// </summary>
        /// <param name="source">
        /// Makes the reader for the client's patch, or for the bundled version file's when the client does not say.
        /// </param>
        public async Task LoadTextsAsync(Func<string?, AugmentTextData> source, CancellationToken token)
        {
            try
            {
                string? patch = await ClientPatchAsync(token).ConfigureAwait(false);
                var texts = await source(patch).LoadAsync(token).ConfigureAwait(false);
                foreach (var augment in _augments)
                {
                    if (texts.TryGetValue(augment.Id, out var text) && text.Tooltip.Length > 0)
                    {
                        augment.Description = text.Tooltip;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The resources were replaced by a newer load; its own call reads the texts.
            }
            catch (Exception ex)
            {
                // Optional enrichment: without network or on a changed format the hand-written texts stay usable.
                AppLog.Shared.Warn(LogCategory.Data, "海克斯说明加载失败", ex);
            }
        }

        // The bundled version file follows Data Dragon, which publishes a patch days after the client has it, so the
        // newest augments were missing. The client's own game version ("16.21.723.1234") is asked for instead.
        private async Task<string?> ClientPatchAsync(CancellationToken token)
        {
            try
            {
                using var response = await _client.GetAsync("/lol-patch/v1/game-version", token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string version = (await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)).Trim('"', ' ');
                var match = System.Text.RegularExpressions.Regex.Match(version, @"^(\d+\.\d+)\.");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                // Without the client the version file decides; the texts may then miss the newest augments.
                AppLog.Shared.Warn(LogCategory.Data, "读取客户端版本失败", ex);
                return null;
            }
        }

        public async Task<(Image? image, string name, string description)> GetInfoAsync(int id)
        {
            var augment = GetById(id);
            if (augment == null)
            {
                return (null, $"Augment {id}", "");
            }

            string rarityCn = RarityToChinese(augment.Rarity);
            string cleanDesc = CleanDescription(augment.Description);
            string fullDesc = $"[{rarityCn}] {cleanDesc}";

            if (string.IsNullOrEmpty(augment.IconPath))
            {
                return (null, augment.Name, fullDesc);
            }

            var art = await LoadArtAsync(augment);
            if (art != null)
            {
                return (art, augment.Name, fullDesc);
            }

            using var image = await _imageCache.GetOrDownloadImageAsync(augment.IconPath, "augments", $"{id}.png", id);

            var displayImage =
                image == null ? null : League.UIHelpers.AugmentIconRenderer.Render(image, augment.Rarity);
            return (displayImage, augment.Name, fullDesc);
        }

        private const int ArtSize = 64;

        // The game shows an augment as its full-colour art, transparent and without a frame, its rarity in its own
        // colours. The catalog only names the small icon, a white mask meant to be tinted, so the art is asked for at
        // the same path with the large suffix, from the client first and then from the public copy; the tinted mask
        // stays as the fallback. The art is 256 pixels and is drawn at 32 to 48, so it is reduced once here rather
        // than kept at full size in every cache.
        private async Task<Image?> LoadArtAsync(AugmentInfo augment)
        {
            string? path = LargeArtPath(augment.IconPath);
            if (path == null || _missingArt.ContainsKey(augment.Id))
            {
                return null;
            }

            string? publicUrl = PublicArtUrl(path, _publicHost);
            using var art =
                await _imageCache.GetOrDownloadImageAsync(path, "augments-large", "", augment.Id)
                ?? (
                    publicUrl == null
                        ? null
                        : await _publicArt.GetOrDownloadImageAsync(publicUrl, "augments-large", "", augment.Id)
                );
            if (art == null)
            {
                _missingArt[augment.Id] = true;
                return null;
            }

            var reduced = new Bitmap(ArtSize, ArtSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(reduced);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            graphics.DrawImage(art, new Rectangle(0, 0, ArtSize, ArtSize));
            return reduced;
        }

        /// <summary>
        /// Where CommunityDragon keeps a file the client serves under /lol-game-data/assets/: the game's own path,
        /// lower-cased. Null for any other path.
        /// </summary>
        public static string? PublicArtUrl(string? clientPath, string host)
        {
            const string prefix = "/lol-game-data/assets/";
            return clientPath?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true
                ? $"{host.TrimEnd('/')}/game/{clientPath[prefix.Length..].ToLowerInvariant()}"
                : null;
        }

        /// <summary>The large art beside a small augment icon, or null when the path does not name a small icon.</summary>
        public static string? LargeArtPath(string? smallIconPath)
        {
            const string small = "_small.";
            int at = smallIconPath?.LastIndexOf(small, StringComparison.OrdinalIgnoreCase) ?? -1;
            return at < 0 ? null : smallIconPath![..at] + "_large." + smallIconPath[(at + small.Length)..];
        }

        private static string CleanDescription(string desc) =>
            string.IsNullOrEmpty(desc) ? "" : desc.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();

        private static string RarityToChinese(string? rarity) =>
            rarity?.ToLower() switch
            {
                "ksilver" => "白银",
                "kgold" => "黄金",
                "kprismatic" => "棱彩",
                _ => "未知",
            };
    }
}
