using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Clients;

public sealed class OpggClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1,1);
    private readonly Dictionary<string,(DateTime Time,JObject Data)> _cache = new();
    public OpggClient(HttpMessageHandler? handler = null)
    {
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri("https://lol-api-champion.op.gg");
        _http.Timeout = TimeSpan.FromSeconds(20);
    }
    public static void Validate(OpggQuery query)
    {
        if (!new[]{"global","kr","na","euw"}.Contains(query.Region) || !new[]{"ranked","aram","arena","urf","nexus_blitz"}.Contains(query.Mode)
            || !new[]{"all","ibsg","gold_plus","platinum_plus","emerald_plus","diamond_plus","master","master_plus","grandmaster","challenger"}.Contains(query.Tier)
            || (query.Mode=="ranked" && !new[]{"top","jungle","mid","adc","support"}.Contains(query.Position)))
            throw new InvalidOperationException("该模式或筛选条件暂不支持 OP.GG 攻略。");
    }
    public async Task<OpggBuild> GetBuildAsync(OpggQuery query,bool refresh,CancellationToken token)
    {
        query = query.Normalize(); Validate(query);
        if(query.ChampionId<=0) throw new InvalidOperationException("请选择英雄。");
        string path = $"/api/{query.Region}/champions/{query.Mode}/{query.ChampionId}" + (query.Mode=="arena" ? "" : "/"+query.Position) + "?tier="+query.Tier;
        return OpggBuild.Parse(await GetAsync(path,refresh,token),query);
    }
    public async Task<(string Version,OpggTier[] Rows)> GetTiersAsync(OpggQuery query,bool refresh,CancellationToken token)
    {
        query=query.Normalize(); Validate(query);
        var raw=await GetAsync($"/api/{query.Region}/champions/{query.Mode}?tier={query.Tier}",refresh,token);
        return ((string?)raw["meta"]?["version"] ?? "未知",OpggBuild.ParseTiers(raw,query.Mode));
    }
    private async Task<JObject> GetAsync(string path,bool refresh,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if(!refresh && _cache.TryGetValue(path,out var entry) && DateTime.UtcNow-entry.Time<TimeSpan.FromMinutes(15)) return (JObject)entry.Data.DeepClone();
            for(int attempt=0;attempt<2;attempt++)
            {
                using var response=await _http.GetAsync(path,token);
                if(attempt==0 && ((int)response.StatusCode>=500 || (int)response.StatusCode==429)) { await Task.Delay(1000,token); continue; }
                if(response.StatusCode==System.Net.HttpStatusCode.NotFound || (int)response.StatusCode==422) throw new InvalidDataException("当前英雄、模式或分路暂无可用数据。");
                response.EnsureSuccessStatusCode();
                var data=JObject.Parse(await response.Content.ReadAsStringAsync(token));
                if(_cache.Count>=128) _cache.Remove(_cache.MinBy(e=>e.Value.Time).Key);
                _cache[path]=(DateTime.UtcNow,data);
                return (JObject)data.DeepClone();
            }
            throw new HttpRequestException("OP.GG 暂时无法访问，请稍后重试。");
        }
        finally { _gate.Release(); }
    }
    public void Dispose() => _http.Dispose();
}
