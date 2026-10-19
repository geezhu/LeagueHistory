using System.Net;
using System.Security.Cryptography;
using System.Text;
using League.Services;
using League.Tests.Support;

namespace League.Tests.Services;

/// <summary>
/// The pinned speech model files: what a directory reports about them, the verified download with its resume and
/// mirror fallback, and the hand-import of a file a browser downloaded. Every store is built over a temporary
/// directory with an injected stub transport and a few invented bytes as the pinned files, so no request leaves
/// the process and the user's own model directory is never read or written.
/// </summary>
public class VoiceModelStoreTests
{
    private const string UpstreamHost = "https://huggingface.co/";

    /// <summary>The invented contents of the two pinned files; their hashes are computed for real.</summary>
    private static byte[] ModelBytes => Encoding.UTF8.GetBytes("model-bytes");

    private static byte[] TokensBytes => Encoding.UTF8.GetBytes("tokens");

    /// <summary>The two pinned files every test injects instead of the real 228 MiB model.</summary>
    private static VoiceModelFile[] Pinned => [Model("a.bin", ModelBytes), Model("b.txt", TokensBytes)];

    /// <summary>The pinned revision both sources serve, taken from the public upstream URL.</summary>
    private static string Revision => VoiceModelStore.Source[UpstreamHost.Length..];

    [Fact]
    public void ModelId_PinnedModel_IsTheSenseVoiceInt8Release()
    {
        Assert.Equal("SenseVoice-2024-07-17-int8", VoiceModelStore.ModelId);
    }

    [Fact]
    public void DirectoryPath_ConstructedStore_IsTheDirectoryItWasGiven()
    {
        using var folder = new TempFolder("league-voice-directory");
        var store = Store(folder, PinnedSource());

        Assert.Equal(folder.Root, store.DirectoryPath);
    }

    [Fact]
    public void MissingFilesAndAvailableDirectory_NothingPresent_ListsBothFilesAndOffersNoDirectory()
    {
        using var folder = new TempFolder("league-voice-absent");
        var store = Store(folder, PinnedSource());

        Assert.Equal(new[] { "a.bin", "b.txt" }, store.MissingFiles);
        Assert.Null(store.AvailableDirectory);
    }

    [Fact]
    public void MissingFilesAndAvailableDirectory_EveryPinnedFilePresent_ReportsNothingMissingAndTheDirectory()
    {
        using var folder = new TempFolder("league-voice-present");
        var store = Store(folder, PinnedSource());
        WritePinnedFiles(folder);

        Assert.Empty(store.MissingFiles);
        Assert.Equal(folder.Root, store.AvailableDirectory);
    }

    [Fact]
    public void MissingFilesAndAvailableDirectory_PinnedFileOfTheWrongSize_ListsItAndOffersNoDirectory()
    {
        using var folder = new TempFolder("league-voice-size");
        var store = Store(folder, PinnedSource());
        WritePinnedFiles(folder);
        File.WriteAllBytes(folder.File("b.txt"), new byte[TokensBytes.Length + 1]);

        Assert.Equal(new[] { "b.txt" }, store.MissingFiles);
        Assert.Null(store.AvailableDirectory);
    }

    [Fact]
    public void DownloadLinks_TwoPinnedFiles_OfferEachFileOnBothSources()
    {
        using var folder = new TempFolder("league-voice-links");
        var store = Store(folder, PinnedSource());

        var links = store.DownloadLinks.ToArray();

        Assert.Equal(new[] { "a.bin", "a.bin", "b.txt", "b.txt" }, links.Select(link => link.File));
        Assert.Equal(
            new[] { "hf-mirror.com", "huggingface.co", "hf-mirror.com", "huggingface.co" },
            links.Select(link => link.Source)
        );
    }

    [Theory]
    [InlineData(0, "hf-mirror.com", "a.bin")]
    [InlineData(1, "huggingface.co", "a.bin")]
    [InlineData(2, "hf-mirror.com", "b.txt")]
    [InlineData(3, "huggingface.co", "b.txt")]
    public void DownloadLinks_EveryUrl_NamesThePinnedFileOnItsSource(int index, string host, string file)
    {
        using var folder = new TempFolder("league-voice-link-url");
        var store = Store(folder, PinnedSource());

        string url = store.DownloadLinks.ElementAt(index).Url;

        Assert.Equal("https://" + host + "/" + Revision + file + "?download=true", url);
    }

