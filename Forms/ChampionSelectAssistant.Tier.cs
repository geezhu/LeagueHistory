namespace League;

internal sealed partial class ChampionSelectAssistant
{
    private void AddTierSummary(string tier,string statistics)
    {
        var row=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,MaximumSize=new Size(282,0),Margin=new Padding(0,2,0,2)};
        var color=League.Controls.AramMetaView.TierColor(tier);
        var badge=new Label{Text=tier,Size=new Size(42,24),TextAlign=ContentAlignment.MiddleCenter,BackColor=Color.FromArgb(24,color),ForeColor=color,Margin=new Padding(0,0,7,0),AccessibleName="英雄梯队 "+tier};
        var bold=new Font(Font,FontStyle.Bold);badge.Font=bold;badge.Disposed+=(_,_)=>bold.Dispose();
        _tips.SetToolTip(badge,"当前统计条件下的英雄梯队："+tier);
        row.Controls.Add(badge);
        row.Controls.Add(new Label{Text=statistics,AutoSize=true,MaximumSize=new Size(230,0),ForeColor=Color.FromArgb(208,185,130),Margin=new Padding(0,3,0,0)});
        _body.Controls.Add(row);
    }
}
