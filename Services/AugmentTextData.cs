using System.IO.Hashing;
using System.Text;
using League.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Services;

/// <summary>An augment's texts from the game: its name, the one-line summary and the tooltip with its numbers filled.</summary>
public sealed record AugmentText(int Id, string Name, string Summary, string Tooltip);

/// <summary>
/// The ARAM: Mayhem augments' own descriptions, read from CommunityDragon's copy of the game for the client's patch.
/// The client's catalog carries no descriptions at all, which left a hand-written table. The mode's data
/// (kiwi.bin, about 13 MB) gives each augment its text keys and its spell values and formulas; the string table
/// (about 30 MB) is read as a stream keeping only those keys. The result is a few hundred kilobytes, cached per patch,
/// so the large files are read once a patch. Runs off the UI thread.
///
/// The string table keeps a key it knows by name in lower case and any other by the low 38 bits of the XXH3 hash of
/// the lower-cased name, written "{03b9fb7aea}"; both forms are tried.
/// </summary>
public sealed class AugmentTextData
{
    // Bumped whenever how the texts are written changes, so caches from older rules are not reused.
    public const string RulesVersion = "1";

    private static readonly Lazy<HttpClient> SharedHttp = new(() =>
    {
        // The tables are large; a slow connection gets the same time the spell text reader allows.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        // CommunityDragon refuses requests without a user agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LeagueHistory");
        return client;
    });

    private readonly HttpClient _http;
    private readonly string _folder;
    private readonly string _patch;
    private readonly string _host;

