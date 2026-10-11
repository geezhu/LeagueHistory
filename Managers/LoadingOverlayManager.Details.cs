using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    private readonly System.Windows.Forms.Timer _detailPoll = new() { Interval = 400 };
    private readonly OverlayChampionDetailService _championDetails;
    private CancellationTokenSource? _detailCancellation;
    private Task _detailWork = Task.CompletedTask;
    private string _lastDetailRequest = "";
    private bool _readingDetail;

    private void CancelDetail()
    {
        var cancellation = _detailCancellation;
        _detailCancellation = null;
        if (cancellation != null)
        {
            cancellation.Cancel();
            // The cancelled loader may still be joining its skill/image work; retire its source afterwards.
            _ = _detailWork.ContinueWith(
                _ => cancellation.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }
        _lastDetailRequest = "";
    }

    private async Task ReadDetailSelection()
    {
        if (
            _disposed
            || _readingDetail
            || !UseGameBar
            || !(_owner.GetAppConfig()?.EnableLoadingOverlay ?? true)
            || !LoadingRoster.CanDisplay(_phase)
        )
        {
            return;
        }

        _readingDetail = true;
        try
        {
            var request = await _gameBar.ReadDetailRequestAsync();
            if (_disposed || request == null || request.Id == _lastDetailRequest || !UseGameBar)
            {
                return;
            }

            CancelDetail();
            _lastDetailRequest = request.Id;
            if (request.Slot < 0 || _roster == null)
            {
                return;
            }

            int own = _roster.Players.FirstOrDefault(p => p.Self)?.Team ?? 100;
            int[] teams = own == 200 ? [200, 100] : [100, 200];
            int team = teams[request.Slot / 5];
            var player = _roster.Players.Where(p => p.Team == team).Take(5).ElementAtOrDefault(request.Slot % 5);
            if (player?.ChampionId != request.ChampionId)
            {
                return;
            }

            _detailCancellation = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
            _detailCancellation.CancelAfter(TimeSpan.FromSeconds(45));
            _detailWork = LoadChampionDetail(
                request,
                player,
                _roster.Queue,
                _ranks.GetValueOrDefault(player.Key)?.Position ?? "none",
                _detailCancellation.Token
            );
        }
        catch (Exception e) when (e is IOException or Newtonsoft.Json.JsonException)
        {
            // Widget requests can be replaced mid-read; the next poll retries the current selection.
        }
        finally
        {
            _readingDetail = false;
        }
    }

    private async Task LoadChampionDetail(
        OverlayDetailRequest request,
        LoadingPlayer player,
        int queue,
        string position,
        CancellationToken token
    )
    {
        JObject? last = null;
        void Publish(JObject data)
        {
            if (!_disposed && !token.IsCancellationRequested && _lastDetailRequest == request.Id)
            {
                last = (JObject)data.DeepClone();
                _gameBar.PublishDetail(request, data);
            }
        }
        Publish(
            new JObject
            {
                ["state"] = "loading",
                ["source"] = "正在读取英雄技能和推荐…",
                ["mode"] = OverlayChampionDetailService.ModeName(queue),
                ["sections"] = new JArray(),
            }
        );
        try
        {
            Publish(await _championDetails.LoadAsync(player.ChampionId, queue, position, token, Publish));
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && !_life.IsCancellationRequested && _lastDetailRequest == request.Id)
            {
                var partial = last ?? new JObject { ["sections"] = new JArray() };
                partial["state"] = "partial";
                partial["source"] = "部分详情读取超时，已加载内容仍可查看；可点击重试。";
                _gameBar.PublishDetail(request, partial);
            }
        }
        catch
        {
            Publish(
                new JObject
                {
                    ["state"] = "error",
                    ["source"] = "详情暂不可用，请重试。",
                    ["sections"] = new JArray(),
                }
            );
        }
    }
}
