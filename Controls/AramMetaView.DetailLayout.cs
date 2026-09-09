using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed partial class AramMetaView
{
    private AramMetaPage? _detailData;
    private FlowLayoutPanel? _detailBody;
    private int _detailHero,_detailTab,_rarity,_augmentSort;
    private bool _detailFailed;
    private CancellationToken _detailToken;

    private void MarkDetailUnavailable()
    {
        _detailFailed=true;
        if(_detailTab!=1)RenderDetailBody();
    }

    private void RenderDetail(AramMetaPage page,int id,CancellationToken token,bool equipmentOnly=false)
    {
        _detailData=page;_detailToken=token;_detailFailed=false;
        if(equipmentOnly && _detailHero==id && _detailBody is {IsDisposed:false})
        {
            if(_detailTab==0 && _overviewEquipment is {IsDisposed:false})RenderOverviewEquipment();
            else if(_detailTab==2)RenderDetailBody();
            return;
        }
        if(_detailHero!=id){_detailTab=0;_rarity=0;_augmentSort=0;_augmentSearch="";}
        _detailHero=id;
        _detail.SuspendLayout();
        try
        {
            foreach(Control control in _detail.Controls.Cast<Control>().ToArray())control.Dispose();
            var champion=page.Index.Champions.FirstOrDefault(c=>c.Id==id);
            if(champion==null){Section("暂无英雄数据");return;}
            var summary=Section(Name(champion));
            summary.BackColor=Color.FromArgb(226,242,243);
            summary.Controls[0].Font=new Font(Font.FontFamily,14,FontStyle.Bold);
            var header=Row(summary);
            Icon(header,(string?)champion.Data["image"],Name(champion),token,48,"champion",id);
            var tierInk=TierColor(champion.Tier);
            header.Controls.Add(new Label {Text=champion.Tier,Size=new Size(44,30),TextAlign=ContentAlignment.MiddleCenter,ForeColor=tierInk,BackColor=Color.FromArgb(24,tierInk),Margin=new Padding(4,17,8,0)});
            foreach(var (label,value) in new[]{("调整后胜率",Rate(champion.WinRate)),("原始胜率",Rate(champion.RawWinRate)),("样本选取率",Rate(page.Index.PickRate(champion))),("样本量",champion.Games?.ToString("N0")??"—")})
            {
                var metric=new Label {Text=value+"\n"+label,AutoSize=false,Size=new Size(116,46),TextAlign=ContentAlignment.MiddleCenter,ForeColor=label=="调整后胜率"?Color.FromArgb(0,130,141):Color.FromArgb(115,133,145),Margin=new Padding(4)};
                header.Controls.Add(metric);
            }
            _tips.SetToolTip(summary,"调整后胜率来自 arammeta 的统计模型；样本选取率按完整 5v5 样本计算，仅代表来源样本。");
            var navigation=Section("");navigation.Controls[0].Dispose();
            var tabs=Row(navigation);
            var names=new[]{"概览","强化比较","出装与技能"};
            for(int i=0;i<names.Length;i++)
            {
                int selected=i;
                var button=new Button {Text=names[i],Width=112,Height=32,FlatStyle=FlatStyle.Flat,Margin=new Padding(0,0,8,0)};
                tabs.Controls.Add(button);
                button.Click+=(_,_)=>{_detailTab=selected;UpdateTabs();RenderDetailBody();};
            }
            void UpdateTabs()
            {
                for(int i=0;i<tabs.Controls.Count;i++)
                {
                    tabs.Controls[i].BackColor=i==_detailTab?Color.FromArgb(0,153,163):Color.White;
                    tabs.Controls[i].ForeColor=i==_detailTab?Color.White:Color.FromArgb(60,82,99);
                }
            }
            UpdateTabs();
            _detailBody=Section("");_detailBody.Controls[0].Dispose();
            RenderDetailBody();ResizeSections();
        }
        finally {_detail.ResumeLayout(true);}
    }

    private void RenderDetailBody()
    {
        if(_detailBody==null || _detailBody.IsDisposed || _detailData==null)return;
        _detailBody.SuspendLayout();
        try
        {
            foreach(Control control in _detailBody.Controls.Cast<Control>().ToArray())control.Dispose();
            if(_detailTab==0)RenderOverview();else if(_detailTab==1)RenderAugmentComparison();else RenderBuildComparison();
            ResizeSections();
        }
        finally {_detailBody.ResumeLayout(true);}
    }

    private TableLayoutPanel AddStatsRow(Control host,Control? icon,string name,string win,string pick,string games,bool heading=false)
    {
        var row=new TableLayoutPanel {Height=heading?34:54,Width=Math.Max(510,host.Width-24),MinimumSize=new Size(510,0),ColumnCount=5,RowCount=1,Margin=new Padding(0,0,0,3),BackColor=heading?Color.FromArgb(235,242,247):Color.FromArgb(247,250,252)};
        row.RowStyles.Add(new RowStyle(SizeType.Absolute,heading?34:54));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,48));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<3;i++)row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,94));
        row.Controls.Add(icon??new Panel(),0,0);
        int column=1;
        foreach(var text in new[]{name,win,pick,games})row.Controls.Add(new Label {Text=text,Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=column==1?ContentAlignment.MiddleLeft:ContentAlignment.MiddleCenter,ForeColor=column==2&&!heading?Color.FromArgb(0,145,153):Color.FromArgb(55,76,94)},column++,0);
        host.Controls.Add(row);return row;
    }

}
