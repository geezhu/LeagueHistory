using System.Net;
using System.Security.Cryptography;
using System.Text;
using League.Services;
using static StatisticsCheckSupport;

/// <summary>
/// The speech model store: which pinned files are present, hand-importing files a browser downloaded, and the
/// verified download with its resume and mirror fallback. Every file here is a few bytes invented by the check
/// and the transport is a fake handler, so nothing is downloaded and no audio is involved.
/// </summary>
internal static class VoiceModelStoreChecks
{
    public static async Task RunAsync()
    {
        await PresenceChecks();
        Links();
        await ValidateChecks();
        await ImportAccepted();
        await ImportRefused();
        await DownloadWritesFiles();
        await DownloadResumeChecks();
        await DownloadRefusalChecks();
        Console.WriteLine(
            "PASS: model presence and download links, hash validation, verified hand-import, and a verified "
                + "download with resume, a range-ignoring server, oversize data, mirror fallback and the licence."
        );
    }

    private static async Task PresenceChecks()
    {
        var (first, second) = Contents();
        var files = new[] { Model("a.bin", first), Model("b.txt", second) };
        string directory = Temporary();
        try
        {
            var store = new VoiceModelStore(directory, () => new HttpClient(), files);
            Check(store.MissingFiles.Length == 2, "A directory without the pinned files reports both missing");
            Check(store.AvailableDirectory == null, "A directory without the pinned files is unavailable");

            await File.WriteAllBytesAsync(Path.Combine(directory, "a.bin"), first);
            await File.WriteAllBytesAsync(Path.Combine(directory, "b.txt"), second);
            Check(store.MissingFiles.Length == 0, "Files of the pinned size are no longer missing");
            Check(store.AvailableDirectory == directory, "A directory holding every pinned file is available");

            await File.WriteAllBytesAsync(Path.Combine(directory, "b.txt"), new byte[second.Length + 1]);
            Check(store.MissingFiles.SequenceEqual(["b.txt"]), "A file of the wrong size counts as missing again");
            Check(store.AvailableDirectory == null, "A half-present directory falls back to nothing");
        }
        finally
        {
            Cleanup(directory);
        }
    }

    private static void Links()
    {
        var (first, second) = Contents();
        string directory = Temporary();
        try
        {
            var store = new VoiceModelStore(
                directory,
                () => new HttpClient(),
                [Model("a.bin", first), Model("b.txt", second)]
            );
            var links = store.DownloadLinks.ToArray();
            Check(links.Length == 4, "Each pinned file has a link on each source");
            Check(
                links.All(link =>
                    link.Url.EndsWith("?download=true", StringComparison.Ordinal)
                    && link.Url.Contains(link.File, StringComparison.Ordinal)
                ),
                "Every link names its file and is marked as a download"
            );
            Check(
                links.Select(link => link.Source).ToHashSet().SetEquals(["hf-mirror.com", "huggingface.co"]),
                "Both the mirror and the upstream source are offered"
            );
        }
        finally
        {
            Cleanup(directory);
        }
    }

    private static async Task ValidateChecks()
    {
        var (first, second) = Contents();
        var files = new[] { Model("a.bin", first), Model("b.txt", second) };
        string directory = Temporary();
        try
        {
            var store = new VoiceModelStore(directory, () => new HttpClient(), files);
            Check(
                await Rejects(() => store.ValidateAsync(CancellationToken.None), typeof(VoiceRecognitionException)),
                "Recording without a model is refused with its own exception"
            );

            await File.WriteAllBytesAsync(Path.Combine(directory, "a.bin"), first);
            await File.WriteAllBytesAsync(Path.Combine(directory, "b.txt"), second);
            Check(
                await store.ValidateAsync(CancellationToken.None) == directory,
                "A directory holding the pinned model validates"
            );

            string tampered = Path.Combine(directory, "a.bin");
            await File.WriteAllBytesAsync(tampered, new byte[first.Length]);
            File.SetLastWriteTimeUtc(tampered, DateTime.UtcNow.AddSeconds(5)); // The size is unchanged, so only the hash can tell.
            Check(
                await Rejects(() => store.ValidateAsync(CancellationToken.None), typeof(VoiceRecognitionException)),
                "A file whose contents no longer match the pinned hash is refused"
            );
        }
        finally
        {
            Cleanup(directory);
        }
    }

