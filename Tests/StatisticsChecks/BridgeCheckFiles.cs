internal static class BridgeCheckFiles
{
    public static async Task<string> ReadAsync(string path, string expectation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!File.Exists(path))
            {
                await Task.Delay(10, timeout.Token);
            }
            return await File.ReadAllTextAsync(path, timeout.Token);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"{expectation}: {Path.GetFileName(path)}", ex);
        }
    }
}
