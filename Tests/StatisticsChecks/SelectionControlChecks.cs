using System.Reflection;
using League.Controls;
using League.Models;
using Newtonsoft.Json.Linq;

internal static class SelectionControlChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        using var parent = new CancellationTokenSource();
        var assembly = typeof(TimelineView).Assembly;
        using var equipment = (Control)
            Activator.CreateInstance(
                assembly.GetType("League.Controls.EquipmentComboBox")!,
                new object[] { Array.Empty<JObject>(), parent.Token, "item" }
            )!;
        using var runes = (Control)
            Activator.CreateInstance(
                assembly.GetType("League.Controls.RuneSchemeComboBox")!,
                new object[]
                {
                    Array.Empty<OpggRunes>(),
                    (Func<OpggRunes, string[]>)(_ => Array.Empty<string>()),
                    parent.Token,
                }
            )!;
        Check(equipment);
        Check(runes);
        if (parent.IsCancellationRequested)
        {
            throw new Exception("Closing one selector must not cancel its containing recommendation view");
        }
        Console.WriteLine(
            "PASS: equipment/rune selector retirement waits for the image worker and releases its linked source. No resources downloaded."
        );
    }

    private static void Check(Control selector)
    {
        var type = selector.GetType();
        var source = (CancellationTokenSource)type.GetField("_images", PrivateInstance)!.GetValue(selector)!;
        // Empty choices avoid resource requests; substitute a pending worker to exercise the disposal boundary.
        var worker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        type.GetField("_imageWork", PrivateInstance)!.SetValue(selector, worker.Task);
        selector.Dispose();
        if (!source.Token.IsCancellationRequested || worker.Task.IsCompleted)
        {
            throw new Exception("Selector disposal must cancel immediately and retain its unfinished worker");
        }
        worker.SetResult();
        if (!SpinWait.SpinUntil(() => Disposed(source), 2000))
        {
            throw new Exception("Selector cancellation source was not disposed after its worker completed");
        }
        selector.Dispose();
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
            // Token access confirms source disposal after image work has ended.
            return true;
        }
    }
}
