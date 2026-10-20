namespace League.Models
{
    public class ChampionMasteryInfo
    {
        public int ChampionId { get; set; }
        public string ChampionName { get; set; } = "";
        public int ChampionLevel { get; set; }
        public long ChampionPoints { get; set; }
        public string HighestGrade { get; set; } = "";   // S+, S, A+ ...
        public long LastPlayTime { get; set; }           // 毫秒时间戳
        public Image? Icon { get; set; }

        /// <summary>
        /// Tooltip 文本
        /// </summary>
        public string TooltipText
        {
            get
            {
                string points = ChampionPoints >= 10000
                    ? $"{ChampionPoints / 10000.0:F1}万"
                    : ChampionPoints.ToString("N0");

                string lastPlay = "未知";
                if (LastPlayTime > 0)
                {
                    var dt = DateTimeOffset.FromUnixTimeMilliseconds(LastPlayTime).LocalDateTime;
                    var days = (DateTime.Now - dt).TotalDays;
                    lastPlay = days < 1 ? "今天"
                             : days < 2 ? "昨天"
                             : days < 30 ? $"{(int)days}天前"
                             : dt.ToString("yyyy-MM-dd");
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(ChampionName);
                sb.AppendLine($"成就等级 {ChampionLevel}");
                sb.AppendLine($"成就点数 {points}");
                if (!string.IsNullOrWhiteSpace(HighestGrade) && HighestGrade != "-")
                    sb.AppendLine($"最高评价 {HighestGrade}");
                sb.Append($"最近游玩 {lastPlay}");
                return sb.ToString();
            }
        }
    }
}