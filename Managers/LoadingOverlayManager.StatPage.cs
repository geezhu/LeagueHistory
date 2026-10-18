using League.Models;
using League.Services;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    private readonly SpellTextData _spellTextData = new();
    private IReadOnlyDictionary<string, string>? _spellTexts;
    private bool _loadingSpellTexts;
    private long _spellTextsRetry;

    // Stats for the Game Bar's stat page: 2999's panel for the player, estimates from the level, items and recorded
    // augments for everyone else, with the same haste the cooldown calculator uses.
    private Dictionary<string, ChampionStatSheet> DescribeSheets(
        LoadingRoster roster,
        LiveMatchSnapshot snapshot,
        LiveMatchInsight insight,
        IReadOnlyDictionary<string, AugmentEffects> effects,
        IReadOnlyDictionary<string, PlayerHaste> haste
    )
    {
        var result = new Dictionary<string, ChampionStatSheet>();
        foreach (var player in roster.Players.Where(p => p.ChampionId > 0))
        {
            if (insight.Players.GetValueOrDefault(player.Key) is not { Level: > 0 } live)
            {
                continue;
            }

            var stats = _baseStats?.GetValueOrDefault(player.ChampionId);
            var record = _spells.TryGetValue(player.ChampionId, out var spell)
                ? spell.Facts.Record
                : ChampionRecord.None;
            if (player.Self && snapshot.Active?.Stats is { } actual)
            {
                result[player.Key] = ChampionStatSheet.FromLive(stats, record, live.Level, actual);
            }
            else if (stats != null)
            {
                result[player.Key] = ChampionStatSheet.Estimate(
                    stats,
                    record,
                    live.Level,
                    PlayerItems(live),
                    effects.GetValueOrDefault(player.Key) ?? AugmentEffects.None,
                    haste.GetValueOrDefault(player.Key)?.Value ?? 0
                );
            }
        }

        return result;
    }

    /// <summary>The champions whose game data is read, for the stat page; the tooltips follow once loaded.</summary>
    private StatPageSources StatPageSources()
    {
        _ = LoadBaseStatsAsync();
        _ = LoadSpellTextsAsync();
        return new(
            _spells.ToDictionary(
                entry => entry.Key,
                entry => new StatPageSource(
                    entry.Value.Facts,
                    entry.Value.Champion,
                    _baseStats?.GetValueOrDefault(entry.Key)
                )
            ),
            _spellTexts,
            CurrentMode()
        );
    }

    private async Task LoadSpellTextsAsync()
    {
        if (
            _spellTexts != null
            || _loadingSpellTexts
            || _disposed
            || _spells.Count == 0
            || Environment.TickCount64 < _spellTextsRetry
        )
        {
            return;
        }

        _loadingSpellTexts = true;
        try
        {
            var texts = await _spellTextData.LoadAsync(_life.Token);
            if (!_disposed && texts.Count > 0)
            {
                _spellTexts = texts;
                SchedulePaint();
            }
            else
            {
                _spellTextsRetry = Environment.TickCount64 + 60_000;
            }
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested)
        {
            // Shutting down; nothing to keep.
        }
        catch (Exception e)
            when (e
                    is HttpRequestException
                        or TaskCanceledException
                        or IOException
                        or UnauthorizedAccessException
                        or Newtonsoft.Json.JsonException
            )
        {
            // Tooltips are optional: the stat page keeps the stats and cooldowns and retries in a minute.
            _spellTextsRetry = Environment.TickCount64 + 60_000;
            AppLog.Shared.Warn(LogCategory.Data, "技能说明读取失败", e);
        }
        finally
        {
            _loadingSpellTexts = false;
        }
    }
}
