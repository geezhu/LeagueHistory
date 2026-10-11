using League.Models;

internal static class AssistantVisibilityChecks
{
    public static void Run()
    {
        var state = new AssistantVisibility();
        void Check(bool result, string message)
        {
            if (!result)
                throw new Exception(message);
        }
        Check(state.ShouldShow(true, false, true, true, false, 0), "Focused client shows assistant");
        Check(state.ShouldShow(true, false, false, false, false, 100), "Transient missing handle retains panel");
        Check(state.ShouldShow(true, false, true, false, false, 850), "Transient foreground changes do not flicker");
        Check(!state.ShouldShow(true, false, true, false, false, 1600), "Unrelated foreground eventually hides panel");
        Check(state.ShouldShow(true, false, true, true, false, 1700), "Client focus restores assistant");
        Check(state.ShouldShow(true, false, true, false, true, 4000), "Dragging keeps assistant visible");
        Check(!state.ShouldShow(true, true, true, true, true, 4100), "Minimized client hides immediately");
        Check(!state.ShouldShow(false, false, true, true, false, 4200), "Leaving champion select hides immediately");
        Console.WriteLine("PASS: assistant focus, transient handle loss, dragging, minimize and exit visibility.");
    }
}
