using System.Net;
using League.Clients;

internal static class ClientChecks
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request, token);
    }

    public static async Task RunAsync()
    {
        await ChampionMasteryChecks.RunAsync();
        await LoadingRosterIdentityChecks.Run();
        await OpggChecks.RunAsync();
        await AramMetaChecks.RunAsync();
        await StatisticsRequests();
        Console.WriteLine(
            "PASS: isolated client cache, identity, cancellation, retry, version and persistence checks."
        );
    }

    private static async Task StatisticsRequests()
    {
        var missing = await new PlayerStatisticsService(() => null).GetAsync("/", CancellationToken.None);
        Check(
            missing.Data == null && missing.Message.Contains("尚未连接"),
            "Disconnected client is not empty statistics"
        );
        using var forbidden = new HttpClient(
            new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))
        )
        {
            BaseAddress = new Uri("https://test.invalid/"),
        };
        var denied = await new PlayerStatisticsService(() => forbidden).GetAsync("/", CancellationToken.None);
        Check(denied.Status == 403 && denied.Data == null, "Private statistics retain rejection status");
        using var cancelled = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var waiting = new HttpClient(
            new Handler(
                async (_, token) =>
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                    return new(HttpStatusCode.OK);
                }
            )
        )
        {
            BaseAddress = new Uri("https://test.invalid/"),
        };
        var pending = new PlayerStatisticsService(() => waiting).GetAsync("/", cancelled.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        await cancelled.CancelAsync();
        try
        {
            await pending;
            throw new Exception("Caller cancellation was swallowed");
        }
        catch (OperationCanceledException)
        {
            // Cancellation must propagate rather than produce a cacheable empty response.
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