    private static async Task ImportAccepted()
    {
        var (first, second) = Contents();
        var files = new[] { Model("a.bin", first), Model("b.txt", second) };
        string directory = Temporary();
        string source = Temporary();
        try
        {
            var store = new VoiceModelStore(directory, () => new HttpClient(), files);
            string downloaded = Path.Combine(source, "renamed-by-the-browser.bin");
            await File.WriteAllBytesAsync(downloaded, first);

            Check(
                (await store.ImportAsync([downloaded], CancellationToken.None)).SequenceEqual(["a.bin"]),
                "A hand-downloaded file is recognized by its size and stored under its pinned name"
            );
            Check(store.MissingFiles.SequenceEqual(["b.txt"]), "The files that were not imported are still missing");
            Check(!File.Exists(Path.Combine(directory, "LICENSE")), "The licence waits until the model is complete");

            await File.WriteAllBytesAsync(Path.Combine(source, "b.txt"), second);
            await store.ImportAsync([Path.Combine(source, "b.txt")], CancellationToken.None);
            Check(File.Exists(Path.Combine(directory, "LICENSE")), "The licence is written once every file is present");
            Check(!Directory.EnumerateFiles(directory, "*.import").Any(), "No partial import file is left behind");
        }
        finally
        {
            Cleanup(directory);
            Cleanup(source);
        }
    }

    private static async Task ImportRefused()
    {
        var (first, second) = Contents();
        var files = new[] { Model("a.bin", first), Model("b.txt", second) };
        string directory = Temporary();
        string source = Temporary();
        try
        {
            var store = new VoiceModelStore(directory, () => new HttpClient(), files);
            await File.WriteAllBytesAsync(Path.Combine(directory, "a.bin"), first);

            string tooSmall = Path.Combine(source, "unrelated.bin");
            await File.WriteAllBytesAsync(tooSmall, [1, 2, 3]);
            Check(
                await Rejects(
                    () => store.ImportAsync([tooSmall], CancellationToken.None),
                    typeof(InvalidDataException)
                ),
                "A file of an unknown size is refused"
            );

            string forged = Path.Combine(source, "forged.bin");
            await File.WriteAllBytesAsync(forged, new byte[first.Length]);
            Check(
                await Rejects(() => store.ImportAsync([forged], CancellationToken.None), typeof(InvalidDataException)),
                "A file of the right size but the wrong hash is refused"
            );
            Check(
                (await File.ReadAllBytesAsync(Path.Combine(directory, "a.bin"))).SequenceEqual(first),
                "A refused import does not replace the file that was already there"
            );
            Check(
                !Directory.EnumerateFiles(directory, "*.import").Any(),
                "A refused import leaves no partial file behind"
            );
        }
        finally
        {
            Cleanup(directory);
            Cleanup(source);
        }
    }

    private static async Task DownloadWritesFiles()
    {
        var (first, second) = Contents();
        var files = new[] { Model("a.bin", first), Model("b.txt", second) };
        string directory = Temporary();
        try
        {
            var handler = new Transport(request =>
                (HttpStatusCode.OK, Body(request.RequestUri!.AbsoluteUri, first, second))
            );
            var store = new VoiceModelStore(directory, () => new HttpClient(handler), files);
            var progress = new Recorder();
            await store.DownloadAsync(progress, CancellationToken.None);

            Check(
                (await File.ReadAllBytesAsync(Path.Combine(directory, "a.bin"))).SequenceEqual(first)
                    && (await File.ReadAllBytesAsync(Path.Combine(directory, "b.txt"))).SequenceEqual(second),
                "A verified download writes both pinned files"
            );
            Check(File.Exists(Path.Combine(directory, "LICENSE")), "A finished download writes the licence");
            Check(progress.Values.Count > 0 && progress.Values[^1] == 100, "A finished download reports 100%");
            Check(
                progress.Values.Zip(progress.Values.Skip(1)).All(pair => pair.Second >= pair.First),
                "Download progress never goes backwards"
            );
            Check(store.MissingFiles.Length == 0, "Nothing is missing after the download");
        }
        finally
        {
            Cleanup(directory);
        }
    }

