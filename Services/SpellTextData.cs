using Newtonsoft.Json;

namespace League.Services;

/// <summary>
/// The game's Chinese tooltips for champion spells, from CommunityDragon's extraction of the string table for the
/// client's patch. The table holds every text of the game (about 30 MB); it is read as a stream and only the tooltip
/// entries are kept, cached per patch, so the full table is never held in memory. Runs off the UI thread.
/// </summary>
public sealed class SpellTextData
{
    // Bumped whenever which entries are kept changes, so caches from older rules are not reused.
    public const string RulesVersion = "1";

    private static readonly HttpClient SharedHttp = CreateClient();
    private readonly HttpClient _http;
    private readonly string _folder;
    private readonly string _patch;
    private readonly string _host;

    public SpellTextData(
        string? folder = null,
        string? patch = null,
        HttpClient? http = null,
        string host = "https://raw.communitydragon.org"
    )
    {
        _folder =
            folder
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "League",
                "Cache",
                "cdragon-text"
            );
        _patch = patch ?? ChampionSpellData.ClientPatch();
        _http = http ?? SharedHttp;
        _host = host.TrimEnd('/');
    }

    /// <summary>Tooltips by lower-case string table key.</summary>
    public async Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken token)
    {
        string path = Path.Combine(_folder, $"{_patch}-r{RulesVersion}.json");
        if (File.Exists(path))
        {
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(
                    await File.ReadAllTextAsync(path, token).ConfigureAwait(false)
                ) ?? [];
        }

        string url = $"{_host}/{_patch}/game/zh_cn/data/menu/en_us/lol.stringtable.json";
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        Dictionary<string, string> texts;
        await using (var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
        {
            await using var reader = new JsonTextReader(new StreamReader(stream));
            texts = await ReadTooltipsAsync(reader, token).ConfigureAwait(false);
        }

        Directory.CreateDirectory(_folder);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonConvert.SerializeObject(texts), token).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
        return texts;
    }

    /// <summary>
    /// Spell tooltips are keyed "spell_annieq_tooltip", "buff_ahrip_tooltip" or "game_character_passive…_jax"; the
    /// generated variants carry the tooltip a second time with headers, and TFT has its own.
    /// </summary>
    public static bool Keeps(string key) =>
        (
            key.Contains("tooltip", StringComparison.Ordinal)
            && !key.StartsWith("generatedtip_", StringComparison.Ordinal)
            && !key.StartsWith("tft", StringComparison.Ordinal)
        ) || key.StartsWith("game_character_passive", StringComparison.Ordinal);

    /// <summary>The kept entries of a string table's "entries" object, reading the table once from start to end.</summary>
    public static Task<Dictionary<string, string>> ReadTooltipsAsync(JsonReader reader, CancellationToken token) =>
        ReadEntriesAsync(reader, Keeps, token);

    /// <summary>The entries of a string table whose lower-case key passes <paramref name="keep"/>.</summary>
    public static async Task<Dictionary<string, string>> ReadEntriesAsync(
        JsonReader reader,
        Func<string, bool> keep,
        CancellationToken token
    )
    {
        var texts = new Dictionary<string, string>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (reader.TokenType != JsonToken.PropertyName || (string?)reader.Value != "entries" || reader.Depth != 1)
            {
                continue;
            }

            await reader.ReadAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false) && reader.TokenType == JsonToken.PropertyName)
            {
                string key = ((string)reader.Value!).ToLowerInvariant();
                string? value = await reader.ReadAsStringAsync(token).ConfigureAwait(false);
                if (value != null && keep(key))
                {
                    texts[key] = value;
                }
            }

            break;
        }

        return texts;
    }

    private static HttpClient CreateClient()
    {
        // The table is large; a slow connection gets more time than the per-champion reads.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        // CommunityDragon refuses requests without a user agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LeagueHistory");
        return client;
    }
}
