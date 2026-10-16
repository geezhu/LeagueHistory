namespace League
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (
                args.Length == 1
                && args[0] is "--test-game-chat-once" or "--test-game-enter-once" or "--test-game-chat-short-once"
            )
            {
                // Command-line probe: no window or message loop exists yet, so blocking Main is safe.
                Environment.ExitCode = Services
                    .GameChatSendProbe.RunAsync(
                        args[0] == "--test-game-enter-once",
                        args[0] == "--test-game-chat-short-once"
                    )
                    .GetAwaiter()
                    .GetResult();
                return;
            }
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Services.LogRetention.CleanInBackground();
            Application.Run(new FormMain());
        }
    }
}
