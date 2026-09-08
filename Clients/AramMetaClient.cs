using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Clients;

public sealed class AramMetaClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly Dictionary<string,JObject> _details=new();
    private AramMetaIndex? _index;
    private DateTime _fetched;
    public AramMetaClient(HttpMessageHandler? handler=null)
    {
        _http=handler==null?new():new(handler);
        _http.BaseAddress=new Uri("https://arammeta.com/");_http.Timeout=TimeSpan.FromSeconds(20);
    }
    private async Task<AramMetaIndex> FetchIndex(CancellationToken token)=>AramMetaIndex.Parse(JObject.Parse(await _http.GetStringAsync("api/tier-list.json",token)));
    public async Task<AramMetaPage> GetAsync(int champion,bool refresh,CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var old=_index;var oldTime=_fetched;
            try
            {
                var index=_index;
                bool cached=!refresh && index!=null && DateTime.UtcNow-_fetched<TimeSpan.FromHours(1);
                if(!cached)index=await FetchIndex(token);
                if(champion>0 && !index!.Champions.Any(c=>c.Id==champion))throw new InvalidDataException("此英雄暂无海克斯统计，可返回英雄榜查看。");
                JObject? detail=null;
                if(champion>0)
                {
                    var key=index!.Version+":"+champion;
                    if(!refresh && _details.TryGetValue(key,out var saved))detail=saved;
                    else
                    {
                        for(int attempt=0;attempt<2;attempt++)
                        {
                            detail=JObject.Parse(await _http.GetStringAsync($"api/champions/{champion}.json?v={Uri.EscapeDataString(index.Version)}",token));
                            var confirmed=await FetchIndex(token);
                            if(confirmed.Version==index.Version)break;
                            index=confirmed;detail=null;
                        }
                        if(detail==null)throw new InvalidDataException("数据正在更新，请稍后刷新。");
                        if(!detail.Properties().Any(p=>p.Name is "items" or "spells" or "boots" or "bot"))throw new InvalidDataException("英雄详情数据不完整。");
                        if(_details.Count>=200)_details.Clear();
                        _details[index.Version+":"+champion]=(JObject)detail.DeepClone();
                        cached=false;
                    }
                }
                if(_index?.Version!=index!.Version)_details.Keys.Where(k=>!k.StartsWith(index.Version+":",StringComparison.Ordinal)).ToList().ForEach(k=>_details.Remove(k));
                _index=index;if(!cached)_fetched=DateTime.UtcNow;
                return ClonePage(index,detail,cached,_fetched);
            }
            catch(Exception ex) when(!token.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
            {
                if(old!=null && (champion==0 || _details.ContainsKey(old.Version+":"+champion)))
                    return ClonePage(old,champion==0?null:_details[old.Version+":"+champion],true,oldTime);
                throw;
            }
        }
        finally {_gate.Release();}
    }
    private static AramMetaPage ClonePage(AramMetaIndex index,JObject? detail,bool cached,DateTime time)=>
        new(AramMetaIndex.Parse(index.Raw),(JObject?)detail?.DeepClone(),cached,time);
    public void Dispose()=>_http.Dispose();
}
