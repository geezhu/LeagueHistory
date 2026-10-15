using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

public sealed partial class GameBarBridge
{
    /// <summary>The ally summoner timers the widget's player started this session; null when missing or malformed.</summary>
    public async Task<SummonerTimer[]?> ReadSummonerTimersAsync()
    {
        string session = _detailScope.Session;
        string directory = _testDirectory ?? _directory;
        if (_disposed || session.Length == 0 || directory.Length == 0)
        {
            return null;
        }

        string path = Path.Combine(directory, "summoner-timers.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 8192)
            {
                return null;
            }

            return SummonerSpells.ParseTimers(
                JObject.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false)),
                session
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            // The widget replaces the file atomically; a read during the swap is retried on the next poll.
            return null;
        }
    }

    // Enemies' spells carry no cooldown or start, so the widget has nothing to time them with.
    private static void AddSummonerFields(JObject row, string key, OverlayLiveState live)
    {
        if (live.Summoners.GetValueOrDefault(key) is not { Length: > 0 } summoners)
        {
            return;
        }

        row["summoners"] = new JArray(
            summoners.Select(summoner =>
            {
                bool timed = summoner.Cooldown > 0;
                var value = new JObject
                {
                    ["slot"] = summoner.Slot,
                    ["spell"] = summoner.Spell.Id,
                    ["name"] = summoner.Spell.Name,
                    ["timed"] = timed,
                };
                if (timed)
                {
                    value["cooldown"] = Math.Round(summoner.Cooldown, 1);
                    value["started"] = summoner.Started;
                }

                return value;
            })
        );
    }

    // The icons once per snapshot by spell, rather than on every card that has the spell.
    private static JObject SummonerIconsJson(OverlayLiveState? live) =>
        new(
            (live?.SummonerIcons ?? new Dictionary<string, string>())
                .Where(icon =>
                    live!.Summoners.Values.Any(summoners => summoners.Any(summoner => summoner.Spell.Id == icon.Key))
                )
                .Select(icon => new JProperty(icon.Key, icon.Value))
        );
}
