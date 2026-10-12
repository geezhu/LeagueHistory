using League.Models;
using static StatisticsCheckSupport;

internal static class AssistantVisibilityChecks
{
    public static void Run()
    {
        var state = new AssistantVisibility();
        Check(state.ShouldShow(true, false, true, false, 0), "Available client shows assistant");
        Check(state.ShouldShow(true, false, false, false, 100), "Transient missing handle retains panel");
        Check(state.ShouldShow(true, false, false, false, 850), "Short handle loss does not flicker");
        Check(!state.ShouldShow(true, false, false, false, 1600), "Lost client eventually hides panel");
        Check(state.ShouldShow(true, false, true, false, 1700), "Background client opens without a click");
        Check(state.ShouldShow(true, false, false, true, 4000), "Dragging keeps assistant visible");
        Check(!state.ShouldShow(true, true, true, true, 4100), "Minimized client hides immediately");
        Check(!state.ShouldShow(false, false, true, false, 4200), "Leaving champion select hides immediately");
        Console.WriteLine(
            "PASS: assistant opens without client focus, handle loss grace, dragging, minimize and exit."
        );
    }
}
