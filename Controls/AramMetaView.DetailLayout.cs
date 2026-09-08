using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed partial class AramMetaView
{
    private AramMetaPage? _detailData;
    private FlowLayoutPanel? _detailBody;
    private int _detailHero,_detailTab,_rarity,_augmentSort;
    private bool _allAugments,_detailFailed;
    private CancellationToken _detailToken;

    private void MarkDetailUnavailable()
    {
        _detailFailed=true;
        if(_detailTab!=0)RenderDetailBody();
    }

    private void RenderDetail(AramMetaPage page,int id,CancellationToken token,bool equipmentOnly=false)
    {
        _detailData=page;_detailToken=token;_detailFailed=false;
        if(equipmentOnly && _detailHero==id && _detailBody is {IsDisposed:false})
        {
            if(_detailTab!=0)RenderDetailBody();
            return;
        }
        if(_detailHero!=id){_detailTab=0;_rarity=0;_augmentSort=0;_allAugments=false;}
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
            Icon(header,(string?)champion.Data["image"],Name(champion),token,64,"champion",id);
            var tierInk=TierColor(champion.Tier);
            header.Controls.Add(new Label {Text=champion.Tier,Size=new Size(44,30),TextAlign=ContentAlignment.MiddleCenter,ForeColor=tierInk,BackColor=Color.FromArgb(24,tierInk),Margin=new Padding(4,17,8,0)});
            foreach(var (label,value) in new[]{("调整后胜率",Rate(champion.WinRate)),("原始胜率",Rate(champion.RawWinRate)),("样本选取率",Rate(page.Index.PickRate(champion))),("样本量",champion.Games?.ToString("N0")??"—")})
            {
                var metric=new Label {Text=value+"\n"+label,AutoSize=false,Size=new Size(116,58),TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.FromArgb(0,130,141),Margin=new Padding(4)};
                header.Controls.Add(metric);
            }
            _tips.SetToolTip(summary,"调整后胜率来自 arammeta 的统计模型；样本选取率按完整 5v5 样本计算，仅代表来源样本。");
            var navigation=Section("海克斯大乱斗 · 版本 "+page.Index.Patch);
            var tabs=Row(navigation);
            var names=new[]{"强化推荐","装备推荐","召唤师技能"};
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
            if(_detailTab==0)RenderAugments();else RenderEquipment();
            ResizeSections();
        }
        finally {_detailBody.ResumeLayout(true);}
    }

    private void RenderAugments()
    {
        var page=_detailData!;var host=_detailBody!;
        var filters=Row(host);
        var rarity=new ComboBox {Width=112,DropDownStyle=ComboBoxStyle.DropDownList};
        rarity.Items.AddRange(new object[]{"棱彩强化","黄金强化","白银强化"});rarity.SelectedIndex=_rarity;
        var sort=new ComboBox {Width=144,DropDownStyle=ComboBoxStyle.DropDownList};
        sort.Items.AddRange(new object[]{"来源推荐顺序","胜率从高到低","选取率从高到低"});sort.SelectedIndex=_augmentSort;
        filters.Controls.AddRange(new Control[]{rarity,sort});
        rarity.SelectedIndexChanged+=(_,_)=>{_rarity=rarity.SelectedIndex;_allAugments=false;RenderDetailBody();};
        sort.SelectedIndexChanged+=(_,_)=>{_augmentSort=sort.SelectedIndex;RenderDetailBody();};
        var champion=page.Index.Champions.First(c=>c.Id==_detailHero);
        var entries=(champion.Data["top"]?[new[]{"kPrismatic","kGold","kSilver"}[_rarity]] as JArray??new()).OfType<JObject>();
        if(_augmentSort!=0)entries=entries.OrderByDescending(e=>AramMetaIndex.Rate(e[_augmentSort==1?"wr":"pick"])??-1);
        var all=entries.ToArray();
        AddStatsRow(host,null,"强化","调整后胜率","选取率","样本量",true);
        foreach(var entry in all.Take(_allAugments?all.Length:10))
        {
            int id=(int?)entry["id"]??0;
            var meta=page.Index.Augments[id.ToString()] as JObject??new();
            var local=FormMain.Globals.resLoading?.GetAugmentById(id);
            string name=local?.Name??(string?)meta["name_zh"]??(string?)meta["name_en"]??$"强化 {id}";
            string desc=local?.Description??(string?)meta["desc_zh"]??(string?)meta["desc_en"]??"暂无说明";
            var icons=new FlowLayoutPanel {Dock=DockStyle.Fill,Margin=Padding.Empty};
            var picture=Icon(icons,(string?)meta["icon"],TooltipText.Format(name,desc),_detailToken,40,"augment",id);
            picture.BackColor=Color.FromArgb(25,31,43);
            var row=AddStatsRow(host,icons,name,Rate(entry["wr"]),Rate(entry["pick"]),((long?)entry["g"])?.ToString("N0")??"—");
            _tips.SetToolTip(row.Controls[1],TooltipText.Format(name,desc+$"\n来源推荐分：{(double?)entry["score"]:0.000}（不是胜率）"));
        }
        if(all.Length==0)host.Controls.Add(Text("暂无推荐数据"));
        if(all.Length>10)
        {
            var expand=new Button {Text=_allAugments?"收起":"展开全部（"+all.Length+"）",AutoSize=true,Height=32};
            expand.Click+=(_,_)=>{_allAugments=!_allAugments;RenderDetailBody();};host.Controls.Add(expand);
        }
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

    private void RenderEquipment()
    {
        var page=_detailData!;var host=_detailBody!;
        if(page.Detail==null){host.Controls.Add(Text(_detailFailed?"详情加载失败，请点击刷新重试。":"正在加载详情…"));return;}
        var groups=_detailTab==2?new[]{("spells","召唤师技能")}:new[]{("items","装备组合"),("singleItems","单件装备"),("boots","鞋子")};
        foreach(var (key,title) in groups)
        {
            host.Controls.Add(Text(title+" · 来源推荐顺序"));
            var entries=(page.Detail[key]?["top"] as JArray??new()).OfType<JObject>().ToArray();
            if(entries.Length==0)host.Controls.Add(Text("暂无数据"));
            foreach(var entry in entries)
            {
                var row=Row(host);row.BackColor=Color.FromArgb(247,250,252);
                foreach(var item in (entry["items"] as JArray??new()).OfType<JObject>())
                {
                    int id=(int?)item["id"]??0;bool spell=key=="spells";
                    var name=spell?FormMain.Globals.resLoading?.GetSpellById(id)?.Name:FormMain.Globals.resLoading?.GetItemById(id)?.Name;
                    var url=spell?(string?)item["icon"]:$"https://ddragon.leagueoflegends.com/cdn/{page.Index.Raw["ddv"]}/img/item/{id}.png";
                    Icon(row,url,name??(string?)item["name_en"]??id.ToString(),_detailToken,36,spell?"spell":"item",id);
                }
                row.Controls.Add(Text($"胜率 {Rate(entry["wr"])}    选取率 {Rate(entry["pick"])}    {(long?)entry["g"]:N0} 场"));
            }
        }
    }
}