    [Fact]
    public async Task ValidateAsync_NoPinnedFilePresent_ThrowsVoiceRecognitionException()
    {
        using var folder = new TempFolder("league-voice-validate-absent");
        var store = Store(folder, PinnedSource());

        await Assert.ThrowsAsync<VoiceRecognitionException>(() => store.ValidateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_EveryPinnedFilePresent_ReturnsTheStoreDirectory()
    {
        using var folder = new TempFolder("league-voice-validate-present");
        var store = Store(folder, PinnedSource());
        WritePinnedFiles(folder);

        string directory = await store.ValidateAsync(CancellationToken.None);

        Assert.Equal(folder.Root, directory);
    }

    [Fact]
    public async Task ValidateAsync_FileOfThePinnedSizeWithTheWrongHash_ThrowsVoiceRecognitionException()
    {
        using var folder = new TempFolder("league-voice-validate-hash");
        var store = Store(folder, PinnedSource());
        WritePinnedFiles(folder);
        await File.WriteAllBytesAsync(folder.File("a.bin"), new byte[ModelBytes.Length]);

        await Assert.ThrowsAsync<VoiceRecognitionException>(() => store.ValidateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_VerifiedFileRewrittenInPlace_ThrowsVoiceRecognitionException()
    {
        using var folder = new TempFolder("league-voice-validate-rewrite");
        var store = Store(folder, PinnedSource());
        WritePinnedFiles(folder);
        Assert.Equal(folder.Root, await store.ValidateAsync(CancellationToken.None));
        await File.WriteAllBytesAsync(folder.File("a.bin"), new byte[ModelBytes.Length]);
        // Only a newer timestamp tells the second validation that the file it already verified has changed.
        File.SetLastWriteTimeUtc(folder.File("a.bin"), DateTime.UtcNow.AddSeconds(5));

        await Assert.ThrowsAsync<VoiceRecognitionException>(() => store.ValidateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_NoPinnedFilePresent_WritesBothPinnedFiles()
    {
        using var folder = new TempFolder("league-voice-download");
        var store = Store(folder, PinnedSource());

        await store.DownloadAsync(null, CancellationToken.None);

        Assert.Equal(ModelBytes, await File.ReadAllBytesAsync(folder.File("a.bin")));
        Assert.Equal(TokensBytes, await File.ReadAllBytesAsync(folder.File("b.txt")));
    }

    [Fact]
    public async Task DownloadAsync_Complete_WritesTheLicense()
    {
        using var folder = new TempFolder("league-voice-license");
        var store = Store(folder, PinnedSource());

        await store.DownloadAsync(null, CancellationToken.None);

        Assert.True(File.Exists(folder.File("LICENSE")));
    }

    [Fact]
    public async Task DownloadAsync_Complete_LeavesNothingMissing()
    {
        using var folder = new TempFolder("league-voice-complete");
        var store = Store(folder, PinnedSource());

        await store.DownloadAsync(null, CancellationToken.None);

        Assert.Empty(store.MissingFiles);
    }

    [Fact]
    public async Task DownloadAsync_Complete_EndsAtOneHundredPercent()
    {
        using var folder = new TempFolder("league-voice-progress-end");
        var store = Store(folder, PinnedSource());
        var progress = new ProgressRecorder();

        await store.DownloadAsync(progress, CancellationToken.None);

        Assert.Equal(100, progress.Values[^1]);
    }

    [Fact]
    public async Task DownloadAsync_Complete_ReportsProgressWhileFilesAreStillArriving()
    {
        using var folder = new TempFolder("league-voice-progress-mid");
        var store = Store(folder, PinnedSource());
        var progress = new ProgressRecorder();

        await store.DownloadAsync(progress, CancellationToken.None);

        Assert.InRange(progress.Values[0], 1, 99);
    }

    [Fact]
    public async Task DownloadAsync_Complete_NeverReportsProgressThatFalls()
    {
        using var folder = new TempFolder("league-voice-progress-order");
        var store = Store(folder, PinnedSource());
        var progress = new ProgressRecorder();

        await store.DownloadAsync(progress, CancellationToken.None);

        Assert.Equal(progress.Values.OrderBy(value => value), progress.Values);
    }

    [Fact]
    public async Task DownloadAsync_PartialFileOnDisk_ContinuesTheFileToItsPinnedContent()
    {
        using var folder = new TempFolder("league-voice-resume");
        await File.WriteAllBytesAsync(folder.File("a.bin.download"), ModelBytes[..5]);
        var store = Store(folder, ResumingSource());

        await store.DownloadAsync(null, CancellationToken.None);

        Assert.Equal(ModelBytes, await File.ReadAllBytesAsync(folder.File("a.bin")));
    }

    [Fact]
    public async Task DownloadAsync_PartialFileOnDisk_AsksTheSourceForTheRestOfTheFile()
    {
        using var folder = new TempFolder("league-voice-resume-range");
        await File.WriteAllBytesAsync(folder.File("a.bin.download"), ModelBytes[..5]);
        var handler = ResumingSource();
        var store = Store(folder, handler);

        await store.DownloadAsync(null, CancellationToken.None);

        Assert.Equal(new long?[] { 5, null }, handler.Ranges);
    }

    [Fact]
    public async Task DownloadAsync_SourceIgnoresTheRange_StartsTheFileOverInsteadOfAppending()
    {
        using var folder = new TempFolder("league-voice-restart");
        await File.WriteAllBytesAsync(folder.File("a.bin.download"), ModelBytes[..5]);
        var store = Store(folder, PinnedSource());

        await store.DownloadAsync(null, CancellationToken.None);

        Assert.Equal(ModelBytes, await File.ReadAllBytesAsync(folder.File("a.bin")));
    }

    [Fact]
    public async Task DownloadAsync_MoreBytesThanThePinnedSize_ThrowsIOException()
    {
        using var folder = new TempFolder("league-voice-oversize");
        var store = Store(folder, new StubTransport(_ => (HttpStatusCode.OK, new byte[ModelBytes.Length + 8])));

        await Assert.ThrowsAsync<IOException>(() => store.DownloadAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_RefusedForOversizeData_PublishesNothing()
    {
        using var folder = new TempFolder("league-voice-unpublished");
        var store = Store(folder, new StubTransport(_ => (HttpStatusCode.OK, new byte[ModelBytes.Length + 8])));

        await Assert.ThrowsAsync<IOException>(() => store.DownloadAsync(null, CancellationToken.None));

        Assert.Equal(new[] { "a.bin", "b.txt" }, store.MissingFiles);
    }

    [Fact]
    public async Task DownloadAsync_EverySourceRefuses_ThrowsIOExceptionAfterTryingBothSources()
    {
        using var folder = new TempFolder("league-voice-refused");
        var handler = new StubTransport(_ => (HttpStatusCode.InternalServerError, []));
        var store = Store(folder, handler);

        await Assert.ThrowsAsync<IOException>(() => store.DownloadAsync(null, CancellationToken.None));

        Assert.Equal(new[] { "hf-mirror.com", "huggingface.co" }, handler.Urls.Select(url => new Uri(url).Host));
    }

    [Theory]
    [InlineData("a.bin")]
    [InlineData("b.txt")]
    public async Task DownloadAsync_MirrorRefuses_WritesTheFileFromTheUpstreamSource(string name)
    {
        using var folder = new TempFolder("league-voice-fallback");
        var store = Store(folder, new StubTransport(request => Fallback(request.RequestUri!.AbsoluteUri)));

        await store.DownloadAsync(null, CancellationToken.None);

        Assert.Equal(PinnedContent(name), await File.ReadAllBytesAsync(folder.File(name)));
    }

    [Fact]
    public async Task DownloadAsync_CancelledBeforeTheFirstRequest_ThrowsWithoutAskingASource()
    {
        using var folder = new TempFolder("league-voice-cancelled");
        var handler = PinnedSource();
        var store = Store(folder, handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.DownloadAsync(null, new CancellationToken(canceled: true))
        );

        Assert.Empty(handler.Urls);
    }

    [Fact]
    public async Task ImportAsync_FileOfAPinnedSize_StoresItUnderItsPinnedName()
    {
        using var folder = new TempFolder("league-voice-import");
        using var downloads = new TempFolder("league-voice-hand-downloaded");
        var store = Store(folder, PinnedSource());
        string hand = downloads.File("renamed-by-the-browser.bin");
        await File.WriteAllBytesAsync(hand, ModelBytes);

        string[] imported = await store.ImportAsync([hand], CancellationToken.None);

        Assert.Equal(new[] { "a.bin" }, imported);
        Assert.Equal(ModelBytes, await File.ReadAllBytesAsync(folder.File("a.bin")));
    }

    [Fact]
    public async Task ImportAsync_OneOfTwoFilesImported_LeavesTheOtherMissing()
    {
        using var folder = new TempFolder("league-voice-import-partial");
        using var downloads = new TempFolder("league-voice-hand-downloaded");
        var store = Store(folder, PinnedSource());
        string hand = downloads.File("renamed-by-the-browser.bin");
        await File.WriteAllBytesAsync(hand, ModelBytes);

        await store.ImportAsync([hand], CancellationToken.None);

        Assert.Equal(new[] { "b.txt" }, store.MissingFiles);
    }

    [Fact]
    public async Task ImportAsync_OneOfTwoFilesImported_DoesNotWriteTheLicense()
    {
        using var folder = new TempFolder("league-voice-import-license");
        using var downloads = new TempFolder("league-voice-hand-downloaded");
        var store = Store(folder, PinnedSource());
        string hand = downloads.File("renamed-by-the-browser.bin");
        await File.WriteAllBytesAsync(hand, ModelBytes);

        await store.ImportAsync([hand], CancellationToken.None);

        Assert.False(File.Exists(folder.File("LICENSE")));
    }

    [Fact]
    public async Task ImportAsync_EveryPinnedFileImported_WritesTheLicenseAndLeavesNoPartialFile()
    {
        using var folder = new TempFolder("league-voice-import-complete");
        using var downloads = new TempFolder("league-voice-hand-downloaded");
        var store = Store(folder, PinnedSource());
        string hand = downloads.File("renamed-by-the-browser.bin");
        await File.WriteAllBytesAsync(hand, ModelBytes);
        await File.WriteAllBytesAsync(downloads.File("tokens.txt"), TokensBytes);

        await store.ImportAsync([hand, downloads.File("tokens.txt")], CancellationToken.None);

        Assert.True(File.Exists(folder.File("LICENSE")));
        Assert.Empty(Directory.EnumerateFiles(folder.Root, "*.import"));
    }

    [Fact]
    public async Task ImportAsync_FileOfAnUnknownSize_ThrowsInvalidDataException()
    {
        using var folder = new TempFolder("league-voice-import-size");
        using var downloads = new TempFolder("league-voice-hand-downloaded");
        var store = Store(folder, PinnedSource());
        string hand = downloads.File("unrelated.bin");
        await File.WriteAllBytesAsync(hand, [1, 2, 3]);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync([hand], CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_FileOfAPinnedSizeWithTheWrongHash_ThrowsInvalidDataException()
    {
        using var folder = new TempFolder("league-voice-import-hash");
        using var downloads = new TempFolder("league-voice-hand-downloaded");
        var store = Store(folder, PinnedSource());
        string hand = downloads.File("forged.bin");
        await File.WriteAllBytesAsync(hand, new byte[ModelBytes.Length]);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync([hand], CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_Refused_KeepsTheFileThatWasAlreadyThere()
    {
        using var folder = new TempFolder("league-voice-import-keep");
        using var downloads = new TempFolder("league-voice-hand-downloaded");
        var store = Store(folder, PinnedSource());
        await File.WriteAllBytesAsync(folder.File("a.bin"), ModelBytes);
        string hand = downloads.File("forged.bin");
        await File.WriteAllBytesAsync(hand, new byte[ModelBytes.Length]);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync([hand], CancellationToken.None));

        Assert.Equal(ModelBytes, await File.ReadAllBytesAsync(folder.File("a.bin")));
    }

    [Fact]
    public async Task ImportAsync_Refused_LeavesNoPartialFileBehind()
    {
        using var folder = new TempFolder("league-voice-import-partial-left");
        using var downloads = new TempFolder("league-voice-hand-downloaded");
        var store = Store(folder, PinnedSource());
        string hand = downloads.File("forged.bin");
        await File.WriteAllBytesAsync(hand, new byte[ModelBytes.Length]);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync([hand], CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(folder.Root, "*.import"));
    }

    [Fact]
    public async Task ImportAsync_TheSameFileTwice_ReportsItOnce()
    {
        using var folder = new TempFolder("league-voice-import-twice");
        using var downloads = new TempFolder("league-voice-hand-downloaded");
        var store = Store(folder, PinnedSource());
        string hand = downloads.File("renamed-by-the-browser.bin");
        await File.WriteAllBytesAsync(hand, ModelBytes);

        string[] imported = await store.ImportAsync([hand, hand], CancellationToken.None);

        Assert.Equal(new[] { "a.bin" }, imported);
    }

    /// <summary>A store in <paramref name="folder"/> whose pinned files are the invented ones above.</summary>
    private static VoiceModelStore Store(TempFolder folder, HttpMessageHandler transport) =>
        new(folder.Root, () => new HttpClient(transport), Pinned);

    /// <summary>A source that serves the pinned file its URL names, at its pinned size.</summary>
    private static StubTransport PinnedSource() =>
        new(request => (HttpStatusCode.OK, PinnedContent(request.RequestUri!.AbsoluteUri)));

    /// <summary>A source that answers a range request with the rest of the file, as a resuming server does.</summary>
    private static StubTransport ResumingSource() =>
        new(request =>
            request.RequestUri!.AbsoluteUri.EndsWith("a.bin", StringComparison.Ordinal)
                ? (HttpStatusCode.PartialContent, ModelBytes[5..])
                : (HttpStatusCode.OK, TokensBytes)
        );

    /// <summary>The mirror refuses while the upstream source answers, which is the case the fallback exists for.</summary>
    private static (HttpStatusCode Status, byte[] Body) Fallback(string url) =>
        url.Contains("hf-mirror.com", StringComparison.Ordinal)
            ? (HttpStatusCode.InternalServerError, [])
            : (HttpStatusCode.OK, PinnedContent(url));

    /// <summary>The invented bytes of the pinned file a URL or a file name points at.</summary>
    private static byte[] PinnedContent(string urlOrName) =>
        urlOrName.EndsWith("a.bin", StringComparison.Ordinal) ? ModelBytes : TokensBytes;

    private static VoiceModelFile Model(string name, byte[] content) =>
        new(name, content.Length, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());

    /// <summary>Writes both invented files into <paramref name="folder"/> at their pinned sizes.</summary>
    private static void WritePinnedFiles(TempFolder folder)
    {
        File.WriteAllBytes(folder.File("a.bin"), ModelBytes);
        File.WriteAllBytes(folder.File("b.txt"), TokensBytes);
    }

    /// <summary>Collects what a download reports, so progress can be asserted without looking at the files.</summary>
    private sealed class ProgressRecorder : IProgress<int>
    {
        public List<int> Values { get; } = new();

        public void Report(int value) => Values.Add(value);
    }

    /// <summary>Answers every request from one function and records what was asked for; no request leaves the process.</summary>
    private sealed class StubTransport(Func<HttpRequestMessage, (HttpStatusCode Status, byte[] Body)> reply)
        : HttpMessageHandler
    {
        /// <summary>The start of the range each request asked for, or null when it asked for the whole file.</summary>
        public List<long?> Ranges { get; } = new();

        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Urls.Add(request.RequestUri!.AbsoluteUri);
            Ranges.Add(request.Headers.Range?.Ranges.FirstOrDefault()?.From);
            var (status, body) = reply(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }
}
