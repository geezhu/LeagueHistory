using League.Controls;
using League.Infrastructure;
using Newtonsoft.Json.Linq;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    private readonly Dictionary<int,JObject> _skillCache=new();
    internal sealed record Skill(string Key,string Name,string Description,string Icon);
    internal static Skill[] ParseSkills(JObject champion)
    {
        var result=new List<Skill>();
        void Add(string key,JObject data)
        {
            result.Add(new Skill(key,(string?)data["name"]??key,
                TooltipText.PlainText((string?)data["description"]??"暂无技能说明"),
                (string?)data["abilityIconPath"]??(string?)data["iconPath"]??""));
        }
        if(champion["passive"] is JObject passive)Add("被动",passive);
        var spells=(champion["spells"] as JArray??new()).OfType<JObject>().ToArray();
        var keys=new[]{"Q","W","E","R"};
        for(int i=0;i<Math.Min(4,spells.Length);i++)
        {
            var key=((string?)spells[i]["spellKey"])?.ToUpperInvariant();
            Add(key!=null && keys.Contains(key)?key:keys[i],spells[i]);
        }
        result=result.OrderBy(skill=>Array.IndexOf(new[]{"被动","Q","W","E","R"},skill.Key)).ToList();
        return result.ToArray();
    }
    private async Task LoadSkillsAsync(int hero,CancellationToken token)
    {
        var section=new FlowLayoutPanel {AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,MaximumSize=new Size(282,0),Margin=new Padding(0,4,0,10)};
        var artwork=new ProfileBackgroundPanel {Size=new Size(282,138),Padding=new Padding(10),BackColor=Color.FromArgb(16,31,43),Margin=Padding.Empty};
        var title=new Label {Text=FormMain.Globals.resLoading?.GetChampionById(hero)?.Name??$"英雄 {hero}",Dock=DockStyle.Top,Height=28,ForeColor=Color.WhiteSmoke,BackColor=Color.Transparent};
        var titleFont=new Font(Font.FontFamily,12,FontStyle.Bold);title.Font=titleFont;title.Disposed+=(_,_)=>titleFont.Dispose();
        var icons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=44,Margin=Padding.Empty,BackColor=Color.Transparent};
        artwork.Controls.Add(icons);artwork.Controls.Add(title);
        var description=new Label {Text="正在加载技能…",Visible=false,AutoSize=true,MaximumSize=new Size(278,0),ForeColor=Color.FromArgb(187,206,214),Margin=new Padding(0,6,0,4)};
        section.Controls.AddRange(new Control[]{artwork,description});_body.Controls.Add(section);
        try
        {
            var client=FormMain.Globals.lcuClient?.Client;
            if(client==null)throw new InvalidOperationException("客户端未连接");
            if(!_skillCache.TryGetValue(hero,out var data))
            {
                data=JObject.Parse(await client.GetStringAsync($"/lol-game-data/assets/v1/champions/{hero}.json",token));
                token.ThrowIfCancellationRequested();
                if((int?)data["id"]!=hero)throw new InvalidDataException("技能资料与英雄不匹配");
                _skillCache[hero]=data;
            }
            var skills=ParseSkills(data);
            if(section.IsDisposed || token.IsCancellationRequested)return;
            description.Text=skills.Length==0?"暂无技能资料":skills[0].Name+"\n"+skills[0].Description;
            var cache=new ImageCacheService(client);
            _=LoadArtwork();
            async Task LoadArtwork()
            {
                try
                {
                    var path=ProfileBackgroundPanel.FindSplash(data,hero*1000);
                    if(path==null)return;
                    var image=await cache.GetOrDownloadImageAsync(path,"profile-backgrounds",$"{hero*1000}.png");
                    if(artwork.IsDisposed || token.IsCancellationRequested){image?.Dispose();return;}
                    artwork.SetChampionArtwork(image);
                }
                catch { /* The dark header remains usable when artwork is unavailable. */ }
            }
            foreach(var skill in skills)
            {
                var button=new Button {Text=skill.Key,Size=new Size(36,36),Margin=new Padding(2),FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.BottomCenter,ForeColor=Color.White,BackColor=Color.FromArgb(29,45,61),BackgroundImageLayout=ImageLayout.Zoom};
                icons.Controls.Add(button);
                button.Click+=(_,_)=>{var text=skill.Key+" · "+skill.Name+"\n"+skill.Description;description.Visible=!description.Visible || description.Text!=text;description.Text=text;};
                _tips.SetToolTip(button,TooltipText.Format(skill.Key+" · "+skill.Name,skill.Description));
                _=LoadIcon(button,skill);
            }
            async Task LoadIcon(Button button,Skill skill)
            {
                if(string.IsNullOrEmpty(skill.Icon) || !skill.Icon.StartsWith("/lol-game-data/assets/",StringComparison.OrdinalIgnoreCase))return;
                var image=await cache.GetOrDownloadImageAsync(skill.Icon,"skills",$"{hero}-{skill.Key}.png");
                if(button.IsDisposed || token.IsCancellationRequested){image?.Dispose();return;}
                button.BackgroundImage=image;button.Disposed+=(_,_)=>image?.Dispose();
            }
        }
        catch(OperationCanceledException){}
        catch(Exception){if(!section.IsDisposed && !token.IsCancellationRequested){description.Visible=true;description.Text="技能资料暂不可用，点击底部“重试”重新加载。";}}
    }
}
