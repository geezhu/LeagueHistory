using League.Services;

namespace League;

public partial class FormMain
{
    private Panel _searchToolbar = null!, _searchBox = null!;
    private static readonly Color SearchBackground = Color.FromArgb(232,238,246);
    private bool _searchInProgress;
    private readonly PlayerSearchHistory _searchHistory = new();
    private void InitializeSearchHistory()
    {
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Color.FromArgb(244,247,251), Padding = new Padding(16,8,16,8) };
        label1.Text = "战绩查询";
        label1.AutoSize = false;
        label1.SetBounds(16,8,130,34);
        label1.Font = new Font("Microsoft YaHei UI",12f,FontStyle.Bold);
        label1.ForeColor = Color.FromArgb(36,63,98);
        label1.TextAlign = ContentAlignment.MiddleLeft;
        var search = new Panel { Width = 200, Height = 28, BackColor = SearchBackground };
        void RoundSearch()
        {
            if (search.Width < 16 || search.Height < 16) return;
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            const int diameter = 12;
            path.AddArc(0,0,diameter,diameter,180,90);
            path.AddArc(search.Width-diameter,0,diameter,diameter,270,90);
            path.AddArc(search.Width-diameter,search.Height-diameter,diameter,diameter,0,90);
            path.AddArc(0,search.Height-diameter,diameter,diameter,90,90);
            path.CloseFigure();
            var oldRegion = search.Region;
            search.Region = new Region(path);
            oldRegion?.Dispose();
        }
        search.SizeChanged += (_,_) => RoundSearch();
        RoundSearch();
        search.Paint += (_,e) =>
        {
            using var pen = new Pen(Color.FromArgb(111,123,139),1.6f);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawEllipse(pen,10,9,10,10); e.Graphics.DrawLine(pen,19,18,24,23);
        };
        txtGameName.FlatStyle = FlatStyle.Flat;
        txtGameName.SetBounds(28,2,164,24);
        txtGameName.ForeColor = Color.FromArgb(36,63,98);
        txtGameName.BackColor = search.BackColor;
        txtGameName.AccessibleName = "输入 Riot ID，回车查询";
        search.Controls.Add(txtGameName);
        label1.Visible = false;
        toolbar.Controls.Add(search);
        _searchToolbar = toolbar; _searchBox = search;
        toolbar.SizeChanged += (_,_) => { if (search.Parent == toolbar) search.Location = new Point(Math.Max(0,toolbar.ClientSize.Width-search.Width-8),2); };
        panelMatchList.Dock = DockStyle.Fill;
        tabPage1.BackColor = toolbar.BackColor;
        tabPage1.Controls.Add(toolbar);
        panelMatchList.BringToFront();
        txtGameName.DropDownStyle = ComboBoxStyle.DropDown;
        txtGameName.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        txtGameName.AutoCompleteSource = AutoCompleteSource.ListItems;
        txtGameName.MaxDropDownItems = 10;
        txtGameName.DropDownWidth = 280;
        RefreshSearchHistory();
        txtGameName.SelectionChangeCommitted += (_,_) =>
        {
            txtGameName.DroppedDown = false;
        };
        txtGameName.KeyDown += async (_,e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true; e.SuppressKeyPress = true;
            txtGameName.DroppedDown = false;
            await SubmitPlayerSearchAsync();
        };
        components ??= new System.ComponentModel.Container();
        var hint = new ToolTip(components);
        hint.SetToolTip(txtGameName,"输入或选择 Riot ID，按回车查询");
        var menu = new ContextMenuStrip(components);
        menu.Items.Add("复制",null,(_,_) => { if (txtGameName.SelectedText.Length > 0) Clipboard.SetText(txtGameName.SelectedText); });
        menu.Items.Add("粘贴",null,(_,_) => { if (Clipboard.ContainsText()) txtGameName.SelectedText = Clipboard.GetText(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("删除此搜索记录",null,(_,_) => { _searchHistory.Remove(txtGameName.Text); RefreshSearchHistory(); });
        menu.Items.Add("清空搜索历史",null,(_,_) => { _searchHistory.Clear(); RefreshSearchHistory(); });
        txtGameName.ContextMenuStrip = menu;
    }
    private void RefreshSearchHistory()
    {
        string input = txtGameName.Text;
        txtGameName.BeginUpdate();
        txtGameName.Items.Clear();
        txtGameName.Items.AddRange(_searchHistory.Items.Cast<object>().ToArray());
        txtGameName.Text = input;
        txtGameName.EndUpdate();
    }

    private void AttachSearchToPlayerTabs()
    {
        if (_matchTabContent == null) return;
        var content = _matchTabContent;
        var tabs = content.MainTabControl;
        tabs.Padding = new Point(20,6);
        void Arrange()
        {
            if (content.IsDisposed || _searchBox.IsDisposed || !ReferenceEquals(content,_matchTabContent)) return;
            int lastRight = tabs.TabCount == 0 ? 0 : tabs.GetTabRect(tabs.TabCount-1).Right;
            bool fits = content.ClientSize.Width-lastRight >= _searchBox.Width+16;
            if (fits)
            {
                _searchBox.BackColor = txtGameName.BackColor = SearchBackground;
                _searchToolbar.Visible = false;
                if (_searchBox.Parent != content) content.Controls.Add(_searchBox);
                _searchBox.Location = new Point(Math.Max(0,content.ClientSize.Width-_searchBox.Width-6),0);
                _searchBox.BringToFront();
            }
            else
            {
                _searchBox.BackColor = txtGameName.BackColor = SearchBackground;
                // Keep every tab accessible when the strip has no spare room.
                if (_searchBox.Parent != _searchToolbar) _searchToolbar.Controls.Add(_searchBox);
                _searchToolbar.Visible = true;
                _searchBox.Location = new Point(Math.Max(0,_searchToolbar.ClientSize.Width-_searchBox.Width-8),2);
            }
        }
        void Schedule() { if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(Arrange)); }
        content.SizeChanged += (_,_) => Arrange();
        tabs.ControlAdded += (_,_) => Schedule();
        tabs.ControlRemoved += (_,_) => Schedule();
        tabs.SelectedIndexChanged += (_,_) => Schedule();
        Arrange();
    }
}
