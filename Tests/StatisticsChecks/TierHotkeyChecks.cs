using System.Reflection;

internal static class TierHotkeyChecks
{
    public static void Run()
    {
        var context = SynchronizationContext.Current;
        var type = typeof(League.FormMain).Assembly.GetType("League.Managers.TierHotkeyWindow")!;
        var events = new List<Keys>();
        // Isolate registration from the user's running application shortcuts.
        var keys = new[]
        {
            Keys.F23,
            Keys.F24,
            Keys.Control | Keys.F22,
            Keys.Control | Keys.F23,
            Keys.Control | Keys.F24,
        };
        using (
            var window = (IDisposable)
                Activator.CreateInstance(type, new object[] { (Action<Keys>)(key => events.Add(key)), keys })!
        )
        {
            var native = (NativeWindow)window;
            foreach (var (key, index) in keys.Select((key, index) => (key, index)))
            {
                int id = index + 1;
                bool registered = (bool)type.GetMethod("Handles")!.Invoke(window, new object[] { key })!;
                if (!registered)
                {
                    throw new Exception(
                        "Test shortcut registration failed; inspect registration diagnostics before retrying"
                    );
                }
                // Deliver WM_HOTKEY directly to this isolated receiver; never inject keyboard input.
                var message = Message.Create(native.Handle, 0x0312, new IntPtr(id), IntPtr.Zero);
                type.GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(window, new object[] { message });
            }
            if (!events.SequenceEqual(keys))
            {
                throw new Exception("Native shortcut dispatch must preserve each side's key");
            }
        }
        using (
            var replacement = (IDisposable)
                Activator.CreateInstance(type, new object[] { (Action<Keys>)(_ => { }), keys })!
        )
        {
            if (!(bool)type.GetMethod("Handles")!.Invoke(replacement, new object[] { Keys.F23 })!)
            {
                throw new Exception("Shortcut disposal must release registration");
            }
        }

        SynchronizationContext.SetSynchronizationContext(context);
        Console.WriteLine(
            "PASS: five native hotkey registrations, distinct plain/Ctrl dispatch and release; no keys or chat messages sent."
        );
    }
}
