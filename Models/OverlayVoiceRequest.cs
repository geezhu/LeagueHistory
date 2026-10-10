using Newtonsoft.Json.Linq;

namespace League.Models;

// A short lease for recording only. This protocol never authorizes chat input.
public sealed record OverlayVoiceRequest(string Id,string Session,long CreatedAt,long UpdatedAt,string Action,long Ack)
{
    public static OverlayVoiceRequest? Parse(JObject value,string session,long now)
    {
        if(session.Length==0 || value["protocol"]?.Type!=JTokenType.Integer || (int?)value["protocol"]!=1 ||
            value["session"]?.Type!=JTokenType.String || (string?)value["session"]!=session ||
            value["id"]?.Type!=JTokenType.String || !Guid.TryParse((string?)value["id"],out var id) ||
            value["action"]?.Type!=JTokenType.String || (string?)value["action"] is not ("start" or "stop" or "cancel") ||
            value["createdAt"]?.Type!=JTokenType.Integer || value["updatedAt"]?.Type!=JTokenType.Integer ||
            value["ack"]?.Type!=JTokenType.Integer || value["text"]!=null || value["confirmed"]!=null)return null;
        if(!long.TryParse(value["createdAt"]!.ToString(),out long created) || !long.TryParse(value["updatedAt"]!.ToString(),out long updated) ||
            !long.TryParse(value["ack"]!.ToString(),out long ack) || created<0 || updated<created || updated>now || now-updated>3000 ||
            now-created>60000 || ack<0 || ack>1000000)return null;
        return new(id.ToString("N"),session,created,updated,(string)value["action"]!,ack);
    }
    public bool SameRecording(OverlayVoiceRequest other)=>Id==other.Id && Session==other.Session && CreatedAt==other.CreatedAt;
}
