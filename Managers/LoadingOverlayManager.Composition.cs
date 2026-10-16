using League.Models;
using League.Services;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    // The composition page, computed once per roster; the capability grades sample random teams, so they are built
    // once per statistics index off the UI thread.
    private GameComposition? _composition;
    private string _compositionKey = "";
    private bool _compositionBusy;
    private long _compositionRetry;
    private AramMetaIndex? _gradesIndex;
    private AramCapabilityGrades? _grades;

    private static string CompositionKey(LoadingRoster roster) =>
        roster.GameId
        + "|"
        + roster.Queue
        + "|"
        + string.Join(",", roster.Players.Select(p => p.Team + ":" + p.ChampionId + (p.Self ? "*" : "")));

    /// <summary>The composition of the current roster; null while it loads or after the roster changed.</summary>
    private GameComposition? CurrentComposition() =>
        _roster != null && _compositionKey == CompositionKey(_roster) ? _composition : null;

    private void RequestComposition()
    {
        var roster = _roster;
        if (!UseGameBar || roster == null || !GameBarComposition.Supports(roster))
        {
            return;
        }

        string key = CompositionKey(roster);
        if (key != _compositionKey && !_compositionBusy && Environment.TickCount64 >= _compositionRetry)
        {
            _ = LoadCompositionAsync(roster, key);
        }
    }

    private async Task LoadCompositionAsync(LoadingRoster roster, string key)
    {
        _compositionBusy = true;
        try
        {
            var index = await _championDetails.LoadAramMetaIndexAsync(_life.Token);
            if (!ReferenceEquals(index, _gradesIndex) || _grades == null)
            {
                _grades = await Task.Run(() => AramCapabilityGrades.Build(index), _life.Token);
                _gradesIndex = index;
            }

            // A roster that changed meanwhile is computed on the next refresh instead.
            if (_disposed || _roster == null || CompositionKey(_roster) != key)
            {
                return;
            }

            _composition = GameBarComposition.Build(index, _grades, roster, Champion);
            _compositionKey = key;
            SchedulePaint();
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested)
        {
            // Closing the overlay cancels the read; nothing is published afterwards.
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException)
        {
            AppLog.Shared.Warn(LogCategory.Data, "对局速览：阵容分析统计读取失败，一分钟后重试", e);
            _compositionRetry = Environment.TickCount64 + 60000;
        }
        finally
        {
            _compositionBusy = false;
        }
    }
}
