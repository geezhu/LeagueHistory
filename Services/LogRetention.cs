namespace League.Services;

/// <summary>
/// 日志目录的保留规则：超过 7 天的删除；剩下的总大小超过 20 MB 时从最旧的开始删。
/// 只处理日志目录顶层的日志文件，不进子目录。
/// </summary>
public static class LogRetention
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    public const long MaxTotalBytes = 20L * 1024 * 1024;

    private static readonly string[] Patterns = { "*.log", "*.log.previous", "*.txt" };

    public static string LogsDirectory { get; } = Path.Combine(AppDataPaths.LocalData, "League", "Logs");

    /// <summary>返回删除的文件数。正在被别的进程写的文件删不掉时跳过，下次再清。</summary>
    public static int Clean(
        string directory,
        DateTime nowUtc,
        TimeSpan? maxAge = null,
        long maxTotalBytes = MaxTotalBytes
    )
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var files = Patterns
            .SelectMany(pattern => new DirectoryInfo(directory).EnumerateFiles(pattern, SearchOption.TopDirectoryOnly))
            .DistinctBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();

        int deleted = 0;
        DateTime oldest = nowUtc - (maxAge ?? MaxAge);
        foreach (var file in files.Where(f => f.LastWriteTimeUtc < oldest).ToList())
        {
            if (TryDelete(file))
            {
                files.Remove(file);
                deleted++;
            }
        }

        long total = files.Sum(f => f.Length);
        foreach (var file in files.ToList())
        {
            if (total <= maxTotalBytes)
            {
                break;
            }

            // Delete() invalidates the cached size, so read it first.
            long size = file.Length;
            if (TryDelete(file))
            {
                total -= size;
                deleted++;
            }
        }
        return deleted;
    }

    /// <summary>启动时在后台清理，不占用界面线程，失败也不影响程序启动。</summary>
    public static void CleanInBackground() =>
        _ = Task.Run(() =>
        {
            try
            {
                Clean(LogsDirectory, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 日志目录不可读时下次启动再清。
            }
        });

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 文件正被写入或没有权限，跳过。
            return false;
        }
    }
}
