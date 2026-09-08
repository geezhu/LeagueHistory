using League.Services;

namespace League;

public partial class FormMain
{
    private readonly PlayerSearchHistory _searchHistory = new();
    private void InitializeSearchHistory()
    {
        txtGameName.DropDownStyle = ComboBoxStyle.DropDown;
        txtGameName.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        txtGameName.AutoCompleteSource = AutoCompleteSource.ListItems;
        txtGameName.MaxDropDownItems = 10;
        txtGameName.DropDownWidth = 280;
        RefreshSearchHistory();
        txtGameName.SelectionChangeCommitted += (_,_) =>
        {
            txtGameName.DroppedDown = false;
            BeginInvoke(new Action(() => { if (!IsDisposed && btn_search.Enabled) btn_search.PerformClick(); }));
        };
        txtGameName.KeyDown += (_,e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true; e.SuppressKeyPress = true;
            txtGameName.DroppedDown = false;
            if (btn_search.Enabled) btn_search.PerformClick();
        };
        components ??= new System.ComponentModel.Container();
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
}
