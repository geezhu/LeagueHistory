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
        Console.WriteLine("PASS: arammeta models, cache, cancellation, version consistency.");
    }
    public static void Render()
    {
        var client=new AramMetaClient(new Handler((r,t)=>Task.FromResult(Json(Fixture(r.RequestUri!.AbsolutePath.EndsWith("tier-list.json")?"index":"103")))));
        using var images=new HttpClient(new Handler((r,t)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        var type=typeof(PlayerCardControl).Assembly.GetType("League.Controls.AramMetaView")!;
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var view=(Control)Activator.CreateInstance(type,flags,null,new object[]{client,images},null)!;
        using var form=new Form {ClientSize=new Size(1060,800)};form.Controls.Add(view);form.Show();Application.DoEvents();
        void Run(string method,params object[] args)
        {
            var task=(Task)type.GetMethod(method,flags)!.Invoke(view,args)!;
            while(!task.IsCompleted){Application.DoEvents();Thread.Sleep(5);}task.GetAwaiter().GetResult();Application.DoEvents();
        }
        Run("LoadAsync",false);
        var grid=(DataGridView)type.GetField("_grid",flags)!.GetValue(view)!;
        var search=(TextBox)type.GetField("_search",flags)!.GetValue(view)!;
        Check(grid.Rows.Count==3,"Rankings display complete fixture");search.Text="Ahri";Check(grid.Rows.Count==1,"Champion search filters aliases");
        Run("NavigateAsync",103);Save("arammeta-detail",1060,800);
        Run("TravelAsync",-1);Check(search.Text=="Ahri" && grid.Rows.Count==1 && grid.Visible,"Back restores ranking search");
        Run("TravelAsync",1);Check((int)type.GetField("_champion",flags)!.GetValue(view)! ==103,"Forward restores hero");
        form.ClientSize=new Size(660,700);Application.DoEvents();Save("arammeta-narrow",660,700);
        void Save(string file,int width,int height){using var bmp=new Bitmap(width,height);form.DrawToBitmap(bmp,new Rectangle(0,0,width,height));bmp.Save(Path.Combine(AppContext.BaseDirectory,file+".png"));}
        form.Close();Console.WriteLine("PASS: arammeta navigation, filtering, desktop layouts.");
    }
}
