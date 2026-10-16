using System.Text;

namespace League.Services;

/// <summary>
/// 后台追加写日志：调用方只入队，不等磁盘；队列有上限，写入跟不上时丢弃最旧的记录。
/// 文件超过大小上限时改名为 .previous 再写新文件。日志是可选的，写不进去只记下原因，不影响业务。
/// </summary>
public sealed class LogFileWriter
{
    private readonly object _gate = new();
    private readonly Queue<string> _pending = new();
    private readonly Func<string> _path;
    private readonly long _maxBytes;
    private readonly int _maxPending;
    private Task _writer = Task.CompletedTask;
    private bool _writing;

    public LogFileWriter(string path, long maxBytes, int maxPending)
        : this(() => path, maxBytes, maxPending) { }

    /// <param name="path">每次写入时取路径，按天分文件的日志可以在跨天后自动换文件。</param>
    public LogFileWriter(Func<string> path, long maxBytes, int maxPending)
    {
        _path = path;
        _maxBytes = maxBytes;
        _maxPending = maxPending;
    }

    public string CurrentPath => _path();

    /// <summary>最近一次写入失败的原因；成功写入后清空。</summary>
    public string? LastError { get; private set; }

    public void Write(string line)
    {
        lock (_gate)
        {
            if (_pending.Count >= _maxPending)
            {
                _pending.Dequeue();
            }

            _pending.Enqueue(SingleLine(line));
            if (!_writing)
            {
                _writing = true;
                _writer = Task.Run(WritePending);
            }
        }
    }

    /// <summary>
    /// 崩溃处理用：进程马上要退出，后台写入可能来不及执行，所以先写完队列里的记录再同步写这一行。
    /// </summary>
    public void WriteNow(string line)
    {
        string[] lines;
        lock (_gate)
        {
            lines = _pending.Append(SingleLine(line)).ToArray();
            _pending.Clear();
        }
        Append(lines);
    }

    public async Task FlushAsync()
    {
        while (true)
        {
            Task writer;
            lock (_gate)
            {
                writer = _writer;
            }

            await writer;
            lock (_gate)
            {
                if (!_writing)
                {
                    return;
                }
            }
        }
    }

    private static string SingleLine(string line) => line.Replace('\r', ' ').Replace('\n', ' ');

    private void WritePending()
    {
        while (true)
        {
            string[] lines;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    _writing = false;
                    return;
                }
                lines = _pending.ToArray();
                _pending.Clear();
            }
            Append(lines);
        }
    }

    private void Append(string[] lines)
    {
        if (lines.Length == 0)
        {
            return;
        }

        try
        {
            string path = _path();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > _maxBytes)
            {
                File.Move(path, path + ".previous", true);
            }

            File.AppendAllLines(path, lines, Encoding.UTF8);
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 日志被占用或目录不可写时放弃这批记录，不能打断调用方的业务。
            LastError = "日志保存失败：" + ex.GetType().Name;
        }
    }
}
