using League.Services;
using System.Reflection;

internal static class ShutdownUiChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run()
    {
        var previous=SynchronizationContext.Current;
        using var context=new WindowsFormsSynchronizationContext();SynchronizationContext.SetSynchronizationContext(context);
        using var owner=new League.FormMain();
        using var preview=new Form{ClientSize=new Size(760,400)};
        var flow=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};
        preview.Controls.Add(flow);
        var store=new ReadOnlyTasks();var service=new ScheduledShutdownService(tasks:store);
        typeof(League.FormMain).GetMethod("ConfigureShutdownSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(owner,new object[]{flow,service});
        var summary=(Label)flow.Controls.Find("ShutdownPlanSummary",true).Single();
        var status=(Label)flow.Controls.Find("ShutdownStatus",true).Single();
        var create=(Button)flow.Controls.Find("CreateShutdownPlan",true).Single();
        var delete=(Button)flow.Controls.Find("DeleteShutdownPlan",true).Single();
        var log=(Button)flow.Controls.Find("ShutdownLog",true).Single();
        preview.Show();
        try
        {
            Pump(()=>summary.Text=="尚未创建每日关机计划");
            Check(create.Enabled && !delete.Enabled && log.Enabled && !status.Text.Contains("失败"),"An absent task enables creation without a read error");
            var time=(DateTimePicker)flow.Controls.Find("ShutdownTime",true).Single();time.Value=DateTime.Today.AddHours(21);
            store.Error=new UnauthorizedAccessException("read-only test denied");
            Pump(()=>summary.Text=="每日关机计划读取失败");
            Check(!create.Enabled && !delete.Enabled && log.Enabled && status.Text.Contains("失败"),"Read failures stop the loading display and disable task mutations");
            store.Error=null;Pump(()=>create.Enabled);
            Check(time.Value.Hour==21 && summary.Text=="尚未创建每日关机计划","Recovery keeps the user's unsaved time choice");
            Check(store.Mutations==0,"Opening and refreshing never create, enable, delete or stop a task");
            Console.WriteLine("PASS: shutdown settings handle missing/read-failed/recovered tasks and retain unsaved input. No system task changed.");
        }
        finally{preview.Close();SynchronizationContext.SetSynchronizationContext(previous);}
    }
    private static void Pump(Func<bool> ready)
    {
        var until=DateTime.UtcNow.AddSeconds(9);
        while(!ready() && DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(5);}
        Check(ready(),"Shutdown settings update completes");
    }
    private sealed class ReadOnlyTasks : IShutdownTaskStore
    {
        public volatile Exception? Error;public int Mutations;
        public ShutdownTaskRecord? Read(){if(Error is {} error)throw error;return null;}
        public void Register(string xml)=>Mutations++;
        public void Stop()=>Mutations++;
        public void Delete()=>Mutations++;
        public void SetEnabled(bool enabled)=>Mutations++;
    }
}
