using League.Models;
using League.Services;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    private readonly OverlayChatRequests _chatRequests = new();
    private bool _readingChat,
        _sendingChat;

    private async Task ReadChatCommand()
    {
        if (_disposed || _readingChat || !UseGameBar || !(_owner.GetAppConfig()?.EnableLoadingOverlay ?? true))
        {
            return;
        }

        _readingChat = true;
        try
        {
            var request = await _gameBar.ReadChatRequestAsync();
            if (_disposed || request == null || !_chatRequests.Take(request))
            {
                return;
            }

            if (_sendingChat)
            {
                _gameBar.PublishChatStatus(request, "busy", "上一条消息仍在处理，请稍后重试。", true);
                return;
            }
            _sendingChat = true;
            _ = SendChatCommand(request);
        }
        finally
        {
            _readingChat = false;
        }
    }

    private async Task SendChatCommand(OverlayChatRequest request)
    {
        string state = "cancelled",
            message = "未发送：请求已取消或对局已变化。";
        void Report(string next, string text)
        {
            state = next;
            message = text;
            _gameBar.PublishChatStatus(request, state, message, false);
        }
        int generation = _generation;
        var roster = _roster;
        var players = request.IsRosterFree || roster == null ? [] : OverlayTierChatMessage.Select(roster, request);
        bool currentRequest = true;
        bool Current() => CurrentChatContext(request, generation, players, currentRequest);
        try
        {
            if (request.Cancelled)
            {
                return;
            }

            var rejection = ChatRejection();
            if (rejection.HasValue)
            {
                (state, message) = rejection.Value;
                return;
            }
            if (!Current())
            {
                return;
            }

            var messages = BuildChatMessages(roster, request);
            Report("waiting", "请在 20 秒内按 Win＋G 返回游戏，保持聊天输入框关闭；返回后发送到游戏聊天。");
            var readiness = await WaitForChatReadinessAsync(request, Current);
            if (!Current() || readiness == OverlayChatReadiness.Cancelled)
            {
                state = "cancelled";
                message = "未发送：请求已取消或对局已变化。";
                return;
            }
            if (readiness != OverlayChatReadiness.Ready)
            {
                state = "timeout";
                message = "未发送：等待返回游戏超时，请重新点击发送。";
                return;
            }
            // Counts and countdowns change while the user is in Game Bar; use the latest polled data.
            if (request.Kind is "deathStreak" or "waves")
            {
                messages = BuildChatMessages(roster, request);
            }
            await SendWithMonitorAsync(request, messages, Report, Current, value => currentRequest = value);
        }
        catch (OperationCanceledException)
        {
            state = "cancelled";
            message = "未发送：请求已取消。";
        }
        catch (InvalidOperationException e)
        {
            state = "unavailable";
            message = e.Message;
        }
        catch
        {
            state = "error";
            message = "未发送：读取请求失败，请重新点击发送。";
        }
        finally
        {
            _sendingChat = false;
            _gameBar.PublishChatStatus(request, state, message, true);
        }
    }

    private (string State, string Message)? ChatRejection()
    {
        if (_phase != "InProgress")
        {
            return ("phase", "进入游戏后才能发送；加载界面只提供查看。");
        }
        if (!GameInputService.HasInputPermission())
        {
            return ("permission", "未发送：请以管理员身份启动 League.exe。");
        }
        return null;
    }

    private IReadOnlyList<string> BuildChatMessages(LoadingRoster? roster, OverlayChatRequest request) =>
        request.Kind switch
        {
            "voice" => OverlayVoiceChatText.Build(request),
            "mastery" => OverlayMasteryChatMessage.Build(roster!, request, _masteries, Champion),
            "deathStreak" => BuildDeathStreakMessages(request),
            "waves" => MinionWaveChatMessage.Build(
                _liveState?.Waves,
                request.Target,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ),
            _ => OverlayTierChatMessage.Build(roster!, request, _ranks, Champion),
        };

    private bool CurrentChatContext(
        OverlayChatRequest request,
        int generation,
        LoadingPlayer[] players,
        bool currentRequest
    ) =>
        currentRequest
        && !_disposed
        && !_life.IsCancellationRequested
        && generation == _generation
        && UseGameBar
        && (_owner.GetAppConfig()?.EnableLoadingOverlay ?? true)
        && _phase == "InProgress"
        && Environment.TickCount64 - _lastPhaseRead < 15000
        && (
            request.IsRosterFree
            || (
                _roster != null
                && players.Length > 0
                && players.All(p =>
                    _roster.Players.Any(q => p.Key == q.Key && p.ChampionId == q.ChampionId && p.Team == q.Team)
                )
            )
        );

    private IReadOnlyList<string> BuildDeathStreakMessages(OverlayChatRequest request)
    {
        if (_lastDeathStreakRead == 0 || Environment.TickCount64 - _lastDeathStreakRead >= 15000)
        {
            throw new InvalidOperationException("未发送：本局事件数据未更新，请等待 2999 连接恢复。");
        }

        return OverlayDeathStreakChatMessage.Build(_roster!, request, _deathStreaks, Champion);
    }

    private async Task<OverlayChatReadiness> WaitForChatReadinessAsync(OverlayChatRequest request, Func<bool> current)
    {
        var gate = new OverlayChatReady(request);
        var readiness = OverlayChatReadiness.Waiting;
        while (current())
        {
            var latest = await _gameBar.ReadChatRequestAsync();
            // SDK dismissal and foreground game must both agree. Pinning is not required.
            readiness = gate.Observe(
                latest,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                latest?.Dismissed == true && GameInputService.ModifiersReleased() && GameInputService.IsGameForeground()
            );
            if (readiness != OverlayChatReadiness.Waiting)
            {
                break;
            }

            await Task.Delay(200, _life.Token);
        }
        return readiness;
    }

    private async Task SendWithMonitorAsync(
        OverlayChatRequest request,
        IReadOnlyList<string> messages,
        Action<string, string> report,
        Func<bool> current,
        Action<bool> setCurrentRequest
    )
    {
        using var monitor = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        async Task Observe()
        {
            try
            {
                while (!monitor.IsCancellationRequested)
                {
                    var latest = await _gameBar.ReadChatRequestAsync();
                    bool currentRequest =
                        latest != null && request.SameCommand(latest) && !latest.Cancelled && latest.Dismissed;
                    setCurrentRequest(currentRequest);
                    if (!currentRequest)
                    {
                        return;
                    }

                    await Task.Delay(100, monitor.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelling the monitor joins its worker before the token source is disposed.
            }
        }
        var watching = Observe();
        try
        {
            await _owner.SendOverlayTiersAsync(messages, report, current, monitor.Token);
        }
        finally
        {
            await monitor.CancelAsync();
            await watching;
        }
    }
}
