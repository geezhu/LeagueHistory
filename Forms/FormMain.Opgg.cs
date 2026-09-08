using League.Controls;

namespace League;

public partial class FormMain
{
    private void ConfigureOpgg()
    {
        var icon=new Bitmap(40,40);
        using(var graphics=Graphics.FromImage(icon))
        using(var font=new Font("Segoe UI",10,FontStyle.Bold))
        {
            graphics.Clear(Color.FromArgb(48,118,218));
            TextRenderer.DrawText(graphics,"OP\nGG",font,new Rectangle(0,0,40,40),Color.White,TextFormatFlags.HorizontalCenter);
        }
        var page=new TabPage("OP.GG 英雄攻略") { Tag=icon };
        page.Controls.Add(new OpggView());
        page.Disposed+=(_,_)=>icon.Dispose();
        imageTabControl1.TabPages.Add(page);
    }
}
