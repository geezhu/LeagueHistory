using System.Diagnostics;

namespace League.Infrastructure
{
    public class ImageCacheService
    {
        public HttpClient _client;
        private readonly string _baseCacheDir;

        public ImageCacheService(HttpClient client, string baseCacheDir = "Assets")
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _baseCacheDir = baseCacheDir;
        }

        public void UpdateClient(HttpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        // A bounded pool also coordinates different cache-service instances using the same files.
        private static readonly SemaphoreSlim[] CacheLocks = Enumerable
            .Range(0, 128)
            .Select(_ => new SemaphoreSlim(1, 1))
            .ToArray();

        public async Task<Image?> GetOrDownloadImageAsync(
            string iconPath,
            string cacheSubDir,
            string fileName,
            int? idForNaming = null
        )
        {
            string cacheDir = Path.Combine(Application.StartupPath, _baseCacheDir, cacheSubDir);
            string cachePath = Path.GetFullPath(
                Path.Combine(cacheDir, idForNaming.HasValue ? $"{idForNaming}.png" : fileName)
            );
            var gate = CacheLocks[
                (int)((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(cachePath) % (uint)CacheLocks.Length)
            ];
            await gate.WaitAsync();
            try
            {
                if (File.Exists(cachePath))
                {
                    try
                    {
                        await using var input = File.OpenRead(cachePath);
                        using var source = Image.FromStream(input);
                        return new Bitmap(source);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"图片缓存不可读 {cachePath}: {ex.Message}");
                    }
                }
                try
                {
                    using var response = await _client.GetAsync(iconPath);
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    await using var input = await response.Content.ReadAsStreamAsync();
                    using var source = Image.FromStream(input);
                    var image = new Bitmap(source); // Independent of the response stream's lifetime.
                    try
                    {
                        Directory.CreateDirectory(cacheDir);
                        await SaveImageToCacheAsync(image, cachePath);
                    }
                    catch (Exception ex)
                    {
                        // A disk-cache failure must not discard a successfully downloaded image.
                        Debug.WriteLine($"图片缓存写入失败 {cachePath}: {ex.Message}");
                    }
                    return image;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"图片加载失败 {iconPath}: {ex.Message}");
                    return null;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task SaveImageToCacheAsync(Image image, string cachePath)
        {
            string tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";

            try
            {
                int retry = 0;
                while (retry < 3)
                {
                    try
                    {
                        await using var fs = new FileStream(
                            tempPath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            4096,
                            true
                        );
                        await Task.Run(() => image.Save(fs, System.Drawing.Imaging.ImageFormat.Png));
                        await fs.FlushAsync();
                        break;
                    }
                    catch (IOException) when (retry < 2)
                    {
                        retry++;
                        await Task.Delay(100 * retry);
                    }
                }

                File.Move(tempPath, cachePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    TryDelete(tempPath);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch { }
        }
    }
}
