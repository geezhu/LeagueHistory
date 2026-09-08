using League.Clients;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Reflection;

internal static class AramMetaChecks
{
    private static JObject Fixture(string name)=>JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","AramMeta",name+".json")));
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)=>send(r,t);}
    private static HttpResponseMessage Json(JObject value)=>new(HttpStatusCode.OK){Content=new StringContent(value.ToString())};
    public static async Task RunAsync()
    {
        var index=AramMetaIndex.Parse(Fixture("index"));var ahri=index.Champions.Single(c=>c.Id==103);
        Check(index.Patch=="16.17" && ahri.WinRate!=ahri.RawWinRate && ahri.Tier=="T1","arammeta preserves adjusted and raw rates separately");
        Check(AramMetaIndex.Rate(null)==null && AramMetaIndex.Rate(new JValue(2))==null,"Missing or invalid rates are not zero");
        var population=index with {Champions=Enumerable.Range(1,20).Select(id=>ahri with {Id=id,Games=50}).ToArray()};
        Check(population.SampleMatches==100 && population.PickRate(population.Champions[0])==.5,"Pick rate uses match count, not share of participant slots");
        var missing=population with {Champions=new[]{ahri with {Games=null}}};
        Check(missing.SampleMatches==null && missing.PickRate(ahri)==null,"Incomplete population has no fabricated pick rate");
        Check(AramMetaIndex.RelativeRate(.6,.6)==1 && AramMetaIndex.RelativeRate(.3,.6)==.5 && AramMetaIndex.RelativeRate(0,0)==0,"Win-rate bars normalize to the highest displayed rate");
        int calls=0;bool fail=false;
        using var client=new AramMetaClient(new Handler((r,t)=>
        {
            calls++;if(fail)throw new HttpRequestException("offline");
            return Task.FromResult(Json(Fixture(r.RequestUri!.AbsolutePath.EndsWith("tier-list.json")?"index":"103")));
        }));
        var first=await client.GetAsync(103,false,default);
        Check(calls==3 && first.Detail!=null,"Hero detail verified against current index version");
        first.Detail!.RemoveAll();
        var second=await client.GetAsync(103,false,default);
        Check(calls==3 && second.Cached && second.Detail!.HasValues,"Cached detail is isolated from consumers");
        fail=true;var fallback=await client.GetAsync(103,true,default);
        Check(fallback.Cached && fallback.Detail!.HasValues,"Offline refresh preserves complete cached data");
        using var slow=new AramMetaClient(new Handler(async (r,t)=>{await Task.Delay(5000,t);return Json(Fixture("index"));}));
        using var cancel=new CancellationTokenSource(20);bool cancelled=false;
        try{await slow.GetAsync(0,false,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
        Check(cancelled,"Superseded loads cancel");
        int indices=0,details=0;
        using var changing=new AramMetaClient(new Handler((r,t)=>
        {
            if(r.RequestUri!.AbsolutePath.EndsWith("tier-list.json")){var data=Fixture("index");data["detailVersion"]=++indices==1?"old":"new";return Task.FromResult(Json(data));}
            details++;return Task.FromResult(Json(Fixture("103")));
        }));
        var updated=await changing.GetAsync(103,false,default);
        Check(details==2 && updated.Index.Version=="new","Version change retries detail against new snapshot");
        int indexBodies=0,notModified=0;
        string version="one";
        using var conditional=new AramMetaClient(new Handler((r,t)=>
        {
            if(!r.RequestUri!.AbsolutePath.EndsWith("tier-list.json"))return Task.FromResult(Json(Fixture("103")));
            var tag="\""+version+"\"";
            if(r.Headers.IfNoneMatch.Any(v=>v.Tag==tag))
            {
                notModified++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }
            indexBodies++;var data=Fixture("index");data["detailVersion"]=version;
            var response=Json(data);response.Headers.ETag=new System.Net.Http.Headers.EntityTagHeaderValue(tag);
            return Task.FromResult(response);
        }));
        await conditional.GetAsync(103,false,default);
        await conditional.GetAsync(157,false,default);
        Check(indexBodies==1 && notModified==2,"Opening new heroes validates ETag without downloading another index body");
        version="two";
        var changed=await conditional.GetAsync(103,true,default);
        Check(changed.Index.Version=="two" && indexBodies==2 && notModified==3,"Conditional index refresh still detects new versions");
        string cacheFile=Path.Combine(Path.GetTempPath(),"arammeta-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            using(var writer=new AramMetaClient(new Handler((r,t)=>
            {
                var response=Json(Fixture(r.RequestUri!.AbsolutePath.EndsWith("tier-list.json")?"index":"103"));
                response.Headers.ETag=new System.Net.Http.Headers.EntityTagHeaderValue("\"disk\"");
                return Task.FromResult(response);
            }),cacheFile))await writer.GetAsync(103,false,default);
            Check(File.Exists(cacheFile),"Statistics snapshot is persisted");
            int requests=0;var lifetime=TimeSpan.FromHours(1);
            using(var reader=new AramMetaClient(new Handler((r,t)=>
            {
                requests++;
                Check(r.Headers.IfNoneMatch.Any(v=>v.Tag=="\"disk\""),"Disk cache restores HTTP validators");
                throw new HttpRequestException("offline");
            }),cacheFile,()=>lifetime))
            {
                var restored=await reader.GetAsync(103,false,default);
                Check(requests==0 && restored.Cached && restored.Detail!=null,"Restart reuses unexpired statistics without network");
                lifetime=TimeSpan.Zero;
                var stale=await reader.GetAsync(103,false,default);
                Check(requests==1 && stale.UpdateFailed && stale.FetchedAt==restored.FetchedAt,"Lifetime changes apply immediately and failed refresh does not renew stale data");
                lifetime=TimeSpan.FromHours(1);
                await reader.GetAsync(103,true,default);
                Check(requests==2,"Manual refresh bypasses fresh disk data");
            }
            File.WriteAllText(cacheFile,"broken json");
            using var recovery=new AramMetaClient(new Handler((r,t)=>Task.FromResult(Json(Fixture("index")))),cacheFile);
            var recovered=await recovery.GetAsync(0,false,default);
            await recovery.FlushAsync();
            Check(recovered.Index.Champions.Length==3 && JObject.Parse(File.ReadAllText(cacheFile))["schema"]?.Value<int>()==1,"Corrupt cache is replaced with a valid response");
        }
        finally{if(File.Exists(cacheFile))File.Delete(cacheFile);}
        Check(Newtonsoft.Json.JsonConvert.DeserializeObject<LeagueConfig>("{}")!.AramMetaCacheMinutes==1440,"Existing settings default to one day");
        var detailStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDetail=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AramMetaPage? preview=null;
        using var progressive=new AramMetaClient(new Handler(async (r,t)=>
        {
            if(r.RequestUri!.AbsolutePath.EndsWith("tier-list.json"))return Json(Fixture("index"));
            detailStarted.TrySetResult();await releaseDetail.Task.WaitAsync(t);return Json(Fixture("103"));
        }));
        var loading=progressive.GetAsync(103,false,default,page=>preview=page);
        await detailStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(preview!=null && preview.Detail==null && preview.Index.Champions.Any(c=>c.Id==103) && !loading.IsCompleted,"Hero summary is delivered before a slow detail response");
        releaseDetail.SetResult();
        Check((await loading).Detail!=null,"Full detail follows the preview");
        int repeatedPreviews=0;
        await progressive.GetAsync(103,false,default,_=>repeatedPreviews++);
        Check(repeatedPreviews==0,"Cached heroes render once without a loading preview");
        Console.WriteLine("PASS: arammeta models, cache, cancellation, version consistency.");
    }
    public static void Render()
    {
        var client=new AramMetaClient(new Handler((r,t)=>Task.FromResult(Json(Fixture(r.RequestUri!.AbsolutePath.EndsWith("tier-list.json")?"index":"103")))));
        int imageRequests=0;
        using var images=new HttpClient(new Handler((r,t)=>{imageRequests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));}));
        var type=typeof(PlayerCardControl).Assembly.GetType("League.Controls.AramMetaView")!;
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var view=(Control)Activator.CreateInstance(type,flags,null,new object[]{client,images},null)!;
        var resourceName="arammeta-test-"+Guid.NewGuid().ToString("N");
        var champion=new Champion {Id=999999,Name=resourceName,Alias=resourceName};
        var cacheFile=Path.Combine(Application.StartupPath,"Assets","champion",resourceName+".png");
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        using(var source=new Bitmap(16,16)){source.SetPixel(0,0,Color.Red);source.Save(cacheFile);}
        League.FormMain.Globals.resLoading.Champions.Champions.Add(champion);
        try
        {
            var task=(Task<Image?>)type.GetMethod("ImageAsync",flags)!.Invoke(view,new object[]{"https://arammeta.com/assets/icons/"+resourceName+".png","champion",champion.Id})!;
            while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(5);}
            var cached=task.GetAwaiter().GetResult();
            Check(cached is Bitmap bitmap && bitmap.GetPixel(0,0).R==255 && imageRequests==0,"arammeta reuses match-history disk images without a public image request");
        }
        finally
        {
            League.FormMain.Globals.resLoading.Champions.Champions.Remove(champion);
            File.Delete(cacheFile);
        }
        using var form=new Form {ClientSize=new Size(1060,800)};form.Controls.Add(view);form.Show();Application.DoEvents();
        void Run(string method,params object[] args)
        {
            var task=(Task)type.GetMethod(method,flags)!.Invoke(view,args)!;
            var deadline=DateTime.UtcNow.AddSeconds(10);
            while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(5);if(DateTime.UtcNow>deadline)throw new Exception("Timed out: "+method);}task.GetAwaiter().GetResult();Application.DoEvents();
        }
        Run("LoadAsync",false);
        var grid=(DataGridView)type.GetField("_grid",flags)!.GetValue(view)!;
        var search=(TextBox)type.GetField("_search",flags)!.GetValue(view)!;
        Check(grid.Rows.Count==3,"Rankings display complete fixture (rows="+grid.Rows.Count+", search="+search.Text+", context="+SynchronizationContext.Current?.GetType().Name+"): "+((Label)type.GetField("_status",flags)!.GetValue(view)!).Text);Save("arammeta-rankings",1060,800);search.Text="Ahri";Check(grid.Rows.Count==1,"Champion search filters aliases");
        Run("NavigateAsync",103);
        var detailPanel=(FlowLayoutPanel)type.GetField("_detail",flags)!.GetValue(view)!;
        var complete=(AramMetaPage)type.GetField("_page",flags)!.GetValue(view)!;
        var render=type.GetMethod("RenderDetail",flags)!;
        render.Invoke(view,new object[]{complete with {Detail=null},103,CancellationToken.None,false});
        var summary=detailPanel.Controls[0];
        render.Invoke(view,new object[]{complete,103,CancellationToken.None,true});
        Check(ReferenceEquals(summary,detailPanel.Controls[0]) && detailPanel.Controls.Count==8,"Equipment completion preserves summary controls and replaces only placeholders");
        Save("arammeta-detail",1060,800);
        Run("TravelAsync",-1);Check(search.Text=="Ahri" && grid.Rows.Count==1 && grid.Visible,"Back restores ranking search");
        Run("TravelAsync",1);Check((int)type.GetField("_champion",flags)!.GetValue(view)! ==103,"Forward restores hero");
        form.ClientSize=new Size(660,700);Application.DoEvents();Save("arammeta-narrow",660,700);
        void Save(string file,int width,int height){using var bmp=new Bitmap(width,height);form.DrawToBitmap(bmp,new Rectangle(0,0,width,height));bmp.Save(Path.Combine(AppContext.BaseDirectory,file+".png"));}
        form.Close();Console.WriteLine("PASS: arammeta navigation, filtering, desktop layouts.");
    }
}
