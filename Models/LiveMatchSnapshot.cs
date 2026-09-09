using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record LiveMatchItem(int Id, string Name, int Count);
public sealed record LiveMatchPlayer(string Key, string Name, string ChampionAlias, string ChampionName,
    string Team, bool Self, int Level, LiveMatchItem[]? Items);
public sealed record LiveMatchSnapshot(string Mode, int Map, double Time, LiveMatchPlayer[] Players)
{
    public static LiveMatchSnapshot Parse(JObject data)
    {
        if(data["allPlayers"] is not JArray rows || data["gameData"] is not JObject game)
            throw new InvalidDataException("游戏数据不完整。");
        var active=data["activePlayer"] as JObject;
        string? activeId=(string?)active?["riotId"];
        string? activeName=(string?)active?["summonerName"];
        var players=rows.OfType<JObject>().Select((row,index)=>
        {
            string name=(string?)row["riotId"]??(string?)row["summonerName"]??$"玩家 {index+1}";
            bool self=!string.IsNullOrWhiteSpace(activeId)?(string?)row["riotId"]==activeId:
                !string.IsNullOrWhiteSpace(activeName) && (string?)row["summonerName"]==activeName;
            const string prefix="game_character_displayname_";
            string raw=(string?)row["rawChampionName"]??"";
            string alias=raw.StartsWith(prefix,StringComparison.Ordinal)?raw[prefix.Length..]:"";
            var items=(row["items"] as JArray)?.OfType<JObject>().Where(i=>(int?)i["itemID"]>0)
                .Select(i=>new LiveMatchItem((int)i["itemID"]!,(string?)i["displayName"]??"未知装备",Math.Max(1,(int?)i["count"]??1))).ToArray();
            return new LiveMatchPlayer(name+"|"+index,name,alias,(string?)row["championName"]??alias,
                (string?)row["team"]??"UNKNOWN",self,(int?)row["level"]??0,items);
        }).ToArray();
        return new((string?)game["gameMode"]??"未知",(int?)game["mapNumber"]??0,(double?)game["gameTime"]??0,players);
    }
    public string TeamLabel(LiveMatchPlayer player)
    {
        string? own=Players.FirstOrDefault(p=>p.Self)?.Team;
        return own==null || own=="UNKNOWN" || player.Team=="UNKNOWN" ? player.Team : player.Team==own?"我方":"对方";
    }
}
