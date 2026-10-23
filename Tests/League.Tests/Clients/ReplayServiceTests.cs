using System.Net;
using System.Text;
using League.Clients;

namespace League.Tests.Clients;

/// <summary>
/// How a replay download ends, decided from the client's replay metadata. Every answer comes from a stub transport
/// behind a fake host; the paths that wait for a download to progress poll every two seconds and are not driven here.
/// </summary>
public class ReplayServiceTests
{
    private const string Metadata = "/lol-replays/v1/metadata/5";
    private const string Download = "/lol-replays/v1/rofls/5/download";

    [Fact]
    public async Task DownloadReplayAsync_AFinishedReplay_AlreadyExists()
    {
        var routes = new Routes(_ => (HttpStatusCode.OK, """{"state":"watch","downloadProgress":100}"""));

        ReplayDownloadResult result = await Service(routes).DownloadReplayAsync(5);

        Assert.Equal(ReplayDownloadResult.AlreadyExists, result);
    }

    [Fact]
    public async Task DownloadReplayAsync_AnIncompatibleReplay_IsExpiredWithoutADownload()
    {
        var routes = new Routes(_ => (HttpStatusCode.OK, """{"state":"incompatible","downloadProgress":0}"""));

        ReplayDownloadResult result = await Service(routes).DownloadReplayAsync(5);

        Assert.Equal(ReplayDownloadResult.Incompatible, result);
        Assert.Equal([Metadata], routes.Paths);
    }

    [Fact]
    public async Task DownloadReplayAsync_ARefusedDownload_IsARequestError()
    {
        var routes = new Routes(path =>
            path == Metadata ? (HttpStatusCode.NotFound, "{}") : (HttpStatusCode.InternalServerError, "{}")
        );

        ReplayDownloadResult result = await Service(routes).DownloadReplayAsync(5);

        Assert.Equal(ReplayDownloadResult.RequestError, result);
        Assert.Equal([Metadata, Download], routes.Paths);
    }

    // The client has reported progress like 4294967295; out-of-range progress counts as not started.
    [Theory]
    [InlineData("4294967295")]
    [InlineData("-1")]
    [InlineData("\"lots\"")]
    public async Task DownloadReplayAsync_ProgressOutOfRange_IsNotAFinishedReplay(string progress)
    {
        var routes = new Routes(path =>
            path == Metadata
                ? (HttpStatusCode.OK, $$"""{"state":"watch","downloadProgress":{{progress}}}""")
                : (HttpStatusCode.InternalServerError, "{}")
        );

        ReplayDownloadResult result = await Service(routes).DownloadReplayAsync(5);

        Assert.NotEqual(ReplayDownloadResult.AlreadyExists, result);
    }

    private static ReplayService Service(Routes routes) =>
        new(new LcuClient(new HttpClient(routes) { BaseAddress = new Uri("https://lcu.test/") }));

    private sealed class Routes(Func<string, (HttpStatusCode Status, string Body)> reply) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            var (status, body) = reply(path);
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
        }
    }
}
