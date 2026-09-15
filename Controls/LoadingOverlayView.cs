using League.Models;
using League.Services;

namespace League.Controls;

public sealed class LoadingOverlayView : UserControl
{
    private readonly Label _status=new(){Dock=DockStyle.Top,Height=32,Padding=new Padding(12,5,12,0)};
    private readonly TableLayoutPanel _teams=new(){Dock=DockStyle.Fill,ColumnCount=2,RowCount=6,Padding=new Padding(10,0,10,0)};
    private readonly Label _details=new(){Dock=DockStyle.Bottom,Height=104,Padding=new Padding(16,10,16,8),BackColor=Color.FromArgb(12,37,48)};
    private readonly List<(Panel Card,PictureBox Icon,Label Name,Label Champion,Label Rank,Label Rates)> _rows=new();
    private readonly Func<int,string> _champion;
    private readonly Func<LoadingPlayer,string> _playerDetails;
    private LoadingRoster? _roster;
    private IReadOnlyDictionary<string,PlayerChampionTier> _ranks=new Dictionary<string,PlayerChampionTier>();
    private string? _selected;
    public LoadingOverlayView(Func<int,string> champion,Func<LoadingPlayer,string> details)
    {
        _champion=champion;_playerDetails=details;Dock=DockStyle.Fill;DoubleBuffered=true;
        BackColor=Color.FromArgb(8,29,40);ForeColor=Color.FromArgb(211,229,233);Font=new Font("Microsoft YaHei UI",9);
        _teams.ColumnStyles.Add(new(SizeType.Percent,50));_teams.ColumnStyles.Add(new(SizeType.Percent,50));
        _teams.RowStyles.Add(new(SizeType.Absolute,30));for(int i=0;i<5;i++)_teams.RowStyles.Add(new(SizeType.Percent,20));
        for(int side=0;side<2;side++)
        {
            _teams.Controls.Add(new Label{Text=side==0?"我方":"对方",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=side==0?Color.Turquoise:Color.SandyBrown,Padding=new Padding(8,0,0,0)},side,0);
            for(int row=0;row<5;row++)
            {
                var panel=new Panel{Dock=DockStyle.Fill,Margin=new Padding(4),Padding=new Padding(8),BackColor=Color.FromArgb(17,46,58),Cursor=Cursors.Hand};
                var icon=new PictureBox{Dock=DockStyle.Left,Width=48,SizeMode=PictureBoxSizeMode.Zoom};
                var rank=new Label{Dock=DockStyle.Right,Width=42,TextAlign=ContentAlignment.MiddleCenter};
                var text=new Panel{Dock=DockStyle.Fill,Padding=new Padding(10,0,0,0)};
                var name=new Label{Dock=DockStyle.Top,Height=20,AutoEllipsis=true};
                var hero=new Label{Dock=DockStyle.Top,Height=20,AutoEllipsis=true,ForeColor=Color.FromArgb(232,199,121)};
                var rates=new Label{Dock=DockStyle.Fill,AutoEllipsis=true,ForeColor=Color.FromArgb(83,202,201)};
                text.Controls.Add(rates);text.Controls.Add(hero);text.Controls.Add(name);
                panel.Controls.Add(text);panel.Controls.Add(rank);panel.Controls.Add(icon);
                void Select(object? s,EventArgs e){if(panel.Tag is LoadingPlayer p){_selected=p.Key;ShowDetails(p);}}
                foreach(Control c in new Control[]{panel,icon,rank,text,name,hero,rates})c.Click+=Select;
                _teams.Controls.Add(panel,side,row+1);_rows.Add((panel,icon,name,hero,rank,rates));
            }
        }
        Controls.Add(_teams);Controls.Add(_details);Controls.Add(_status);
        _details.Text="点击玩家卡片，查看已有的段位、近期战绩与统计来源。";
    }
    public void UpdateRoster(LoadingRoster? roster,IReadOnlyDictionary<string,PlayerChampionTier> ranks,IReadOnlyDictionary<int,Image> icons,string status)
    {
        _roster=roster;_ranks=ranks;_status.Text=status;SuspendLayout();
        int own=roster?.Players.FirstOrDefault(p=>p.Self)?.Team??100;
        int[] teams=own==200?[200,100]:[100,200];
        for(int side=0;side<2;side++)
        {
            _teams.GetControlFromPosition(side,0)!.Text=roster?.TeamLabel(teams[side])??(side==0?"我方":"对方");
            var players=roster?.Players.Where(p=>p.Team==teams[side]).Take(5).ToArray()??[];
            for(int i=0;i<5;i++)
            {
                var ui=_rows[side*5+i];var p=players.ElementAtOrDefault(i);ui.Card.Tag=p;
                ui.Icon.Image=p!=null?icons.GetValueOrDefault(p.ChampionId):null;
                ui.Name.Text=p==null?"等待玩家数据":string.IsNullOrWhiteSpace(p.Name)?"玩家名称尚未提供":p.Name+(p.Self?" · 自己":"");
                ui.Champion.Text=p?.ChampionId>0?_champion(p.ChampionId):"英雄待确认";
                var rank=p!=null?ranks.GetValueOrDefault(p.Key):null;
                ui.Rank.Text=rank?.Tier??"—";ui.Rank.ForeColor=AramMetaView.TierColor(rank?.Tier??"—");
                ui.Rates.Text=rank==null?"统计读取中…":rank.Rate+" · 选 "+rank.PickRate;
                if(p==null)ui.Rates.Text="";
            }
        }
        var selected=roster?.Players.FirstOrDefault(p=>p.Key==_selected);
        if(selected!=null)ShowDetails(selected);
        else
        {
            _selected=null;
            string source=ranks.Values.FirstOrDefault()?.Note.Replace("\n"," · ")??"英雄统计尚未读取。";
            _details.Text="点击玩家卡片，查看已有的段位与近期战绩。胜率和选用率属于英雄模式统计。\n"+source;
        }
        ResumeLayout();
    }
    private void ShowDetails(LoadingPlayer p)
    {
        string source=_ranks.GetValueOrDefault(p.Key)?.Note??"英雄统计尚未读取。";
        _details.Text=(string.IsNullOrWhiteSpace(p.Name)?_champion(p.ChampionId):p.Name)+" · "+_champion(p.ChampionId)+"\n"+_playerDetails(p)+"\n"+source.Replace("\n"," · ");
    }
}
