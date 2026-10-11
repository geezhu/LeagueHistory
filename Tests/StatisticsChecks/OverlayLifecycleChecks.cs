using System.Reflection;
using System.Runtime.CompilerServices;
using League.Services;

internal static class OverlayLifecycleChecks
{
    public static void Run()
    {
        var type = typeof(GameBarBridge).Assembly.GetType("League.Managers.LoadingOverlayManager")!;
        // Test only the retired-detail lifecycle, without creating timers, windows or the live bridge.
        var manager = RuntimeHelpers.GetUninitializedObject(type);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        using var cancellation = new CancellationTokenSource();
        var worker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        type.GetField("_detailCancellation", flags)!.SetValue(manager, cancellation);
        type.GetField("_detailWork", flags)!.SetValue(manager, worker.Task);
        type.GetField("_lastDetailRequest", flags)!.SetValue(manager, "retired-request");
        type.GetMethod("CancelDetail", flags)!.Invoke(manager, null);
        if (!cancellation.Token.IsCancellationRequested || worker.Task.IsCompleted)
        {
            throw new Exception("Retired detail must cancel without ending or disposing its unfinished worker");
        }
        if (
            type.GetField("_detailCancellation", flags)!.GetValue(manager) != null
            || (string?)type.GetField("_lastDetailRequest", flags)!.GetValue(manager) != ""
        )
        {
            throw new Exception("Retired detail must immediately release the active selection");
        }
        worker.SetResult();
        if (!SpinWait.SpinUntil(() => Disposed(cancellation), 2000))
        {
            throw new Exception("Detail cancellation source was not disposed after its worker completed");
        }
        Console.WriteLine(
            "PASS: detail retirement cancels immediately, joins the worker and then disposes its source. No timers, bridge or input used."
        );
    }

    private static bool Disposed(CancellationTokenSource source)
    {
        try
        {
            _ = source.Token;
            return false;
        }
        catch (ObjectDisposedException)
        {
            // Token access is deliberately used to verify source disposal after worker completion.
            return true;
        }
    }
}
