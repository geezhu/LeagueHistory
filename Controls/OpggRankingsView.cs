using League.Models;
using System.Drawing.Drawing2D;

namespace League.Controls;

internal sealed class OpggRankingsView : UserControl
{
    private readonly DataGridView _grid=new() { Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,
        AllowUserToResizeRows=false,RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,
        BackgroundColor=Color.FromArgb(246,248,251),BorderStyle=BorderStyle.None,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,
        ColumnHeadersHeight=42,EnableHeadersVisualStyles=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill };
    private readonly Dictionary<int,Image?> _icons=new();
    private readonly HashSet<int> _pending=new();
    private readonly SemaphoreSlim _imageGate=new(4,4);
    private readonly CancellationTokenSource _lifetime=new();
    private OpggTier[] _rows=Array.Empty<OpggTier>();
    private int _sortColumn; private bool _descending;
    public event Action<OpggTier>? ChampionRequested;
    public bool HasRows=>_rows.Length>0;
    private static string Name(int id)=>FormMain.Globals.resLoading?.GetChampionById(id)?.Name??$"英雄 {id}";
    private static string Rate(double? value)=>value is double rate?$"{rate:P2}":"—";
    public OpggRankingsView()
    {
        Dock=DockStyle.Fill;Padding=new Padding(12,0,12,12);
        _grid.DefaultCellStyle=new DataGridViewCellStyle {Font=new Font("Microsoft YaHei UI",9f),ForeColor=Color.FromArgb(40,60,80),BackColor=Color.White,
            SelectionBackColor=Color.FromArgb(218,238,251),SelectionForeColor=Color.FromArgb(27,75,106),Alignment=DataGridViewContentAlignment.MiddleCenter};
        _grid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle {BackColor=Color.FromArgb(238,243,249),ForeColor=Color.FromArgb(85,106,129),Font=new Font("Microsoft YaHei UI",9f)};
        _grid.GridColor=Color.FromArgb(231,237,244);_grid.RowTemplate.Height=52;
        foreach(var (title,weight,min) in new[]{("排名",45,45),("英雄",200,175),("梯队",65,55),("胜率",100,80),("登场率",100,80),("禁用率",100,80),("劣势对局",130,112)})
            _grid.Columns.Add(new DataGridViewTextBoxColumn {HeaderText=title,FillWeight=weight,MinimumWidth=min,SortMode=DataGridViewColumnSortMode.Programmatic});
        _grid.CellPainting+=PaintCell;
        _grid.CellClick+=(_,e)=> {if(e.RowIndex>=0 && _grid.Rows[e.RowIndex].Tag is OpggTier row)ChampionRequested?.Invoke(row);};
        _grid.KeyDown+=(_,e)=> {if(e.KeyCode==Keys.Enter && _grid.CurrentRow?.Tag is OpggTier row){e.Handled=true;e.SuppressKeyPress=true;ChampionRequested?.Invoke(row);}};
        _grid.ColumnHeaderMouseClick+=(_,e)=>
        {
            if(e.ColumnIndex==6)return;
            _descending=_sortColumn==e.ColumnIndex?!_descending:e.ColumnIndex is 3 or 4 or 5;_sortColumn=e.ColumnIndex;FillRows();
        };
        _grid.CellToolTipTextNeeded+=(_,e)=>
        {
            if(e.RowIndex>=0 && _grid.Rows[e.RowIndex].Tag is OpggTier row)
                e.ToolTipText=e.ColumnIndex==6?"劣势对局："+(row.Counters.Length>0?string.Join("、",row.Counters.Select(Name)):"暂无数据"):"点击查看 "+Name(row.ChampionId)+" 的攻略";
        };
        _grid.Cursor=Cursors.Hand;Controls.Add(_grid);
    }
    public void ClearRows(){_rows=Array.Empty<OpggTier>();_grid.Rows.Clear();}
    public void SetRows(OpggTier[] rows){_rows=rows;_sortColumn=0;_descending=false;FillRows();}
    private void FillRows()
    {
        var sorted=_sortColumn==1?(_descending?_rows.OrderByDescending(r=>Name(r.ChampionId)):_rows.OrderBy(r=>Name(r.ChampionId))):
            _descending?_rows.OrderByDescending(Value):_rows.OrderBy(Value);
        double Value(OpggTier r)=>_sortColumn switch {2=>r.Tier??99,3=>r.WinRate??-1,4=>r.PickRate??-1,5=>r.BanRate??-1,_=>r.Rank??9999};
        _grid.Rows.Clear();
        foreach(var row in sorted)
        {
            int index=_grid.Rows.Add(row.Rank?.ToString()??"—",Name(row.ChampionId),row.Tier?.ToString()??"—",Rate(row.WinRate),Rate(row.PickRate),Rate(row.BanRate),"");
            _grid.Rows[index].Tag=row;
        }
        foreach(DataGridViewColumn col in _grid.Columns)col.HeaderCell.SortGlyphDirection=col.Index==_sortColumn?(_descending?SortOrder.Descending:SortOrder.Ascending):SortOrder.None;
        _grid.ClearSelection();
    }
    private void PaintCell(object? sender,DataGridViewCellPaintingEventArgs e)
    {
        if(e.RowIndex<0 || _grid.Rows[e.RowIndex].Tag is not OpggTier row || e.ColumnIndex is not (1 or 2 or 6))return;
        e.PaintBackground(e.ClipBounds,true);var g=e.Graphics!;g.SmoothingMode=SmoothingMode.AntiAlias;
        if(e.ColumnIndex==1)
        {
            DrawHero(g,row.ChampionId,new Rectangle(e.CellBounds.X+10,e.CellBounds.Y+9,34,34));
            TextRenderer.DrawText(g,Name(row.ChampionId),_grid.Font,new Rectangle(e.CellBounds.X+52,e.CellBounds.Y,e.CellBounds.Width-56,e.CellBounds.Height),e.CellStyle!.ForeColor,
                TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        }
        else if(e.ColumnIndex==2)
        {
            var color=row.Tier switch {1=>Color.FromArgb(0,160,206),2=>Color.FromArgb(49,171,149),3=>Color.FromArgb(91,139,202),_=>Color.FromArgb(136,146,165)};
            int x=e.CellBounds.X+(e.CellBounds.Width-24)/2,y=e.CellBounds.Y+12;
            using var brush=new SolidBrush(color);g.FillPolygon(brush,new[]{new Point(x,y),new Point(x+24,y),new Point(x+24,y+24),new Point(x+12,y+30),new Point(x,y+24)});
            TextRenderer.DrawText(g,row.Tier?.ToString()??"—",_grid.Font,new Rectangle(x,y,24,25),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
        else if(row.Counters.Length==0)TextRenderer.DrawText(g,"—",_grid.Font,e.CellBounds,Color.Gray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        else for(int i=0;i<row.Counters.Length;i++)DrawHero(g,row.Counters[i],new Rectangle(e.CellBounds.X+8+i*30,e.CellBounds.Y+13,26,26));
        e.Handled=true;
    }
    private void DrawHero(Graphics g,int id,Rectangle rect)
    {
        using var path=new GraphicsPath();path.AddEllipse(rect);var state=g.Save();g.SetClip(path);
        if(_icons.TryGetValue(id,out var icon) && icon!=null)g.DrawImage(icon,rect);
        else {using var brush=new SolidBrush(Color.FromArgb(219,229,241));g.FillEllipse(brush,rect);}
        g.Restore(state);
        if(!_icons.ContainsKey(id) && _pending.Add(id))_ = LoadIconAsync(id);
    }
    private async Task LoadIconAsync(int id)
    {
        bool entered=false;Image? image=null;
        try
        {
            await _imageGate.WaitAsync(_lifetime.Token);entered=true;
            var resources=FormMain.Globals.resLoading;
            if(resources!=null)image=await resources.GetChampionIconAsync(id);
            if(IsDisposed){image?.Dispose();return;}
            _icons[id]=image;_grid.Invalidate();
        }
        catch(OperationCanceledException){image?.Dispose();}
        catch{if(!IsDisposed)_icons[id]=null;}
        finally{_pending.Remove(id);if(entered)_imageGate.Release();}
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){_lifetime.Cancel();foreach(var icon in _icons.Values)icon?.Dispose();_icons.Clear();}
        base.Dispose(disposing);
    }
}
