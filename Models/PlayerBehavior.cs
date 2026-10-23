namespace League.Models
{
    public class PlayerBehavior
    {
        public double SurrenderRate { get; set; } = 0; // 投降率（%）

        // 平均值（判定标签用）
        public double AvgMissingPings { get; set; } = 0; // 平均 Missing Ping
        public double AvgVisionPings { get; set; } = 0; // 平均视野 Ping
        public double AvgGetBackPings { get; set; } = 0; // 平均撤退信号
        public double AvgDangerPings { get; set; } = 0; // 平均危险信号

        // 总次数（有特征时发送）
        public int TotalMissingPings { get; set; } = 0;
        public int TotalVisionPings { get; set; } = 0;
        public int TotalGetBackPings { get; set; } = 0;
        public int TotalDangerPings { get; set; } = 0;

        public int ShortGameCount { get; set; } = 0; // 短局数量
        public string BehaviorSummary { get; set; } = ""; // 特征标签，以「、」分隔，不带括号

        /// <summary>The chat line's signal counts, such as 「发问号 12次」; signals never sent are left out.</summary>
        public IReadOnlyList<string> SignalCounts() =>
            new (string Name, int Count)[]
            {
                ("发问号", TotalMissingPings),
                ("发提醒", TotalVisionPings),
                ("发撤退", TotalGetBackPings),
                ("发危险", TotalDangerPings),
            }
                .Where(signal => signal.Count > 0)
                .Select(signal => $"{signal.Name} {signal.Count}次")
                .ToArray();
    }
}
