using Newtonsoft.Json.Linq;

namespace League.Models;

public static class MatchMap
{
    // Client game-mode-mutators.json: match ExpandedMutator case-insensitively.
    private static readonly Dictionary<string,string> BridgeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MapSkin_Map12_Bloom"] = "莲华栈桥",
        ["MapSkin_HA_Bilgewater"] = "屠夫之桥",
        ["MapSkin_HA_Crepe"] = "进步之桥"
    };

    private static string GetBridgeName(JObject raw)
    {
        if (raw["gameModeMutators"] is not JArray mutators) return "随机地图";
        foreach (var mutator in mutators)
            if (mutator.Type == JTokenType.String && BridgeNames.TryGetValue((string)mutator!,out var name)) return name;
        // An explicitly supplied list without an override uses the client's base map.
        return "嚎哭深渊";
    }

    public static string GetName(JObject? raw)
    {
        raw = raw?["json"] as JObject ?? raw;
        raw = raw?["info"] as JObject ?? raw;
        if (raw?["mapId"]?.Type != JTokenType.Integer) return "未知地图";
        return (int)raw["mapId"]! switch
        {
            11 => "召唤师峡谷",
            12 => GetBridgeName(raw),
            21 => "极限闪击",
            22 => "云顶之弈",
            30 => "斗魂竞技场",
            33 => "无尽狂潮",
            35 => "班德尔之森",
            453 => "经典召唤师峡谷",
            _ => $"地图 {raw["mapId"]}"
        };
    }
}
