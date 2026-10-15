using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>
/// The control page's settings as the widget writes them to <c>control-settings.json</c>: whether enemies' and
/// allies' new survival items sound. <see cref="Test"/> changes when the user turns either on, asking for one sample,
/// as the respawn marks do. Widgets before the ally choice wrote only <c>survivalSound</c>, the enemy setting.
/// </summary>
public sealed record ControlSettings(bool EnemySound, bool AllySound, long Test)
{
    public static readonly ControlSettings Default = new(true, false, 0);

    public static ControlSettings? Parse(JObject value) =>
        value["protocol"]?.Type == JTokenType.Integer
        && value["protocol"]!.ToString() == "1"
        && value["survivalSound"]?.Type == JTokenType.Boolean
        && value["allySound"]?.Type is null or JTokenType.Boolean
        && value["test"]?.Type == JTokenType.Integer
            ? new((bool)value["survivalSound"]!, (bool?)value["allySound"] ?? false, (long)value["test"]!)
            : null;
}
