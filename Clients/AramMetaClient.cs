using League.Models;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http.Headers;

namespace League.Clients;

public sealed class AramMetaClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly Dictionary<string,JObject> _details=new();
    private AramMetaIndex? _index;
    private DateTime _fetched;
    private AramMetaIndex? _validatedIndex;
    private EntityTagHeaderValue? _indexTag;
    private DateTimeOffset? _indexModified;
    private readonly string? _cachePath;
    private readonly Func<TimeSpan> _cacheLifetime;
    private bool _diskLoaded;
    private Task _saveTask=Task.CompletedTask;
    public AramMetaClient(HttpMessageHandler? handler=null,string? cachePath=null,Func<TimeSpan>? cacheLifetime=null)
    {
        _cachePath=cachePath??(handler==null?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"League","Cache","arammeta","statistics.json"):null);
        _cacheLifetime=cacheLifetime??(()=>TimeSpan.FromDays(1));
        _http=new(handler??new HttpClientHandler {AutomaticDecompression=DecompressionMethods.All});
        _http.BaseAddress=new Uri("https://arammeta.com/");_http.Timeout=TimeSpan.FromSeconds(20);
    }
    private async Task<AramMetaIndex> FetchIndex(CancellationToken token)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"api/tier-list.json");
        if(_validatedIndex!=null)
        {
            if(_indexTag!=null)request.Headers.IfNoneMatch.Add(_indexTag);
            else if(_indexModified.HasValue)request.Headers.IfModifiedSince=_indexModified;
        }
        using var response=await _http.SendAsync(request,token).ConfigureAwait(false);
        if(response.StatusCode==HttpStatusCode.NotModified && _validatedIndex!=null)return _validatedIndex;
        response.EnsureSuccessStatusCode();
        var index=AramMetaIndex.Parse(JObject.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)));
        _validatedIndex=index;_indexTag=response.Headers.ETag;_indexModified=response.Content.Headers.LastModified;
        return index;
    }
    public async Task<AramMetaPage> GetAsync(int champion,bool refresh,CancellationToken token,Action<AramMetaPage>? preview=null)
    {
        await _gate.WaitAsync(token);
        try
        {
            if(!_diskLoaded){await LoadDiskAsync();_diskLoaded=true;}
            token.ThrowIfCancellationRequested();
            var old=_index;var oldTime=_fetched;
            try
            {
                var index=_index;
                bool cached=!refresh && index!=null && DateTime.UtcNow-_fetched<_cacheLifetime();
                var indexTime=_fetched;
                if(!cached){index=await FetchIndex(token);indexTime=DateTime.UtcNow;}
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
                            token.ThrowIfCancellationRequested();
                            preview?.Invoke(ClonePage(index,null,cached,indexTime));
                            detail=JObject.Parse(await _http.GetStringAsync($"api/champions/{champion}.json?v={Uri.EscapeDataString(index.Version)}",token));
                            var confirmed=await FetchIndex(token);
                            indexTime=DateTime.UtcNow;
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
                _index=index;_fetched=indexTime;
                if(!cached)QueueDiskSave();
                return ClonePage(index,detail,cached,_fetched);
            }
            catch(Exception ex) when(!token.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
            {
                if(old!=null && (champion==0 || _details.ContainsKey(old.Version+":"+champion)))
                    return ClonePage(old,champion==0?null:_details[old.Version+":"+champion],true,oldTime) with {UpdateFailed=true};
                throw;
            }
        }
        finally {_gate.Release();}
    }
    private static AramMetaPage ClonePage(AramMetaIndex index,JObject? detail,bool cached,DateTime time)=>
        new(AramMetaIndex.Parse(index.Raw),(JObject?)detail?.DeepClone(),cached,time);
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
            _index=_validatedIndex=index;_fetched=fetched;
            if(EntityTagHeaderValue.TryParse((string?)saved["etag"],out var tag))_indexTag=tag;
            _indexModified=(DateTimeOffset?)saved["lastModified"];
            foreach(var entry in details)_details[entry.Name]=(JObject)entry.Value;
        }
        catch(Exception ex){System.Diagnostics.Debug.WriteLine("arammeta cache read: "+ex.Message);}
    });
    private void QueueDiskSave()
    {
        if(_cachePath==null || _index==null)return;
        // Stored documents are immutable internally. Capture references while holding _gate;
        // serialize and write on the worker, without blocking rendering or the next request.
        var raw=_index.Raw;
        var details=_details.ToDictionary(entry=>entry.Key,entry=>entry.Value);
        var fetched=_fetched;var tag=_indexTag?.ToString();var modified=_indexModified;
        _saveTask=_saveTask.ContinueWith(_=>WriteDisk(raw,details,fetched,tag,modified),CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default);
    }
    public Task FlushAsync()=>_saveTask;
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
    public void Dispose(){_http.Dispose();_saveTask.GetAwaiter().GetResult();}
}
