using System.Net;
using League.Clients;
using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Tests.Clients;

/// <summary>
/// The client that reads the running game's state from the game's own loopback endpoint.
///
/// A handler is injected, so nothing leaves the process and no game has to be running. What is pinned is the address
/// it asks - one path on 127.0.0.1:2999 - and that an incomplete answer is reported rather than half-read: the
/// snapshot parser refuses a payload without players or game data, and that refusal has to reach the caller.
/// </summary>
public class LiveMatchClientTests
{
    private static string Valid() =>
        new JObject
        {
            ["allPlayers"] = new JArray(),
            ["gameData"] = new JObject
            {
                ["gameMode"] = "CLASSIC",
                ["mapNumber"] = 11,
                ["gameTime"] = 123.5,
            },
        }.ToString();

    /// <summary>Answers one body and remembers the addresses it was asked for.</summary>
    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    // ---- where it asks ----

    [Fact]
    public async Task GetAsync_AsksTheGameEndpointOnTheLoopbackPort()
    {
        // One path, on the game's own port; the client is never pointed anywhere else.
        Handler handler = new(Valid());
        using LiveMatchClient client = new(handler);

        await client.GetAsync(CancellationToken.None);

        Uri request = Assert.Single(handler.Requests);
        Assert.Equal("https://127.0.0.1:2999/liveclientdata/allgamedata", request.ToString());
    }

    [Fact]
    public async Task GetAsync_AsksOnce()
    {
        Handler handler = new(Valid());
        using LiveMatchClient client = new(handler);

        await client.GetAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    // ---- what it makes of the answer ----

    [Fact]
    public async Task GetAsync_APayload_BecomesASnapshot()
    {
        using LiveMatchClient client = new(new Handler(Valid()));

        LiveMatchSnapshot snapshot = await client.GetAsync(CancellationToken.None);

        Assert.Equal("CLASSIC", snapshot.Mode);
        Assert.Equal(11, snapshot.Map);
        Assert.Equal(123.5, snapshot.Time, 3);
        Assert.Empty(snapshot.Players);
    }

    [Fact]
    public async Task GetAsync_NoPlayersInThePayload_IsRefused()
    {
        // Half a snapshot is worse than none: every panel that reads it would show nobody.
        var payload = new JObject { ["gameData"] = new JObject() };
        using LiveMatchClient client = new(new Handler(payload.ToString()));

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetAsync(CancellationToken.None)
        );

        Assert.Equal("游戏数据不完整。", error.Message);
    }

    [Fact]
    public async Task GetAsync_NoGameDataInThePayload_IsRefused()
    {
        var payload = new JObject { ["allPlayers"] = new JArray() };
        using LiveMatchClient client = new(new Handler(payload.ToString()));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_AnErrorStatus_ReachesTheCaller()
    {
        // The game is not running or has not finished loading; the caller decides how long to wait.
        using LiveMatchClient client = new(new Handler("", HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_ACancelledCall_DoesNotFetch()
    {
        // Nothing is asked for once the caller has given up, which is what keeps a dead game from being polled.
        Handler handler = new(Valid());
        using LiveMatchClient client = new(handler);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(cancelled.Token));
        Assert.Empty(handler.Requests);
    }

    // ---- the handler it owns ----

    [Fact]
    public void Dispose_Twice_IsHarmless()
    {
        // The client is disposed on the path that closes the window; a second call must not throw.
        LiveMatchClient client = new(new Handler(Valid()));

        client.Dispose();
        client.Dispose();
    }
}
