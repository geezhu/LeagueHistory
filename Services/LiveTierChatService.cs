using League.Clients;
using League.Models;

namespace League.Services;

public sealed class LiveTierChatService : IDisposable
{
    private readonly LiveMatchClient _live=new();
    private readonly OpggClient _opgg=new();
    private readonly AramMetaClient _am;
    public LiveTierChatService(Func<TimeSpan> lifetime)=>_am=new(cacheLifetime:lifetime);
    public async Task<IReadOnlyList<string>> BuildAsync(int queue,bool enemy,IEnumerable<Champion> champions,CancellationToken token)
    {
        ChatSendDiagnostics.Shared.Report("live","正在读取游戏内双方英雄。");
        var match=await _live.GetAsync(token);
        int originalQueue=queue;
        queue=LiveMatchMode.RecommendationQueue(queue,match);
        if(queue==0){ChatSendDiagnostics.Shared.Report("mode",$"未发送：队列 {originalQueue}、游戏模式 {match.Mode} 暂不支持，当前仅支持普通和海克斯大乱斗。");return [];}
        ChatSendDiagnostics.Shared.Report("mode-resolved",$"队列 {originalQueue}、游戏模式 {match.Mode}，使用{(queue==2400?"海克斯大乱斗":"普通大乱斗")}统计。");
        ChatSendDiagnostics.Shared.Report("statistics",$"已读取 {match.Players.Length} 位英雄，正在读取{(queue==2400?"arammeta":"OP.GG")}梯队。");
        var ranks=new Dictionary<int,(string Tier,double? Win,double? Pick)>();string version;
        if(queue==2400)
        {
            var page=await _am.GetAsync(0,false,token);version=page.Index.Patch+(page.UpdateFailed?" 旧缓存":"");
            foreach(var c in page.Index.Champions)ranks[c.Id]=(c.Tier,c.WinRate,page.Index.PickRate(c));
        }
        else
        {
            var page=await _opgg.GetTiersAsync(new("global","aram","all","none",0),false,token);version=page.Version;
            foreach(var c in page.Rows)ranks[c.ChampionId]=(c.Tier.HasValue?"T"+c.Tier:"暂无梯队",c.WinRate,c.PickRate);
        }
        // Refresh participants after a potentially slow statistics request.
        var latest=await _live.GetAsync(token);
        if(LiveMatchMode.RecommendationQueue(originalQueue,latest)!=queue || latest.Time+2<match.Time || !latest.Players.Select(p=>p.Key).SequenceEqual(match.Players.Select(p=>p.Key)))
        {ChatSendDiagnostics.Shared.Report("match-changed","未发送：对局或玩家列表已变化，请重新按快捷键。");return [];}
        var result=BuildMessages(latest,queue,enemy,champions,ranks,version);
        if(result.Count==0)ChatSendDiagnostics.Shared.Report("identity","未发送：无法确定自己的阵营，或所选阵营没有英雄。");
        else ChatSendDiagnostics.Shared.Report("prepared",$"数据已准备，将分 {result.Count} 条发送。");
        return result;
    }
    public static IReadOnlyList<string> BuildMessages(LiveMatchSnapshot match,int queue,bool enemy,IEnumerable<Champion> champions,
        IReadOnlyDictionary<int,(string Tier,double? Win,double? Pick)> ranks,string version)
    {
        var self=match.Players.FirstOrDefault(p=>p.Self);
        if(self==null || self.Team=="UNKNOWN" || queue is not(450 or 2400))return [];
        var catalog=champions.ToArray();
        var rows=match.Players.Where(p=>p.Team!="UNKNOWN" && (enemy?p.Team!=self.Team:p.Team==self.Team)).Select(p=>
        {
            var hero=LiveChampionIdentity.Resolve(p,catalog);
            var rank=ranks.GetValueOrDefault(hero?.Id??0);
            return new ChampionTierChatEntry(hero?.Name??p.ChampionName,rank.Tier??"暂无梯队",rank.Win,rank.Pick);
        });
        return ChampionTierChatMessage.Build(queue==2400?"海克斯大乱斗":"大乱斗",queue==2400?"arammeta":"OP.GG",version,enemy?"对方":"我方",rows,queue==2400);
    }
    public void Dispose(){_live.Dispose();_opgg.Dispose();_am.Dispose();}
}
