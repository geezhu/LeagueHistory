using System.Text.RegularExpressions;
using League.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>
/// The facts read from a champion's game data: slows for the control page, cooldowns, and the character record and
/// spell numbers the Game Bar's stat page shows.
/// </summary>
public sealed record ChampionSpellFacts(SlowSkill[] Slows, SpellCooldown[] Cooldowns)
{
    public static readonly ChampionSpellFacts None = new([], []);

    public ChampionRecord Record { get; init; } = ChampionRecord.None;

    public ChampionSpellScalings Scalings { get; init; } = ChampionSpellScalings.None;

    /// <summary>Slows a game mode changes, by lower-case mode; other modes use <see cref="Slows"/>.</summary>
    public Dictionary<string, SlowSkill[]> ModeSlows { get; init; } = new();

    public SlowSkill[] SlowsFor(string mode) => ModeSlows.GetValueOrDefault(mode.ToLowerInvariant()) ?? Slows;
}

/// <summary>
/// Downloads a champion's game data from CommunityDragon for the client's patch and keeps only the extracted slows,
/// cooldowns, character record and spell numbers, cached per patch. The client's own resources carry neither slow amounts nor cooldowns per rank, so
/// this is the one remote read for them; it runs off the UI thread.
/// </summary>
public sealed class ChampionSpellData
{
    // Bumped whenever what is extracted changes, so results from older rules are not reused.
    public const string RulesVersion = "8";

    private static readonly HttpClient Http = CreateClient();
    private readonly string _folder;
    private readonly string _patch;

    public ChampionSpellData(string? folder = null, string? patch = null)
    {
        _folder =
            folder
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "League",
                "Cache",
                "cdragon-spells"
            );
        _patch = patch ?? ClientPatch();
    }

    public async Task<ChampionSpellFacts> LoadAsync(JObject champion, CancellationToken token)
    {
        string alias = ((string?)champion["alias"] ?? "").ToLowerInvariant();
        if (alias.Length == 0 || !Regex.IsMatch(alias, "^[a-z0-9]+$"))
        {
            return ChampionSpellFacts.None;
        }

        string path = Path.Combine(_folder, $"{_patch}-r{RulesVersion}", alias + ".json");
        if (File.Exists(path))
        {
            return JsonConvert.DeserializeObject<ChampionSpellFacts>(
                    await File.ReadAllTextAsync(path, token).ConfigureAwait(false)
                ) ?? ChampionSpellFacts.None;
        }

        string url = $"https://raw.communitydragon.org/{_patch}/game/data/characters/{alias}/{alias}.bin.json";
        var bin = JObject.Parse(await Http.GetStringAsync(url, token).ConfigureAwait(false));
        var slows = ChampionSlowSkills.Parse(bin, champion);
        var facts = new ChampionSpellFacts(slows, ChampionCooldowns.Parse(bin, champion))
        {
            ModeSlows = ChampionSlowSkills.ParseModes(bin, champion, slows),
            Record = ChampionRecord.Parse(bin, (string?)champion["alias"] ?? ""),
            Scalings = ChampionSpellScalings.Parse(bin, (string?)champion["alias"] ?? ""),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonConvert.SerializeObject(facts), token).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
        return facts;
    }

    // CommunityDragon keeps one folder per patch ("16.20"); without a local version the latest data stands in.
    internal static string ClientPatch()
    {
        try
        {
            string version = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "ddragon_version.txt"));
            var match = Regex.Match(version, @"^\s*(\d+\.\d+)");
            return match.Success ? match.Groups[1].Value : "latest";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A missing or unreadable version file only loses the patch match; the latest data is close enough.
            return "latest";
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // CommunityDragon refuses requests without a user agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LeagueHistory");
        return client;
    }
}