    private static async Task DownloadResumeChecks()
    {
        var (first, second) = Contents();
        var files = new[] { Model("a.bin", first), Model("b.txt", second) };

        string resumed = Temporary();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(resumed, "a.bin.download"), first[..5]);
            var handler = new Transport(request =>
                request.RequestUri!.AbsoluteUri.EndsWith("a.bin", StringComparison.Ordinal)
                    ? (HttpStatusCode.PartialContent, first[5..])
                    : (HttpStatusCode.OK, second)
            );
            var store = new VoiceModelStore(resumed, () => new HttpClient(handler), files);
            await store.DownloadAsync(null, CancellationToken.None);

            Check(
                (await File.ReadAllBytesAsync(Path.Combine(resumed, "a.bin"))).SequenceEqual(first),
                "A partial download is continued rather than restarted"
            );
            Check(handler.Ranges.Contains(5), "The continued download asks the server for the rest of the file");
        }
        finally
        {
            Cleanup(resumed);
        }

        string restarted = Temporary();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(restarted, "a.bin.download"), first[..5]);
            var handler = new Transport(request =>
                (HttpStatusCode.OK, Body(request.RequestUri!.AbsoluteUri, first, second))
            );
            var store = new VoiceModelStore(restarted, () => new HttpClient(handler), files);
            await store.DownloadAsync(null, CancellationToken.None);

            Check(
                (await File.ReadAllBytesAsync(Path.Combine(restarted, "a.bin"))).SequenceEqual(first),
                "A server that ignores the range makes the download start over instead of appending"
            );
        }
        finally
        {
            Cleanup(restarted);
        }
    }

    private static async Task DownloadRefusalChecks()
    {
        var (first, second) = Contents();
        var files = new[] { Model("a.bin", first), Model("b.txt", second) };

        string oversized = Temporary();
        try
        {
            var handler = new Transport(_ => (HttpStatusCode.OK, new byte[first.Length + 8]));
            var store = new VoiceModelStore(oversized, () => new HttpClient(handler), files);
            Check(
                await Rejects(() => store.DownloadAsync(null, CancellationToken.None), typeof(IOException)),
                "More bytes than the pinned size is refused"
            );
            Check(store.MissingFiles.Length == 2, "A refused download publishes nothing");
        }
        finally
        {
            Cleanup(oversized);
        }

        string refused = Temporary();
        try
        {
            var handler = new Transport(_ => (HttpStatusCode.InternalServerError, []));
            var store = new VoiceModelStore(refused, () => new HttpClient(handler), files);
            Check(
                await Rejects(() => store.DownloadAsync(null, CancellationToken.None), typeof(IOException)),
                "A download that every source refuses fails"
            );
            Check(handler.Urls.Count == 2, "Both sources are tried before giving up");
        }
        finally
        {
            Cleanup(refused);
        }
    }

    private static byte[] Body(string url, byte[] first, byte[] second) =>
        url.EndsWith("a.bin", StringComparison.Ordinal) ? first : second;

    private static async Task<bool> Rejects(Func<Task> action, Type expected)
    {
        try
        {
            await action();
            return false;
        }
        catch (Exception ex)
        {
            return ex.GetType() == expected;
        }
    }

    private static (byte[] First, byte[] Second) Contents() =>
        (Encoding.UTF8.GetBytes("model-bytes"), Encoding.UTF8.GetBytes("tokens"));

    private static VoiceModelFile Model(string name, byte[] content) =>
        new(name, content.Length, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());

    private static string Temporary()
    {
        string path = Path.Combine(Path.GetTempPath(), "league-voice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("Voice model check left its temporary folder: " + path);
        }
    }

    private sealed class Recorder : IProgress<int>
    {
        public List<int> Values { get; } = [];

        public void Report(int value) => Values.Add(value);
    }

    /// <summary>Answers every request from one function and remembers what was asked for.</summary>
    private sealed class Transport(Func<HttpRequestMessage, (HttpStatusCode Status, byte[] Body)> reply)
        : HttpMessageHandler
    {
        public List<long?> Ranges { get; } = [];
        public List<string> Urls { get; } = [];

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
