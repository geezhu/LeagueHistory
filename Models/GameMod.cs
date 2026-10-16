namespace League.Models
{
    internal class GameMod
    {
        // 根据 queueId 和 gameMode 映射模式名称
        public static string GetModeName(int? queueId, string gameMode = null)
        {
            // 优先用 queueId 映射（转换为字符串）
            if (queueId.HasValue)
            {
                string queueKey = queueId.Value.ToString();
                if (_modeTranslations.TryGetValue(queueKey, out string name))
                    return name;
            }

            // 其次尝试用 gameMode 映射（通常为 CLASSIC、ARAM 等）
            if (!string.IsNullOrEmpty(gameMode))
            {
                string modeKey = gameMode.ToUpperInvariant();
                if (_modeTranslations.TryGetValue(modeKey, out string name))
                    return name;
            }

            //return $"未知模式{queueId}|{gameMode}";
            // 明确显示 null 值和格式
            //return $"未知模式（QueueID={queueId?.ToString() ?? "null"} | GameMode={gameMode ?? "null"}）";
            return $"未知({queueId?.ToString()})";
        }

        // 模式映射表（键为 queueId 或 gameMode 的字符串形式）
        // 名称与 MatchDataParser.GetModeFromQueueId / MapGameTags 的实际输出保持一致
        private static readonly Dictionary<string, string> _modeTranslations = new()
        {
            // 排位
            ["420"] = "单双排",
            ["440"] = "灵活排位",

            // 轮换模式
            ["450"] = "极地大乱斗",
            ["480"] = "快速模式", // 根据 queueId=480 映射
            ["900"] = "无限火力",
            ["1020"] = "克隆大作战",
            ["1300"] = "极限闪击",
            ["1400"] = "终极魔典",
            ["700"] = "冠军杯赛",

            // 云顶之弈
            ["1090"] = "云顶之弈：排位",
            ["1130"] = "云顶之弈：双人",

            // 云顶
            ["1090"] = "云顶匹配",
            ["1100"] = "云顶排位",
            ["1130"] = "云顶疾速",
            ["1160"] = "云顶双人",

            // 自定义 / 训练 / 经典
            ["0"] = "自定义",
            ["-1"] = "自定义模式",
            ["3100"] = "自定义峡谷",
            ["3140"] = "训练模式",
            ["3270"] = "自定义海克斯",
            ["4310"] = "经典峡谷",
            ["4320"] = "经典人机",

            // 末日人机
            ["950"] = "末日人机",
            ["960"] = "末日人机",

            // gameMode 字符串兼容
            ["CLASSIC"] = "召唤师峡谷",
            ["ARAM"] = "极地大乱斗",
            ["SWIFTPLAY"] = "快速模式", // 根据 gameMode=SWIFTPLAY 映射
            ["URF"] = "无限火力",
            ["ONEFORALL"] = "克隆大作战",
            ["NEXUSBLITZ"] = "极限闪击",
            ["CHERRY"] = "斗魂竞技场",
            ["TFT"] = "云顶之弈",
        };
    }
}
