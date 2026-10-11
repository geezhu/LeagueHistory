using Newtonsoft.Json.Linq;

namespace League.Models;

public static class PlayerPrivacy
{
    public static bool? IsPublic(JObject? profile) =>
        profile?["privacy"]?.ToString().ToUpperInvariant() switch
        {
            "PUBLIC" => true,
            "PRIVATE" => false,
            _ => null,
        };

    public static string Describe(JObject? profile) =>
        IsPublic(profile) switch
        {
            true => "公开",
            false => "隐藏",
            _ => "未知",
        };
}
