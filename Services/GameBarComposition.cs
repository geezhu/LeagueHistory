using System.Globalization;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>Both teams of a running ARAM-type game, evaluated with arammeta's model.</summary>
public sealed record GameComposition(
    AramTeamEvaluation Own,
    AramTeamEvaluation Enemy,
    AramCapabilityGrades Grades,
    bool Mayhem,
    JObject Json
);

/// <summary>
/// The Game Bar composition page: each team's capability grades (D is a gap), damage mix and front line, and in
/// Mayhem the estimated win rate and the strongest pairs. The model is trained on ARAM games, so the page exists only
/// for ARAM and ARAM: Mayhem; mid-game it states facts and suggests no swaps.
/// </summary>
public static class GameBarComposition
{
    public static bool Supports(LoadingRoster roster) =>
        PlayerChampionTier.NormalizeQueue(roster.Queue, roster.Mode) is 450 or 2400;

    public static bool IsMayhem(LoadingRoster roster) =>
        PlayerChampionTier.NormalizeQueue(roster.Queue, roster.Mode) == 2400;

    /// <summary>Null when the roster has no self player or no champions yet.</summary>
    public static GameComposition? Build(
        AramMetaIndex index,
        AramCapabilityGrades grades,
        LoadingRoster roster,
        Func<int, string> champion
    )
    {
        int own = roster.Players.FirstOrDefault(p => p.Self)?.Team ?? 0;
        int[] Team(bool ally) =>
            roster.Players.Where(p => p.ChampionId > 0 && (p.Team == own) == ally).Select(p => p.ChampionId).ToArray();
        int[] allies = Team(true),
            enemies = Team(false);
        if (own == 0 || allies.Length == 0 || enemies.Length == 0)
        {
            return null;
        }

        bool mayhem = IsMayhem(roster);
        var ownEvaluation = AramTeamScore.Evaluate(index, allies);
        var enemyEvaluation = AramTeamScore.Evaluate(index, enemies);
        var json = new JObject
        {
            ["protocol"] = 1,
            ["mayhem"] = mayhem,
            ["revision"] = $"{index.Patch}|{string.Join(",", allies)}|{string.Join(",", enemies)}",
            ["note"] =
                $"arammeta {(mayhem ? "海克斯大乱斗" : "大乱斗")} {index.Patch} 统计模型。等级按同人数随机阵容排名，"
                + $"{AramCapabilityGrades.Gap} 为短板"
                + (mayhem ? "；估计胜率不保证单场胜负。" : "；普通大乱斗不显示估计胜率。"),
            ["teams"] = new JArray(
                Describe("我方", ownEvaluation, grades, mayhem, champion),
                Describe("敌方", enemyEvaluation, grades, mayhem, champion)
            ),
        };
        return new(ownEvaluation, enemyEvaluation, grades, mayhem, json);
    }

    private static JObject Describe(
        string title,
        AramTeamEvaluation evaluation,
        AramCapabilityGrades grades,
        bool mayhem,
        Func<int, string> champion
    )
    {
        int size = evaluation.Team.Length;
        var team = new JObject
        {
            ["title"] = title,
            ["damage"] =
                $"物理伤害 {Percent(evaluation.AdShare, "0")}{Mix(evaluation.AdShare)} · 前排 {evaluation.FrontCount} 人",
            ["lacks"] = string.Join("、", evaluation.Lacks.Select(lack => lack.Name)),
            ["missing"] = string.Join("、", evaluation.Missing.Select(champion)),
            ["capabilities"] = new JArray(
                evaluation.Capabilities.Select(capability => new JObject
                {
                    ["name"] = capability.Name,
                    ["grade"] = grades.Grade(capability, size),
                    ["percentile"] = Math.Round(grades.Percentile(capability, size), 3),
                    ["lack"] = capability.Lack,
                    ["tip"] =
                        $"{capability.Name}：全队合计 {capability.Sum:0.##}，短板阈值 {capability.Threshold:0.##}；"
                        + $"高于 {Percent(grades.Percentile(capability, size), "0")} 的同人数随机阵容。",
                })
            ),
        };
        if (mayhem)
        {
            team["winRate"] = "估计胜率 " + Percent(evaluation.EstimatedWinRate, "0.0");
            team["winDetail"] =
                $"英雄 {Percent(evaluation.BaseWinRate, "0.0")} · 搭配 {Signed(evaluation.PairLift)} · "
                + $"阵容 {Signed(evaluation.CompositionScore)} · 可信度 {evaluation.Confidence}";
            team["pairs"] = new JArray(
                evaluation
                    .Pairs.OrderByDescending(pair => Math.Abs(pair.Lift))
                    .Take(3)
                    .Select(pair =>
                        $"{champion(pair.First)} + {champion(pair.Second)} {Signed(pair.Lift)}（同队 {pair.Games:N0} 场）"
                    )
            );
        }

        return team;
    }

    /// <summary>
    /// One chat line with a side's capability grades and damage mix; the composition must be read first.
    /// </summary>
    public static IReadOnlyList<string> ChatMessage(GameComposition? composition, OverlayChatRequest request)
    {
        if (request.Target is not ("own" or "enemy"))
        {
            throw new InvalidOperationException("未发送：阵容分析只能按我方或对方发送。");
        }

        if (composition == null)
        {
            throw new InvalidOperationException("未发送：阵容分析还在读取，或当前模式没有 arammeta 统计。");
        }

        bool own = request.Target == "own";
        return
        [
            TeamAnalysisChatMessage.Grades(
                own ? composition.Own : composition.Enemy,
                composition.Grades,
                composition.Mayhem,
                own ? "我方" : "对方"
            ),
        ];
    }

    private static string Mix(double adShare) =>
        adShare switch
        {
            < 0.25 => "（魔法为主）",
            > 0.75 => "（物理为主）",
            _ => "",
        };

    private static string Percent(double value, string format) =>
        (value * 100).ToString(format, CultureInfo.InvariantCulture) + "%";

    private static string Signed(double value) =>
        Math.Abs(value) < 0.0005 ? "±0.0%" : (value > 0 ? "+" : "-") + Percent(Math.Abs(value), "0.0");
}
