using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed partial class OpggView
{
    private async Task RenderMatchupsAsync(OpggBuild build,CancellationToken token)
    {
        var section=Section("对局分析");
        var opponents=(build.Data["counters"] as JArray??new()).OfType<JObject>()
            .Where(c=>(int?)c["champion_id"]>0 && (long?)c["play"]>0 && OpggBuild.Ratio(c["win"],c["play"]) is >=0 and <=1).ToArray();
        if(opponents.Length==0){section.Controls.Add(TextLabel("暂无对局统计"));return;}
        var layout=Inline(section);
        var groups=new[]{
            (Title:"优势对局 · 胜率 ≥ 50%",Rows:opponents.Where(c=>OpggBuild.Ratio(c["win"],c["play"])>=0.5).OrderByDescending(c=>OpggBuild.Ratio(c["win"],c["play"])).ToArray(),Color:Color.FromArgb(0,143,104)),
            (Title:"劣势对局 · 胜率 < 50%",Rows:opponents.Where(c=>OpggBuild.Ratio(c["win"],c["play"])<0.5).OrderBy(c=>OpggBuild.Ratio(c["win"],c["play"])).ToArray(),Color:Color.FromArgb(210,77,91))};
        foreach(var group in groups)
        {
            var column=new FlowLayoutPanel {AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new Padding(0,0,12,0)};
            layout.Controls.Add(column);
            column.Controls.Add(new Label {Text=$"{group.Title}（{group.Rows.Length}）",AutoSize=true,ForeColor=Color.SlateGray,Margin=new Padding(2,0,0,6)});
            if(group.Rows.Length==0)column.Controls.Add(TextLabel("暂无符合条件的对局"));
            foreach(var opponent in group.Rows)
            {
                token.ThrowIfCancellationRequested();
                int id=(int)opponent["champion_id"]!;
                var row=new Panel {Size=new Size(320,38),Margin=Padding.Empty,Tag=id};
                column.Controls.Add(row);
                var iconHost=new FlowLayoutPanel {Bounds=new Rectangle(0,1,36,36),Margin=Padding.Empty,WrapContents=false};
                row.Controls.Add(iconHost);
                var name=new Label {Text=ChampionName(id),Bounds=new Rectangle(40,0,115,38),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};
                var count=new Label {Text=$"{(long)opponent["play"]!:N0} 场",Bounds=new Rectangle(155,0,95,38),TextAlign=ContentAlignment.MiddleRight,ForeColor=Color.Gray};
                var rate=new Label {Text=$"{OpggBuild.Ratio(opponent["win"],opponent["play"]):P2}",Bounds=new Rectangle(253,0,65,38),TextAlign=ContentAlignment.MiddleRight,ForeColor=group.Color,Font=new Font(Font,FontStyle.Bold)};
                row.Controls.AddRange(new Control[]{name,count,rate});
                _tips.SetToolTip(name,ChampionName(id));
                _tips.SetToolTip(rate,"当前攻略英雄对阵该英雄的胜率");
                await AddIconAsync(iconHost,id,"champion",token);
            }
        }
    }

    private Control RecommendationStats(JToken data)
    {
        var panel=new Panel {Size=new Size(176,44),Margin=Padding.Empty};
        var win=new Label {Text=Percent(OpggBuild.Ratio(data["win"],data["play"])),Bounds=new Rectangle(0,2,98,20),TextAlign=ContentAlignment.MiddleCenter};
        var count=new Label {Text=(data["play"] is {} plays?$"{(long)plays:N0} 场":"— 场"),Bounds=new Rectangle(0,22,98,20),TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.Gray};
        var pick=new Label {Text=Percent((double?)data["pick_rate"]),Bounds=new Rectangle(100,10,76,24),TextAlign=ContentAlignment.MiddleRight,ForeColor=Color.FromArgb(0,158,171),Font=new Font(Font,FontStyle.Bold)};
        panel.Controls.AddRange(new Control[]{win,count,pick});
        _tips.SetToolTip(win,"胜率");_tips.SetToolTip(count,"统计场次");_tips.SetToolTip(pick,"选用率");
        return panel;
    }

    private void RenderSkills(OpggBuild build)
    {
        var section=Section("技能加点");
        var layout=Inline(section);
        var left=new FlowLayoutPanel {AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};
        layout.Controls.Add(left);
        var mastery=build.Data["skill_masteries"]?.First;
        var skill=build.Data["skills"]?.First;
        void Chips(JToken? sequence,bool priority)
        {
            var values=(sequence as JArray)?.Values<string>().ToArray()??Array.Empty<string>();
            var row=new FlowLayoutPanel {AutoSize=true,WrapContents=false,Margin=Padding.Empty};left.Controls.Add(row);
            if(values.Length==0){row.Controls.Add(TextLabel("暂无数据"));return;}
            for(int i=0;i<values.Length;i++)
            {
                var value=values[i];int size=priority?32:22;
                var color=value switch {"Q"=>Color.FromArgb(29,146,220),"W"=>Color.FromArgb(0,167,154),"E"=>Color.FromArgb(225,147,44),_=>Color.FromArgb(91,105,206)};
                var chip=new Label {Text=value,TextAlign=ContentAlignment.MiddleCenter,Size=new Size(size,size),Margin=new Padding(1,2,1,2),
                    ForeColor=value=="R"?Color.White:color,BackColor=value=="R"?color:Color.FromArgb(239,246,250)};
                chip.Paint+=(_,e)=>{using var pen=new Pen(color);e.Graphics.DrawRectangle(pen,0,0,chip.Width-1,chip.Height-1);};
                _tips.SetToolTip(chip,priority?"技能升级优先级":$"第 {i+1} 级：{value}");row.Controls.Add(chip);
                if(priority && i<values.Length-1)row.Controls.Add(new Label {Text="›",Size=new Size(24,32),TextAlign=ContentAlignment.MiddleCenter,ForeColor=Color.Gray,Margin=new Padding(2)});
            }
        }
        Chips(mastery?["ids"],true);Chips(skill?["order"],false);
        if(skill!=null){var stats=RecommendationStats(skill);stats.Margin=new Padding(12,8,0,0);layout.Controls.Add(stats);}
    }

    private async Task RenderStartingEquipmentAsync(OpggBuild build,CancellationToken token)
    {
        var section=Section("出门装与鞋子");
        var layout=Inline(section);
        foreach(var (key,title) in new[]{("starter_items","出门装"),("boots","鞋子")})
        {
            var column=new FlowLayoutPanel {AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new Padding(0,0,12,0)};
            layout.Controls.Add(column);
            column.Controls.Add(new Label {Text=title,AutoSize=true,Margin=new Padding(2,0,0,4),ForeColor=Color.SlateGray});
            var options=(build.Data[key] as JArray??new()).OfType<JObject>().Take(3).ToArray();
            if(options.Length==0)column.Controls.Add(TextLabel("暂无数据"));
            foreach(var option in options)
            {
                var ids=(option["ids"] as JArray??new()).Values<int>().ToArray();
                // Keep the three data columns together; only the entire section may wrap.
                int iconWidth=Math.Max(108,ids.Length*36);
                var row=new Panel {Size=new Size(iconWidth+180,44),Margin=Padding.Empty};
                column.Controls.Add(row);
                var icons=new FlowLayoutPanel {Location=new Point(0,4),Size=new Size(iconWidth,36),WrapContents=false,Margin=Padding.Empty};
                row.Controls.Add(icons);
                foreach(var id in ids)await AddIconAsync(icons,id,"item",token);
                token.ThrowIfCancellationRequested();
                var stats=RecommendationStats(option);stats.Location=new Point(iconWidth,0);row.Controls.Add(stats);
            }
        }
    }
}
