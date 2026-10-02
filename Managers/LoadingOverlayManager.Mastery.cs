using League.Clients;
using League.Models;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    private readonly Dictionary<string,PlayerChampionMastery> _masteries=new();
    private readonly HashSet<string> _masteryRequests=new();
    private async Task LoadMastery(LoadingPlayer player,int generation)
    {
        string requestKey=$"{generation}:{player.Key}:{player.ChampionId}";
        if(!_masteryRequests.Add(requestKey))return;
        string puuid=player.Puuid;
        if(player.Self && (string.IsNullOrWhiteSpace(puuid) || puuid==Guid.Empty.ToString()))puuid=_own;
        try
        {
            var page=player.IdentityHidden && !player.Self ? null : await ChampionMasteryService.Shared.GetAsync(puuid,_life.Token);
            if(_disposed || generation!=_generation || _roster?.Players.Any(p=>p.Key==player.Key && p.ChampionId==player.ChampionId)!=true)return;
            var mastery=page?.ForChampion(player.ChampionId)??PlayerChampionMastery.Unavailable(player.ChampionId);
            if(_masteries.GetValueOrDefault(player.Key)!=mastery){_masteries[player.Key]=mastery;SchedulePaint();}
        }
        catch(OperationCanceledException){}
        catch(Exception)
        {
            if(!_disposed && generation==_generation && _roster?.Players.Any(p=>p.Key==player.Key && p.ChampionId==player.ChampionId)==true)
            {_masteries[player.Key]=new(player.ChampionId,null,null,null,false,Message:"英雄成就暂不可用，后台稍后重试。");SchedulePaint();}
        }
        finally{_masteryRequests.Remove(requestKey);}
    }
}
