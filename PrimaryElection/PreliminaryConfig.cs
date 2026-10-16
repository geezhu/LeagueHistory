namespace League.PrimaryElection
{
    public class PreliminaryConfig
    {
        /// <summary>The general list: used when the assigned position or the mode has no list of its own.</summary>
        public List<PreliminaryHero> Heroes { get; set; } = new List<PreliminaryHero>();

        /// <summary>Lists by key (top, jungle, middle, bottom, utility, aram); see <see cref="PreliminaryLists"/>.</summary>
        public Dictionary<string, List<PreliminaryHero>> PositionHeroes { get; set; } = new();

        public bool EnableAutoPreliminary { get; set; } = true;
        public bool Modified { get; set; } = false; // 可用于判断是否需要保存
    }

    public class PreliminaryHero
    {
        public int ChampionId { get; set; }
        public string ChampionName { get; set; } = string.Empty;
        public string Position { get; set; } = "Top"; // 可存中文或枚举字符串
        public DateTime AddedTime { get; set; } = DateTime.Now;
        public int Priority { get; set; } = 1;
    }
}
