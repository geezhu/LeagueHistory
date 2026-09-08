using League.Models;

namespace League.Controls;

public partial class PlayerCardControl
{
    private readonly Label _recentKda = new() { Text = "KDA  —", AutoEllipsis = true };
    private readonly Label _recentWinRate = new() { Text = "胜率  —", AutoEllipsis = true };
    private ToolTip? _statisticsTips;

    public void SetRecentStatistics(PlayerMatchInfo info)
    {
        var statistics = RoomRecentStatistics.From(info);
        _recentKda.Text = statistics.Kda is double kda ? $"KDA  {kda:0.0}" : "KDA  —";
        _recentWinRate.Text = statistics.WinRate is double rate ? $"胜率  {rate:0.#}%" : "胜率  —";
        var sample = $"当前战绩列表最近 {statistics.Games} 场（最多 20 场，包含重开）";
        _statisticsTips?.SetToolTip(_recentKda, sample + "\nKDA = 3 ×（总击杀 + 总助攻）/ 总死亡；零死亡按 1 计算。");
        _statisticsTips?.SetToolTip(_recentWinRate, sample + "\n胜率 = 胜场 / 总场次。");
    }
}
