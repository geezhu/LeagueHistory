using League.Services;

namespace League.Controls;

public sealed class ChatSendStatusLabel : Label
{
    public ChatSendStatusLabel()
    {
        AutoSize=true;ChatSendDiagnostics.Shared.Changed+=Changed;
        UpdateText();
    }
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);UpdateText();}
    private void Changed()
    {
        if(IsDisposed || !IsHandleCreated)return;
        try{if(InvokeRequired)BeginInvoke((Action)UpdateText);else UpdateText();}catch(InvalidOperationException){}
    }
    private void UpdateText()
    {
        if(IsDisposed)return;var current=ChatSendDiagnostics.Shared.Current;
        Text=$"快捷发送 · {current.Time:HH:mm:ss} · {current.Message}";
    }
    protected override void Dispose(bool disposing){if(disposing)ChatSendDiagnostics.Shared.Changed-=Changed;base.Dispose(disposing);}
}
