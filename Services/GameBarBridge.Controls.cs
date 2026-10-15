using System.Globalization;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

public sealed partial class GameBarBridge
{
    // Control skills arrive once the champion data is read; a missing field means it is still loading.
    private static void AddControls(
        JObject row,
        LoadingPlayer player,
        IReadOnlyDictionary<int, ControlSkill[]>? controls,
        SpellCooldown[]? cooldowns,
        OverlayLiveState? live
    )
    {
        if (controls?.GetValueOrDefault(player.ChampionId) is not { } skills)
        {
            return;
        }

        int level = live?.Insight.Players.GetValueOrDefault(player.Key)?.Level ?? 0;
        var haste = live?.Haste.GetValueOrDefault(player.Key);
        var options = live?.Effects.GetValueOrDefault(player.Key)?.Cooldown;
        row["controls"] = new JArray(
            skills.Select(skill =>
            {
                var slow = ChampionSlowSkills.AtLevel(skill, level);
                var coverage =
                    slow != null
                        ? SlowCoverage.Estimate(
                            slow,
                            cooldowns?.FirstOrDefault(c => c.Key == skill.Key),
                            haste,
                            options
                        )
                        : null;
                var value = new JObject
                {
                    ["key"] = skill.Key,
                    ["name"] = skill.Name,
                    ["effects"] = string.Join(
                        "/",
                        slow is { Slow: var at }
                            ? skill.Effects.Append(
                                $"减速{at.Percent}%{Seconds(at.Duration, " ")}"
                                    + (coverage != null ? $" 覆盖{coverage.Share * 100:0}%" : "")
                            )
                            : skill.Effects
                    ),
                    ["description"] = skill.Description,
                };
                // The widget reads these as numbers; a null would not parse, so they appear only with a slow.
                if (slow != null && skill.Range is int range)
                {
                    value["slow"] = slow.Slow.Percent;
                    value["range"] = range;
                    value["slowNote"] = SlowNote(slow) + (coverage != null ? "\n" + coverage.Note : "");
                }

                if (slow?.Slow.Duration > 0)
                {
                    value["slowDuration"] = slow.Slow.Duration;
                }

                if (slow?.Slow.Zone > 0)
                {
                    value["slowZone"] = slow.Slow.Zone;
                }

                if (coverage != null)
                {
                    value["slowCoverage"] = Math.Round(coverage.Share, 3);
                }

                return value;
            })
        );
    }

    /// <summary>
    /// What the cooldown page starts from: each ability's cooldowns by rank, the ranks (2999's for the player, the
    /// highest the level allows for everyone else) and the ability haste. The widget recalculates as the user
    /// changes ranks, haste or per-ability augments.
    /// </summary>
    private static void AddCalculator(
        JObject row,
        LoadingPlayer player,
        SpellCooldown[]? cooldowns,
        OverlayLiveState? live
    )
    {
        if (cooldowns is not { Length: > 0 })
        {
            return;
        }

        int level = live?.Insight.Players.GetValueOrDefault(player.Key)?.Level ?? 0;
        var haste = live?.Haste.GetValueOrDefault(player.Key);
        bool actual = player.Self && live?.AbilityRanks.Count > 0;
        row["calc"] = new JObject
        {
            ["level"] = level,
            ["haste"] = Math.Round(haste?.Value ?? 0, 1),
            ["hasteDetails"] = haste?.Details ?? "未进入对局，技能急速按 0。",
            // Augment haste such as Ult Bot's 100 is in 2999's value but not in an estimate; the widget adds it there.
            ["hasteEstimated"] = haste?.Estimated ?? true,
            ["options"] = OptionsJson(live?.Effects.GetValueOrDefault(player.Key)?.Cooldown),
            ["rankSource"] =
                actual ? "技能等级取自 2999。"
                : level > 0 ? $"技能等级按 {level} 级可达的最高等级；实际加点未知。"
                : "等级未知，技能等级按满级。",
            ["abilities"] = new JArray(
                cooldowns.Select(spell => new JObject
                {
                    ["key"] = spell.Key,
                    ["name"] = spell.Name,
                    ["rank"] =
                        actual ? Math.Clamp(live!.AbilityRanks.GetValueOrDefault(spell.Key), 0, spell.Cooldowns.Length)
                        : level > 0 ? ChampionSlowSkills.RankAt(spell.Key, spell.Cooldowns.Length, level)
                        : spell.Cooldowns.Length,
                    ["cooldowns"] = new JArray(spell.Cooldowns),
                    ["recharge"] = new JArray(spell.Recharge),
                    ["charges"] = new JArray(spell.Charges),
                })
            ),
        };
    }

    // The cooldown rules the player's recorded augments imply; the page applies them and lists them.
    private static JObject OptionsJson(CooldownOptions? options)
    {
        var set = options ?? CooldownOptions.None;
        return new JObject
        {
            ["bread"] = new JArray(set.Bread),
            ["purist"] = set.Purist,
            ["pursuit"] = set.Pursuit,
            ["pursuitHaste"] = set.PursuitHaste > 0 ? set.PursuitHaste : 80,
            ["ultBot"] = set.UltBot,
            ["ultimateHaste"] = set.UltimateHaste,
            ["summary"] = set.Summary(),
        };
    }

    private static string SlowNote(SlowAtLevel slow) =>
        slow.Basis
        + (slow.Slow.Duration > 0 ? $"：减速持续{Seconds(slow.Slow.Duration, " ")}" : "：数据里没有减速时长")
        + (slow.Slow.Zone > 0 ? $"，区域持续{Seconds(slow.Slow.Zone, " ")}，停留其中可能再次减速" : "");

    private static string Seconds(double seconds, string prefix) =>
        seconds > 0 ? prefix + seconds.ToString("0.##", CultureInfo.InvariantCulture) + "秒" : "";
}
