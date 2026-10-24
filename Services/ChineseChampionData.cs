using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>
/// A champion's client data (/lol-game-data/assets/v1/champions/{id}.json) in Simplified Chinese, from
/// CommunityDragon's copy of the client for the client's patch. Control and slow recognition read the descriptions for
/// Chinese words, and the client describes abilities in its own language: an English client on the Japanese server
/// writes "roots" and "stunned". Kept in memory per champion; runs off the UI thread.
/// </summary>
public sealed class ChineseChampionData
{
    private static readonly Lazy<HttpClient> SharedHttp = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // CommunityDragon refuses requests without a user agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LeagueHistory");
        return client;
    });

    private readonly ConcurrentDictionary<int, JObject> _champions = new();
    private readonly HttpClient _http;
    private readonly string _patch;
    private readonly string _host;

    public ChineseChampionData(
        string? patch = null,
        HttpClient? http = null,
        string host = "https://raw.communitydragon.org"
    )
    {
        _patch = patch ?? ChampionSpellData.ClientPatch();
        _http = http ?? SharedHttp.Value;
        _host = host.TrimEnd('/');
    }

    /// <summary>The champion's data in Chinese, or null when it cannot be read and the client's own must stand in.</summary>
    public async Task<JObject?> LoadAsync(int champion, CancellationToken token)
    {
        if (_champions.TryGetValue(champion, out var cached))
        {
            return cached;
        }

        try
        {
            string text = await _http
                .GetStringAsync(
                    $"{_host}/{_patch}/plugins/rcp-be-lol-game-data/global/zh_cn/v1/champions/{champion}.json",
                    token
                )
                .ConfigureAwait(false);
            var data = JObject.Parse(text);
            if ((int?)data["id"] != champion)
            {
                AppLog.Shared.Warn(LogCategory.Data, $"中文英雄数据与请求的英雄不符：{champion}");
                return null;
            }

            _champions[champion] = data;
            return data;
        }
        catch (Exception e)
            when (e is HttpRequestException or JsonException
                || (e is OperationCanceledException && !token.IsCancellationRequested)
            )
        {
            // The client's own data stands in, which a Chinese client still reads correctly.
            AppLog.Shared.Warn(LogCategory.Data, $"中文英雄数据读取失败：{champion}", e);
            return null;
        }
    }
}
