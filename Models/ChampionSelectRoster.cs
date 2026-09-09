using Newtonsoft.Json.Linq;

namespace League.Models;

public sealed record ChampionSelectRosterEntry(int ChampionId,int CellId,bool IsSelf,bool Preview);
public sealed record ChampionSelectRoster(ChampionSelectRosterEntry[] Team,int[] Bench,bool BenchEnabled)
{
    public static ChampionSelectRoster Parse(JObject session)
    {
        int own=(int?)session["localPlayerCellId"]??-1;
        var picks=(session["actions"] as JArray??new()).OfType<JArray>().SelectMany(a=>a.OfType<JObject>()).Where(a=>(string?)a["type"]=="pick").ToArray();
        var team=(session["myTeam"] as JArray??new()).OfType<JObject>().Select(p=>
        {
            int cell=(int?)p["cellId"]??-1,id=(int?)p["championId"]??0;
            bool preview=false;
            if(id<=0){id=(int?)p["championPickIntent"]??0;preview=id>0;}
            var action=picks.LastOrDefault(a=>(int?)a["actorCellId"]==cell && (int?)a["championId"]>0);
            if(id<=0 && action!=null){id=(int)action["championId"]!;preview=true;}
            if(action!=null && (int?)action["championId"]==id && (bool?)action["completed"]==false)preview=true;
            return new ChampionSelectRosterEntry(id,cell,cell==own,preview);
        }).ToArray();
        var bench=(session["benchChampions"] as JArray??new()).OfType<JObject>().Select(c=>(int?)c["championId"]??0).Where(id=>id>0).Distinct().ToArray();
        return new(team,bench,(bool?)session["benchEnabled"]??session["benchChampions"] is JArray);
    }
    public string Signature=>string.Join(";",Team.Select(t=>$"{t.CellId}:{t.ChampionId}:{t.IsSelf}:{t.Preview}"))+"|"+string.Join(",",Bench)+"|"+BenchEnabled;
}
