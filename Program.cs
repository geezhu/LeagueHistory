using League.Infrastructure;
using System.Diagnostics;

namespace League
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            // 用 Trace，Release 也会生效；不要用 Debug.Listeners
            Trace.Listeners.Add(new DebugFileTraceListener());
            Trace.AutoFlush = true;
            Trace.WriteLine("[Program] 软件启动");

            Application.Run(new FormMain());
        }
    }
}