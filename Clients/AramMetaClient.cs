using League.Models;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http.Headers;

namespace League.Clients;

public sealed class AramMetaClient : IDisposable
{
    private readonly HttpClient _http;
    // Clients in the main page, assistant and game tools share one snapshot per file.
    private sealed class CacheState
    {
        public readonly SemaphoreSlim Gate=new(1,1);
        public readonly Dictionary<string,JObject> Details=new();
        public AramMetaIndex? Index;
        public DateTime Fetched;
        public AramMetaIndex? ValidatedIndex;
        public EntityTagHeaderValue? IndexTag;
        public DateTimeOffset? IndexModified;
        public bool DiskLoaded;
        public Task SaveTask=Task.CompletedTask;
        public int Users;
    }
    private static readonly Dictionary<string,CacheState> SharedCaches=new(StringComparer.OrdinalIgnoreCase);
    private readonly CacheState _cache;
    private readonly string? _cachePath;
    private readonly Func<TimeSpan> _cacheLifetime;
    private int _disposed;
    public AramMetaClient(HttpMessageHandler? handler=null,string? cachePath=null,Func<TimeSpan>? cacheLifetime=null)
    {
        _cachePath=cachePath??(handler==null?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"League","Cache","arammeta","statistics.json"):null);
        if(_cachePath!=null)_cachePath=Path.GetFullPath(_cachePath);
        lock(SharedCaches)
        {
            if(_cachePath==null)_cache=new();
            else if(SharedCaches.TryGetValue(_cachePath,out var shared))_cache=shared;
            else SharedCaches[_cachePath]=_cache=new();
            _cache.Users++;
        }
        _cacheLifetime=cacheLifetime??(()=>TimeSpan.FromDays(1));
        _http=new(handler??new HttpClientHandler {AutomaticDecompression=DecompressionMethods.All});
        _http.BaseAddress=new Uri("https://arammeta.com/");_http.Timeout=TimeSpan.FromSeconds(20);
    }
    public bool IsCurrent(AramMetaPage page,int champion)
    {
        if(_disposed!=0 || !_cache.Gate.Wait(0))return false;
        try
        {
            return !page.UpdateFailed && _cache.Index!=null && page.FetchedAt==_cache.Fetched &&
                page.Index.Version==_cache.Index.Version && DateTime.UtcNow-_cache.Fetched<_cacheLifetime() &&
                (champion==0 || page.Detail!=null && _cache.Details.ContainsKey(_cache.Index.Version+":"+champion));
        }
        finally {_cache.Gate.Release();}
    }
    private async Task<AramMetaIndex> FetchIndex(CancellationToken token)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"api/tier-list.json");
        if(_cache.ValidatedIndex!=null)
        {
            if(_cache.IndexTag!=null)request.Headers.IfNoneMatch.Add(_cache.IndexTag);
            else if(_cache.IndexModified.HasValue)request.Headers.IfModifiedSince=_cache.IndexModified;
        }
        using var response=await _http.SendAsync(request,token).ConfigureAwait(false);
        if(response.StatusCode==HttpStatusCode.NotModified && _cache.ValidatedIndex!=null)return _cache.ValidatedIndex;
        response.EnsureSuccessStatusCode();
        var index=AramMetaIndex.Parse(JObject.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)));
        _cache.ValidatedIndex=index;_cache.IndexTag=response.Headers.ETag;_cache.IndexModified=response.Content.Headers.LastModified;
        return index;
    }
    public async Task<AramMetaPage> GetAsync(int champion,bool refresh,CancellationToken token,Action<AramMetaPage>? preview=null)
    {
        ObjectDisposedException.ThrowIf(_disposed!=0,this);
        await _cache.Gate.WaitAsync(token);
        try
        {
            if(!_cache.DiskLoaded){await LoadDiskAsync();_cache.DiskLoaded=true;}
            token.ThrowIfCancellationRequested();
            var old=_cache.Index;var oldTime=_cache.Fetched;
            try
            {
                var index=_cache.Index;
                bool cached=!refresh && index!=null && DateTime.UtcNow-_cache.Fetched<_cacheLifetime();
                var indexTime=_cache.Fetched;
                if(!cached){index=await FetchIndex(token);indexTime=DateTime.UtcNow;}
                if(champion>0 && !index!.Champions.Any(c=>c.Id==champion))throw new InvalidDataException("此英雄暂无海克斯统计，可返回英雄榜查看。");
                JObject? detail=null;
                if(champion>0)
                {
                    var key=index!.Version+":"+champion;
                    if(!refresh && _cache.Details.TryGetValue(key,out var saved))detail=saved;
                    else
                    {
                        for(int attempt=0;attempt<2;attempt++)
                        {
                            token.ThrowIfCancellationRequested();
                            if(preview!=null)preview(await ClonePageAsync(index,null,cached,indexTime,token));
                            detail=JObject.Parse(await _http.GetStringAsync($"api/champions/{champion}.json?v={Uri.EscapeDataString(index.Version)}",token));
                            var confirmed=await FetchIndex(token);
                            indexTime=DateTime.UtcNow;
                            if(confirmed.Version==index.Version)break;
                            index=confirmed;detail=null;
                        }
                        if(detail==null)throw new InvalidDataException("数据正在更新，请稍后刷新。");
                        if(!detail.Properties().Any(p=>p.Name is "items" or "spells" or "boots" or "bot"))throw new InvalidDataException("英雄详情数据不完整。");
                        if(_cache.Details.Count>=200)_cache.Details.Clear();
                        _cache.Details[index.Version+":"+champion]=(JObject)detail.DeepClone();
                        cached=false;
                    }
                }
                if(_cache.Index?.Version!=index!.Version)_cache.Details.Keys.Where(k=>!k.StartsWith(index.Version+":",StringComparison.Ordinal)).ToList().ForEach(k=>_cache.Details.Remove(k));
                _cache.Index=index;_cache.Fetched=indexTime;
                if(!cached)QueueDiskSave();
                return await ClonePageAsync(index,detail,cached,_cache.Fetched,token);
            }
            catch(Exception ex) when(!token.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
            {
                if(old!=null && (champion==0 || _cache.Details.ContainsKey(old.Version+":"+champion)))
                    return (await ClonePageAsync(old,champion==0?null:_cache.Details[old.Version+":"+champion],true,oldTime,token)) with {UpdateFailed=true};
                throw;
            }
        }
        finally {_cache.Gate.Release();}
    }
    // Keep the caller's synchronization context for previews, but copy large JSON off the UI thread.
    private static Task<AramMetaPage> ClonePageAsync(AramMetaIndex index,JObject? detail,bool cached,DateTime time,CancellationToken token)=>
        Task.Run(()=>new AramMetaPage(AramMetaIndex.Parse(index.Raw),(JObject?)detail?.DeepClone(),cached,time),token);
    private Task LoadDiskAsync()=>_cachePath==null?Task.CompletedTask:Task.Run(()=>
    {
        if(_cachePath==null || !File.Exists(_cachePath))return;
        try
        {
            var saved=JObject.Parse(File.ReadAllText(_cachePath));
            if((int?)saved["schema"]!=1 || saved["index"] is not JObject raw)return;
            var index=AramMetaIndex.Parse(raw);
            var fetched=(DateTime?)saved["fetchedUtc"]??DateTime.MinValue;
            if(fetched.Kind!=DateTimeKind.Utc || fetched>DateTime.UtcNow.AddMinutes(1))return;
            var details=(saved["details"] as JObject??new()).Properties()
                .Where(p=>p.Name.StartsWith(index.Version+":",StringComparison.Ordinal) && p.Value is JObject value && value.Properties().Any(v=>v.Name is "items" or "spells" or "boots" or "bot"))
                .Take(200).ToArray();
            _cache.Index=_cache.ValidatedIndex=index;_cache.Fetched=fetched;
            if(EntityTagHeaderValue.TryParse((string?)saved["etag"],out var tag))_cache.IndexTag=tag;
            _cache.IndexModified=(DateTimeOffset?)saved["lastModified"];
            foreach(var entry in details)_cache.Details[entry.Name]=(JObject)entry.Value;
        }
        catch(Exception ex){System.Diagnostics.Debug.WriteLine("arammeta cache read: "+ex.Message);}
    });
    private void QueueDiskSave()
    {
        if(_cachePath==null || _cache.Index==null)return;
        // Stored documents are immutable internally. Capture references while holding _cache.Gate;
        // serialize and write on the worker, without blocking rendering or the next request.
        var raw=_cache.Index.Raw;
        var details=_cache.Details.ToDictionary(entry=>entry.Key,entry=>entry.Value);
        var fetched=_cache.Fetched;var tag=_cache.IndexTag?.ToString();var modified=_cache.IndexModified;
        _cache.SaveTask=_cache.SaveTask.ContinueWith(_=>WriteDisk(raw,details,fetched,tag,modified),CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default);
    }
    public Task FlushAsync()=>_cache.SaveTask;
    private void WriteDisk(JObject raw,Dictionary<string,JObject> details,DateTime fetched,string? tag,DateTimeOffset? modified)
    {
        string temporary=_cachePath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            var saved=new JObject { ["schema"]=1,["fetchedUtc"]=fetched,["index"]=raw.DeepClone(),
                ["etag"]=tag,["lastModified"]=modified,
                ["details"]=JObject.FromObject(details) };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_cachePath!))!);
            File.WriteAllText(temporary,saved.ToString(Newtonsoft.Json.Formatting.None));
            File.Move(temporary,_cachePath!,true);
        }
        catch(Exception ex){System.Diagnostics.Debug.WriteLine("arammeta cache write: "+ex.Message);}
        finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0)return;
        _http.Dispose();
        lock(SharedCaches)
        {
            _cache.SaveTask.GetAwaiter().GetResult();
            if(--_cache.Users==0 && _cachePath!=null)SharedCaches.Remove(_cachePath);
        }
    }
}
