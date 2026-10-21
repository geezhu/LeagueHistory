using League.Models;
using League.Services;
using League.Tests.Support;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The chat request the widget writes and the status the bridge writes back.
///
/// Both directions are refused unless a session is published, and the request reader checks the session twice - once
/// before it reads and once after - because the roster can change while the file is being read, and a request that
/// arrived for the previous roster must not be applied to the new one.
///
/// Every test here stops before a request is accepted, because the session the bridge publishes carries a per-process
/// instance id that a test cannot know in advance. What is pinned is the refusals, and they are most of the code.
///
/// The bridges are kept and drained in <see cref="Dispose"/>: they write on pool threads, and deleting the folder
/// while a temporary file is still open turns the whole suite red on a machine that is merely busy.
/// </summary>
public class GameBarBridgeChatTests : IDisposable
{
    private readonly TempFolder _folder = new("bridge-chat");
    private readonly List<GameBarBridge> _bridges = [];

    public void Dispose()
    {
        foreach (GameBarBridge bridge in _bridges)
        {
            bridge.Dispose();
            bridge.Completion.Wait(TimeSpan.FromSeconds(10));
        }

        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    private GameBarBridge Track(GameBarBridge bridge)
    {
        _bridges.Add(bridge);
        return bridge;
    }

    private static LoadingPlayer Player(int championId, bool self, string key) =>
        new(
            Key: key,
            Name: key,
            Puuid: $"puuid-{key}",
            SummonerId: championId,
            ChampionId: championId,
            Team: self ? 200 : 100,
            Self: self
        );

    private static LoadingRoster Roster() =>
        new("123", 2400, "KIWI", [Player(2, self: true, "self-key"), Player(1, self: false, "enemy-key")]);

    /// <summary>Publishes once so the bridge has a session, then hands the bridge back.</summary>
    private GameBarBridge Published()
    {
        using var icon = new Bitmap(8, 8);
        GameBarBridge bridge = Track(new GameBarBridge(_folder.Root));
        bridge.Publish(
            Roster(),
            new Dictionary<string, PlayerChampionTier>(),
            new Dictionary<int, Image> { [1] = icon, [2] = icon },
            champion: id => "英雄" + id,
            details: _ => "",
            session: "match-1",
            status: "当前对局",
            enabled: true,
            active: true,
            show: true
        );
        return bridge;
    }

    private string RequestPath => Path.Combine(_folder.Root, "chat-request.json");

    private static string RequestJson(string session) =>
        $$"""
            {"protocol":1,"session":"{{session}}","id":"6f9619ff-8b86-d011-b42d-00cf4fc964ff",
            "target":"own","createdAt":1,"updatedAt":2,"dismissed":false,"cancelled":false}
            """;

    private static OverlayChatRequest RequestFrom(string session) => new("i", session, "own", -1, 0, 1, false, false);

    // ---- before there is anything to read ----

    [Fact]
    public async Task ReadChatRequestAsync_BeforeAnythingWasPublished_IsNothing()
    {
        // The session is what the request is matched against; without one there is nothing to read it for.
        GameBarBridge bridge = Track(new GameBarBridge(_folder.Root));

        Assert.Null(await bridge.ReadChatRequestAsync());
    }

    [Fact]
    public async Task ReadChatRequestAsync_NoFile_YieldsNothing()
    {
        GameBarBridge bridge = Published();

        Assert.Null(await bridge.ReadChatRequestAsync());
    }

    [Fact]
    public async Task ReadChatRequestAsync_AFileThatIsTooLarge_IsRefused()
    {
        // Four kilobytes is the writer's own limit; a larger file is not a request.
        GameBarBridge bridge = Published();
        await File.WriteAllTextAsync(RequestPath, new string('x', 5000));

        Assert.Null(await bridge.ReadChatRequestAsync());
    }

    [Fact]
    public async Task ReadChatRequestAsync_AFileThatIsNotJson_IsRefusedInsteadOfThrowing()
    {
        // The widget replaces the file atomically, so a read during the swap must be harmless.
        GameBarBridge bridge = Published();
        await File.WriteAllTextAsync(RequestPath, "{ not json ");

        Assert.Null(await bridge.ReadChatRequestAsync());
    }

    [Fact]
    public async Task ReadChatRequestAsync_ARequestForAnotherSession_IsRefused()
    {
        // The file names the session it belongs to; one written before the roster changed must not be applied now.
        GameBarBridge bridge = Published();
        await File.WriteAllTextAsync(RequestPath, RequestJson("another-game"));

        Assert.Null(await bridge.ReadChatRequestAsync());
    }

    [Fact]
    public async Task ReadChatRequestAsync_AfterBeingDisposed_IsNothing()
    {
        GameBarBridge bridge = Published();
        bridge.Dispose();

        Assert.Null(await bridge.ReadChatRequestAsync());
    }

    // ---- the status the bridge writes back ----

    [Fact]
    public void PublishChatStatus_AfterBeingDisposed_WritesNothing()
    {
        // A closed bridge must not start a write.
        GameBarBridge bridge = Published();
        bridge.Dispose();

        bridge.PublishChatStatus(RequestFrom("s"), "sending", "正在发送", complete: false);

        Assert.False(File.Exists(Path.Combine(_folder.Root, "chat-status.json")));
    }

    [Fact]
    public void PublishChatStatus_ARequestFromAnotherSession_WritesNothing()
    {
        // A status only answers a request the published session made; anything else would be answering another game.
        GameBarBridge bridge = Published();

        bridge.PublishChatStatus(RequestFrom("another-game"), "sending", "正在发送", complete: false);

        Assert.False(File.Exists(Path.Combine(_folder.Root, "chat-status.json")));
    }

    [Fact]
    public void PublishChatStatus_TogetherWithTheWriteLoop_WritesNoStatusForAnotherSession()
    {
        // Even when the write loop drains, a queued status for another session is skipped rather than written.
        GameBarBridge bridge = Published();
        bridge.PublishChatStatus(RequestFrom("another-game"), "sending", "x", complete: false);
        bridge.Dispose();
        bridge.Completion.Wait(TimeSpan.FromSeconds(10));

        Assert.False(File.Exists(Path.Combine(_folder.Root, "chat-status.json")));
    }

    [Fact]
    public void PublishChatStatus_WritesNothingAndLeavesNoTemporaryFileBehind()
    {
        // The write is atomic: a temporary file is renamed into place, and none is left when nothing is written.
        GameBarBridge bridge = Published();
        bridge.PublishChatStatus(RequestFrom("another-game"), "sending", "x", complete: false);
        bridge.Dispose();
        bridge.Completion.Wait(TimeSpan.FromSeconds(10));

        Assert.Empty(Directory.GetFiles(_folder.Root, "chat-*.tmp"));
    }
}
