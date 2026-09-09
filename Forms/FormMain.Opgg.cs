using League.Controls;

namespace League;

public partial class FormMain
{
    internal async void OpenAssistantGuide(int hero,int queue,string? position)
    {
        Show();WindowState=FormWindowState.Normal;Activate();
        if(queue==2400)
        {
            imageTabControl1.SelectedIndex=4;
            await imageTabControl1.TabPages[4].Controls.OfType<AramMetaView>().Single().OpenChampionAsync(hero);
        }
        else
        {
            imageTabControl1.SelectedIndex=3;
            await imageTabControl1.TabPages[3].Controls.OfType<OpggView>().Single().OpenChampionAsync(hero,queue,position);
        }
    }
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
        var aramIcon=new Bitmap(40,40);
        using(var graphics=Graphics.FromImage(aramIcon))
        using(var font=new Font("Segoe UI",12,FontStyle.Bold))
        {
            graphics.Clear(Color.FromArgb(0,145,151));
            TextRenderer.DrawText(graphics,"AM",font,new Rectangle(0,0,40,40),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
        var aramPage=new TabPage("arammeta · 海克斯大乱斗") {Tag=aramIcon};
        aramPage.Controls.Add(new AramMetaView(()=>TimeSpan.FromMinutes(Math.Clamp(_appConfig?.AramMetaCacheMinutes??1440,1,10080))));aramPage.Disposed+=(_,_)=>aramIcon.Dispose();
        imageTabControl1.TabPages.Add(aramPage);
    }
}
