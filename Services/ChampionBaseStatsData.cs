using System.Text.RegularExpressions;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>
/// Downloads Data Dragon's champion list for the client's version and keeps it per version. The client's own
/// champion resources carry no base stats; this one file has every champion's health and resistances.
/// </summary>
public sealed class ChampionBaseStatsData
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _folder;
    private readonly string? _version;

    public ChampionBaseStatsData(string? folder = null, string? version = null)
    {
        _folder =
            folder
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "League",
                "Cache",
                "ddragon-stats"
            );
        _version = version ?? ClientVersion();
    }

    /// <summary>The stats by champion id; empty when the client version is unknown.</summary>
    public async Task<Dictionary<int, ChampionBaseStats>> LoadAsync(CancellationToken token)
    {
        if (_version == null)
        {
            return [];
        }

        string path = Path.Combine(_folder, _version + ".json");
        if (File.Exists(path))
        {
            return ChampionBaseStats.ParseAll(
                JObject.Parse(await File.ReadAllTextAsync(path, token).ConfigureAwait(false))
            );
        }

        string url = $"https://ddragon.leagueoflegends.com/cdn/{_version}/data/zh_CN/champion.json";
        string text = await Http.GetStringAsync(url, token).ConfigureAwait(false);
        var stats = ChampionBaseStats.ParseAll(JObject.Parse(text));
        Directory.CreateDirectory(_folder);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, text, token).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
        return stats;
    }

    // Data Dragon folders are full versions ("16.20.1"); there is no "latest" folder to fall back to.
    private static string? ClientVersion()
    {
        try
        {
            string version = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "ddragon_version.txt"));
            var match = Regex.Match(version, @"^\s*(\d+\.\d+\.\d+)\s*$");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Without the version the estimate is left out; health from 2999 for the player still shows.
            return null;
        }
    }
}
