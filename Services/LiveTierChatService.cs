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
        if(queue is not(450 or 2400))return [];
        var match=await _live.GetAsync(token);
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
        if(latest.Time+2<match.Time || !latest.Players.Select(p=>p.Key).SequenceEqual(match.Players.Select(p=>p.Key)))return [];
        return BuildMessages(latest,queue,enemy,champions,ranks,version);
    }
    public static IReadOnlyList<string> BuildMessages(LiveMatchSnapshot match,int queue,bool enemy,IEnumerable<Champion> champions,
        IReadOnlyDictionary<int,(string Tier,double? Win,double? Pick)> ranks,string version)
    {
        var self=match.Players.FirstOrDefault(p=>p.Self);
        if(self==null || self.Team=="UNKNOWN" || queue is not(450 or 2400))return [];
        var catalog=champions.ToArray();
        var rows=match.Players.Where(p=>p.Team!="UNKNOWN" && (enemy?p.Team!=self.Team:p.Team==self.Team)).Select(p=>
        {
            var hero=catalog.FirstOrDefault(c=>!string.IsNullOrWhiteSpace(p.ChampionAlias) && c.Alias.Equals(p.ChampionAlias,StringComparison.OrdinalIgnoreCase));
            var rank=ranks.GetValueOrDefault(hero?.Id??0);
            return new ChampionTierChatEntry(hero?.Name??p.ChampionName,rank.Tier??"暂无梯队",rank.Win,rank.Pick);
        });
        return ChampionTierChatMessage.Build(queue==2400?"海克斯大乱斗":"大乱斗",queue==2400?"arammeta":"OP.GG",version,enemy?"对方":"我方",rows,queue==2400);
    }
    public void Dispose(){_live.Dispose();_opgg.Dispose();_am.Dispose();}
}
