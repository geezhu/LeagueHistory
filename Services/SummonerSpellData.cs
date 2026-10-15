using System.Text.RegularExpressions;
using League.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>
/// Data Dragon's summoner spells for the client's version: cooldown, name and icon by internal name, which is what
/// 2999 reports for each player. The list and icons are kept per version; the icons go to the widget as Base64.
/// The cooldowns a mode changes come from CommunityDragon's extraction of the game data, kept as a small file.
/// </summary>
public sealed class SummonerSpellData
{
    // Bumped whenever what is extracted from the game data changes, so older results are not reused.
    private const string ModesVersion = "1";
    private static readonly HttpClient Http = CreateClient();
    private readonly string _folder;
    private readonly string? _version;

    public SummonerSpellData(string? folder = null, string? version = null)
    {
        _folder =
            folder
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "League",
                "Cache",
                "ddragon-summoners"
            );
        _version = version ?? ClientVersion();
    }

    /// <summary>The spells by internal name; empty when the client version is unknown.</summary>
    public async Task<Dictionary<string, SummonerSpellInfo>> LoadAsync(CancellationToken token)
    {
        if (_version == null)
        {
            return [];
        }

        string path = Path.Combine(_folder, _version, "summoner.json");
        string text;
        if (File.Exists(path))
        {
            text = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
        }
        else
        {
            text = await Http.GetStringAsync(
                    $"https://ddragon.leagueoflegends.com/cdn/{_version}/data/zh_CN/summoner.json",
                    token
                )
                .ConfigureAwait(false);
            await SaveAsync(path, text, token).ConfigureAwait(false);
        }

        return Parse(JObject.Parse(text));
    }

    /// <summary>The spell's icon as Base64 PNG, downloaded once per version.</summary>
    public async Task<string> IconAsync(SummonerSpellInfo spell, CancellationToken token)
    {
        if (_version == null || !Regex.IsMatch(spell.Image, @"^[A-Za-z0-9_]+\.png$"))
        {
            return "";
        }

        string path = Path.Combine(_folder, _version, spell.Image);
        if (!File.Exists(path))
        {
            byte[] png = await Http.GetByteArrayAsync(
                    $"https://ddragon.leagueoflegends.com/cdn/{_version}/img/spell/{spell.Image}",
                    token
                )
                .ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temporary, png, token).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }

        return Convert.ToBase64String(await File.ReadAllBytesAsync(path, token).ConfigureAwait(false));
    }

    /// <summary>
    /// Base cooldowns a mode replaces, by spell and lower-case mode ("aram": Exhaust 290); empty when the client
    /// version is unknown. The shared game data is several megabytes, so only the overrides are kept.
    /// </summary>
    public async Task<Dictionary<string, Dictionary<string, double>>> LoadModesAsync(CancellationToken token)
    {
        if (_version == null)
        {
            return [];
        }

        string path = Path.Combine(_folder, _version, $"modes-r{ModesVersion}.json");
        if (File.Exists(path))
        {
            return JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, double>>>(
                    await File.ReadAllTextAsync(path, token).ConfigureAwait(false)
                ) ?? [];
        }

        // CommunityDragon keeps one folder per patch ("16.20").
        string patch = string.Join('.', _version.Split('.').Take(2));
        string text = await Http.GetStringAsync(
                $"https://raw.communitydragon.org/{patch}/game/shared.cdtb.bin.json",
                token
            )
            .ConfigureAwait(false);
        var modes = ParseModes(JObject.Parse(text));
        await SaveAsync(path, JsonConvert.SerializeObject(modes), token).ConfigureAwait(false);
        return modes;
    }

    // The mode overrides sit in the same unnamed field as the champions' ("{f9c2333e}"); unnamed modes are skipped.
    public static Dictionary<string, Dictionary<string, double>> ParseModes(JObject shared)
    {
        const string prefix = "Shared/Spells/";
        var result = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        foreach (
            var entry in shared
                .Properties()
                .Where(p => p.Name.StartsWith(prefix + "Summoner", StringComparison.Ordinal))
        )
        {
            var modes = (entry.Value["mSpell"]?["{f9c2333e}"] as JObject ?? new())
                .Properties()
                .Where(mode => mode.Name != "__type" && !mode.Name.StartsWith('{'))
                .Select(mode =>
                    (Mode: mode.Name.ToLowerInvariant(), Value: (mode.Value["value"] as JArray)?.FirstOrDefault())
                )
                .Where(mode => mode.Value?.Type is JTokenType.Integer or JTokenType.Float && (double)mode.Value! > 0)
                .ToDictionary(mode => mode.Mode, mode => (double)mode.Value!);
            if (modes.Count > 0)
            {
                result[entry.Name[prefix.Length..]] = modes;
            }
        }

        return result;
    }

    public static Dictionary<string, SummonerSpellInfo> Parse(JObject list)
    {
        var result = new Dictionary<string, SummonerSpellInfo>(StringComparer.Ordinal);
        foreach (var entry in (list["data"] as JObject ?? new()).Properties())
        {
            if (
                entry.Value is not JObject spell
                || (spell["cooldown"] as JArray)?.FirstOrDefault()
                    is not { Type: JTokenType.Integer or JTokenType.Float } cooldown
            )
            {
                continue;
            }

            result[entry.Name] = new(
                entry.Name,
                (string?)spell["name"] ?? entry.Name,
                Math.Max(0, (double)cooldown),
                (string?)spell["image"]?["full"] ?? ""
            );
        }

        return result;
    }

    private static async Task SaveAsync(string path, string text, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, text, token).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    private static HttpClient CreateClient()
    {
        // The shared game data is several megabytes; CommunityDragon refuses requests without a user agent.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LeagueHistory");
        return client;
    }

    // Data Dragon folders are full versions ("16.20.1").
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
            // Without the version the summoner spells are left out of the cards.
            return null;
        }
    }
}
