using System.Reflection;

internal static class TierHotkeyChecks
{
    public static void Run()
    {
        var context=SynchronizationContext.Current;
        var type=typeof(League.FormMain).Assembly.GetType("League.Managers.TierHotkeyWindow")!;
        var events=new List<Keys>();
        // Isolate registration from the user's running application shortcuts.
        var keys = new[] { Keys.F23, Keys.F24 };
        using(var window=(IDisposable)Activator.CreateInstance(type,new object[]{(Action<Keys>)(key=>events.Add(key)), keys})!)
        {
            var native=(NativeWindow)window;
            foreach(var (id,key) in new[]{(1,Keys.F23),(2,Keys.F24)})
            {
                bool registered=(bool)type.GetMethod("Handles")!.Invoke(window,new object[]{key})!;
                if(!registered)throw new Exception("Test shortcut registration failed; inspect registration diagnostics before retrying");
                // Deliver WM_HOTKEY directly to this isolated receiver; never inject keyboard input.
                var message=Message.Create(native.Handle,0x0312,new IntPtr(id),IntPtr.Zero);
                type.GetMethod("WndProc",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,new object[]{message});
            }
            if(!events.SequenceEqual(keys))throw new Exception("Native shortcut dispatch must preserve each side's key");
        }
        using(var replacement=(IDisposable)Activator.CreateInstance(type,new object[]{(Action<Keys>)(_=>{}), keys})!)
            if(!(bool)type.GetMethod("Handles")!.Invoke(replacement,new object[]{Keys.F23})!)throw new Exception("Shortcut disposal must release registration");
        var stateType=typeof(League.FormMain).Assembly.GetType("League.Managers.TierHotkeyState")!;
        var state=Activator.CreateInstance(stateType)!;
        Keys[] Read(bool ctrl,bool f9,bool f11)=>(Keys[])stateType.GetMethod("Read")!.Invoke(state,new object[]{ctrl,f9,f11})!;
        if(Read(false,true,false).Length!=0)throw new Exception("Plain F9 must not trigger tier sending");
        if(!Read(true,true,false).SequenceEqual(new[]{Keys.F9}))throw new Exception("Ctrl added to held F9 must trigger once");
        for(int i=0;i<40;i++)if(Read(true,true,false).Length!=0)throw new Exception("Held shortcut must not repeat");
        if(!Read(true,true,true).SequenceEqual(new[]{Keys.F11}))throw new Exception("Sides must have independent edges");
        Read(false,true,true);
        if(!Read(true,true,true).SequenceEqual(new[]{Keys.F9,Keys.F11}))throw new Exception("Modifier release must reset both chords");
        Read(false,false,false);
        if(!Read(true,false,true).SequenceEqual(new[]{Keys.F11}))throw new Exception("Released shortcut must be usable again");
        SynchronizationContext.SetSynchronizationContext(context);
        Console.WriteLine("PASS: native hotkey registration/dispatch/release and polling chord edges/repeat suppression; no keys or chat messages sent.");
    }
}
