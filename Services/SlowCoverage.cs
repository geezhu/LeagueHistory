using System.Globalization;
using League.Models;

namespace League.Services;

/// <summary>How much of the time a slow can be up: its duration over the cooldown at the same rank.</summary>
public sealed record SlowCoverageEstimate(double Cooldown, double Share, string Note);

/// <summary>
/// The share of time a slow can be kept up if it lands every time it comes back: one slow's duration over the
/// cooldown after ability haste, at the rank the slow is shown at. Charge abilities use one charge's return time.
/// Hits, other cooldown reductions and zones that slow again are not counted, so it is a rough ceiling.
/// </summary>
public static class SlowCoverage
{
    public static SlowCoverageEstimate? Estimate(
        SlowAtLevel slow,
        SpellCooldown? cooldown,
        PlayerHaste? haste,
        CooldownOptions? options = null
    )
    {
        if (cooldown == null || slow.Slow.Duration <= 0 || slow.Rank < 1)
        {
            return null;
        }

        int index = Math.Min(slow.Rank, cooldown.Cooldowns.Length) - 1;
        bool charged = index >= 0 && index < cooldown.Recharge.Length && cooldown.Recharge[index] > 0;
        double seconds =
            charged ? cooldown.Recharge[index]
            : index >= 0 ? cooldown.Cooldowns[index]
            : 0;
        if (seconds <= 0)
        {
            return null;
        }

        double value = haste?.Value ?? 0;
        var set = options ?? CooldownOptions.None;
        double hasted =
            AbilityCooldowns.Hasted(seconds, set.Haste(cooldown.Key, value, haste?.Estimated ?? true)) * set.Multiplier;
        double share = Math.Min(1, slow.Slow.Duration / hasted);
        string source =
            haste == null ? "技能急速未知，按 0"
            : haste.Estimated ? $"估算技能急速 {value:0}"
            : $"技能急速 {value:0}";
        string kind = charged ? "每层充能" : "冷却";
        string summary = set.Summary();
        if (summary.Length > 0)
        {
            source += "，" + summary;
        }

        return new(
            hasted,
            share,
            $"{kind}约 {Seconds(hasted)}（基础 {Seconds(seconds)}，{source}）：每次好了就命中时，"
                + $"减速约占 {share * 100:0}% 的时间；未计命中返还和区域内的再次减速。"
        );
    }

    private static string Seconds(double seconds) =>
        seconds.ToString(seconds < 10 ? "0.#" : "0", CultureInfo.InvariantCulture) + " 秒";
}
