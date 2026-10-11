using System.Runtime.ExceptionServices;

/// <summary>Runs UI checks with the same persistent message loop and synchronization context as the application.</summary>
internal static class WinFormsCheckHost
{
    public static void Run(Action checks)
    {
        if (Application.MessageLoop)
        {
            checks();
            return;
        }
        ExceptionDispatchInfo? failure = null;
        using var host = new Form
        {
            ShowInTaskbar = false,
            Opacity = 0,
            Location = new Point(-30000, -30000),
            StartPosition = FormStartPosition.Manual,
        };
        host.Shown += (_, _) =>
        {
            try
            {
                checks();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                host.Close();
            }
        };
        Application.Run(host);
        failure?.Throw();
    }
}
