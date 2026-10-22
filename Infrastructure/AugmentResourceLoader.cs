using System.Diagnostics;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Infrastructure
{
    public class AugmentResourceLoader
    {
        private readonly HttpClient _client;
        private readonly ImageCacheService _imageCache;

        private List<AugmentInfo> _augments = new();

        public AugmentResourceLoader(HttpClient client, ImageCacheService imageCache)
        {
            _client = client;
            _imageCache = imageCache;
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
        // the same path with the large suffix; the tinted mask stays as the fallback. The art is 256 pixels and is
        // drawn at 32 to 48, so it is reduced once here rather than kept at full size in every cache.
        private async Task<Image?> LoadArtAsync(AugmentInfo augment)
        {
            string? path = LargeArtPath(augment.IconPath);
            if (path == null)
            {
                return null;
            }

            using var art = await _imageCache.GetOrDownloadImageAsync(path, "augments-large", "", augment.Id);
            if (art == null)
            {
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
