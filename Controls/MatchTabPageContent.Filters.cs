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
    private string? _outcomePlayer;

    private sealed record OutcomePageRequest(string Player, string Queue, bool Win, int Page, int Size);

    private async Task<JArray> LoadOutcomePage()
    {
        var request = new OutcomePageRequest(_puuid, _selectedQueueId, _selectedWin!.Value, _currentPage, _pageSize);
        if (_outcomePager == null || _outcomeQueue != request.Queue || _outcomePlayer != request.Player)
        {
            _outcomePager = new OutcomeHistoryPager();
            _outcomeQueue = request.Queue;
            _outcomePlayer = request.Player;
        }
        var pager = _outcomePager;
        Task<JArray> Fetch(int offset, int count) => FetchOutcomeRecordsAsync(request, offset, count);
        var result = await pager.LoadPage(request.Player, request.Win, request.Page, request.Size, Fetch);
        EnsureCurrentOutcomeRequest(request);
        int loadedPages = Math.Max(1, (int)Math.Ceiling(pager.Matches / (double)request.Size));
        TotalPages = pager.Exhausted ? loadedPages : Math.Max(loadedPages, request.Page + 1);
        if (request.Page > TotalPages)
        {
            _currentPage = TotalPages;
            request = request with { Page = TotalPages };
            result = await pager.LoadPage(request.Player, request.Win, request.Page, request.Size, Fetch);
            EnsureCurrentOutcomeRequest(request);
        }
        SafeInvoke(() =>
        {
            if (IsCurrentOutcomeRequest(request))
            {
                _filterScope.Text =
                    $"已检查 {pager.Scanned} 条，找到 {pager.Matches} 场\n第 {request.Page} 页 · "
                    + (pager.Exhausted ? "已查完可用范围（最多 200 条）" : "下一页继续查找");
            }
        });
        return result;
    }

    private async Task<JArray> FetchOutcomeRecordsAsync(OutcomePageRequest request, int offset, int count)
    {
        EnsureCurrentOutcomeRequest(request);
        var result = await LoadDataRequested(request.Player, offset, count, request.Queue);
        EnsureCurrentOutcomeRequest(request);
        return result;
    }

    private bool IsCurrentOutcomeRequest(OutcomePageRequest request) =>
        !_navigationClosing
        && !IsDisposed
        && !_disposed
        && _puuid == request.Player
        && _selectedQueueId == request.Queue
        && _selectedWin == request.Win
        && _currentPage == request.Page
        && _pageSize == request.Size;

    private void EnsureCurrentOutcomeRequest(OutcomePageRequest request)
    {
        if (!IsCurrentOutcomeRequest(request))
        {
            throw new OperationCanceledException("玩家或筛选条件已改变，丢弃旧的战绩请求。");
        }
    }

    private async void OnOutcomeFilterChanged(object? sender, EventArgs e)
    {
        _selectedWin = _outcomeFilter.SelectedIndex switch
        {
            1 => true,
            2 => false,
            _ => null,
        };
        _currentPage = 1;
        if (string.IsNullOrEmpty(_puuid) || _navigationClosing)
        {
            return;
        }
        try
        {
            await UpdateMatchesDisplay();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"战绩结果筛选失败：{ex.GetType().Name}");
        }
    }
}
