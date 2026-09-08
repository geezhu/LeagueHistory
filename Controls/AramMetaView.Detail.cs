using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed partial class AramMetaView
{
    private readonly HttpClient _imageHttp;
    private readonly SemaphoreSlim _imageGate=new(4,4);
    private readonly Dictionary<string,Image> _images=new();
    private readonly Dictionary<string,Task<Image?>> _imageLoads=new();
    private Task<Image?> ImageAsync(string? url,string kind="",int id=0)
    {
        if(string.IsNullOrWhiteSpace(url))return Task.FromResult<Image?>(null);
        if(!Uri.TryCreate(new Uri("https://arammeta.com/"),url,out var uri) || uri.Scheme!="https" || !new[]{"arammeta.com","ddragon.leagueoflegends.com"}.Contains(uri.Host))return Task.FromResult<Image?>(null);
        string key=uri.AbsoluteUri;
        if(_images.TryGetValue(key,out var cached))return Task.FromResult<Image?>(cached);
        if(_imageLoads.TryGetValue(key,out var pending))return pending;
        return _imageLoads[key]=FetchImageAsync(key,kind,id);
    }
    private async Task<Image?> FetchImageAsync(string url,string kind,int id)
    {
        await _imageGate.WaitAsync();
        try
        {
            if(IsDisposed)return null;
            var resources=FormMain.Globals.resLoading;
            Image? image=null;
            if(resources!=null)
            {
                try
                {
                    image=kind switch
                    {
                        "champion" => (await resources.GetChampionInfoAsync(id)).image,
                        "item" => (await resources.GetItemInfoAsync(id)).image,
                        "spell" => (await resources.GetSpellInfoAsync(id)).image,
                        "augment" => (await resources.GetAugmentInfoAsync(id)).image,
                        _ => null
                    };
                }
                catch { /* Missing client resources fall back to the public image cache. */ }
            }
            if(IsDisposed){image?.Dispose();return null;}
            if(image==null)
            {
                var filename=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))+".png";
                image=await new League.Infrastructure.ImageCacheService(_imageHttp).GetOrDownloadImageAsync(url,"arammeta",filename);
            }
            if(IsDisposed){image?.Dispose();return null;}
            if(image!=null)_images[url]=image;
            return image;
        }
        catch {return null;}
        finally {_imageGate.Release();}
    }
    private async Task SetGridImageAsync(DataGridViewRow row,AramMetaChampion champion,CancellationToken token)
    {
        try
        {
            var image=await ImageAsync((string?)champion.Data["image"],"champion",champion.Id).WaitAsync(token);
            if(image!=null && !IsDisposed && !token.IsCancellationRequested && row.DataGridView==_grid)row.Cells[0].Value=image;
        }
        catch(OperationCanceledException){}
    }
    private PictureBox Icon(Control parent,string? url,string tooltip,CancellationToken token,int size=32,string kind="",int id=0)
    {
        var picture=new PictureBox {Size=new Size(size,size),SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(2),BackColor=Color.FromArgb(233,239,245)};
        parent.Controls.Add(picture);_tips.SetToolTip(picture,tooltip);
        _=Load();return picture;
        async Task Load()
        {
            try {var image=await ImageAsync(url,kind,id).WaitAsync(token);if(!picture.IsDisposed && !token.IsCancellationRequested)picture.Image=image;}
            catch(OperationCanceledException){}
        }
    }
    private FlowLayoutPanel Section(string title)
    {
        var panel=new FlowLayoutPanel {AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Color.White,Padding=new Padding(12),Margin=new Padding(0,0,0,10)};
        panel.Controls.Add(new Label {Text=title,AutoSize=true,Font=new Font(Font,FontStyle.Bold),Margin=new Padding(0,0,0,8)});
        _detail.Controls.Add(panel);ResizeSections();return panel;
    }
    private void ResizeSections()
    {
        int width=Math.Max(360,_detail.ClientSize.Width-32);
        foreach(Control section in _detail.Controls)
        {
            section.MinimumSize=new Size(width,0);section.MaximumSize=new Size(width,0);section.Width=width;
            foreach(Control child in section.Controls)if(child is FlowLayoutPanel || child is Label)child.MaximumSize=new Size(width-24,0);
        }
    }
    private FlowLayoutPanel Row(Control parent)=>AddRow(parent);
    private static FlowLayoutPanel AddRow(Control parent)
    {
        var row=new FlowLayoutPanel {AutoSize=true,WrapContents=true,Margin=new Padding(0,2,0,2),MaximumSize=new Size(Math.Max(330,parent.Width-24),0)};parent.Controls.Add(row);return row;
    }
    private Label Text(string text,Color? color=null)=>new(){Text=text,AutoSize=true,ForeColor=color??Color.FromArgb(43,65,83),Margin=new Padding(6,6,6,4)};
    private void MarkDetailUnavailable()
    {
        foreach(Control section in _detail.Controls)
            if(Equals(section.Tag,"equipment"))
                foreach(var label in section.Controls.OfType<Label>())
                    if(label.Text=="正在加载详情…")label.Text="详情加载失败，请点击刷新重试。";
    }
    private void RenderDetail(AramMetaPage page,int id,CancellationToken token,bool equipmentOnly=false)
    {
        _detail.SuspendLayout();
        try
        {
            foreach(Control old in _detail.Controls.Cast<Control>().Where(c=>!equipmentOnly || Equals(c.Tag,"equipment")).ToArray())old.Dispose();
            var champion=page.Index.Champions.FirstOrDefault(c=>c.Id==id);
            if(champion==null){Section("暂无英雄数据");return;}
            if(!equipmentOnly)
            {
            var summary=Section(Name(champion));summary.BackColor=Color.FromArgb(226,242,243);
            var header=Row(summary);Icon(header,(string?)champion.Data["image"],Name(champion),token,56,"champion",id);
            header.Controls.Add(Text($"{champion.Tier}   调整后胜率 {Rate(champion.WinRate)}   原始胜率 {Rate(champion.RawWinRate)}\n样本量 {champion.Games:N0} · 海克斯大乱斗 · 版本 {page.Index.Patch}"));
            var help=Text("ⓘ");_tips.SetToolTip(help,"调整后胜率使用 arammeta 的统计模型；原始胜率是样本获胜比例。梯队按调整后胜率划分：OP≥55%、T1≥52%、T2≥50%、T3≥48%、T4≥46%。");header.Controls.Add(help);
            foreach(var (key,title) in new[]{("kPrismatic","棱彩强化"),("kGold","金色强化"),("kSilver","银色强化")})
            {
                var section=Section(title+" · 来源推荐顺序");var cards=Row(section);
                var recommendations=(champion.Data["top"]?[key] as JArray??new()).OfType<JObject>().ToArray();
                if(recommendations.Length==0)section.Controls.Add(Text("暂无推荐数据"));
                foreach(var recommendation in recommendations)
                {
                    int augmentId=(int?)recommendation["id"]??0;var meta=page.Index.Augments[augmentId.ToString()] as JObject??new();
                    var local=FormMain.Globals.resLoading?.GetAugmentById(augmentId);
                    string name=local?.Name??(string?)meta["name_zh"]??(string?)meta["name_en"]??$"强化 {augmentId}";
                    string desc=!string.IsNullOrWhiteSpace(local?.Description)?local.Description:(string?)meta["desc_zh"]??(string?)meta["desc_en"]??"暂无说明";
                    var card=new Panel {Size=new Size(330,100),Margin=new Padding(0,0,8,8),BackColor=Color.FromArgb(246,248,251)};cards.Controls.Add(card);
                    // Transparent augment artwork has pale highlights intended for a dark surface.
                    var iconHost=new FlowLayoutPanel {Bounds=new Rectangle(8,10,56,56),Padding=new Padding(2),BackColor=Color.FromArgb(25,31,43)};card.Controls.Add(iconHost);
                    var augmentIcon=Icon(iconHost,(string?)meta["icon"],TooltipText.Format(name,desc),token,48,"augment",augmentId);
                    augmentIcon.BackColor=iconHost.BackColor;
                    var label=new Label {Text=name,Bounds=new Rectangle(74,7,248,23),AutoEllipsis=true};card.Controls.Add(label);_tips.SetToolTip(label,TooltipText.Format(name,desc));
                    var stats=new Label {Text=$"调整后胜率 {Rate(recommendation["wr"])}   样本 {(long?)recommendation["g"]:N0}\n选取率 {Rate(recommendation["pick"])}   推荐分 {(double?)recommendation["score"]:0.000}",Bounds=new Rectangle(74,34,250,54),ForeColor=Color.FromArgb(0,133,146)};card.Controls.Add(stats);
                    _tips.SetToolTip(stats,"各指标由 arammeta 提供；推荐分不是胜率。选取率及推荐分的分母、模型以来源项目为准。低样本结果波动较大。");
                }
            }
            }
            foreach(var (key,title,spell) in new[]{("items","装备组合",false),("singleItems","单件装备",false),("boots","鞋子",false),("spells","召唤师技能",true)})
            {
                var section=Section(title+" · 来源推荐顺序");
                section.Tag="equipment";
                var options=(page.Detail?[key]?["top"] as JArray??new()).OfType<JObject>().ToArray();
                if(page.Detail==null)section.Controls.Add(Text("正在加载详情…"));
                else if(options.Length==0)section.Controls.Add(Text("暂无数据"));
                foreach(var option in options)
                {
                    var row=Row(section);
                    foreach(var item in (option["items"] as JArray??new()).OfType<JObject>())
                    {
                        int itemId=(int?)item["id"]??0;
                        string name=spell?(FormMain.Globals.resLoading?.GetSpellById(itemId)?.Name??(string?)item["name_en"]??itemId.ToString()):
                            (FormMain.Globals.resLoading?.GetItemById(itemId)?.Name??(string?)page.Index.Raw["itemLut"]?[itemId.ToString()]?["name_en"]??itemId.ToString());
                        string? url=spell?(string?)item["icon"]:$"https://ddragon.leagueoflegends.com/cdn/{page.Index.Raw["ddv"]}/img/item/{itemId}.png";
                        Icon(row,url,name,token,32,spell?"spell":"item",itemId);
                    }
                    row.Controls.Add(Text($"调整后胜率 {Rate(option["wr"])}\n{(long?)option["g"]:N0} 场"));
                    row.Controls.Add(Text($"选取率 {Rate(option["pick"])}",Color.FromArgb(0,145,155)));
                }
            }
            ResizeSections();
        }
        finally {_detail.ResumeLayout(true);}
    }
}
