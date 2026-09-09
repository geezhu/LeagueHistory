using League.Services;

internal static class ChampionTierChatChecks
{
    public static void Run()
    {
        var messages=ChampionTierChatMessage.Build("海克斯大乱斗","arammeta","16.17","我方",new[]{new ChampionTierChatEntry("拉克丝","T1",.532,.086),new ChampionTierChatEntry("莫甘娜","暂无梯队",null,null)},true);
        if(messages.Count!=1 || !messages[0].Contains("海克斯大乱斗·我方") || !messages[0].Contains("arammeta 16.17") || !messages[0].Contains("莫甘娜 暂无梯队"))throw new Exception("Tier notice source, map and missing data");
        if(!messages[0].Contains("调整后胜率 53.2%｜样本选取率 8.6%") || !messages[0].Contains("样本选取率 暂无数据"))throw new Exception("Mayhem rate definitions and missing values");
        var many=Enumerable.Range(0,30).Select(i=>new ChampionTierChatEntry($"英雄{i}","T2",.5,.01)).ToArray();
        var split=ChampionTierChatMessage.Build("大乱斗","OP.GG","16.17","交换池",many);
        if(split.Count<2 || split.Any(s=>s.Length>180) || many.Any(row=>!string.Join("|",split).Contains(row.Name+" T2｜胜率 50.0%｜登场率 1.0%")))throw new Exception("Tier notice chunking must retain every hero and rates");
        var zero=ChampionTierChatMessage.Build("大乱斗","OP.GG","16.17","我方",new[]{new ChampionTierChatEntry("测试","T5",0,double.NaN)});
        if(!zero[0].Contains("胜率 0.0%｜登场率 暂无数据"))throw new Exception("Zero and invalid rates differ");
        if(ChampionTierChatMessage.Build("大乱斗","OP.GG","16.17","我方",Array.Empty<ChampionTierChatEntry>()).Count!=0)throw new Exception("Empty roster must not send");
        Console.WriteLine("PASS: tier chat source, map, unknown tiers, empty roster and chunking.");
    }
}
