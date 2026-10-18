using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

public sealed partial class GameBarBridge
{
    /// <summary>
    /// The stat page for one player once the champion's game data is read: in a game with the stats of this poll,
    /// before it with the champion's level 1 stats, so the abilities can be read in champion select already.
    /// </summary>
    private static void AddStatPage(JObject row, LoadingPlayer player, StatPageSources? pages, OverlayLiveState? live)
    {
        if (pages?.Sources.GetValueOrDefault(player.ChampionId) is not { } source)
        {
            return;
        }

        var sheet =
            live?.Sheets.GetValueOrDefault(player.Key)
            ?? (
                source.BaseStats is { } stats
                    ? ChampionStatSheet.Estimate(stats, source.Facts.Record, 1, ItemStats.None, AugmentEffects.None, 0)
                    : null
            );
        row["sheet"] = ChampionStatPage.Build(source, sheet, pages.Texts, pages.Mode);
    }
}
