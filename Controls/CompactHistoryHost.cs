namespace League.Controls;

// Clip the native scrollbar while retaining ListView's native wheel and keyboard scrolling.
internal sealed class CompactHistoryHost : Panel
{
    private readonly ListView _list;
    private readonly ScrollRail _rail;

    public CompactHistoryHost(ListView list)
    {
        _list = list;
        _rail = new ScrollRail(list) { Dock = DockStyle.Right, Width = 8 };
        Controls.Add(list); Controls.Add(_rail);
        list.Dock = DockStyle.None;
        list.DrawItem += (_,_) => _rail.Invalidate();
        list.MouseWheel += (_,_) => _rail.Invalidate();
        list.SelectedIndexChanged += (_,_) => _rail.Invalidate();
        SizeChanged += (_,_) => Fit();
        list.SizeChanged += (_,_) => Fit();
        _rail.BringToFront();
    }

    private void Fit()
    {
        var bounds = new Rectangle(0,0,Math.Max(1,ClientSize.Width + SystemInformation.VerticalScrollBarWidth - _rail.Width),ClientSize.Height);
        if (_list.Bounds != bounds) _list.Bounds = bounds;
        if (_list.Columns.Count > 0) _list.Columns[0].Width = Math.Max(30,ClientSize.Width - _rail.Width - 1);
        _rail.Invalidate();
    }

    private sealed class ScrollRail(ListView list) : Control
    {
        private bool _dragging;
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.White);
            if (list.Items.Count == 0 || !list.IsHandleCreated) return;
            int visible = Math.Max(1,list.ClientSize.Height / Math.Max(1,list.GetItemRect(0).Height));
            if (visible >= list.Items.Count) return;
            int length = Math.Min(Height,Math.Max(24,Height * visible / list.Items.Count));
            int top = (list.TopItem?.Index ?? 0) * Math.Max(0,Height-length) / Math.Max(1,list.Items.Count-visible);
            using var brush = new SolidBrush(_dragging ? Color.FromArgb(109,136,163) : Color.FromArgb(190,202,215));
            e.Graphics.FillRectangle(brush,2,top,Math.Max(2,Width-4),length);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _dragging = true; Capture = true; Scroll(e.Y);
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_dragging) Scroll(e.Y); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragging = false; Capture = false; Invalidate(); }
        private void Scroll(int y)
        {
            if (list.Items.Count == 0) return;
            int visible = Math.Max(1,list.ClientSize.Height / Math.Max(1,list.GetItemRect(0).Height));
            int index = (int)Math.Round(Math.Clamp((double)y / Math.Max(1,Height),0,1) * Math.Max(0,list.Items.Count-visible));
            list.TopItem = list.Items[index]; Invalidate();
        }
    }
}
