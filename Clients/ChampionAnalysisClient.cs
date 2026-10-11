using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using League.Models;
using League.Parsers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Clients;

public sealed record ChampionAnalysisSnapshot(
    ChampionAnalysisReport Report,
    DateTimeOffset FetchedAt,
    string ContentHash,
    bool Stale,
    string? Warning
);

/// <summary>Raw snapshots are persisted; parser changes apply without downloading again.</summary>
public sealed class ChampionAnalysisClient : IDisposable
{
    public static ChampionAnalysisClient Shared { get; } = new();
    private readonly HttpClient _http;
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<string, CacheEntry> _memory = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _failures = new();

    private sealed record CacheEntry(DateTimeOffset FetchedAt, string Json);

    public ChampionAnalysisClient(HttpMessageHandler? handler = null, string? directory = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(12) };
        _directory =
            directory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "League",
                "Cache",
                "champion-analysis"
            );
    }

    public async Task<ChampionAnalysisSnapshot> GetAsync(string alias, TimeSpan lifetime, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(alias) || !alias.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("无效的英雄别名。", nameof(alias));
        alias = alias.ToLowerInvariant();
        var gate = _gates.GetOrAdd(alias, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            string file = Path.Combine(_directory, alias + ".json");
            if (!_memory.TryGetValue(alias, out var cached))
            {
                try
                {
                    if (File.Exists(file))
                    {
                        cached = JsonConvert.DeserializeObject<CacheEntry>(await File.ReadAllTextAsync(file, token));
                        if (cached != null)
                        {
                            Snapshot(cached, false, null);
                            _memory[alias] = cached;
                        }
                    }
                }
                catch (Exception ex)
                    when (ex
                            is IOException
                                or UnauthorizedAccessException
                                or JsonException
                                or InvalidDataException
                                or ArgumentException
                    )
                {
                    cached = null;
                }
            }
            var now = DateTimeOffset.UtcNow;
            if (cached != null && cached.FetchedAt <= now && now - cached.FetchedAt < lifetime)
                return Snapshot(cached, false, null);
            if (_failures.TryGetValue(alias, out var failed) && now - failed < TimeSpan.FromMinutes(1))
            {
                if (cached != null)
                    return Snapshot(cached, true, "更新失败，暂用旧缓存；一分钟后可重试。");
                throw new InvalidDataException("英雄数据暂不可用，一分钟后可重试。");
            }
            try
            {
                string json = await _http.GetStringAsync(Source(alias), token);
                var fresh = new CacheEntry(DateTimeOffset.UtcNow, json);
                var result = Snapshot(fresh, false, null); // Validate before replacing valid cache.
                string? persistenceWarning = null;
                try
                {
                    Directory.CreateDirectory(_directory);
                    string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        await File.WriteAllTextAsync(temp, JsonConvert.SerializeObject(fresh), token);
                        File.Move(temp, file, true);
                    }
                    finally
                    {
                        if (File.Exists(temp))
                            File.Delete(temp);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    persistenceWarning = "已读取数据，但磁盘缓存保存失败。";
                }
                _memory[alias] = fresh;
                _failures.TryRemove(alias, out _);
                return result with { Warning = persistenceWarning };
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
                when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException)
            {
                _failures[alias] = DateTimeOffset.UtcNow;
                if (cached != null)
                    return Snapshot(cached, true, "更新失败，暂用旧缓存。");
                throw new InvalidDataException("英雄数据暂不可用，请稍后重试。", ex);
            }

            ChampionAnalysisSnapshot Snapshot(CacheEntry entry, bool stale, string? warning)
            {
                var report = ChampionAnalysisParser.Parse(JObject.Parse(entry.Json), alias, Source(alias), "latest");
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Json)));
                return new(report, entry.FetchedAt, hash, stale, warning);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static string Source(string alias) =>
        $"https://raw.communitydragon.org/latest/game/data/characters/{alias}/{alias}.bin.json";

    public void Dispose() => _http.Dispose();
}
