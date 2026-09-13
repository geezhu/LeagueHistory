using League.Services;

namespace League.Controls;

public partial class PlayerCardControl
{
    private readonly Panel _championBar=new(){Height=42,Dock=DockStyle.Bottom,BackColor=Color.FromArgb(226,238,245)};
    private readonly PictureBox _currentHero=new(){Location=new Point(7,5),Size=new Size(32,32),SizeMode=PictureBoxSizeMode.Zoom};
    private readonly Label _heroName=new(){Location=new Point(45,3),Height=19,AutoEllipsis=true};
    private readonly Label _heroTier=new(){Location=new Point(45,22),Height=18,AutoSize=false,AutoEllipsis=true,ForeColor=Color.FromArgb(0,142,150)};
    private Func<int,Task<PlayerChampionTier>>? _tierLoader;
    private Action<int,PlayerChampionTier>? _openHero;
    private PlayerChampionTier? _currentTier;
    private int _heroGeneration,_iconHero;
    public void BindChampionInfo(Func<int,Task<PlayerChampionTier>> load,Action<int,PlayerChampionTier> open)
    {_tierLoader=load;_openHero=open;_=RefreshChampionAsync();}
    private void ConfigureChampionBar()
    {
        _championBar.Controls.AddRange(new Control[]{_currentHero,_heroName,_heroTier});SummaryPanel.Controls.Add(_championBar);
        _heroName.Text="尚未选择";
        var retry=new System.Windows.Forms.Timer(components!){Interval=10000};
        retry.Tick+=(_,_)=>{if(Visible && CurrentChampionId>0 && _tierLoader!=null && (_currentTier==null || _currentTier.ReferencePosition))_=RefreshChampionAsync();};
        retry.Start();
        _championBar.Resize+=(_,_)=>_heroTier.Width=_heroName.Width=Math.Max(30,_championBar.Width-52);
        foreach(Control control in _championBar.Controls)
        {
            control.Cursor=Cursors.Hand;
            control.Click+=(_,_)=>{if(CurrentChampionId>0 && _currentTier is {Queue:>0} tier && tier.Rate!="暂无梯队")_openHero?.Invoke(CurrentChampionId,tier);};
        }
        Disposed+=(_,_)=>{_heroGeneration++;_currentHero.Image?.Dispose();_currentHero.Image=null;};
    }
    private async Task RefreshChampionAsync()
    {
        if(IsDisposed)return;
        int id=CurrentChampionId,generation=++_heroGeneration;
        _currentTier=null;
        if(_iconHero!=id){_currentHero.Image?.Dispose();_currentHero.Image=null;_iconHero=0;}
        _heroName.Text=id>0?FormMain.Globals.resLoading?.GetChampionById(id)?.Name??$"英雄 {id}":"尚未选择";
        _heroTier.Text=id>0?"梯队读取中…":"";
        foreach(Control child in _championBar.Controls)_statisticsTips?.SetToolTip(child,"");
        if(id<=0)return;
        if(_iconHero!=id){_iconHero=id;_=LoadIcon();}
        if(_tierLoader==null){_heroTier.Text="梯队待加载";return;}
        try
        {
            var tier=await _tierLoader(id);
            if(IsDisposed || generation!=_heroGeneration)return;
            _currentTier=tier;_heroTier.Text=tier.Tier+" · "+tier.Rate;
            _heroTier.ForeColor=AramMetaView.TierColor(tier.Tier);
            foreach(Control c in _championBar.Controls)_statisticsTips?.SetToolTip(c,tier.Note);
        }
        catch {if(!IsDisposed && generation==_heroGeneration)_heroTier.Text="梯队暂不可用";}
        async Task LoadIcon()
        {
            try
            {
                var image=await FormMain.Globals.resLoading.GetChampionIconAsync(id);
                if(IsDisposed || CurrentChampionId!=id){image?.Dispose();return;}
                _currentHero.Image=image;
            }
            catch {if(CurrentChampionId==id)_iconHero=0;}
        }
    }
}
