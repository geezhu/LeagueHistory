namespace League.Models
{
    public class PlayerBehavior
    {
        public double SurrenderRate { get; set; } = 0;  // 投降率（%）

        // 平均值（标签判定用）
        public double AvgMissingPings { get; set; } = 0;    // 平均 Missing Ping
        public double AvgVisionPings { get; set; } = 0;     // 平均视野 Ping
        public double AvgGetBackPings { get; set; } = 0;    // 平均撤退信号
        public double AvgDangerPings { get; set; } = 0;     // 平均危险信号

        // 总次数（有特征时才显示）
        public int TotalMissingPings { get; set; } = 0;
        public int TotalVisionPings { get; set; } = 0;
        public int TotalGetBackPings { get; set; } = 0;
        public int TotalDangerPings { get; set; } = 0;
        public int TotalPings => TotalMissingPings + TotalVisionPings + TotalGetBackPings + TotalDangerPings;

        public int ShortGameCount { get; set; } = 0;        // 短局数量
        public string BehaviorSummary { get; set; } = "";   // 用于发送的消息文字
    }
}