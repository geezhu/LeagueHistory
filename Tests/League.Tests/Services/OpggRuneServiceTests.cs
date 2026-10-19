using System.Globalization;
using System.Net;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Tests.Services;

/// <summary>
/// The rune page this tool writes through the client's perks API: the payload the client reads, the rules that keep
/// an incomplete scheme from being written, the one page a write may touch, and the check that the client really
/// switched to it. The expected values come from the two rune checks in Tests/StatisticsChecks/OpggChecks.cs, which
/// this class replaces. Nothing leaves the process: the client is the in-memory <see cref="PerksClient"/> below.
/// The complete-scheme tests are the positive control for the ones that expect a throw.
/// </summary>
public class OpggRuneServiceTests
{
    private const string Champion = "阿狸";
    private const string ToolPageName = "[League] OP.GG 阿狸";
    private const long PersonalPageId = 99;
    private const long ToolPageId = 12;

    [Theory]
    [InlineData(0, 8100)]
    [InlineData(-1, 8100)]
    [InlineData(8000, 0)]
    [InlineData(8000, -1)]
    [InlineData(8000, 8000)]
    public void CreatePayload_StylesThatCannotFormAPage_Throws(int primary, int secondary)
    {
        OpggRunes runes = Scheme(primary, secondary);

        Assert.Throws<InvalidDataException>(() => OpggRuneService.CreatePayload(runes, Champion));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 8112 })]
    [InlineData(new[] { 8112, 8139, 8136, 8138, 8105, 5008, 5001, 5002 })]
    [InlineData(new[] { 8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002, 5010 })]
    public void CreatePayload_SchemeWithoutNinePerks_Throws(int[] perks)
    {
        OpggRunes runes = SchemeWithPerks(perks);

        Assert.Throws<InvalidDataException>(() => OpggRuneService.CreatePayload(runes, Champion));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreatePayload_PerkWithoutAPositiveId_Throws(int perk)
    {
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, perk, 5001, 5002);

        Assert.Throws<InvalidDataException>(() => OpggRuneService.CreatePayload(runes, Champion));
    }

    [Fact]
    public void CreatePayload_CompleteScheme_BuildsThePageTheClientExpects()
    {
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002);

        JObject payload = OpggRuneService.CreatePayload(runes, Champion);

        Assert.Equal("[League] OP.GG 阿狸", (string?)payload["name"]);
        Assert.Equal(8000, (int?)payload["primaryStyleId"]);
        Assert.Equal(8100, (int?)payload["subStyleId"]);
        Assert.Equal(JTokenType.Array, payload["selectedPerkIds"]!.Type);
        Assert.Equal(
            new[] { 8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002 },
            payload["selectedPerkIds"]!.Values<int>().ToArray()
        );
        Assert.Equal(true, (bool?)payload["current"]);
    }

    [Theory]
    [InlineData("阿狸", "[League] OP.GG 阿狸")]
    [InlineData("Ahri", "[League] OP.GG Ahri")]
    [InlineData("", "[League] OP.GG ")]
    public void CreatePayload_ChampionName_IsAppendedToTheToolPrefix(string champion, string name)
    {
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002);

        JObject payload = OpggRuneService.CreatePayload(runes, champion);

        Assert.Equal(name, (string?)payload["name"]);
    }

    [Theory]
    [InlineData(new[] { 8112, 8139, 8136, 8138, 8105, 5008, 5001, 5008, 5002 })]
    [InlineData(new[] { 9923, 9910, 9938, 9940, 9908, 5005, 5008, 5001, 5002 })]
    public void CreatePayload_EverySlot_KeepsItsRuneIdInOrder(int[] perks)
    {
        OpggRunes runes = SchemeWithPerks(perks);

        JObject payload = OpggRuneService.CreatePayload(runes, Champion);

        Assert.Equal(perks, payload["selectedPerkIds"]!.Values<int>().ToArray());
    }

    [Fact]
    public async Task ApplyAsync_ToolPageAlreadyThere_UpdatesThatPageAndSelectsIt()
    {
        using var server = new PerksClient([
            Page(PersonalPageId, "Personal", current: true),
            Page(ToolPageId, ToolPageName),
        ]);
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002);
        JObject personal = server.Page(PersonalPageId);
        // The page this test claims the service will write to must be the one its own rule claims.
        JObject tool = Assert.Single(server.Pages, IsToolOwned);
        Assert.Equal(ToolPageId, (long?)tool["id"]);

        await OpggRuneService.ApplyAsync(server.Client, runes, Champion, CancellationToken.None);

        Assert.Equal(
            new[] { ("PUT", "/lol-perks/v1/pages/12"), ("PUT", "/lol-perks/v1/currentpage") },
            WriteCalls(server)
        );
        Assert.Equal("application/json", server.Writes[0].MediaType);
        Assert.Equal("[League] OP.GG 阿狸", (string?)JObject.Parse(server.Writes[0].Body)["name"]);
        Assert.Equal(runes.Perks, JObject.Parse(server.Writes[0].Body)["selectedPerkIds"]!.Values<int>().ToArray());
        Assert.Equal("12", server.Writes[1].Body);
        Assert.Equal(runes.Perks, server.Page(ToolPageId)["selectedPerkIds"]!.Values<int>().ToArray());
        AssertPageContentUnchanged(personal, server.Page(PersonalPageId));
    }

    [Fact]
    public async Task ApplyAsync_NoToolPage_CreatesOneAndSelectsIt()
    {
        using var server = new PerksClient([Page(PersonalPageId, "Personal", current: true)]);
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002);
        JObject personal = server.Page(PersonalPageId);
        Assert.DoesNotContain(server.Pages, IsToolOwned);

        await OpggRuneService.ApplyAsync(server.Client, runes, Champion, CancellationToken.None);

        Assert.Equal(
            new[] { ("POST", "/lol-perks/v1/pages"), ("PUT", "/lol-perks/v1/currentpage") },
            WriteCalls(server)
        );
        Assert.Equal("12", server.Writes[1].Body);
        Assert.Equal(runes.Perks, server.Page(ToolPageId)["selectedPerkIds"]!.Values<int>().ToArray());
        Assert.Equal(ToolPageId, (long?)server.CurrentPage["id"]);
        AssertPageContentUnchanged(personal, server.Page(PersonalPageId));
    }

    [Theory]
    [InlineData("Personal", true)]
    [InlineData("[League] OP.GG", true)]
    [InlineData("[League] OP.GG 阿狸", false)]
    public async Task ApplyAsync_PageThatIsNotOurEditableOne_IsNeverWritten(string name, bool editable)
    {
        using var server = new PerksClient([Page(PersonalPageId, name, editable: editable, current: true)]);
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002);
        JObject page = server.Page(PersonalPageId);
        // The fixture must really offer no page the service may claim, or the write assertion below is vacuous.
        Assert.False(IsToolOwned(page));

        await OpggRuneService.ApplyAsync(server.Client, runes, Champion, CancellationToken.None);

        Assert.Equal(("POST", "/lol-perks/v1/pages"), WriteCalls(server)[0]);
        Assert.DoesNotContain(WriteCalls(server), write => write.Path == "/lol-perks/v1/pages/99");
        AssertPageContentUnchanged(page, server.Page(PersonalPageId));
    }

    [Fact]
    public async Task ApplyAsync_WriteWithoutAPageId_FindsTheWrittenPageInTheList()
    {
        using var server = new PerksClient([Page(PersonalPageId, "Personal", current: true)], announceWrittenId: false);
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002);

        await OpggRuneService.ApplyAsync(server.Client, runes, Champion, CancellationToken.None);

        Assert.Equal(
            new[] { ("POST", "/lol-perks/v1/pages"), ("PUT", "/lol-perks/v1/currentpage") },
            WriteCalls(server)
        );
        Assert.Equal("12", server.Writes[1].Body);
        Assert.Equal(ToolPageId, (long?)server.CurrentPage["id"]);
    }

    [Fact]
    public async Task ApplyAsync_WrittenPageNeverAppearsInTheList_Throws()
    {
        using var server = new PerksClient(
            [Page(PersonalPageId, "Personal", current: true)],
            announceWrittenId: false,
            listWrittenPage: false
        );
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OpggRuneService.ApplyAsync(server.Client, runes, Champion, CancellationToken.None)
        );

        Assert.DoesNotContain(WriteCalls(server), write => write.Path == "/lol-perks/v1/currentpage");
        Assert.Equal(PersonalPageId, (long?)server.CurrentPage["id"]);
    }

    [Fact]
    public async Task ApplyAsync_ClientActivatesTheWrittenPageItself_DoesNotSelectItAgain()
    {
        using var server = new PerksClient(
            [Page(PersonalPageId, "Personal", current: true), Page(ToolPageId, ToolPageName)],
            activateOnWrite: true
        );
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002);

        await OpggRuneService.ApplyAsync(server.Client, runes, Champion, CancellationToken.None);

        Assert.Equal(new[] { ("PUT", "/lol-perks/v1/pages/12") }, WriteCalls(server));
        Assert.Equal(ToolPageId, (long?)server.CurrentPage["id"]);
    }

    [Fact]
    public async Task ApplyAsync_ReadRejected_ThrowsBeforeWriting()
    {
        using var server = new PerksClient(
            [Page(PersonalPageId, "Personal", current: true)],
            readStatus: HttpStatusCode.InternalServerError
        );
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002);
        int writesBefore = server.Writes.Count;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            OpggRuneService.ApplyAsync(server.Client, runes, Champion, CancellationToken.None)
        );

        Assert.Equal(writesBefore, server.Writes.Count);
        Assert.Equal(new[] { ("GET", "/lol-perks/v1/pages") }, server.Requests.ToArray());
    }

    [Fact]
    public async Task ApplyAsync_WriteRejected_ThrowsAndLeavesEveryPageAlone()
    {
        using var server = new PerksClient(
            [Page(PersonalPageId, "Personal", current: true), Page(ToolPageId, ToolPageName)],
            writeStatus: HttpStatusCode.Forbidden
        );
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002);
        JObject personal = server.Page(PersonalPageId);
        JObject tool = server.Page(ToolPageId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OpggRuneService.ApplyAsync(server.Client, runes, Champion, CancellationToken.None)
        );

        Assert.DoesNotContain(WriteCalls(server), write => write.Path == "/lol-perks/v1/currentpage");
        AssertPageContentUnchanged(personal, server.Page(PersonalPageId));
        AssertPageContentUnchanged(tool, server.Page(ToolPageId));
        Assert.Equal(PersonalPageId, (long?)server.CurrentPage["id"]);
    }

    [Fact]
    public async Task ApplyAsync_SelectRejected_ThrowsAndStaysOnTheOldPage()
    {
        using var server = new PerksClient(
            [Page(PersonalPageId, "Personal", current: true), Page(ToolPageId, ToolPageName)],
            selectStatus: HttpStatusCode.Forbidden
        );
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OpggRuneService.ApplyAsync(server.Client, runes, Champion, CancellationToken.None)
        );

        Assert.Equal(
            new[] { ("PUT", "/lol-perks/v1/pages/12"), ("PUT", "/lol-perks/v1/currentpage") },
            WriteCalls(server)
        );
        Assert.Equal("12", server.Writes[1].Body);
        Assert.Equal(PersonalPageId, (long?)server.CurrentPage["id"]);
    }

    [Fact]
    public async Task ApplyAsync_ClientNeverSwitchesToTheWrittenPage_Throws()
    {
        using var server = new PerksClient(
            [Page(PersonalPageId, "Personal", current: true), Page(ToolPageId, ToolPageName)],
            ignoreSelect: true
        );
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OpggRuneService.ApplyAsync(server.Client, runes, Champion, CancellationToken.None)
        );

        Assert.Equal(
            new[] { ("PUT", "/lol-perks/v1/pages/12"), ("PUT", "/lol-perks/v1/currentpage") },
            WriteCalls(server)
        );
        Assert.Equal(PersonalPageId, (long?)server.CurrentPage["id"]);
    }

    [Fact]
    public async Task ApplyAsync_TokenAlreadyCancelled_ThrowsBeforeAnyRequestAndKeepsTheGateUsable()
    {
        using var cancelled = new CancellationTokenSource();
        using var server = new PerksClient([Page(PersonalPageId, "Personal", current: true)]);
        OpggRunes runes = SchemeWithPerks(8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002);
        await cancelled.CancelAsync();
        int requestsBefore = server.Requests.Count;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OpggRuneService.ApplyAsync(server.Client, runes, Champion, cancelled.Token)
        );

        Assert.Equal(requestsBefore, server.Requests.Count);

        // The cancelled call must not leave the shared gate taken; the bound keeps a leak from hanging the run.
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await OpggRuneService.ApplyAsync(server.Client, runes, Champion, bound.Token);

        Assert.Equal(
            new[] { ("POST", "/lol-perks/v1/pages"), ("PUT", "/lol-perks/v1/currentpage") },
            WriteCalls(server)
        );
    }

    private static OpggRunes Scheme(int primary, int secondary) => new(primary, secondary, CompletePerks(), 1200, 0.52);

    private static OpggRunes SchemeWithPerks(params int[] perks) => new(8000, 8100, perks, 1200, 0.52);

    private static int[] CompletePerks() => new[] { 8112, 8139, 8136, 8138, 8105, 5008, 5008, 5001, 5002 };

    private static JObject Page(long id, string name, bool editable = true, bool current = false) =>
        new()
        {
            ["id"] = id,
            ["name"] = name,
            ["isEditable"] = editable,
            ["current"] = current,
            ["primaryStyleId"] = 8200,
            ["subStyleId"] = 8300,
            ["selectedPerkIds"] = new JArray(1, 2, 3, 4, 5, 6, 7, 8, 9),
        };

    /// <summary>The rule a page has to satisfy before the service may write to it, applied to the fixture itself.</summary>
    private static bool IsToolOwned(JObject page) =>
        (bool?)page["isEditable"] == true
        && ((string?)page["name"])?.StartsWith("[League] OP.GG ", StringComparison.Ordinal) == true;

    /// <summary>The page fields a write could change. The current flag belongs to the client and is allowed to move.</summary>
    private static void AssertPageContentUnchanged(JObject before, JObject after)
    {
        Assert.Equal((string?)before["name"], (string?)after["name"]);
        Assert.Equal((int?)before["primaryStyleId"], (int?)after["primaryStyleId"]);
        Assert.Equal((int?)before["subStyleId"], (int?)after["subStyleId"]);
        Assert.Equal(
            before["selectedPerkIds"]!.Values<int>().ToArray(),
            after["selectedPerkIds"]!.Values<int>().ToArray()
        );
    }

    private static (string Method, string Path)[] WriteCalls(PerksClient server) =>
        server.Writes.Select(write => (write.Method, write.Path)).ToArray();

    /// <summary>
    /// The client's rune pages kept in memory: the pages the account has, which one it is on, and the writes it
    /// accepted. <c>activateOnWrite</c> is a client that switches as soon as it is written to; <c>announceWrittenId</c>
    /// false answers a write without an id; <c>listWrittenPage</c> false keeps the written page out of the page list;
    /// <c>ignoreSelect</c> accepts the switch without changing the page it is on; the three statuses make that step
    /// fail instead. No request leaves the process.
    /// </summary>
    private sealed class PerksClient : IDisposable
    {
        private const string PagesPath = "/lol-perks/v1/pages";
        private const string CurrentPagePath = "/lol-perks/v1/currentpage";
        private const long NewPageId = 12;

        private readonly List<JObject> _pages;
        private readonly bool _activateOnWrite;
        private readonly bool _announceWrittenId;
        private readonly bool _listWrittenPage;
        private readonly bool _ignoreSelect;
        private readonly HttpStatusCode _readStatus;
        private readonly HttpStatusCode _writeStatus;
        private readonly HttpStatusCode _selectStatus;

        public PerksClient(
            JObject[] pages,
            bool activateOnWrite = false,
            bool announceWrittenId = true,
            bool listWrittenPage = true,
            bool ignoreSelect = false,
            HttpStatusCode readStatus = HttpStatusCode.OK,
            HttpStatusCode writeStatus = HttpStatusCode.OK,
            HttpStatusCode selectStatus = HttpStatusCode.OK
        )
        {
            _activateOnWrite = activateOnWrite;
            _announceWrittenId = announceWrittenId;
            _listWrittenPage = listWrittenPage;
            _ignoreSelect = ignoreSelect;
            _readStatus = readStatus;
            _writeStatus = writeStatus;
            _selectStatus = selectStatus;
            _pages = pages.Select(page => (JObject)page.DeepClone()).ToList();
            Client = new HttpClient(new Handler(SendAsync)) { BaseAddress = new Uri("https://test.invalid") };
        }

        public HttpClient Client { get; }

        public List<(string Method, string Path)> Requests { get; } = new();

        public List<(string Method, string Path, string MediaType, string Body)> Writes { get; } = new();

        public JObject[] Pages => _pages.Select(page => (JObject)page.DeepClone()).ToArray();

        public JObject CurrentPage
        {
            get
            {
                JObject? current = _pages.FirstOrDefault(page => (bool?)page["current"] == true);
                return current == null ? new JObject() : (JObject)current.DeepClone();
            }
        }

        public JObject Page(long id) => (JObject)_pages.First(page => (long?)page["id"] == id).DeepClone();

        public void Dispose() => Client.Dispose();

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            Requests.Add((request.Method.Method, path));
            if (request.Method == HttpMethod.Get && path == CurrentPagePath)
            {
                return Json(CurrentPage);
            }

            if (request.Method == HttpMethod.Get && path == PagesPath)
            {
                return _readStatus == HttpStatusCode.OK
                    ? Json(new JArray(Pages))
                    : new HttpResponseMessage(_readStatus);
            }

            if (request.Method == HttpMethod.Put && path == CurrentPagePath)
            {
                string selection = await Body(request, token);
                Writes.Add((request.Method.Method, path, MediaType(request), selection));
                return _selectStatus == HttpStatusCode.OK ? Select(selection) : new HttpResponseMessage(_selectStatus);
            }

            if (
                (request.Method == HttpMethod.Post && path == PagesPath)
                || (request.Method == HttpMethod.Put && path.StartsWith(PagesPath + "/", StringComparison.Ordinal))
            )
            {
                string body = await Body(request, token);
                Writes.Add((request.Method.Method, path, MediaType(request), body));
                return _writeStatus == HttpStatusCode.OK ? Write(path, body) : new HttpResponseMessage(_writeStatus);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private HttpResponseMessage Write(string path, string body)
        {
            long id =
                path == PagesPath
                    ? NewPageId
                    : long.Parse(path[(PagesPath.Length + 1)..], CultureInfo.InvariantCulture);
            var written = JObject.Parse(body);
            written["id"] = id;
            written["isEditable"] = true;
            _pages.RemoveAll(page => (long?)page["id"] == id);
            if (_listWrittenPage)
            {
                _pages.Add(written);
                if (_activateOnWrite)
                {
                    SetCurrent(written);
                }
            }

            return Json(_announceWrittenId ? new JObject { ["id"] = id } : new JObject());
        }

        private HttpResponseMessage Select(string selection)
        {
            if (_ignoreSelect)
            {
                return Json(new JObject());
            }

            long id = long.Parse(selection, CultureInfo.InvariantCulture);
            JObject? target = _pages.FirstOrDefault(page => (long?)page["id"] == id);
            if (target != null)
            {
                SetCurrent(target);
            }

            return Json(new JObject());
        }

        private void SetCurrent(JObject page)
        {
            foreach (JObject existing in _pages)
            {
                existing["current"] = ReferenceEquals(existing, page);
            }
        }

        private static async Task<string> Body(HttpRequestMessage request, CancellationToken token) =>
            await request.Content!.ReadAsStringAsync(token);

        private static string MediaType(HttpRequestMessage request) => request.Content!.Headers.ContentType!.MediaType!;

        private static HttpResponseMessage Json(JToken value) =>
            new(HttpStatusCode.OK) { Content = new StringContent(value.ToString()) };
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            return await send(request, token);
        }
    }
}
