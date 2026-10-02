using System.Security.Cryptography;
using System.Text;
using League.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace League.Clients;

/// <summary>One list per player, shared in-flight requests and persistent cache. Callers cancel their wait, not other views.</summary>
public sealed class ChampionMasteryService
{
    public static ChampionMasteryService Shared { get; } = new(
        () => FormMain.Globals.lcuClient?.Client,
        () => (FormMain.Globals.sgpClient?.DataScope ?? "") + "|" + FormMain.Globals.CurrentPuuid);
    private sealed class Entry
    {
        public ChampionMasteryPage? Page;
        public Task? Disk;
        public Task<ChampionMasteryPage>? Request;
        public long RetryAt, Access;
        public int Version;
        public bool Expired;
    }
    private readonly Func<HttpClient?> _client;
    private readonly Func<string> _scope;
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly SemaphoreSlim _network = new(3), _disk = new(1);
    public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromMinutes(30);
    public ChampionMasteryService(Func<HttpClient?> client, Func<string>? scope = null, string? directory = null)
    {
        _client = client; _scope = scope ?? (() => "default");
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "League", "Cache", "mastery");
    }
    private string Key(string puuid) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_scope() + "|" + puuid)));
    private string PathFor(string key) => Path.Combine(_directory, key + ".json");
    private static bool PublicIdentity(string puuid) => !string.IsNullOrWhiteSpace(puuid) && (!Guid.TryParse(puuid, out var id) || id != Guid.Empty);

    public async Task<ChampionMasteryPage> GetAsync(string puuid, CancellationToken token, bool refresh = false)
    {
        token.ThrowIfCancellationRequested();
        if (!PublicIdentity(puuid)) return new(null, null, "尚未取得玩家身份，等待对局名单匹配；不代表玩家隐藏了成就。");
        string key = Key(puuid); Entry entry; Task disk;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!)) _entries[key] = entry = new();
            entry.Access = Environment.TickCount64;
            int version=entry.Version;
            disk = entry.Disk ??= Task.Run(() => LoadDisk(key, entry, version));
            if (_entries.Count > 256)
                foreach (var old in _entries.Where(e => e.Key != key && e.Value.Request?.IsCompleted != false && e.Value.Disk?.IsCompleted != false)
                    .OrderBy(e => e.Value.Access).Take(_entries.Count - 256).Select(e => e.Key).ToArray()) _entries.Remove(old);
        }
        await disk.WaitAsync(token).ConfigureAwait(false);
        if(key!=Key(puuid))return new(null,null,"客户端账号或区服已变化，请重新查询。");
        Task<ChampionMasteryPage> request;
        lock (_gate)
        {
            var page = entry.Page;
            if (!refresh && !entry.Expired && page?.Data != null && DateTimeOffset.UtcNow - page.FetchedAt < CacheLifetime) return Copy(page);
            if (!refresh && entry.RetryAt > Environment.TickCount64 && page != null) return Copy(page);
            int version=entry.Version;
            request = entry.Request is { IsCompleted: false } ? entry.Request : entry.Request = Task.Run(() => Fetch(key, puuid, entry, version));
            // Expired successful data remains immediately visible while a single background refresh runs.
            if (!refresh && page?.Data != null) return Copy(page with { Stale = true });
        }
        return Copy(await request.WaitAsync(token).ConfigureAwait(false));
    }
    private static ChampionMasteryPage Copy(ChampionMasteryPage page) => page with { Data = (JArray?)page.Data?.DeepClone() };
    public void Invalidate(string puuid)
    {
        if (!PublicIdentity(puuid)) return;
        string key = Key(puuid); Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!)) _entries[key] = entry = new();
            entry.Version++; entry.Expired=true;entry.Page = entry.Page is { Data: not null } page ? page with { Stale = true } : null;
            entry.Request = null; entry.RetryAt = 0;
            int version=entry.Version;
            entry.Disk=Task.Run(()=>DeleteDisk(key,entry,version));
        }
    }
    private async Task LoadDisk(string key, Entry entry, int version)
    {
        try
        {
            string path = PathFor(key);
            if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return;
            var stored = JObject.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false));
            if ((int?)stored["schema"] != 1 || !TryNormalize(stored["champions"], out var data)) return;
            var fetched = DateTimeOffset.FromUnixTimeMilliseconds((long?)stored["fetchedAt"] ?? 0);
            if (fetched <= DateTimeOffset.UnixEpoch || fetched > DateTimeOffset.UtcNow.AddMinutes(5)) return;
            lock (_gate) if (entry.Version == version) entry.Page = new(data, fetched);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or OverflowException or InvalidCastException) { }
    }
    private async Task<ChampionMasteryPage> Fetch(string key, string puuid, Entry entry, int version)
    {
        await _network.WaitAsync().ConfigureAwait(false);
        ChampionMasteryPage result; bool denied = false;
        try
        {
            var client = _client();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var response = await new PlayerStatisticsService(() => client).GetAsync("/lol-champion-mastery/v1/" + Uri.EscapeDataString(puuid) + "/champion-mastery", timeout.Token).ConfigureAwait(false);
            denied = response.Status is 401 or 403;
            if (TryNormalize(response.Data, out var data)) result = new(data, DateTimeOffset.UtcNow);
            else
            {
                string message = response.Message.Length > 0 ? response.Message : "客户端未返回有效英雄成就。";
                lock (_gate) result = !denied && entry.Page?.Data != null ? entry.Page with { Stale = true, Message = message } : new(null, null, message);
            }
            // Reconnection/account changes must not populate or display a response from the old client.
            if (!ReferenceEquals(client, _client()) || key != Key(puuid)) return new(null, null, "客户端账号或连接已变化，请重新查询。");
        }
        catch (OperationCanceledException)
        {
            lock (_gate) result = entry.Page?.Data != null ? entry.Page with { Stale = true, Message = "请求超时，后台稍后重试。" } : new(null, null, "英雄成就读取超时，后台稍后重试。");
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or JsonException)
        { result = new(null, null, "英雄成就暂不可用，后台稍后重试。"); }
        finally { _network.Release(); }
        lock (_gate)
        {
            if (entry.Version != version) return new(null, null, "英雄成就已更新，请重新查询。");
            entry.Page = result; entry.RetryAt = result.Data != null && !result.Stale ? 0 : Environment.TickCount64 + 60000;
            if(result.Data!=null && !result.Stale)entry.Expired=false;
        }
        if (result.Data != null && !result.Stale) await SaveDisk(key, entry, version, result).ConfigureAwait(false);
        else if (denied) await DeleteDisk(key, entry, version).ConfigureAwait(false);
        return result;
    }
    public static bool TryNormalize(JToken? value, out JArray data)
    {
        data = new();
        if (value is not JArray array) return false;
        long? Number(JToken? token)=>token?.Type==JTokenType.Integer && long.TryParse(token.ToString(),out var number)?number:null;
        foreach (var record in array.OfType<JObject>().Where(r => Number(r["championId"]) is > 0 and <= int.MaxValue && Number(r["championPoints"]) >= 0)
                     .GroupBy(r => (int)Number(r["championId"])!).Select(g => g.OrderByDescending(r => Number(r["championPoints"])).First()))
        {
            var row = new JObject { ["championId"] = record["championId"], ["championPoints"] = record["championPoints"] };
            if (Number(record["championLevel"]) is >= 0 and <= int.MaxValue) row["championLevel"] = record["championLevel"];
            if (Number(record["lastPlayTime"]) is > 0 and <= 253402300799999) row["lastPlayTime"] = record["lastPlayTime"];
            data.Add(row);
        }
        return array.Count == 0 || data.Count > 0;
    }
    private async Task SaveDisk(string key, Entry entry, int version, ChampionMasteryPage page)
    {
        await _disk.WaitAsync().ConfigureAwait(false);
        string temporary = PathFor(key) + ".tmp";
        try
        {
            lock (_gate) if (entry.Version != version) return;
            Directory.CreateDirectory(_directory);
            var data = new JObject { ["schema"] = 1, ["fetchedAt"] = page.FetchedAt!.Value.ToUnixTimeMilliseconds(), ["champions"] = page.Data?.DeepClone() };
            await File.WriteAllTextAsync(temporary, data.ToString(Formatting.None), new UTF8Encoding(false)).ConfigureAwait(false);
            lock (_gate) { if (entry.Version != version) return; File.Move(temporary, PathFor(key), true); }
            foreach (var old in new DirectoryInfo(_directory).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(256)) old.Delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } _disk.Release(); }
    }
    private async Task DeleteDisk(string key, Entry entry, int version)
    {
        await _disk.WaitAsync().ConfigureAwait(false);
        try { lock (_gate) if (entry.Version == version) File.Delete(PathFor(key)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        finally { _disk.Release(); }
    }
}
