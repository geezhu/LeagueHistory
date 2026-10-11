using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

public partial class MatchTabPageContent
{
    private readonly ComboBox _outcomeFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _filterScope = new()
    {
        AutoSize = false,
        Height = 40,
        Dock = DockStyle.Bottom,
    };
    private bool? _selectedWin;
    private OutcomeHistoryPager? _outcomePager;
    private string? _outcomeQueue;

    private async Task<JArray> LoadOutcomePage()
    {
        if (_outcomePager == null || _outcomeQueue != _selectedQueueId)
        {
            _outcomePager = new OutcomeHistoryPager();
            _outcomeQueue = _selectedQueueId;
        }
        Task<JArray> Fetch(int offset, int count)
        {
            if (IsDisposed || _disposed)
                throw new OperationCanceledException();
            return LoadDataRequested(_puuid, offset, count, _selectedQueueId);
        }
        var result = await _outcomePager.LoadPage(_puuid, _selectedWin!.Value, _currentPage, _pageSize, Fetch);
        int loadedPages = Math.Max(1, (int)Math.Ceiling(_outcomePager.Matches / (double)_pageSize));
        TotalPages = _outcomePager.Exhausted ? loadedPages : Math.Max(loadedPages, _currentPage + 1);
        if (_currentPage > TotalPages)
        {
            _currentPage = TotalPages;
            result = await _outcomePager.LoadPage(_puuid, _selectedWin.Value, _currentPage, _pageSize, Fetch);
        }
        SafeInvoke(() =>
            _filterScope.Text =
                $"已检查 {_outcomePager.Scanned} 条，找到 {_outcomePager.Matches} 场\n第 {_currentPage} 页 · "
                + (_outcomePager.Exhausted ? "已查完可用范围（最多 200 条）" : "下一页继续查找")
        );
        return result;
    }
}
