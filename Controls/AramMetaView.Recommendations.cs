using League.Models;

namespace League.Controls;

internal sealed partial class AramMetaView
{
    private FlowLayoutPanel? _overviewEquipment;
    private string _augmentSearch="";
    private readonly Dictionary<string,int> _recommendationChoices=new();
    private static readonly string[] RarityKeys={"kPrismatic","kGold","kSilver"};
    private static readonly string[] RarityNames={"棱彩强化","黄金强化","白银强化"};
    private AramMetaRecommendation[] Recommendations(string category)=>AramMetaRecommendation.Read(_detailData!,_detailHero,category);
    private string RecommendationName(AramMetaRecommendation r,bool augment)=>augment?FormMain.Globals.resLoading.GetAugmentById(r.Ids[0])?.Name??r.Name:r.Name;
    private void RecommendationRow(Control parent,AramMetaRecommendation entry,bool augment=false,bool spell=false)
    {
        var card=new Panel{Size=new Size(430,54),Margin=new Padding(0,0,0,4),BackColor=Color.FromArgb(239,245,248)};parent.Controls.Add(card);
        var row=new FlowLayoutPanel{Location=new Point(5,6),Size=new Size(170,42),Margin=Padding.Empty};card.Controls.Add(row);
        foreach(int id in entry.Ids)
        {
            string description=augment?FormMain.Globals.resLoading.GetAugmentById(id)?.Description??entry.Description:"";
            string name=augment?RecommendationName(entry,true):spell?FormMain.Globals.resLoading.GetSpellById(id)?.Name??entry.Name:FormMain.Globals.resLoading.GetItemById(id)?.Name??entry.Name;
            Icon(row,null,TooltipText.Format(name,description),_detailToken,36,augment?"augment":spell?"spell":"item",id);
        }
        card.Controls.Add(new Label{Text=$"胜率 {entry.WinRate:P1}\n{entry.Games:N0} 场",Location=new Point(180,8),Size=new Size(120,40)});
        card.Controls.Add(new Label{Text=$"使用率 {entry.PickRate:P1}",Location=new Point(305,18),Size=new Size(120,22),ForeColor=Color.FromArgb(0,144,153)});
    }
    private void RenderOverview()
    {
        var host=_detailBody!;
        host.Controls.Add(Text("强化精选 · 按来源推荐顺序"));
        var board=Row(host);int available=Math.Max(510,host.Width-56);int columnWidth=available>=850?(available-24)/3:available;
        for(int i=0;i<3;i++)
        {
            var column=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Width=columnWidth,MinimumSize=new Size(columnWidth,0),MaximumSize=new Size(columnWidth,0),BackColor=Color.FromArgb(239,245,248),Margin=new Padding(0,0,8,8),Padding=new Padding(8)};board.Controls.Add(column);
            column.Controls.Add(Text(RarityNames[i]));
            foreach(var entry in Recommendations(RarityKeys[i]).Take(2))
            {
                var tile=new Panel{Size=new Size(columnWidth-16,62),Margin=new Padding(0,0,0,4)};column.Controls.Add(tile);
                var icons=new FlowLayoutPanel{Location=new Point(0,3),Size=new Size(42,42),Margin=Padding.Empty};tile.Controls.Add(icons);Icon(icons,null,RecommendationName(entry,true),_detailToken,36,"augment",entry.Ids[0]);
                var name=new Label{Text=RecommendationName(entry,true),Location=new Point(44,3),Size=new Size(columnWidth-64,22),AutoEllipsis=true};
                var metric=new Label{Text=$"调整后胜率 {entry.WinRate:P1}",Location=new Point(44,28),AutoSize=true,ForeColor=Color.FromArgb(0,144,153)};
                tile.Controls.AddRange(new Control[]{name,metric});_tips.SetToolTip(name,TooltipText.Format(name.Text,FormMain.Globals.resLoading.GetAugmentById(entry.Ids[0])?.Description??entry.Description)+$"\n选取率 {entry.PickRate:P1} · {entry.Games:N0} 场");
            }
        }
        _overviewEquipment=Row(host);_overviewEquipment.FlowDirection=FlowDirection.TopDown;_overviewEquipment.WrapContents=false;
        RenderOverviewEquipment();
    }
    private void RenderOverviewEquipment()
    {
        var host=_overviewEquipment!;foreach(Control child in host.Controls.Cast<Control>().ToArray())child.Dispose();
        if(_detailData!.Detail==null){host.Controls.Add(Text(_detailFailed?"出装推荐暂不可用，可刷新重试。":"正在加载出装与召唤师技能…"));return;}
        foreach(var (key,title) in new[]{("items","核心装备"),("boots","鞋子")})
        {
            host.Controls.Add(Text(title));
            var options=Recommendations(key);
            var chooser=new EquipmentComboBox(options.Select(r=>r.ToOption()).ToArray(),_detailToken){Width=430};host.Controls.Add(chooser);
            chooser.SelectedIndex=Math.Clamp(_recommendationChoices.GetValueOrDefault($"{_detailHero}:{key}"),0,chooser.Items.Count-1);
            chooser.SelectedIndexChanged+=(_,_)=>_recommendationChoices[$"{_detailHero}:{key}"]=chooser.SelectedIndex;
        }
        var more=new Button{Text="查看后续装备 ›",AutoSize=true,FlatStyle=FlatStyle.Flat};host.Controls.Add(more);more.Click+=(_,_)=>((Button)_detail.Controls[1].Controls.OfType<FlowLayoutPanel>().Single().Controls[2]).PerformClick();
        RenderSpellHighlights(host);
    }
    private void RenderSpellHighlights(Control host)
    {
        host.Controls.Add(Text("召唤师技能"));var columns=Row(host);
        foreach(var (title,entry) in AramMetaRecommendation.SpellHighlights(Recommendations("spells")))
        {
            var card=new FlowLayoutPanel{Width=230,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(230,0),MaximumSize=new Size(230,0),FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(8),Margin=new Padding(0,0,8,4),BackColor=Color.FromArgb(239,245,248)};columns.Controls.Add(card);
            card.Controls.Add(Text(title));
            if(entry==null){card.Controls.Add(Text("暂无数据"));continue;}
            var icons=Row(card);foreach(int id in entry.Ids)Icon(icons,null,TooltipText.Format(FormMain.Globals.resLoading.GetSpellById(id)?.Name??id.ToString(),FormMain.Globals.resLoading.GetSpellById(id)?.Description??""),_detailToken,32,"spell",id);
            card.Controls.Add(Text($"胜率 {entry.WinRate:P1} · 使用率 {entry.PickRate:P1}",Color.FromArgb(0,144,153)));
            _tips.SetToolTip(card,$"样本 {entry.Games:N0} 场");
        }
    }
    private void RenderAugmentComparison()
    {
        var host=_detailBody!;var filters=Row(host);
        for(int i=0;i<3;i++)
        {
            int rarity=i;var button=new Button{Text=RarityNames[i],Width=100,Height=30,FlatStyle=FlatStyle.Flat,BackColor=i==_rarity?Color.FromArgb(0,153,163):Color.White,ForeColor=i==_rarity?Color.White:Color.FromArgb(60,82,99)};
            filters.Controls.Add(button);button.Click+=(_,_)=>{_rarity=rarity;RenderDetailBody();};
        }
        var tools=Row(host);var search=new TextBox{Width=180,PlaceholderText="搜索强化",Text=_augmentSearch};
        var sort=new ComboBox{Width=150,DropDownStyle=ComboBoxStyle.DropDownList};sort.Items.AddRange(new object[]{"来源推荐顺序","调整后胜率","选取率"});sort.SelectedIndex=_augmentSort;tools.Controls.AddRange(new Control[]{search,sort});
        int width=Math.Max(510,host.Width-24);bool wide=width>=850;
        var split=Row(host);var list=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Width=wide?width-285:width,MaximumSize=new Size(wide?width-285:width,0)};
        list.Tag="comparison-list";
        var information=new Label{Tag="comparison-info",Text="点击强化查看完整介绍与统计依据",AutoSize=true,MaximumSize=new Size(wide?260:width,0),MinimumSize=new Size(wide?260:width,80),Padding=new Padding(12),BackColor=Color.FromArgb(226,241,244)};
        split.Controls.AddRange(new Control[]{list,information});
        void Populate()
        {
            list.SuspendLayout();
            try
            {
            foreach(Control child in list.Controls.Cast<Control>().ToArray())child.Dispose();
            AddStatsRow(list,null,"强化","调整后胜率","选取率","样本量",true);
            var entries=AramMetaRecommendation.Sort(Recommendations(RarityKeys[_rarity]),_augmentSort).Where(r=>RecommendationName(r,true).Contains(_augmentSearch,StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach(var entry in entries)
            {
                var icons=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};Icon(icons,null,RecommendationName(entry,true),_detailToken,36,"augment",entry.Ids[0]);
                var row=AddStatsRow(list,icons,RecommendationName(entry,true),$"{entry.WinRate:P1}",$"{entry.PickRate:P1}",entry.Games?.ToString("N0")??"—");
                void Select()
                {
                    foreach(Control sibling in list.Controls)sibling.BackColor=Color.FromArgb(247,250,252);row.BackColor=Color.FromArgb(218,241,243);
                    string desc=FormMain.Globals.resLoading.GetAugmentById(entry.Ids[0])?.Description??entry.Description;
                    information.Text=RecommendationName(entry,true)+"\n\n"+TooltipText.PlainText(desc)+$"\n\n调整后胜率 {entry.WinRate:P2}\n选取率 {entry.PickRate:P2}\n来源样本 {entry.Games:N0} 场\n\n来源推荐顺序不等于胜率排名，各项统计并不证明与某套出装存在联动。";
                }
                void Clickable(Control c){c.Cursor=Cursors.Hand;c.Click+=(_,_)=>Select();foreach(Control child in c.Controls)Clickable(child);}Clickable(row);
            }
            if(entries.Length==0)list.Controls.Add(Text("没有匹配的强化"));
            }
            finally {list.ResumeLayout(true);}
        }
        search.TextChanged+=(_,_)=>{_augmentSearch=search.Text;Populate();};sort.SelectedIndexChanged+=(_,_)=>{_augmentSort=sort.SelectedIndex;Populate();};Populate();
    }
    private void RenderBuildComparison()
    {
        var host=_detailBody!;
        if(_detailData!.Detail==null){host.Controls.Add(Text(_detailFailed?"详情加载失败，请刷新重试。":"正在加载出装详情…"));return;}
        var cores=Recommendations("items");var selection=Row(host);selection.Controls.Add(Text("装备组合"));
        var core=new EquipmentComboBox(cores.Select(r=>r.ToOption()).ToArray(),_detailToken){Width=430};selection.Controls.Add(core);
        core.SelectedIndex=Math.Clamp(_recommendationChoices.GetValueOrDefault($"{_detailHero}:items"),0,core.Items.Count-1);
        var later=Row(host);later.FlowDirection=FlowDirection.TopDown;later.WrapContents=false;
        void Update()
        {
            later.SuspendLayout();
            try
            {
            foreach(Control child in later.Controls.Cast<Control>().ToArray())child.Dispose();
            if(cores.Length==0){later.Controls.Add(Text("暂无装备组合"));return;}
            _recommendationChoices[$"{_detailHero}:items"]=core.SelectedIndex;
            var chosen=cores[core.SelectedIndex];
            later.Controls.Add(Text("单件备选 · 已排除所选组合中的装备（不是固定购买顺序）"));
            var excluded=chosen.Ids.ToHashSet();var seen=new HashSet<int>();
            foreach(var item in Recommendations("singleItems").Where(r=>r.Ids.All(id=>!excluded.Contains(id)) && seen.Add(r.Ids[0])))RecommendationRow(later,item);
            }
            finally {later.ResumeLayout(true);}
        }
        core.SelectedIndexChanged+=(_,_)=>Update();Update();
        foreach(var (key,title) in new[]{("boots","鞋子")})
        {
            host.Controls.Add(Text(title));var options=Recommendations(key);
            var chooser=new EquipmentComboBox(options.Select(r=>r.ToOption()).ToArray(),_detailToken,key=="spells"?"spell":"item"){Width=430};host.Controls.Add(chooser);
            chooser.SelectedIndex=Math.Clamp(_recommendationChoices.GetValueOrDefault($"{_detailHero}:{key}"),0,chooser.Items.Count-1);
            var statistics=new Label{AutoSize=true,Margin=new Padding(8)};host.Controls.Add(statistics);
            void Describe(){if(options.Length>0){_recommendationChoices[$"{_detailHero}:{key}"]=chooser.SelectedIndex;var choice=options[chooser.SelectedIndex];statistics.Text=$"胜率 {choice.WinRate:P1} · 选取率 {choice.PickRate:P1} · {choice.Games:N0} 场";}}
            chooser.SelectedIndexChanged+=(_,_)=>Describe();Describe();
        }
        host.Controls.SetChildIndex(later,host.Controls.Count-1);
        RenderSpellHighlights(host);
    }
    private void ResizeComparison(Control root,int width)
    {
        foreach(Control child in root.Controls)
        {
            if(child.Tag is string listTag && listTag=="comparison-list")
            {
                int listWidth=width>=850?width-285:width;child.MaximumSize=new Size(listWidth,0);child.Width=listWidth;
                foreach(var row in child.Controls.OfType<TableLayoutPanel>())row.Width=Math.Max(510,listWidth-6);
            }
            else if(child.Tag is string infoTag && infoTag=="comparison-info")
            {
                int infoWidth=width>=850?260:width;child.MinimumSize=new Size(infoWidth,80);child.MaximumSize=new Size(infoWidth,0);
            }
            ResizeComparison(child,width);
        }
    }
}
