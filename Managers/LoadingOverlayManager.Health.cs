using League.Models;
using League.Services;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    private readonly ChampionBaseStatsData _baseStatsData = new();
    private readonly Dictionary<int, ItemStats> _itemStats = new();
    private Dictionary<int, ChampionBaseStats>? _baseStats;
    private bool _loadingBaseStats;
    private long _baseStatsRetry;

    // Health lines for the control page: 2999's real stats for the player, estimates from level, items and the
    // recorded augments for everyone else. Base stats arrive once from Data Dragon; until then only the player shows.
    private Dictionary<string, PlayerHealth> DescribeHealth(
        LoadingRoster roster,
        LiveMatchSnapshot snapshot,
        LiveMatchInsight insight,
        IReadOnlyDictionary<string, AugmentEffects> effects
    )
    {
        _ = LoadBaseStatsAsync();
        var result = new Dictionary<string, PlayerHealth>();
        foreach (var player in roster.Players.Where(p => p.ChampionId > 0))
        {
            if (insight.Players.GetValueOrDefault(player.Key) is not { } live)
            {
                continue;
            }

            var items = PlayerItems(live);
            var augments = effects.GetValueOrDefault(player.Key) ?? AugmentEffects.None;
            var estimate =
                _baseStats?.GetValueOrDefault(player.ChampionId) is { } stats && live.Level > 0
                    ? HealthEstimate.Estimate(stats, live.Level, items, augments)
                    : null;
            var actual = player.Self ? snapshot.Active?.Stats : null;
            if (PlayerHealthText.Describe(estimate, live.Level, items, augments, actual) is { } health)
            {
                result[player.Key] = health;
            }
        }

        return result;
    }

    // Ability haste per player for slow coverage and the cooldown calculator, from the same items and augments.
    private Dictionary<string, PlayerHaste> DescribeHaste(
        LoadingRoster roster,
        LiveMatchSnapshot snapshot,
        LiveMatchInsight insight,
        IReadOnlyDictionary<string, AugmentEffects> effects
    )
    {
        var result = new Dictionary<string, PlayerHaste>();
        foreach (var player in roster.Players.Where(p => p.ChampionId > 0))
        {
            if (insight.Players.GetValueOrDefault(player.Key) is { } live)
            {
                var items = PlayerItems(live);
                // Attack speed growth counts as bonus attack speed, as items' does; both feed Purist - Caster.
                double growth =
                    _baseStats?.GetValueOrDefault(player.ChampionId) is { } stats && live.Level > 0
                        ? ChampionBaseStats.Grown(0, stats.AttackSpeedPerLevel, live.Level)
                        : 0;
                result[player.Key] = PlayerHaste.Describe(
                    items,
                    effects.GetValueOrDefault(player.Key) ?? AugmentEffects.None,
                    player.Self ? snapshot.Active?.Stats : null,
                    items.AttackSpeed + growth
                );
            }
        }

        return result;
    }

    private ItemStats PlayerItems(LivePlayerInsight live) =>
        ItemStats.Sum(live.Items.SelectMany(item => Enumerable.Repeat(ItemStatsFor(item.Id), Math.Max(1, item.Count))));

    // Mode copies of items (223157 for 3157) carry the same stats as the original.
    private ItemStats ItemStatsFor(int id)
    {
        if (_itemStats.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var item =
            FormMain.Globals.resLoading?.GetItemById(id)
            ?? (id >= 100000 ? FormMain.Globals.resLoading?.GetItemById(id % 10000) : null);
        var stats = ItemStats.Parse(item?.Description);
        // An item list that has not loaded yet is not cached, so its stats appear once it has.
        if (item != null)
        {
            _itemStats[id] = stats;
        }

        return stats;
    }

    private async Task LoadBaseStatsAsync()
    {
        if (_baseStats != null || _loadingBaseStats || _disposed || Environment.TickCount64 < _baseStatsRetry)
        {
            return;
        }

        _loadingBaseStats = true;
        try
        {
            var stats = await _baseStatsData.LoadAsync(_life.Token);
            if (stats.Count > 0)
            {
                _baseStats = stats;
            }
            else
            {
                _baseStatsRetry = Environment.TickCount64 + 60_000;
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
            // Estimates are optional: other players' health stays hidden and the load is retried in a minute.
            _baseStatsRetry = Environment.TickCount64 + 60_000;
            ChatSendDiagnostics.Shared.Report("base-stats", e.GetType().Name + " " + e.Message);
        }
        finally
        {
            _loadingBaseStats = false;
        }
    }
}
