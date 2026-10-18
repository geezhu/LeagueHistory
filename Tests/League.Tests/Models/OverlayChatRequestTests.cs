using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Models;

/// <summary>The widget's chat requests, written as the widget writes them.</summary>
public class OverlayChatRequestTests
{
    private static JObject Request(string kind, string target = "enemy", int slot = -1, int hero = 0) =>
        new()
        {
            ["protocol"] = 1,
            ["id"] = Guid.NewGuid().ToString(),
            ["session"] = "s",
            ["target"] = target,
            ["kind"] = kind,
            ["slot"] = slot,
            ["championId"] = hero,
            ["updatedAt"] = 1000,
            ["dismissed"] = false,
            ["cancelled"] = false,
        };

    [Fact]
    public void Parse_HardControlsForASide_KeepsTheKind()
    {
        var request = OverlayChatRequest.Parse(Request("hardControls"), "s", new int[10], 2000);

        Assert.Equal("hardControls", request?.Kind);
    }

    [Fact]
    public void Parse_HardControlsForOneHero_IsRefused()
    {
        int[] heroes = [5, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        var request = OverlayChatRequest.Parse(Request("hardControls", "single", 0, 5), "s", heroes, 2000);

        Assert.Null(request);
    }
}
