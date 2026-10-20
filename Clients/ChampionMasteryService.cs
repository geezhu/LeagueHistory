using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace League.Clients
{
    public class ChampionMasteryService
    {
        private readonly LcuClient _client;

        public ChampionMasteryService(LcuClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// 原始熟练度列表（按点数降序）
        /// </summary>
        public async Task<JArray> GetChampionMasteryAsync(string puuid)
        {
            if (string.IsNullOrEmpty(puuid)) return new JArray();

            try
            {
                var response = await _client.GetAsync($"/lol-champion-mastery/v1/{puuid}/champion-mastery");
                if (!response.IsSuccessStatusCode) return new JArray();

                var content = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(content)) return new JArray();

                return JArray.Parse(content);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChampionMasteryService] {ex.Message}");
                return new JArray();
            }
        }
    }
}
