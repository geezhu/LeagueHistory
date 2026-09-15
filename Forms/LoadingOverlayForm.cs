using League.Controls;
using League.Models;
using System.Runtime.InteropServices;

namespace League;

internal sealed class LoadingOverlayForm : Form
{
    public LoadingOverlayView View {get;}
    public event Action? Dismissed;
    private readonly Label _title=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(14,0,0,0)};
    public LoadingOverlayForm(Func<int,string> champion,Func<LoadingPlayer,string> details)
    {
        Text="对局速览";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
        BackColor=Color.FromArgb(8,29,40);ForeColor=Color.WhiteSmoke;ClientSize=new Size(1020,670);
        var header=new Panel{Dock=DockStyle.Top,Height=42,BackColor=Color.FromArgb(19,59,71)};
        var close=new Button{Dock=DockStyle.Right,Width=72,Text="收起",FlatStyle=FlatStyle.Flat,ForeColor=ForeColor};close.FlatAppearance.BorderSize=0;
        close.Click+=(_,_)=>Dismissed?.Invoke();header.Controls.Add(_title);header.Controls.Add(close);
        View=new LoadingOverlayView(champion,details);Controls.Add(View);Controls.Add(header);
        SetRosterTitle();
    }
    public void SetRosterTitle()=>_title.Text="对局速览 · 双方英雄";
    public void SetPreviewTitle()=>_title.Text="对局速览 · 显示测试       点击右侧收起";
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x00000080;return p;}}
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x0021){m.Result=new IntPtr(3);return;} // MA_NOACTIVATE: mouse clicks work without taking game keyboard focus.
        base.WndProc(ref m);
    }
    public static Rectangle Placement(Rectangle game,Rectangle work)=>new(
        Math.Clamp(game.Left+(game.Width-Math.Min(1020,work.Width))/2,work.Left,Math.Max(work.Left,work.Right-Math.Min(1020,work.Width))),
        Math.Clamp(game.Top+(game.Height-Math.Min(670,work.Height))/2,work.Top,Math.Max(work.Top,work.Bottom-Math.Min(670,work.Height))),
        Math.Min(1020,work.Width),Math.Min(670,work.Height));
}
