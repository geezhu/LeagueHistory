using System.Net;
using System.Text;
using League.Services;

namespace League.Tests.Services;

/// <summary>
/// Reading a champion's client data in Chinese from CommunityDragon. The transport answers in place of the network;
/// anything that cannot be read is null, so the caller falls back to the client's own data.
/// </summary>
public class ChineseChampionDataTests
{
    private const string Neeko =
        """{"id":518,"alias":"Neeko","spells":[{"spellKey":"e","description":"造成禁锢效果"}]}""";

    private static ChineseChampionData Data(Answer answer) =>
        new("16.21", new HttpClient(answer), "https://cdn.invalid/");

    [Fact]
    public async Task LoadAsync_AChampion_ReadsThePatchsChineseFile()
    {
        var answer = new Answer(HttpStatusCode.OK, Neeko);

        await Data(answer).LoadAsync(518, CancellationToken.None);

        Assert.Equal(
            "https://cdn.invalid/16.21/plugins/rcp-be-lol-game-data/global/zh_cn/v1/champions/518.json",
            Assert.Single(answer.Requests).ToString()
        );
    }

    [Fact]
    public async Task LoadAsync_AChampion_ReturnsItsChineseDescriptions()
    {
        var data = await Data(new Answer(HttpStatusCode.OK, Neeko)).LoadAsync(518, CancellationToken.None);

        Assert.Equal("造成禁锢效果", (string?)data!["spells"]![0]!["description"]);
    }

    [Fact]
    public async Task LoadAsync_ASecondRead_ComesFromMemory()
    {
        var answer = new Answer(HttpStatusCode.OK, Neeko);
        var data = Data(answer);
        await data.LoadAsync(518, CancellationToken.None);

        await data.LoadAsync(518, CancellationToken.None);

        Assert.Single(answer.Requests);
    }

    [Fact]
    public async Task LoadAsync_AMissingFile_ReturnsNull()
    {
        var data = await Data(new Answer(HttpStatusCode.NotFound, "")).LoadAsync(518, CancellationToken.None);

        Assert.Null(data);
    }

    [Fact]
    public async Task LoadAsync_AnotherChampionsData_ReturnsNull()
    {
        var data = await Data(new Answer(HttpStatusCode.OK, Neeko)).LoadAsync(99, CancellationToken.None);

        Assert.Null(data);
    }

    [Fact]
    public async Task LoadAsync_TextThatIsNotJson_ReturnsNull()
    {
        var data = await Data(new Answer(HttpStatusCode.OK, "<html>")).LoadAsync(518, CancellationToken.None);

        Assert.Null(data);
    }

    [Fact]
    public async Task LoadAsync_AFailedRead_IsAskedAgainNextTime()
    {
        var answer = new Answer(HttpStatusCode.NotFound, "");
        var data = Data(answer);
        await data.LoadAsync(518, CancellationToken.None);

        await data.LoadAsync(518, CancellationToken.None);

        Assert.Equal(2, answer.Requests.Count);
    }

    [Fact]
    public async Task LoadAsync_Cancelled_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Data(new Answer(HttpStatusCode.OK, Neeko)).LoadAsync(518, cancelled.Token)
        );
    }

    [Fact]
    public async Task LoadAsync_TheChineseData_LetsControlRecognitionFindTheRoot()
    {
        var data = await Data(new Answer(HttpStatusCode.OK, Neeko)).LoadAsync(518, CancellationToken.None);

        var skills = ChampionControlSkills.Parse(data!);

        Assert.Equal(["禁锢"], Assert.Single(skills).Effects);
    }

    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
            );
        }
    }
}
