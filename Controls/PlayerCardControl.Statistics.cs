using League.Models;

namespace League.Controls;

public partial class PlayerCardControl
{
    private readonly Label _recentKda = new() { Text = "KDA  —", AutoEllipsis = true };
    private readonly Label _recentWinRate = new() { Text = "胜率  —", AutoEllipsis = true };
    private ToolTip? _statisticsTips;

    public void ClearRecentStatistics()
    {
        _recentKda.Text = "KDA  —";
        _recentWinRate.Text = "胜率  —";
        _recentWinRate.ForeColor = Theme.Ink;
        _statisticsTips?.SetToolTip(_recentKda, "");
        _statisticsTips?.SetToolTip(_recentWinRate, "");
        ReflowRanks();
    }

    public void SetRecentStatistics(PlayerMatchInfo info)
    {
        var statistics = RoomRecentStatistics.From(info);
        _recentWinRate.Text = statistics.WinRate is double rate ? $"胜率  {rate:0.#}%" : "胜率  —";
        _recentWinRate.ForeColor =
            statistics.WinRate is not double shown ? Theme.Ink
            : shown >= 50 ? Theme.Win
            : Theme.Loss;
        var sample = $"当前战绩列表最近 {statistics.Games} 场（最多 20 场，包含重开）";
        // ARAM: Mayhem games carry an estimated score, which says more than the KDA in a mode of constant fights.
        if (statistics.AverageScore is double score)
        {
            _recentKda.Text = $"评分  {score:0.0}";
            _statisticsTips?.SetToolTip(
                _recentKda,
                $"{sample}中 {statistics.ScoredGames} 场海克斯大乱斗的平均估算评分（不含重开）\n"
                    + "按 WeGame 海克斯大乱斗评分拟合的估算，不是 WeGame 的数据；单场平均相差约 0.7 分。"
            );
        }
        else
        {
            _recentKda.Text = statistics.Kda is double kda ? $"KDA  {kda:0.0}" : "KDA  —";
            _statisticsTips?.SetToolTip(
                _recentKda,
                sample + "\nKDA = 3 ×（总击杀 + 总助攻）/ 总死亡；零死亡按 1 计算。"
            );
        }
        _statisticsTips?.SetToolTip(_recentWinRate, sample + "\n胜率 = 胜场 / 总场次。");
        // The rank on each line gets whatever the new text leaves.
        ReflowRanks();
    }
}
