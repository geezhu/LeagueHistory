using Newtonsoft.Json.Linq;

namespace League.Tests.Support;

/// <summary>Annie's game data and tooltips as CommunityDragon serves them; see TestData/CommunityDragon/README.md.</summary>
internal static class CommunityDragonFixture
{
    public static string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "TestData", "CommunityDragon");

    /// <summary>A fresh copy each time, so a test cannot change what the next one reads.</summary>
    public static JObject AnnieBin() => JObject.Parse(File.ReadAllText(Path.Combine(Directory, "annie.bin.json")));

    public static string AnnieStringTablePath { get; } = Path.Combine(Directory, "annie.stringtable.json");
}
