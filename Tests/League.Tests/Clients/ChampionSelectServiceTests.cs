using System.Net;
using System.Text;
using League.Clients;
using League.PrimaryElection;
using Newtonsoft.Json.Linq;

namespace League.Tests.Clients;

/// <summary>
/// Declaring the first available hero of the priority list, on the modern champion select and on the legacy one that
/// Classic Rift (4310/4320) runs with champion ids offset by 60000. The client is a stub behind a fake host whose
/// sessions change once an action is patched, so a declaration is only reported as done when the session shows it.
/// </summary>
public class ChampionSelectServiceTests
{
    private const string ModernSession = "/lol-champ-select/v1/session";
    private const string LegacySession = "/lol-champ-select-legacy/v1/session";

    [Fact]
    public async Task AutoDeclareIntentAsync_AModernQueue_PatchesTheModernActionWithTheChampionId()
    {
        var client = new FakeClient(modern: Turn(queueId: 420, hover: 0), modernAfterPick: Turn(420, hover: 103));

        bool declared = await Service(client).AutoDeclareIntentAsync([Hero(103)]);

        Assert.True(declared, "The hover shows in the session");
        Assert.Equal(("/lol-champ-select/v1/session/actions/3", 103), client.Patched);
    }

    [Fact]
    public async Task AutoDeclareIntentAsync_ClassicRift_PatchesTheLegacyActionWithTheOffsetId()
    {
        var client = new FakeClient(
            modern: new JObject { ["queueId"] = 4310 },
            legacy: Turn(4310, hover: 0),
            legacyAfterPick: Turn(4310, hover: 60103)
        );

        bool declared = await Service(client).AutoDeclareIntentAsync([Hero(103)]);

        Assert.True(declared, "The offset hover shows in the legacy session");
        Assert.Equal(("/lol-champ-select-legacy/v1/session/actions/3", 60103), client.Patched);
    }

    [Fact]
    public async Task AutoDeclareIntentAsync_ClassicRiftWithoutAModernSession_UsesTheLegacySession()
    {
        var client = new FakeClient(legacy: Turn(4320, hover: 0), legacyAfterPick: Turn(4320, hover: 60103));

        bool declared = await Service(client).AutoDeclareIntentAsync([Hero(103)]);

        Assert.True(declared, "A legacy-only champion select is still declared");
    }

    [Fact]
    public async Task AutoDeclareIntentAsync_ClassicRiftWithTheFirstHeroBanned_SkipsItByItsOffsetId()
    {
        var banned = Turn(4310, hover: 0);
        banned["bans"] = new JObject { ["myTeamBans"] = new JArray(60103) };
        var client = new FakeClient(legacy: banned, legacyAfterPick: Turn(4310, hover: 60104));

        await Service(client).AutoDeclareIntentAsync([Hero(103), Hero(104)]);

        Assert.Equal(60104, client.Patched?.Champion);
    }

    [Fact]
    public async Task AutoDeclareIntentAsync_ASessionThatDoesNotShowThePick_ReportsNotDeclared()
    {
        var client = new FakeClient(modern: Turn(420, hover: 0));

        bool declared = await Service(client).AutoDeclareIntentAsync([Hero(103)]);

        Assert.False(declared, "The client accepted the request but the hover never appeared");
        Assert.NotNull(client.Patched);
    }

    [Fact]
    public async Task AutoDeclareIntentAsync_TheHeroAlreadyHovered_IsDoneWithoutAPatch()
    {
        var client = new FakeClient(modern: Turn(420, hover: 103));

        bool declared = await Service(client).AutoDeclareIntentAsync([Hero(103)]);

        Assert.True(declared, "Hovering the first hero is the goal");
        Assert.Null(client.Patched);
    }

    private static ChampionSelectService Service(FakeClient client)
    {
        var lcu = new LcuClient(new HttpClient(client) { BaseAddress = new Uri("https://lcu.test/") });
        return new ChampionSelectService(lcu, new GameflowService(lcu));
    }

    private static PreliminaryHero Hero(int id) =>
        new()
        {
            ChampionId = id,
            ChampionName = $"英雄{id}",
            Priority = id,
        };

    /// <summary>
    /// A champion select where action 3 is our pending pick, hovering the given session champion id. The actions are
    /// groups of actions, as the client sends them; <c>new JArray(otherArray)</c> would copy instead of nesting.
    /// </summary>
    private static JObject Turn(int queueId, int hover) =>
        new()
        {
            ["queueId"] = queueId,
            ["localPlayerCellId"] = 0,
            ["myTeam"] = new JArray(new JObject { ["cellId"] = 0, ["championPickIntent"] = hover }),
            ["actions"] = new JArray
            {
                new JArray
                {
                    new JObject
                    {
                        ["id"] = 3,
                        ["actorCellId"] = 0,
                        ["type"] = "pick",
                        ["completed"] = false,
                        ["championId"] = hover,
                    },
                },
            },
        };

    /// <summary>The client's two champion selects; each answers its after-pick session once an action was patched.</summary>
    private sealed class FakeClient(
        JObject? modern = null,
        JObject? modernAfterPick = null,
        JObject? legacy = null,
        JObject? legacyAfterPick = null
    ) : HttpMessageHandler
    {
        public (string Path, int Champion)? Patched { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Patch)
            {
                var body = JObject.Parse(await request.Content!.ReadAsStringAsync(token));
                Patched = (path, body.Value<int>("championId"));
                return Reply(HttpStatusCode.NoContent, "");
            }

            JObject? session = path switch
            {
                ModernSession => Patched == null ? modern : modernAfterPick ?? modern,
                LegacySession => Patched == null ? legacy : legacyAfterPick ?? legacy,
                _ => null,
            };
            return session == null
                ? Reply(HttpStatusCode.NotFound, "{}")
                : Reply(HttpStatusCode.OK, session.ToString());
        }

        private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
