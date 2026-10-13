using System.Globalization;
using League.Models;

namespace League.Services;

/// <summary>The team analysis for a roster: evaluation, best swap, the chat line and a one-line summary.</summary>
public sealed record TeamAnalysis(AramTeamEvaluation Evaluation, AramTeamSwap? Best, string Message, string Brief);

/// <summary>One champion select chat line summarizing the team analysis, within the 180-character limit.</summary>
public static class TeamAnalysisChatMessage
{
    private const int Limit = 180;

    /// <summary>Evaluates the picked champions; the best swap replaces the user's pick from the bench.</summary>
    public static TeamAnalysis Analyze(
        AramMetaIndex index,
        ChampionSelectRoster roster,
        bool mayhem,
        Func<int, string> name
    )
    {
        int[] team = roster.Team.Where(entry => entry.ChampionId > 0).Select(entry => entry.ChampionId).ToArray();
        var evaluation = AramTeamScore.Evaluate(index, team);
        var self = roster.Team.FirstOrDefault(entry => entry.IsSelf);
        var best =
            self is { ChampionId: > 0 } && roster.Bench.Length > 0
                ? AramTeamScore.Swaps(index, evaluation, self.ChampionId, roster.Bench, mayhem).FirstOrDefault()
                : null;
        return new(
            evaluation,
            best,
            Build(evaluation, best, mayhem, index.Patch, name),
            Brief(evaluation, best, mayhem, name)
        );
    }

    /// <summary>The conclusion for one assistant line: win rate (Mayhem), gaps, damage mix and a helpful swap.</summary>
    public static string Brief(AramTeamEvaluation evaluation, AramTeamSwap? best, bool mayhem, Func<int, string> name)
    {
        var parts = new List<string>();
        if (mayhem)
        {
            parts.Add("胜率 " + Percent(evaluation.EstimatedWinRate));
        }

        var lacks = evaluation.Lacks;
        parts.Add("短板 " + (lacks.Length > 0 ? string.Join("、", lacks.Select(lack => lack.Name)) : "无"));
        parts.Add($"物理 {(evaluation.AdShare * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        if (Recommended(best, mayhem) is { } swap)
        {
            parts.Add($"可换 {Clean(name(swap.ChampionId))}" + (mayhem ? " " + Signed(swap.Delta) : ""));
        }

        return "阵容 " + string.Join(" · ", parts);
    }

    public static string Build(
        AramTeamEvaluation evaluation,
        AramTeamSwap? best,
        bool mayhem,
        string patch,
        Func<int, string> name
    )
    {
        var parts = new List<string>();
        if (mayhem)
        {
            parts.Add(
                $"arammeta {Clean(patch)} 估计胜率 {Percent(evaluation.EstimatedWinRate)}"
                    + $"（英雄 {Percent(evaluation.BaseWinRate)} 搭配 {Signed(evaluation.PairLift)} 阵容 {Signed(evaluation.CompositionScore)}）"
            );
        }

        var lacks = evaluation.Lacks;
        parts.Add("短板：" + (lacks.Length > 0 ? string.Join("、", lacks.Select(lack => lack.Name)) : "无"));
        parts.Add($"物理伤害 {(evaluation.AdShare * 100).ToString("0", CultureInfo.InvariantCulture)}%");
        if (Recommended(best, mayhem) is { } swap)
        {
            string effect = mayhem ? " " + Signed(swap.Delta) : "";
            if (swap.Fixed.Length > 0)
            {
                effect += " 补" + string.Join("、", swap.Fixed.Select(lack => lack.Name));
            }

            parts.Add($"可换 {Clean(name(swap.ChampionId))}{effect}");
        }

        string prefix = mayhem ? "【海克斯大乱斗·阵容分析】" : "【大乱斗·阵容分析】";
        string message = prefix + string.Join("；", parts);
        return message.Length <= Limit ? message : message[..(Limit - 1)] + "…";
    }

    // Only a swap that helps is worth announcing: a higher estimate, or more gaps fixed than added.
    private static AramTeamSwap? Recommended(AramTeamSwap? swap, bool mayhem) =>
        swap == null ? null
        : mayhem ? (swap.Delta >= 0.0005 && swap.Added.Length == 0 ? swap : null)
        : swap.Fixed.Length > swap.Added.Length ? swap
        : null;

    private static string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    // Invariant percent formatting inserts a space before "%"; chat text uses the compact form.
    private static string Percent(double value) => (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Signed(double value) =>
        Math.Abs(value) < 0.0005 ? "±0.0%" : (value > 0 ? "+" : "-") + Percent(Math.Abs(value));
}
