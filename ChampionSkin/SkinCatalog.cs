using League.Models;
using League.PrimaryElection;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.ChampionSkin
{
    /// <summary>
    /// The skin browser's data: every champion from the champion list, with the skins the account owns according to
    /// the client's skins-minimal inventory. Champions without an inventory entry still get their default skin.
    /// </summary>
    public static class SkinCatalog
    {
        public static List<ChampionSkinCardModel> Build(
            IEnumerable<PrimaryChampion> champions,
            IReadOnlyDictionary<int, List<SkinThumbItem>> owned,
            IReadOnlyDictionary<int, int> preferred
        ) => champions.OrderBy(c => c.Name).Select(c => BuildChampion(c, owned, preferred)).ToList();

        // A preference names one of the champion's owned non-default skins; anything else means the default.
        private static ChampionSkinCardModel BuildChampion(
            PrimaryChampion champion,
            IReadOnlyDictionary<int, List<SkinThumbItem>> owned,
            IReadOnlyDictionary<int, int> preferred
        )
        {
            int defaultSkinId = champion.Id * 1000;
            var skins = owned.TryGetValue(champion.Id, out var list) ? list.ToList() : new List<SkinThumbItem>();
            if (!skins.Any(s => s.IsDefault))
            {
                skins.Add(
                    new SkinThumbItem
                    {
                        SkinId = defaultSkinId,
                        Name = "默认",
                        IsBase = true,
                    }
                );
            }

            int preference = preferred.GetValueOrDefault(champion.Id);
            return new ChampionSkinCardModel
            {
                ChampionId = champion.Id,
                Name = champion.Name,
                Title = champion.Title,
                Alias = champion.Alias,
                IsOwned = owned.ContainsKey(champion.Id),
                DefaultSkinId = defaultSkinId,
                PreferredSkinId = skins.Any(s => !s.IsDefault && s.SkinId == preference) ? preference : 0,
                Positions = champion.GetPositions(),
                Skins = skins.OrderBy(s => s.IsDefault ? 0 : 1).ThenBy(s => s.SkinId).ToList(),
            };
        }

        /// <summary>The owned skins by champion; empty when the client or the summoner is not available.</summary>
        public static async Task<Dictionary<int, List<SkinThumbItem>>> LoadOwnedAsync(
            HttpClient http,
            string summonerId,
            CancellationToken cancellationToken
        )
        {
            var result = new Dictionary<int, List<SkinThumbItem>>();
            using var response = await http.GetAsync(
                    $"lol-champions/v1/inventories/{summonerId}/skins-minimal",
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Shared.Warn(LogCategory.Data, $"皮肤浏览：读取已拥有皮肤失败（{(int)response.StatusCode}）");
                return result;
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            foreach (var entry in JArray.Parse(body).OfType<JObject>())
            {
                AddOwnedSkin(result, entry);
            }

            return result;
        }

        /// <summary>
        /// skins-minimal 的一项：只收已拥有的皮肤，缺英雄 ID 时由皮肤 ID 推出，同一皮肤只记一次。
        /// </summary>
        public static void AddOwnedSkin(Dictionary<int, List<SkinThumbItem>> result, JObject entry)
        {
            if (!(entry["ownership"]?["owned"]?.Value<bool>() ?? false))
            {
                return;
            }

            int skinId = entry["id"]?.Value<int>() ?? 0;
            int championId = entry["championId"]?.Value<int>() ?? 0;
            if (championId <= 0 && skinId > 0)
            {
                championId = skinId / 1000;
            }

            if (championId <= 0)
            {
                return;
            }

            if (!result.TryGetValue(championId, out var list))
            {
                list = new List<SkinThumbItem>();
                result[championId] = list;
            }

            if (list.Any(s => s.SkinId == skinId))
            {
                return;
            }

            bool isBase = entry["isBase"]?.Value<bool>() ?? (skinId % 1000 == 0);
            list.Add(
                new SkinThumbItem
                {
                    SkinId = skinId,
                    Name = entry["name"]?.ToString() ?? (isBase ? "默认" : skinId.ToString()),
                    IsBase = isBase,
                    TilePath = Trimmed(entry["tilePath"]?.ToString()),
                    SplashPath = Trimmed(entry["splashPath"]?.ToString()),
                }
            );
        }

        /// <summary>
        /// Fills the skin's missing image paths from the client's champion data
        /// (/lol-game-data/assets/v1/champions/{id}.json), whose skins list every skin with its real asset paths.
        /// </summary>
        public static void ApplyChampionPaths(JObject champion, SkinThumbItem skin)
        {
            var entry = (champion["skins"] as JArray)
                ?.OfType<JObject>()
                .FirstOrDefault(s => s["id"]?.Type == JTokenType.Integer && (int)s["id"]! == skin.SkinId);
            if (entry == null)
            {
                return;
            }

            skin.TilePath ??= Trimmed(entry["tilePath"]?.ToString());
            skin.SplashPath ??= Trimmed(entry["splashPath"]?.ToString());
        }

        private static string? Trimmed(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Trim();
    }
}
