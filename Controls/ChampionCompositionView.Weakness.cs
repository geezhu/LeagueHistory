using League.Models;

namespace League.Controls;

public sealed partial class ChampionCompositionView
{
    private Dictionary<int,ChampionAnalysisReport> Reports()=>_snapshots.ToDictionary(p=>p.Key,p=>p.Value.Report);
    private void RenderWeaknesses()
    {
        var team=Team();var reports=Reports();var result=CompositionWeakness.Analyze(team.Select(e=>e.ChampionId).ToArray(),reports);
        bool preview=(_replacement.SelectedItem as Choice)?.Id>0;
        var findings=CompositionTactics.Analyze(team.Select(e=>e.ChampionId).ToArray(),reports,TacticalDescriptions(),id=>_champion(id).Name);
        RenderRadar(preview,findings);
        if(team.Length==1)
        {
            var heroCard=Card(preview?"英雄能力分析 · 换人预览":"英雄能力分析","当前我方只有一名英雄，先分析个人能力。建议基于已解析数据，不包含装备、符文、强化与地图修正。");
            int id=team[0].ChampionId;
            if(reports.TryGetValue(id,out var report))
            {
                WeaknessLine(heroCard,_champion(id).Name,true);
                if(_snapshots[id].Stale || _errors.ContainsKey(id))WeaknessLine(heroCard,"当前使用旧缓存，更新后请复核。",true);
            }
            else WeaknessLine(heroCard,id<=0?"选择英雄后显示能力分析。":_errors.ContainsKey(id)?"英雄数据读取失败，请重试。":"正在读取英雄能力数据。",false);
            RenderTacticalFindings(findings);
            if(reports.TryGetValue(id,out var details))
            {
                var basics=Card("个人能力建议","技能来源与基础攻击能力");
                foreach(var insight in CompositionWeakness.HeroInsights(details))WeaknessLine(basics,insight,false);
            }
            return;
        }
        var card=Card(preview?"可能的短板 · 换人预览":"可能的短板",$"基于已读取的 {result.Known}/{result.Slots} 个位置，结合技能标签、客户端技能说明与基础属性生成线索。未记录不代表没有；不包含装备、符文、强化和地图修正。");
        if(!result.Complete)WeaknessLine(card,"阵容未选齐或数据未读全，以下仅是已读取部分的线索。",true);
        if(team.Any(e=>e.Preview))WeaknessLine(card,"包含预选英雄，锁定或交换后会重新分析。",false);
        if(team.Any(e=>_snapshots.TryGetValue(e.ChampionId,out var s) && (s.Stale || _errors.ContainsKey(e.ChampionId))))WeaknessLine(card,"部分数据来自旧缓存，结论需在更新后复核。",true);
        RenderTacticalFindings(findings);
    }
    private Dictionary<int,CompositionSkillText[]> TacticalDescriptions()=>_skills.ToDictionary(p=>p.Key,p=>p.Value.Select(s=>new CompositionSkillText(s.Key,s.Name,s.Description)).ToArray());
    private void RenderRadar(bool preview,CompositionFinding[] findings)
    {
        var axes=findings.Select(f=>f.Radar!).ToArray();
        var card=Card(preview?"阵容雷达 · 换人预览":"阵容雷达","固定规则参考分 · 不是胜率或综合战力；未知维度不按零分绘制。");
        card.Controls.Add(new CompositionRadar(axes){Width=Math.Max(240,_overview.ClientSize.Width-48),Margin=Padding.Empty});
        var rules=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Visible=false,Margin=Padding.Empty};
        foreach(var axis in axes)WeaknessLine(rules,axis.Label+"："+axis.Basis,false);
        var button=ActionButton("评分说明");
        button.Click+=(_,_)=>{rules.Visible=!rules.Visible;button.Text=rules.Visible?"收起说明":"评分说明";ResizeDashboard();};
        card.Controls.Add(button);card.Controls.Add(rules);
    }
    private void RenderTacticalFindings(CompositionFinding[] findings)
    {
        var team=Team();
        var loaded=team.Count(e=>_skills.ContainsKey(e.ChampionId));
        foreach(var finding in findings)
        {
            var card=Card(finding.Category+" · "+finding.Status,finding.Detail);
            if(loaded<team.Length)WeaknessLine(card,$"技能说明已读取 {loaded}/{team.Length} 位；客户端未连接或加载失败时，可连接后点击“重新读取”。",false);
            WeaknessLine(card,finding.Advice,false);
            if(finding.Evidence.Length==0){WeaknessLine(card,"暂无可展示的依据；未记录不代表没有。",false);continue;}
            var evidence=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Visible=false,Margin=Padding.Empty};
            var toggle=ActionButton("查看依据");
            toggle.Click+=(_,_)=>
            {
                if(evidence.Controls.Count==0)
                {
                    evidence.SuspendLayout();
                    foreach(var line in finding.Evidence)WeaknessLine(evidence,line,false);
                    evidence.ResumeLayout();
                }
                evidence.Visible=!evidence.Visible;toggle.Text=evidence.Visible?"收起依据":"查看依据";ResizeDashboard();
            };
            card.Controls.Add(toggle);card.Controls.Add(evidence);
        }
    }
    private void WeaknessLine(Control host,string text,bool attention)
    {
        host.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new Size(Math.Max(240,_overview.Width-60),0),Margin=new Padding(0,2,0,8),ForeColor=attention?Color.FromArgb(235,182,116):Color.FromArgb(166,192,202)});
    }
}
