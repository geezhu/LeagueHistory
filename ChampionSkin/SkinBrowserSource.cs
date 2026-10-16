using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;
using static League.FormMain;

namespace League.ChampionSkin
{
    /// <summary>What the skin browser reads; images it returns belong to the caller, which disposes them.</summary>
    public interface ISkinBrowserSource
    {
        Task<List<ChampionSkinCardModel>> LoadAsync(
            IReadOnlyDictionary<int, int> preferred,
            CancellationToken cancellationToken
        );

        Task<Image?> LoadIconAsync(ChampionSkinCardModel champion, CancellationToken cancellationToken);

        Task<Image?> LoadSkinAsync(
            ChampionSkinCardModel champion,
            SkinThumbItem skin,
            SkinImageKind kind,
            CancellationToken cancellationToken
        );
    }

    /// <summary>The running client: the champion list, the account's skins and their images.</summary>
    public sealed class LcuSkinBrowserSource : ISkinBrowserSource
    {
        private readonly SkinImageStore _images = new(() => Globals.lcuClient.Client);
        private readonly Dictionary<int, JObject?> _championData = new();

        public async Task<List<ChampionSkinCardModel>> LoadAsync(
            IReadOnlyDictionary<int, int> preferred,
            CancellationToken cancellationToken
        )
        {
            var champions = Globals.resLoading.ChampionManager;
            await champions.InitializeAsync(forceRefresh: false);
            return SkinCatalog.Build(champions.AllChampions, await LoadOwnedAsync(cancellationToken), preferred);
        }

        // Without a client connection the browser still lists champions, each with only the default skin.
        private static async Task<Dictionary<int, List<SkinThumbItem>>> LoadOwnedAsync(
            CancellationToken cancellationToken
        )
        {
            var http = Globals.lcuClient.Client;
            if (http == null)
            {
                AppLog.Shared.Warn(LogCategory.Data, "皮肤浏览：客户端未连接，只显示默认皮肤");
                return new();
            }

            try
            {
                var summoner = await Globals.lcuClient.GetCurrentSummoner(cancellationToken);
                string? summonerId = summoner?["summonerId"]?.ToString();
                if (string.IsNullOrEmpty(summonerId))
                {
                    AppLog.Shared.Warn(LogCategory.Data, "皮肤浏览：读不到当前召唤师，只显示默认皮肤");
                    return new();
                }

                return await SkinCatalog.LoadOwnedAsync(http, summonerId, cancellationToken);
            }
            catch (Exception e) when (e is HttpRequestException or Newtonsoft.Json.JsonException)
            {
                AppLog.Shared.Warn(LogCategory.Data, "皮肤浏览：读取已拥有皮肤失败", e);
                return new();
            }
        }

        // The shared resource cache reads Assets/champion first and owns its image; the browser keeps its own copy.
        public async Task<Image?> LoadIconAsync(ChampionSkinCardModel champion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shared = await Globals.resLoading.GetChampionIconAsync(champion.ChampionId);
            return shared == null ? null : new Bitmap(shared);
        }

        public async Task<Image?> LoadSkinAsync(
            ChampionSkinCardModel champion,
            SkinThumbItem skin,
            SkinImageKind kind,
            CancellationToken cancellationToken
        )
        {
            if (
                (kind == SkinImageKind.Tile ? skin.TilePath : skin.SplashPath) == null
                && !SkinImageStore.IsValid(_images.FilePath(skin.SkinId, kind))
            )
            {
                await FillPathsAsync(champion.ChampionId, skin, cancellationToken);
            }

            string? path = await _images.EnsureAsync(champion.ChampionId, skin, kind, cancellationToken);
            return path == null ? null : await DecodeAsync(path, cancellationToken);
        }

        /// <summary>
        /// A skin without image paths (the default skin of a champion whose skins are owned but who is not) takes
        /// them from the client's champion data; the guessed v1 paths do not exist for every skin. The data is read
        /// once per champion, including when it fails.
        /// </summary>
        private async Task FillPathsAsync(int championId, SkinThumbItem skin, CancellationToken cancellationToken)
        {
            if (!_championData.TryGetValue(championId, out var data))
            {
                data = await ReadChampionAsync(championId, cancellationToken);
                _championData[championId] = data;
            }

            if (data != null)
            {
                SkinCatalog.ApplyChampionPaths(data, skin);
            }
        }

        private static async Task<JObject?> ReadChampionAsync(int championId, CancellationToken cancellationToken)
        {
            var http = Globals.lcuClient.Client;
            if (http == null)
            {
                return null;
            }

            try
            {
                using var response = await http.GetAsync(
                    $"/lol-game-data/assets/v1/champions/{championId}.json",
                    cancellationToken
                );
                if (!response.IsSuccessStatusCode)
                {
                    AppLog.Shared.Warn(
                        LogCategory.Data,
                        $"皮肤浏览：英雄 {championId} 资料读取失败（{(int)response.StatusCode}）"
                    );
                    return null;
                }

                return JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            }
            catch (Exception e) when (e is HttpRequestException or Newtonsoft.Json.JsonException)
            {
                AppLog.Shared.Warn(LogCategory.Data, $"皮肤浏览：英雄 {championId} 资料读取失败", e);
                return null;
            }
        }

        // Decoding happens off the UI thread; the bitmap is copied so the file is not kept open.
        private static Task<Image?> DecodeAsync(string path, CancellationToken cancellationToken) =>
            Task.Run(
                () =>
                {
                    if (!SkinImageStore.IsValid(path))
                    {
                        return null;
                    }

                    try
                    {
                        using var stream = new MemoryStream(File.ReadAllBytes(path));
                        using var image = Image.FromStream(stream);
                        return (Image?)new Bitmap(image);
                    }
                    catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
                    {
                        AppLog.Shared.Warn(LogCategory.Data, $"皮肤浏览：图片解码失败 {Path.GetFileName(path)}", e);
                        return null;
                    }
                },
                cancellationToken
            );
    }
}
