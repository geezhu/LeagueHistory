using Newtonsoft.Json.Linq;

namespace League.Tests.Support;

/// <summary>
/// The captured arammeta responses under <c>TestData/AramMeta</c>: three champions with three recommendations per
/// rarity, and one champion's detail. The two halves of the same capture are joined here the way the check in
/// Tests/StatisticsChecks/AramMetaChecks.cs joined them, so the index carries the augment metadata and the detail
/// carries the full augment pool.
/// </summary>
internal static class AramMetaFixture
{
    public const string Patch = "16.17";

    public const string Version = "20260908-1788870236";

    public const int Ahri = 103;

    public static JObject Index()
    {
        var index = Json("index");
        index["augs"] = Json("103-augments")["augs"]!.DeepClone();
        return index;
    }

    public static JObject Detail()
    {
        var detail = Json("103");
        detail["poolAugments"] = Json("103-augments")["detail"]!["poolAugments"]!.DeepClone();
        return detail;
    }

    public static JObject Json(string name) =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "AramMeta", name + ".json")));
}
