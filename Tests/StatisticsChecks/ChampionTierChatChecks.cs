using League.Models;
using League.Services;

internal static class ChampionTierChatChecks
{
    public static void Run()
    {
        SourceAndMissingRates();
        TeamSizeAndChunking();
        ZeroAndEmptyRates();
        ModeSelection();
        Console.WriteLine("PASS: tier chat source, map, unknown tiers, empty roster and chunking.");
    }

    private static void SourceAndMissingRates()
    {
        var messages = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "arammeta",
            "16.17",
            "我方",
            new[]
            {
                new ChampionTierChatEntry("拉克丝", "T1", .532, .086),
                new ChampionTierChatEntry("莫甘娜", "暂无梯队", null, null),
            },
            true
        );
        if (
            messages.Count != 1
            || !messages[0].Contains("海克斯大乱斗·我方")
            || !messages[0].Contains("arammeta 16.17")
            || !messages[0].Contains("莫甘娜 — —/—")
        )
        {
            throw new Exception("Tier notice source, map and missing data");
        }

        if (!messages[0].Contains("调整后胜率/样本选取率：拉克丝 T1 53.2%/8.6%"))
        {
            throw new Exception("Mayhem rate definitions and compact values");
        }
    }

    private static void TeamSizeAndChunking()
    {
        var team = ChampionTierChatMessage.Build(
            "海克斯大乱斗",
            "arammeta",
            "16.18",
            "我方",
            new[] { "蒸汽机器人", "邪恶小法师", "无畏战车", "爆破鬼才", "光辉女郎" }.Select(
                name => new ChampionTierChatEntry(name, "T2", .527, .086)
            ),
            true
        );
        if (team.Count != 1 || team[0].Length > 180)
        {
            throw new Exception("A five-player team should fit one compact chat message");
        }

        var many = Enumerable.Range(0, 30).Select(i => new ChampionTierChatEntry($"英雄{i}", "T2", .5, .01)).ToArray();
        var split = ChampionTierChatMessage.Build("大乱斗", "OP.GG", "16.17", "交换池", many);
        if (
            split.Count < 2
            || split.Any(s => s.Length > 180)
            || many.Any(row => !string.Join("|", split).Contains(row.Name + " T2 50.0%/1.0%"))
        )
        {
            throw new Exception("Tier notice chunking must retain every hero and rates");
        }
    }

    private static void ZeroAndEmptyRates()
    {
        var zero = ChampionTierChatMessage.Build(
            "大乱斗",
            "OP.GG",
            "16.17",
            "我方",
            new[] { new ChampionTierChatEntry("测试", "T5", 0, double.NaN) }
        );
        if (!zero[0].Contains("胜率/登场率：测试 T5 0.0%/—"))
        {
            throw new Exception("Zero and invalid rates differ");
        }

        if (
            ChampionTierChatMessage
                .Build("大乱斗", "OP.GG", "16.17", "我方", Array.Empty<ChampionTierChatEntry>())
                .Count != 0
        )
        {
            throw new Exception("Empty roster must not send");
        }
    }

    private static void ModeSelection()
    {
        var players = new[]
        {
            new LiveMatchPlayer("a", "self", "Lux", "拉克丝", "CHAOS", true, 9, []),
            new LiveMatchPlayer("b", "enemy", "Morgana", "莫甘娜", "ORDER", false, 9, []),
        };
        var match = new LiveMatchSnapshot("ARAM", 12, 100, players);
        if (
            LiveMatchMode.RecommendationQueue(3270, match with { Mode = "KIWI" }) != 2400
            || LiveMatchMode.RecommendationQueue(0, match) != 450
            || LiveMatchMode.RecommendationQueue(3270, match with { Mode = "CLASSIC", Map = 11 }) != 0
            || LiveMatchMode.RecommendationQueue(3270, match with { Mode = "UNKNOWN" }) != 0
        )
        {
            throw new Exception(
                "Special queues must use actual ARAM/KIWI mode; unknown queues and map alone cannot select statistics"
            );
        }
    }
}
