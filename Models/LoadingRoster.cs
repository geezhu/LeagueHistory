using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record LoadingPlayer(string Key,string Name,string Puuid,long SummonerId,int ChampionId,int Team,bool Self,bool IdentityHidden=false);
public sealed record LoadingRoster(string GameId,int Queue,string Mode,LoadingPlayer[] Players)
{
    public static bool CanDisplay(string phase)=>phase is "GameStart" or "InProgress" or "Reconnect";
    public static string WaitingStatus(string phase) => phase switch
    {
        "" => "等待连接英雄联盟客户端…",
        "ChampSelect" => "选人中 · 进入加载界面后显示双方英雄。",
        "EndOfGame" or "PreEndOfGame" or "WaitingForStats" => "本局已结束 · 等待下一场对局。",
        _ => "暂无进行中的对局 · 进入加载界面后自动读取。"
    };
    public static LoadingRoster Parse(JObject flow,string ownPuuid)
    {
        var data=flow["gameData"] as JObject??new();
        var selections=(data["playerChampionSelections"] as JArray??new()).OfType<JObject>().ToArray();
        var players=new List<LoadingPlayer>();
        foreach(var (field,team) in new[]{("teamOne",100),("teamTwo",200)})
        foreach(var row in (data[field] as JArray??new()).OfType<JObject>())
        {
            string puuid=(string?)row["puuid"]??"";long id=(long?)row["summonerId"]??0;
            if(!Clients.LoadingRosterIdentityService.HasPuuid(puuid))puuid="";
            var pick=selections.FirstOrDefault(s=>puuid.Length>0 && (string?)s["puuid"]==puuid || id>0 && (long?)s["summonerId"]==id);
            int hero=(int?)row["championId"]??0;if(hero<=0)hero=(int?)pick?["championId"]??0;
            if(puuid.Length==0 && Clients.LoadingRosterIdentityService.HasPuuid((string?)pick?["puuid"]??""))puuid=(string)pick!["puuid"]!;
            string name=(string?)row["gameName"]??"";if(name.Length==0)name=(string?)row["summonerName"]??"";
            string tag=(string?)row["tagLine"]??"";if(tag.Length>0 && name.Length>0)name+="#"+tag;
            players.Add(new(puuid.Length>0?puuid:id>0?id.ToString():$"{team}:{players.Count}",name,puuid,id,hero,team,puuid.Length>0 && puuid==ownPuuid));
        }
        return new((string?)data["gameId"]??"",(int?)data["queue"]?["id"]??0,(string?)data["queue"]?["gameMode"]??(string?)data["gameMode"]??"",players.ToArray());
    }
    public LoadingRoster FromLive(LiveMatchSnapshot live,Func<LiveMatchPlayer,int> championId)=>this with
    {
        Players=live.Players.Select(p=>
        {
            // Keep account identity only for an unambiguous full-name match; never infer it from champion alone.
            var matches=Players.Where(old=>old.Name.Length>0 && old.Name==p.Name).ToArray();
            var known=matches.Length==1?matches[0]:null;
            bool hidden=p.Name.Trim()=="#";
            int team=p.Team=="ORDER"?100:p.Team=="CHAOS"?200:0;
            int hero=championId(p);
            // An unavailable Live key must not erase an LCU pick joined by confirmed account identity.
            if(hero<=0 && !hidden && known is{ChampionId:>0} && team>0 && (known.Team==0 || known.Team==team)
                && (Clients.LoadingRosterIdentityService.HasPuuid(known.Puuid) || known.SummonerId>0))hero=known.ChampionId;
            return new LoadingPlayer(known?.Key??p.Key,hidden?"隐藏玩家":p.Name,hidden?"":known?.Puuid??"",hidden?0:known?.SummonerId??0,hero,team,p.Self,hidden);
        }).ToArray()
    };
    public string TeamLabel(int team)
    {
        int own=Players.FirstOrDefault(p=>p.Self)?.Team??0;
        return own==0?(team==100?"蓝方":"红方"):team==own?"我方":"对方";
    }
}
