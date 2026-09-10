using System.Runtime.InteropServices;
using League.Services;

namespace League.Managers;

/// <summary>Receives exact shortcuts without intercepting unrelated game input.</summary>
internal sealed class TierHotkeyWindow : NativeWindow,IDisposable
{
    private readonly Action<Keys> _pressed;
    private readonly Dictionary<int,Keys> _registered=new();
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool UnregisterHotKey(IntPtr window,int id);
    public TierHotkeyWindow(Action<Keys> pressed, params Keys[] keys)
    {
        _pressed=pressed;
        CreateHandle(new CreateParams{Caption="League tier hotkeys",Parent=new IntPtr(-3)});
        if (keys.Length == 0) keys = new[] { Keys.F9, Keys.F11, Keys.Control | Keys.F7, Keys.Control | Keys.F9, Keys.Control | Keys.F11 };
        foreach(var (key,index) in keys.Select((key,index)=>(key,index)))
        {
            int id = index + 1;
            uint modifiers=0x4000;
            if((key & Keys.Control)!=0)modifiers|=0x0002;
            if((key & Keys.Shift)!=0)modifiers|=0x0004;
            if((key & Keys.Alt)!=0)modifiers|=0x0001;
            string label=new KeysConverter().ConvertToString(key)??key.ToString();
            if(RegisterHotKey(Handle,id,modifiers,(uint)(key & Keys.KeyCode)))
            {
                _registered[id]=key;
                ChatSendDiagnostics.Shared.Report("hotkey-registered",$"{label} 已注册为 Windows 全局快捷键。");
            }
            else
            {
                int error=Marshal.GetLastWin32Error();
                ChatSendDiagnostics.Shared.Report("hotkey-registration-error",$"{label} 注册失败，错误码 {error}"+(error==1409?"（已被其他程序占用，请关闭冲突程序后重启）":"")+"。");
            }
        }
    }
    public bool Handles(Keys key)=>_registered.ContainsValue(key);
    protected override void WndProc(ref Message message)
    {
        if(message.Msg==0x0312 && _registered.TryGetValue(message.WParam.ToInt32(),out var key))
        {
            try{_pressed(key);}catch(Exception ex){ChatSendDiagnostics.Shared.Report("hotkey-dispatch-error","快捷键派发失败："+ex.GetType().Name);}
            return;
        }
        base.WndProc(ref message);
    }
    public void Dispose()
    {
        if(Handle==IntPtr.Zero)return;
        foreach(int id in _registered.Keys)UnregisterHotKey(Handle,id);
        _registered.Clear();DestroyHandle();
    }
}