    public AugmentTextData(
        string? folder = null,
        string? patch = null,
        HttpClient? http = null,
        string host = "https://raw.communitydragon.org"
    )
    {
        _folder =
            folder
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "League",
                "Cache",
                "cdragon-augments"
            );
        _patch = patch ?? ChampionSpellData.ClientPatch();
        _http = http ?? SharedHttp.Value;
        _host = host.TrimEnd('/');
    }

    public string Patch => _patch;

    /// <summary>Whether this patch's texts are already on disk, so loading them downloads nothing.</summary>
    public bool IsCached => File.Exists(CachePath);

    private string CachePath => Path.Combine(_folder, $"{_patch}-r{RulesVersion}.json");

    /// <summary>The texts by the augment's id, the same id the client's catalog uses.</summary>
    /// <param name="refresh">Downloads again even when the patch's texts are cached.</param>
    /// <param name="progress">
    /// 0 to 1 over a download: the mode data is the first 30%, the string table the rest, each by bytes read.
    /// </param>
    public async Task<IReadOnlyDictionary<int, AugmentText>> LoadAsync(
        CancellationToken token,
        bool refresh = false,
        Action<double>? progress = null
    )
    {
        string path = CachePath;
        if (!refresh && File.Exists(path))
        {
            var cached = JsonConvert.DeserializeObject<List<AugmentText>>(
                await File.ReadAllTextAsync(path, token).ConfigureAwait(false)
            );
            return (cached ?? []).ToDictionary(text => text.Id);
        }

        progress?.Invoke(0);
        var mode = await ReadJsonAsync(
                $"{_host}/{_patch}/game/maps/modespecificdata/kiwi.bin.json",
                ModeBytes,
                Share(progress, 0, ModeShare),
                token
            )
            .ConfigureAwait(false);
        var augments = mode.Properties()
            .Select(property => property.Value)
            .OfType<JObject>()
            .Where(entry => (string?)entry["__type"] == "AugmentData" && (int?)entry["AugmentPlatformId"] > 0)
            .ToArray();
        var wanted = augments
            .SelectMany(a => new[] { a["NameTra"], a["DescriptionTra"], a["AugmentTooltipTra"] })
            .Select(key => (string?)key)
            .OfType<string>()
            .SelectMany(key => new[] { key.ToLowerInvariant(), HashedKey(key) })
            .ToHashSet(StringComparer.Ordinal);
        var strings = await ReadStringsAsync(
                key => wanted.Contains(key) || MayBeQuoted(key),
                Share(progress, ModeShare, 1 - ModeShare),
                token
            )
            .ConfigureAwait(false);

        var texts = augments
            .Select(augment => Text(augment, mode, strings))
            .OfType<AugmentText>()
            .GroupBy(text => text.Id)
            .Select(group => group.First())
            .ToList();
        Directory.CreateDirectory(_folder);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonConvert.SerializeObject(texts), token).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
        RemoveOtherPatches(path);
        progress?.Invoke(1);
        return texts.ToDictionary(text => text.Id);
    }

    // The mode data is about a third of what is read, the string table the rest.
    private const double ModeShare = 0.3;

    // CommunityDragon sends both files chunked, without a length, so the progress is measured against their size on
    // 16.21 instead; it stops short of the end until the read actually finishes.
    private const long ModeBytes = 13_700_000;
    private const long StringTableBytes = 32_700_000;

    private static Action<double>? Share(Action<double>? progress, double start, double length) =>
        progress == null ? null : part => progress(start + part * length);

    // A patch's texts replace the last patch's; the older files are a few hundred kilobytes each and never read again.
    private void RemoveOtherPatches(string current)
    {
        foreach (string file in Directory.EnumerateFiles(_folder, "*.json"))
        {
            if (!string.Equals(file, current, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException ex)
                {
                    // Another instance may be reading it; it is removed with the next patch.
                    AppLog.Shared.Warn(LogCategory.Data, "旧海克斯说明缓存删除失败", ex);
                }
            }
        }
    }

    private async Task<Dictionary<string, string>> ReadStringsAsync(
        Func<string, bool> keep,
        Action<double>? progress,
        CancellationToken token
    )
    {
        using var response = await _http
            .GetAsync(
                $"{_host}/{_patch}/game/zh_cn/data/menu/en_us/lol.stringtable.json",
                HttpCompletionOption.ResponseHeadersRead,
                token
            )
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = Counted(
            await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false),
            response.Content.Headers.ContentLength ?? StringTableBytes,
            progress
        );
        await using var reader = new JsonTextReader(new StreamReader(stream));
        return await SpellTextData.ReadEntriesAsync(reader, keep, token).ConfigureAwait(false);
    }

    private static Stream Counted(Stream stream, long length, Action<double>? progress) =>
        progress == null || length <= 0 ? stream : new ProgressStream(stream, length, progress);

    /// <summary>The key a string table uses for a text whose name it does not keep: "{03b9fb7aea}".</summary>
    public static string HashedKey(string name)
    {
        ulong hash = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(name.ToLowerInvariant()));
        return "{"
            + (hash & ((1UL << 38) - 1)).ToString("x10", System.Globalization.CultureInfo.InvariantCulture)
            + "}";
    }

    // A tooltip may quote another text ("{{ Item_Keyword_OnHit }}", "{{Kiwi_SpellVolley_Summary}}"), which is not
    // known until the tooltips are read. Every text kept only by hash is a few megabytes in all, and the named ones
    // quoted come from these families, so they are kept on the way through rather than reading the table twice.
    private static bool MayBeQuoted(string key) =>
        key.StartsWith('{')
        || key.StartsWith("item_keyword_", StringComparison.Ordinal)
        || key.StartsWith("kiwi_", StringComparison.Ordinal)
        || key.StartsWith("augment_", StringComparison.Ordinal)
        || key.StartsWith("cherry_", StringComparison.Ordinal)
        || key.StartsWith("quest_", StringComparison.Ordinal)
        || key.StartsWith("upgrade", StringComparison.Ordinal);

    private static AugmentText? Text(JObject augment, JObject mode, IReadOnlyDictionary<string, string> strings)
    {
        string? Find(string key) =>
            strings.GetValueOrDefault(key.ToLowerInvariant()) ?? strings.GetValueOrDefault(HashedKey(key));
        string? Lookup(string field) => (string?)augment[field] is string key ? Find(key) : null;
        string? name = Lookup("NameTra");
        if (name == null)
        {
            return null;
        }

        var spell = mode[(string?)augment["RootSpell"] ?? ""] as JObject ?? [];
        string summary = AugmentTooltipFormatter.Render(Lookup("DescriptionTra") ?? "", spell, Find);
        string? template = Lookup("AugmentTooltipTra");
        string tooltip = template == null ? "" : AugmentTooltipFormatter.Render(template, spell, Find);
        // A tooltip that was nothing but match counters, or a quote of a text chosen in the match, leaves nothing;
        // the summary stands in.
        if (TooltipText.PlainText(tooltip).Length == 0)
        {
            tooltip = summary;
        }
        return new AugmentText((int)augment["AugmentPlatformId"]!, name, summary, tooltip);
    }

    private async Task<JObject> ReadJsonAsync(
        string url,
        long expected,
        Action<double>? progress,
        CancellationToken token
    )
    {
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = Counted(
            await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false),
            response.Content.Headers.ContentLength ?? expected,
            progress
        );
        await using var reader = new JsonTextReader(new StreamReader(stream));
        return await JObject.LoadAsync(reader, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts what is read and reports the share of the expected length, in steps of at least 1%. An estimated length
    /// may be off, so the share stays below 1; the caller reports the end.
    /// </summary>
    private sealed class ProgressStream(Stream inner, long length, Action<double> progress) : Stream
    {
        private const double MostBeforeTheEnd = 0.99;

        private long _read;
        private double _reported = -1;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
            Count(await inner.ReadAsync(buffer, token).ConfigureAwait(false));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            ReadAsync(buffer.AsMemory(offset, count), token).AsTask();

        private int Count(int read)
        {
            _read += read;
            double share = Math.Min(MostBeforeTheEnd, (double)_read / length);
            if (share - _reported >= 0.01)
            {
                _reported = share;
                progress(share);
            }
            return read;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
