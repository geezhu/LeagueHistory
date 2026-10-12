using System.Runtime.InteropServices;
using League.Services;
using static StatisticsCheckSupport;

internal static class AssistantWindowChecks
{
    private const uint NativeOwner = 4;
    private const uint NextWindow = 2;
    private const int ShowMinimizedWithoutActivation = 7;
    private const int ShowWithoutActivation = 4;

    public static void Run() => WinFormsCheckHost.Run(WindowLifecycle);

    private static void WindowLifecycle()
    {
        using var client = new QuietForm();
        using var assistant = new QuietForm { TopMost = true };
        using var layer = new AssistantWindowLayer(assistant);
        AttachmentAndStacking(client, assistant, layer);
        MinimizeAndRecreate(client, assistant, layer);
        ClientReplacement(client, assistant, layer);
        Console.WriteLine(
            "PASS: assistant native ownership, normal z-order, no activation, dialogs, minimize, rebind and handle recreation."
        );
    }

    private static void AttachmentAndStacking(QuietForm client, QuietForm assistant, AssistantWindowLayer layer)
    {
        client.Show();
        Application.DoEvents();
        var foreground = GetForegroundWindow();
        Check(layer.Attach(client.Handle), "Assistant can attach to an external HWND without a managed Form owner");
        assistant.Show();
        Application.DoEvents();
        Check(
            GetWindow(assistant.Handle, NativeOwner) == client.Handle && assistant.Owner == null,
            "Native ownership points to the client, not the League main form"
        );
        Check(
            !assistant.TopMost && (GetWindowLong(assistant.Handle, -20) & 8) == 0,
            "Assistant must not use the global topmost band"
        );
        Check(
            (GetWindowLong(assistant.Handle, -16) & 0x40000000) == 0,
            "Assistant remains an independent top-level window"
        );
        Check(GetForegroundWindow() == foreground, "Attaching and showing must not steal focus");
        Raise(client.Handle);
        Check(Above(assistant.Handle, client.Handle), "Activating the client must not cover its assistant");
        using var other = new QuietForm();
        other.Show();
        Raise(other.Handle);
        for (int i = 0; i < 5; i++)
        {
            Check(layer.Attach(client.Handle), "Repeated follow ticks remain attached");
        }

        Check(
            Above(other.Handle, assistant.Handle),
            "Following the client must not raise the assistant above unrelated applications"
        );

        using var editor = new QuietForm();
        editor.Show(assistant);
        Check(Above(editor.Handle, assistant.Handle), "Equipment and roster dialogs remain above the assistant");
        editor.Hide();
        OpenBehindCoveringApplication(client, assistant, layer, other);
    }

    private static void OpenBehindCoveringApplication(
        QuietForm client,
        QuietForm assistant,
        AssistantWindowLayer layer,
        QuietForm other
    )
    {
        // Champion select can start while another application covers the background client.
        assistant.Hide();
        Raise(other.Handle);
        Check(layer.Attach(client.Handle), "Hidden assistant stays attached to the background client");
        layer.StackAboveClient();
        assistant.Show();
        layer.StackAboveClient();
        Application.DoEvents();
        Check(
            Above(other.Handle, assistant.Handle) && Above(assistant.Handle, client.Handle),
            "Opening for a background client stacks between the client and the covering application"
        );
    }

    private static void MinimizeAndRecreate(QuietForm client, QuietForm assistant, AssistantWindowLayer layer)
    {
        // Native minimization should hide an owned popup immediately, without waiting for a timer.
        ShowWindow(client.Handle, ShowMinimizedWithoutActivation);
        Application.DoEvents(); // SW_SHOWMINNOACTIVE
        Check(!IsWindowVisible(assistant.Handle), "Minimizing the client immediately hides the attached assistant");
        ShowWindow(client.Handle, ShowWithoutActivation);
        Application.DoEvents(); // SW_SHOWNOACTIVATE
        assistant.Hide();
        assistant.Show();
        Check(
            layer.Attach(client.Handle) && GetWindow(assistant.Handle, NativeOwner) == client.Handle,
            "Minimize/restore and hide/show preserve native ownership"
        );
        assistant.Recreate();
        Application.DoEvents();
        Check(
            GetWindow(assistant.Handle, NativeOwner) == client.Handle,
            "DPI/style handle recreation reattaches the assistant"
        );
    }

    private static void ClientReplacement(QuietForm client, QuietForm assistant, AssistantWindowLayer layer)
    {
        using var replacement = new QuietForm();
        replacement.Show();
        Check(layer.Attach(replacement.Handle), "Client handle replacement can rebind without recreating the form");
        client.Dispose();
        Check(
            !assistant.IsDisposed && IsWindowVisible(assistant.Handle),
            "Destroying the old client does not close the rebound assistant"
        );
        Check(!layer.Attach(IntPtr.Zero), "Missing client handles are rejected");
        replacement.Dispose();
        Check(!assistant.IsDisposed, "Native client shutdown must not dispose the reusable managed assistant");
        using var restarted = new QuietForm();
        restarted.Show();
        Check(layer.Attach(restarted.Handle), "Assistant can attach after the previous client HWND was destroyed");
        assistant.Show();
        Application.DoEvents();
        Check(
            GetWindow(assistant.Handle, NativeOwner) == restarted.Handle,
            "Restarted client retains the correct owner"
        );
        layer.Detach();
        Check(
            GetWindow(assistant.Handle, NativeOwner) == IntPtr.Zero,
            "Leaving champion select clears native ownership"
        );
        restarted.Dispose();
        Check(!assistant.IsDisposed, "Detached assistant survives client shutdown");
    }

    private static void Raise(IntPtr window) => SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x13); // NOMOVE | NOSIZE | NOACTIVATE

    private static bool Above(IntPtr first, IntPtr second)
    {
        for (
            IntPtr window = GetWindow(first, NextWindow);
            window != IntPtr.Zero;
            window = GetWindow(window, NextWindow)
        ) // GW_HWNDNEXT
        {
            if (window == second)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class QuietForm : Form
    {
        public QuietForm()
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-16000, -16000);
            Size = new Size(320, 200);
            ShowInTaskbar = false;
        }

        protected override bool ShowWithoutActivation => true;

        public void Recreate() => RecreateHandle();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr after,
        int x,
        int y,
        int width,
        int height,
        uint flags
    );
}
