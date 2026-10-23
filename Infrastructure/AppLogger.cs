using System.Diagnostics;
using System.Text;

namespace League.Infrastructure
{
    public sealed class DebugFileTraceListener : TraceListener
    {
        private static readonly object _lock = new();
        private readonly string _logDir;

        public DebugFileTraceListener()
        {
            _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log");
            Directory.CreateDirectory(_logDir);
        }

        public override void Write(string? message) => WriteLine(message);

        public override void WriteLine(string? message)
        {
            if (string.IsNullOrEmpty(message)) return;

            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
            string path = Path.Combine(_logDir, $"{DateTime.Now:yyyy-MM-dd}.log");

            lock (_lock)
            {
                try
                {
                    // 每次打开→写入→关闭，立刻可见
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
                catch { }
            }
        }
    }
}