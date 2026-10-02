using Newtonsoft.Json.Linq;

namespace League.Models;

// Commands select existing roster entries; the widget cannot supply chat text.
public sealed record OverlayChatRequest(string Id,string Session,string Target,int Slot,int ChampionId,long CreatedAt,bool Dismissed,bool Cancelled,string Kind="tier")
{
    public static OverlayChatRequest? Parse(JObject value,string session,int[] heroes,long now)
    {
        if(value["protocol"]?.Type!=JTokenType.Integer || value["protocol"]!.ToString()!="1" ||
           value["session"]?.Type!=JTokenType.String || (string?)value["session"]!=session || session.Length==0 ||
           value["id"]?.Type!=JTokenType.String || !Guid.TryParse((string?)value["id"],out var id) ||
           value["target"]?.Type!=JTokenType.String || value["updatedAt"]?.Type!=JTokenType.Integer ||
           value["slot"]?.Type!=JTokenType.Integer || value["championId"]?.Type!=JTokenType.Integer ||
           value["dismissed"]?.Type!=JTokenType.Boolean || value["cancelled"]?.Type!=JTokenType.Boolean)return null;
        if(!long.TryParse(value["updatedAt"]!.ToString(),out long stamp) || stamp<0 || stamp>now || now-stamp>30000 ||
           !int.TryParse(value["slot"]!.ToString(),out int slot) || !int.TryParse(value["championId"]!.ToString(),out int hero))return null;
        string target=(string)value["target"]!;
        if(value["kind"]!=null && (value["kind"]!.Type!=JTokenType.String || (string?)value["kind"] is not ("tier" or "mastery")))return null;
        string kind=(string?)value["kind"]??"tier";
        if(target=="single" ? slot<0 || slot>=heroes.Length || hero<=0 || heroes[slot]!=hero :
           target is not ("own" or "enemy") || slot!=-1 || hero!=0)return null;
        return new(id.ToString("N"),session,target,slot,hero,stamp,(bool)value["dismissed"]!,(bool)value["cancelled"]!,kind);
    }
    public bool SameCommand(OverlayChatRequest other)=>Id==other.Id && Session==other.Session && Target==other.Target &&
        Slot==other.Slot && ChampionId==other.ChampionId && CreatedAt==other.CreatedAt && Kind==other.Kind;
}

// One click, at most one send. IDs stay consumed for the whole match, including failures.
public sealed class OverlayChatRequests
{
    private string _session="";
    private readonly HashSet<string> _seen=new();
    public bool Take(OverlayChatRequest request)
    {
        if(_session!=request.Session){_seen.Clear();_session=request.Session;}
        return _seen.Count<256 && _seen.Add(request.Id);
    }
}

public enum OverlayChatReadiness { Waiting,Ready,Cancelled,TimedOut }

public sealed class OverlayChatReady(OverlayChatRequest request)
{
    private long? _stableSince;
    public OverlayChatReadiness Observe(OverlayChatRequest? latest,long now,bool gameReady)
    {
        if(latest==null || !request.SameCommand(latest) || latest.Cancelled)return OverlayChatReadiness.Cancelled;
        if(now<request.CreatedAt || now-request.CreatedAt>=20000)return OverlayChatReadiness.TimedOut;
        if(!latest.Dismissed || !gameReady){_stableSince=null;return OverlayChatReadiness.Waiting;}
        _stableSince??=now;
        return now-_stableSince>=600?OverlayChatReadiness.Ready:OverlayChatReadiness.Waiting;
    }
}
