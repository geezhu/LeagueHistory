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
        var players=new[]{new League.Models.LiveMatchPlayer("a","self","Lux","拉克丝","CHAOS",true,9,[]),new League.Models.LiveMatchPlayer("b","enemy","Morgana","莫甘娜","ORDER",false,9,[])};
        var match=new League.Models.LiveMatchSnapshot("ARAM",12,100,players);
        var heroes=new[]{new League.Models.Champion{Id=99,Alias="Lux",Name="拉克丝"},new League.Models.Champion{Id=25,Alias="Morgana",Name="莫甘娜"}};
        var ranks=new Dictionary<int,(string Tier,double? Win,double? Pick)>{{99,("T1",.53,.08)},{25,("T2",.51,.1)}};
        string ours=string.Join("",LiveTierChatService.BuildMessages(match,450,false,heroes,ranks,"16.17"));
        string enemy=string.Join("",LiveTierChatService.BuildMessages(match,2400,true,heroes,ranks,"16.17"));
        if(!ours.Contains("拉克丝 T1｜胜率 53.0%") || ours.Contains("莫甘娜") || !enemy.Contains("莫甘娜 T2｜调整后胜率 51.0%") || enemy.Contains("拉克丝"))throw new Exception("In-game tier chat must choose sides using actual self team");
        if(LiveTierChatService.BuildMessages(match with{Players=players.Select(p=>p with{Self=false}).ToArray()},450,true,heroes,ranks,"16.17").Count!=0)throw new Exception("Unknown self must not send guessed opponents");
        if(LiveTierChatService.BuildMessages(match,420,false,heroes,ranks,"16.17").Count!=0)throw new Exception("Unsupported modes must not use ARAM statistics");
        Console.WriteLine("PASS: tier chat source, map, unknown tiers, empty roster and chunking.");
    }
}
