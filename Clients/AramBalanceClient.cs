using System.Globalization;
using Newtonsoft.Json.Linq;

namespace League.Clients;

public sealed class AramBalanceClient : IDisposable
{
    public static readonly AramBalanceClient Shared = new();
    private readonly HttpClient _http;
    private readonly string? _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private JObject? _data;
    private DateTime _fetched,
        _failed;
    private bool _loaded;

    public AramBalanceClient(HttpMessageHandler? handler = null, string? cachePath = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(10) };
        _path =
            cachePath
            ?? (
                handler == null
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "League",
                        "Cache",
                        "aramonly",
                        "balance.json"
                    )
                    : null
            );
    }

    public static JArray Nodes(JObject data) =>
        data.SelectToken("result.data.allAramModifiersJson.nodes") as JArray is { Count: > 0 } nodes
            ? nodes
            : throw new InvalidDataException("平衡数据结构不完整。");

    private static string Key(string value) =>
        new(value.Replace("&", "and").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static string Describe(JObject data, string alias)
    {
        string key = Key(alias);
        if (key == "monkeyking")
        {
            key = "wukong";
        }

        if (key == "nunu")
        {
            key = "nunuandwillump";
        }

        if (key == "renata")
        {
            key = "renataglasc";
        }

        var row = Nodes(data)
            .OfType<JObject>()
            .FirstOrDefault(r =>
                r["champion"] is JObject champion
                && (
                    Key(champion["name"]?.Type == JTokenType.String ? (string)champion["name"]! : "") == key
                    || Key(
                        champion["sanitizedName"]?.Type == JTokenType.String ? (string)champion["sanitizedName"]! : ""
                    ) == key
                )
            );
        if (row == null || key.Length == 0)
        {
            return "暂未匹配到该英雄的平衡数据。";
        }

        var lines = new List<string>();
        bool missing = false;
        foreach (
            var (field, title) in new[]
            {
                ("aramDamageDealt", "造成伤害"),
                ("aramDamageTaken", "受到伤害"),
                ("aramHealing", "治疗效果"),
                ("aramShielding", "护盾效果"),
                ("aramTenacity", "韧性"),
                ("aramAbilityHaste", "技能急速"),
                ("aramAttackSpeed", "攻速成长"),
                ("aramEnergyRegen", "能量回复"),
            }
        )
        {
            if (row[field]?.Type is not (JTokenType.Integer or JTokenType.Float))
            {
                missing = true;
                continue;
            }
            double value = (double)row[field]!;
            bool flat = field == "aramAbilityHaste";
            double delta = flat ? value : (value - 1) * 100;
            if (Math.Abs(delta) < .00001)
            {
                continue;
            }

            lines.Add(title + " " + delta.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + (flat ? "" : "%"));
        }
        if (missing)
        {
            lines.Add("部分调整项暂无数据。");
        }

        return lines.Count > 0 ? string.Join("\n", lines) : "来源列出的各项属性均无额外调整。";
    }

    public async Task<string> GetAsync(string alias, TimeSpan lifetime, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!_loaded)
            {
                _loaded = true;
                try
                {
                    if (_path != null && File.Exists(_path))
                    {
                        var cache = JObject.Parse(await File.ReadAllTextAsync(_path, token));
                        var data = (JObject)cache["data"]!;
                        Nodes(data);
                        _fetched = cache["fetched"]!.Value<DateTime>().ToUniversalTime();
                        _data = data;
                    }
                }
                catch (OperationCanceledException)
                {
                    _loaded = false;
                    throw;
                }
                catch { }
            }
            bool stale = _data == null || DateTime.UtcNow - _fetched > lifetime || _fetched > DateTime.UtcNow;
            if (stale && DateTime.UtcNow - _failed > TimeSpan.FromMinutes(1))
            {
                try
                {
                    var data = JObject.Parse(
                        await _http.GetStringAsync(
                            "https://www.aramonly.com/page-data/aram-changes/page-data.json",
                            token
                        )
                    );
                    Nodes(data);
                    _data = data;
                    _fetched = DateTime.UtcNow;
                    _failed = default;
                    stale = false;
                    if (_path != null)
                    {
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                            string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            try
                            {
                                await File.WriteAllTextAsync(
                                    temp,
                                    new JObject { ["fetched"] = _fetched, ["data"] = data }.ToString()
                                );
                                File.Move(temp, _path, true);
                            }
                            finally
                            {
                                if (File.Exists(temp))
                                {
                                    File.Delete(temp);
                                }
                            }
                        }
                        catch
                        { /* Keep the valid memory snapshot if disk is unavailable. */
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    _failed = DateTime.UtcNow;
                }
            }
            if (_data == null)
            {
                return "普通大乱斗平衡\n暂时无法读取数据，稍后悬浮重试。";
            }

            string patch = (string?)_data.SelectToken("result.data.patchVersionJson.patchVersion") ?? "未知";
            string updated = (string?)_data.SelectToken("result.data.aramModifiersJson.timestamp") ?? "未知";
            return "普通大乱斗平衡\n"
                + Describe(_data, alias)
                + $"\n\n来源：aramonly · 版本 {patch}\n来源更新时间：{updated}\n缓存时间：{_fetched.ToLocalTime():MM-dd HH:mm}"
                + (stale ? "（更新失败，使用旧缓存）" : "")
                + "\n来源版本可能落后于客户端；不代表海克斯大乱斗。";
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _http.Dispose();
}
