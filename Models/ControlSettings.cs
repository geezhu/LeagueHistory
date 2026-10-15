using Newtonsoft.Json.Linq;

namespace League.Models;

/// <summary>
/// The control page's settings as the widget writes them to <c>control-settings.json</c>: whether enemies' and
/// allies' new survival items sound. Widgets before the ally choice wrote only <c>survivalSound</c>, the enemy
/// setting, and older ones a <c>test</c> counter asking for a sample, which is ignored: the sample used a real item's
/// sound and was mistaken for a purchase.
/// </summary>
public sealed record ControlSettings(bool EnemySound, bool AllySound)
{
    public static readonly ControlSettings Default = new(true, false);

    public static ControlSettings? Parse(JObject value) =>
        value["protocol"]?.Type == JTokenType.Integer
        && value["protocol"]!.ToString() == "1"
        && value["survivalSound"]?.Type == JTokenType.Boolean
        && value["allySound"]?.Type is null or JTokenType.Boolean
            ? new((bool)value["survivalSound"]!, (bool?)value["allySound"] ?? false)
            : null;
}
