using System.Runtime.ExceptionServices;

namespace League.Tests.Support;

/// <summary>
/// Runs a UI test body on a dedicated STA thread inside a real WinForms message loop, the way the application runs.
/// xUnit runs test methods on pool threads: those are MTA and have no message loop, so controls cannot be relied on
/// there, and an awaited continuation posted through <see cref="WindowsFormsSynchronizationContext"/> is never
/// pumped — the test would hang instead of failing. The body receives a host window that <see cref="Application.Run"/>
/// has already shown; add controls to it and give them a <c>Dock</c> or explicit bounds.
/// </summary>
public static class UiHost
{
    private const int HostWidth = 640;
    private const int HostHeight = 400;

    /// <summary>Runs a body that drives the interface without awaiting anything.</summary>
    public static void Run(Action<Form> body) =>
        Run(host =>
        {
            body(host);
            return Task.CompletedTask;
        });

    /// <summary>Runs a body that awaits the application's own work; the loop keeps pumping while it waits.</summary>
    public static void Run(Func<Form, Task> body)
    {
        ExceptionDispatchInfo? failure = null;
        OnStaThread(() =>
        {
            using var host = Host();
            void Capture(object? sender, ThreadExceptionEventArgs e)
            {
                // A failure inside a window message would otherwise show a dialog and hang the run.
                failure ??= ExceptionDispatchInfo.Capture(e.Exception);
                host.Close();
            }

            host.Shown += async (_, _) =>
            {
                try
                {
                    await body(host);
                }
                catch (Exception ex)
                {
                    failure ??= ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    host.Close();
                }
            };
            Application.ThreadException += Capture;
            try
            {
                Application.Run(host);
            }
            finally
            {
                Application.ThreadException -= Capture;
            }
        });
        failure?.Throw();
    }

    /// <summary>The thread a UI test body runs on, for tests that assert they stayed on it.</summary>
    public static bool OnUiThread => SynchronizationContext.Current is WindowsFormsSynchronizationContext;

    private static void OnStaThread(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        })
        {
            IsBackground = true,
            Name = "ui-test",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    // Off screen rather than transparent: DrawToBitmap of a layered window comes back blank.
    private static Form Host() =>
        new()
        {
            ClientSize = new Size(HostWidth, HostHeight),
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-30000, -30000),
        };
}
