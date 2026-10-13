using System.Security.Cryptography;

namespace League.Services;

public sealed class VoiceRecognitionException(string message) : InvalidOperationException(message);

public sealed record VoiceModelFile(string Name, long Size, string Sha256);

/// <summary>Pinned local model artifacts only. Audio never enters the downloader.</summary>
public sealed class VoiceModelStore
{
    public const string ModelId = "SenseVoice-2024-07-17-int8";
    private const string Revision =
        "csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17/resolve/2365baeacb507f821a0c8120fcee3d484dba7a07/";
    public const string Source = "https://huggingface.co/" + Revision;

    // The mirror first: huggingface.co is often unreachable from mainland China. Both serve the same pinned revision.
    private static readonly string[] Sources = ["https://hf-mirror.com/" + Revision, Source];
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    public static readonly VoiceModelFile[] Files =
    [
        new("model.int8.onnx", 239233841, "c71f0ce00bec95b07744e116345e33d8cbbe08cef896382cf907bf4b51a2cd51"),
        new("tokens.txt", 315894, "f449eb28dc567533d7fa59be34e2abca8784f771850c78a47fb731a31429a1dc"),
    ];
    public static VoiceModelStore Shared { get; } =
        new(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "League",
                "VoiceModels",
                ModelId
            )
        );
    private readonly SemaphoreSlim _download = new(1, 1);
    private readonly Dictionary<string, string> _checked = new();
    private readonly VoiceModelFile[] _files;
    private readonly Func<HttpClient> _client;
    public string DirectoryPath { get; }

    public VoiceModelStore(string directory, Func<HttpClient>? client = null, VoiceModelFile[]? files = null)
    {
        DirectoryPath = directory;
        _client = client ?? (() => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        _files = files ?? Files;
    }

    private bool HasFiles(string directory) =>
        _files.All(f =>
            File.Exists(Path.Combine(directory, f.Name))
            && new FileInfo(Path.Combine(directory, f.Name)).Length == f.Size
        );

    public string? AvailableDirectory
    {
        get
        {
            try
            {
                if (HasFiles(DirectoryPath))
                {
                    return DirectoryPath;
                }

                string bundled = Path.Combine(AppContext.BaseDirectory, "VoiceModels", "SenseVoice");
                return HasFiles(bundled) ? bundled : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public async Task<string> ValidateAsync(CancellationToken token)
    {
        string directory =
            AvailableDirectory
            ?? throw new VoiceRecognitionException(
                "请先到设置 → 消息发送 → 本机语音识别下载模型（约 228 MiB），再开始录音。"
            );
        foreach (var file in _files)
        {
            token.ThrowIfCancellationRequested();
            string path = Path.Combine(directory, file.Name);
            var info = new FileInfo(path);
            string signature = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            lock (_checked)
            {
                if (_checked.GetValueOrDefault(path) == signature)
                {
                    continue;
                }
            }

            if (!await MatchesAsync(path, file, token).ConfigureAwait(false))
            {
                throw new VoiceRecognitionException("语音模型校验失败，请在设置 → 消息发送重新下载模型。");
            }

            lock (_checked)
            {
                _checked[path] = signature;
            }
        }
        return directory;
    }

    public async Task DownloadAsync(IProgress<int>? progress, CancellationToken token)
    {
        await _download.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            using var client = _client();
            long total = _files.Sum(f => f.Size),
                completed = 0;
            int last = -1;
            void Report(long current)
            {
                int value = (int)Math.Min(99, (completed + current) * 100 / total);
                if (value != last)
                {
                    last = value;
                    progress?.Report(value);
                }
            }

            foreach (var file in _files)
            {
                string destination = Path.Combine(DirectoryPath, file.Name);
                if (!await MatchesAsync(destination, file, token).ConfigureAwait(false))
                {
                    await DownloadVerifiedAsync(client, file, destination, Report, token).ConfigureAwait(false);
                }

                completed += file.Size;
                Report(0);
            }
            await WriteLicenseAsync(token).ConfigureAwait(false);
            progress?.Report(100);
        }
        finally
        {
            _download.Release();
        }
    }

    /// <summary>Direct links for a browser or download manager; every source serves the pinned revision.</summary>
    public IEnumerable<(string File, string Source, string Url)> DownloadLinks =>
        _files.SelectMany(file =>
            Sources.Select(source => (file.Name, new Uri(source).Host, source + file.Name + "?download=true"))
        );

    /// <summary>Pinned files not yet present at the expected size in the download directory.</summary>
    public string[] MissingFiles =>
        _files
            .Where(f =>
                !File.Exists(Path.Combine(DirectoryPath, f.Name))
                || new FileInfo(Path.Combine(DirectoryPath, f.Name)).Length != f.Size
            )
            .Select(f => f.Name)
            .ToArray();

    /// <summary>
    /// Imports files the user downloaded by hand. Files are recognized by size, since browsers may
    /// rename them, and published only after the copy matches the pinned SHA-256.
    /// </summary>
    public async Task<string[]> ImportAsync(IEnumerable<string> paths, CancellationToken token)
    {
        await _download.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var imported = new List<string>();
            foreach (string path in paths)
            {
                string name = Path.GetFileName(path);
                long size = new FileInfo(path).Length;
                var file =
                    _files.FirstOrDefault(f => f.Size == size)
                    ?? throw new InvalidDataException($"“{name}”不是所需的模型文件，或尚未下载完整。");
                string destination = Path.Combine(DirectoryPath, file.Name),
                    part = destination + ".import";
                try
                {
                    await CopyAsync(path, part, token).ConfigureAwait(false);
                    if (!await MatchesAsync(part, file, token).ConfigureAwait(false))
                    {
                        throw new InvalidDataException($"“{name}”校验失败，文件可能已损坏，请重新下载。");
                    }

                    File.Move(part, destination, true);
                }
                finally
                {
                    DeletePartial(part);
                }

                imported.Add(file.Name);
            }

            if (MissingFiles.Length == 0)
            {
                await WriteLicenseAsync(token).ConfigureAwait(false);
            }

            return imported.Distinct().ToArray();
        }
        finally
        {
            _download.Release();
        }
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken token)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            true
        );
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            true
        );
        await input.CopyToAsync(output, token).ConfigureAwait(false);
    }

    // Keep the upstream model license reference with the local weights.
    private Task WriteLicenseAsync(CancellationToken token) =>
        File.WriteAllTextAsync(
            Path.Combine(DirectoryPath, "LICENSE"),
            "SenseVoice model: https://github.com/FunAudioLLM/SenseVoice\nModel license: https://github.com/modelscope/FunASR#license\nExported model: "
                + Source
                + "\n",
            token
        );

    /// <summary>
    /// Tries each source in turn, resuming the same partial file. Every source serves the pinned
    /// revision, and the SHA-256 check decides what is published, so a mirror cannot alter the model.
    /// A partial file survives cancellation and network errors so the next attempt continues it.
    /// </summary>
    private async Task DownloadVerifiedAsync(
        HttpClient client,
        VoiceModelFile file,
        string destination,
        Action<long> report,
        CancellationToken token
    )
    {
        string part = destination + ".download";
        Exception? failure = null;
        foreach (string source in Sources)
        {
            try
            {
                await DownloadFileAsync(client, source + file.Name, file, part, report, token).ConfigureAwait(false);
                failure = null;
                break;
            }
            catch (Exception e)
                when (!token.IsCancellationRequested
                    && e is HttpRequestException or IOException or TimeoutException or OperationCanceledException
                )
            {
                failure = e;
            }
        }

        if (failure != null)
        {
            throw new IOException("语音模型下载失败，所有下载源都不可用；已下载部分会在重试时继续。", failure);
        }

        if (!await MatchesAsync(part, file, token).ConfigureAwait(false))
        {
            DeletePartial(part);
            throw new IOException("语音模型下载校验失败，请重试。");
        }

        File.Move(part, destination, true);
    }

    private static void DeletePartial(string part)
    {
        try
        {
            File.Delete(part);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A locked partial file is replaced by the next attempt's fresh download.
        }
    }

    private static async Task DownloadFileAsync(
        HttpClient client,
        string url,
        VoiceModelFile file,
        string part,
        Action<long> report,
        CancellationToken token
    )
    {
        long existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (existing >= file.Size)
        {
            report(existing);
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
        }

        using var stall = CancellationTokenSource.CreateLinkedTokenSource(token);
        stall.CancelAfter(StallTimeout);
        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        // A server that ignores the range sends the whole file; start over instead of appending it.
        bool resumed = existing > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        long bytes = resumed ? existing : 0;
        await using var input = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
        await using var output = new FileStream(
            part,
            resumed ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            true
        );
        byte[] buffer = new byte[128 * 1024];
        int count;
        // The stall timer restarts with every chunk: a slow link may take long, a dead one fails fast.
        while ((count = await input.ReadAsync(buffer, stall.Token).ConfigureAwait(false)) > 0)
        {
            stall.CancelAfter(StallTimeout);
            bytes += count;
            if (bytes > file.Size)
            {
                throw new IOException("语音模型下载内容大小不正确。");
            }

            await output.WriteAsync(buffer.AsMemory(0, count), stall.Token).ConfigureAwait(false);
            report(bytes);
        }
    }

    private static async Task<bool> MatchesAsync(string path, VoiceModelFile file, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != file.Size)
        {
            return false;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        return Convert
            .ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false))
            .Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
