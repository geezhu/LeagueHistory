using System.Drawing.Drawing2D;
using League.Models;

namespace League.Controls;

/// <summary>Five fixed, documented scales. Missing evidence is never plotted at zero.</summary>
public sealed class CompositionRadar : Control
{
    private readonly CompositionRadarAxis[] _axes;
    private readonly ToolTip _tips=new(){AutoPopDelay=20000};
    private readonly List<(RectangleF Bounds,string Text)> _targets=new();
    private string _hover="";
    public CompositionRadar(CompositionRadarAxis[] axes)
    {
        _axes=axes;
        DoubleBuffered=true;ResizeRedraw=true;AutoSize=true;
        BackColor=Color.FromArgb(16,45,57);ForeColor=Color.FromArgb(202,222,228);
        Size=new Size(540,290);MinimumSize=new Size(240,250);
        AccessibleName="阵容能力雷达图";
        AccessibleDescription=string.Join("；",axes.Select(a=>a.Label+"："+(a.Score.HasValue?$"{a.Score:0} / 100":"未知")+"。"+a.Basis));
        _tips.SetToolTip(this,AccessibleDescription);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string text=_targets.FirstOrDefault(t=>t.Bounds.Contains(e.Location)).Text??"各轴为固定规则参考分，不是胜率或综合战力。灰色问号表示资料不足。";
        if(text!=_hover){_hover=text;_tips.SetToolTip(this,text);}
    }
    public override Size GetPreferredSize(Size proposedSize)=>new(Math.Max(240,Math.Min(540,proposedSize.Width>0?proposedSize.Width:540)),Math.Max(290,(int)(Font.Height/15f*290)));
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);if(_axes.Length!=5)return;
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        float unit=Font.Height/15f;
        float radius=Math.Max(30,Math.Min(Width/2f-78*unit,Height/2f-46*unit));
        var center=new PointF(Width/2f,Height/2f+3*unit);
        PointF Point(int i,float fraction)
        {
            double angle=-Math.PI/2+2*Math.PI*i/5;
            return new(center.X+(float)Math.Cos(angle)*radius*fraction,center.Y+(float)Math.Sin(angle)*radius*fraction);
        }
        using var grid=new Pen(Color.FromArgb(43,76,86));
        using var spoke=new Pen(Color.FromArgb(54,85,94)){DashStyle=DashStyle.Dot};
        using var ink=new SolidBrush(ForeColor);
        using var muted=new SolidBrush(Color.FromArgb(132,163,177));
        using var accent=new Pen(Color.FromArgb(45,208,203),2);
        using var pointBrush=new SolidBrush(accent.Color);
        using var fill=new SolidBrush(Color.FromArgb(48,45,208,203));
        for(int ring=1;ring<=4;ring++)g.DrawPolygon(grid,Enumerable.Range(0,5).Select(i=>Point(i,ring/4f)).ToArray());
        for(int i=0;i<5;i++)g.DrawLine(spoke,center,Point(i,1));
        var values=_axes.Select((a,i)=>a.Score is double score?Point(i,(float)Math.Clamp(score/100,0,1)):(PointF?)null).ToArray();
        if(values.All(p=>p.HasValue))
        {
            var polygon=values.Select(p=>p!.Value).ToArray();g.FillPolygon(fill,polygon);g.DrawPolygon(accent,polygon);
        }
        else for(int i=0;i<5;i++)if(values[i].HasValue && values[(i+1)%5].HasValue)g.DrawLine(accent,values[i]!.Value,values[(i+1)%5]!.Value);
        _targets.Clear();
        using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
        for(int i=0;i<5;i++)
        {
            var a=_axes[i];var outer=Point(i,1);var label=Point(i,1.32f);
            float labelWidth=102*unit,labelHeight=40*unit;
            var rect=new RectangleF(Math.Clamp(label.X-labelWidth/2,0,Math.Max(0,Width-labelWidth)),Math.Clamp(label.Y-labelHeight/2,0,Math.Max(0,Height-labelHeight)),labelWidth,labelHeight);
            string score=a.Score.HasValue?$"{a.Score:0} / 100":"未知";
            g.DrawString(a.Label+"\n"+score,Font,a.Score.HasValue?ink:muted,rect,format);
            _targets.Add((rect,a.Label+" · "+score+"\n"+a.Basis));
            if(values[i] is PointF p){g.FillEllipse(pointBrush,p.X-3,p.Y-3,6,6);_targets.Add((new RectangleF(p.X-9,p.Y-9,18,18),a.Basis));}
            else{g.DrawEllipse(spoke,outer.X-6,outer.Y-6,12,12);g.DrawString("?",Font,muted,new RectangleF(outer.X-7,outer.Y-10,14,20),format);}
        }
    }
    protected override void Dispose(bool disposing){if(disposing)_tips.Dispose();base.Dispose(disposing);}
}
