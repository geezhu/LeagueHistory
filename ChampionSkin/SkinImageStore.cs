using System.Collections.Concurrent;
using System.Drawing.Imaging;
using League.Models;
using League.Services;

namespace League.ChampionSkin
{
    public enum SkinImageKind
    {
        /// <summary>The square tile the client's skin picker shows.</summary>
        Tile,

        /// <summary>The full splash art.</summary>
        Splash,
    }

    /// <summary>
    /// Downloads skin images from the client and keeps them as JPEG files: tiles at most 256 pixels on the long edge
    /// (the existing Assets/skins cache), splash art at most 1280 in %LOCALAPPDATA%\League\Cache\skin-splashes. Each
    /// file is downloaded once at a time and written atomically; at most two downloads run together.
    /// </summary>
    public sealed class SkinImageStore
    {
        private const long JpegQuality = 85L;
        private const int MinimumBytes = 256;

        private readonly Func<HttpClient?> _client;
        private readonly string _tileDirectory;
        private readonly string _splashDirectory;
        private readonly SemaphoreSlim _downloads = new(2, 2);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);

        public SkinImageStore(Func<HttpClient?> client, string? tileDirectory = null, string? splashDirectory = null)
        {
            _client = client;
            _tileDirectory = tileDirectory ?? Path.Combine(Application.StartupPath, "Assets", "skins");
            _splashDirectory =
                splashDirectory
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "League",
                    "Cache",
                    "skin-splashes"
                );
        }

        /// <summary>The cached file for the skin, downloading it first; null when no source answered.</summary>
        public async Task<string?> EnsureAsync(
            int championId,
            SkinThumbItem skin,
            SkinImageKind kind,
            CancellationToken cancellationToken
        )
        {
            string path = FilePath(skin.SkinId, kind);
            if (IsValid(path))
            {
                return path;
            }

            var fileLock = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
            await fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Another request may have written the file while this one waited.
                if (IsValid(path))
                {
                    return path;
                }

                var http = _client();
                if (http == null)
                {
                    return null;
                }

                await _downloads.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await DownloadAsync(
                            http,
                            Sources(championId, skin, kind),
                            path,
                            MaxEdge(kind),
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                        ? path
                        : null;
                }
                finally
                {
                    _downloads.Release();
                }
            }
            finally
            {
                fileLock.Release();
            }
        }

        public string FilePath(int skinId, SkinImageKind kind) =>
            Path.Combine(kind == SkinImageKind.Tile ? _tileDirectory : _splashDirectory, $"{skinId}.jpg");

        private static int MaxEdge(SkinImageKind kind) => kind == SkinImageKind.Tile ? 256 : 1280;

        /// <summary>Candidate client paths in order: the inventory's own path first, then the standard locations.</summary>
        public static List<string> Sources(int championId, SkinThumbItem skin, SkinImageKind kind)
        {
            var paths = new List<string>();
            string? own = kind == SkinImageKind.Tile ? skin.TilePath : skin.SplashPath;
            if (!string.IsNullOrWhiteSpace(own))
            {
                paths.Add(own.StartsWith('/') ? own : "/" + own);
            }

            if (kind == SkinImageKind.Tile)
            {
                paths.Add($"/lol-game-data/assets/v1/champion-tiles/{championId}/{skin.SkinId}.jpg");
            }
            else
            {
                paths.Add($"/lol-game-data/assets/v1/champion-splashes/{championId}/{skin.SkinId}.jpg");
                paths.Add($"/lol-game-data/assets/v1/champion-splashes/uncentered/{championId}/{skin.SkinId}.jpg");
            }

            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static async Task<bool> DownloadAsync(
            HttpClient http,
            List<string> sources,
            string path,
            int maxEdge,
            CancellationToken cancellationToken
        )
        {
            foreach (string source in sources)
            {
                byte[]? bytes = await TryGetAsync(http, source, cancellationToken).ConfigureAwait(false);
                if (bytes != null && Save(bytes, path, maxEdge))
                {
                    return true;
                }
            }

            AppLog.Shared.Warn(
                LogCategory.Data,
                $"皮肤浏览：图片读取失败 {Path.GetFileName(path)}（{sources.Count} 个地址）"
            );
            return false;
        }

        private static async Task<byte[]?> TryGetAsync(
            HttpClient http,
            string path,
            CancellationToken cancellationToken
        )
        {
            try
            {
                using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                return bytes.Length >= MinimumBytes ? bytes : null;
            }
            catch (HttpRequestException)
            {
                // 单个地址失败（404、断线）时换下一个候选地址；全部失败时由调用方记录。
                return null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The request timed out: try the next source.
                return null;
            }
        }

        /// <summary>解码 → 缩边 → 转 24bpp → JPEG 写入临时文件后替换。</summary>
        private static bool Save(byte[] bytes, string path, int maxEdge)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var input = new MemoryStream(bytes))
                using (var source = Image.FromStream(input))
                using (var scaled = Scale(source, maxEdge))
                {
                    var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    using var parameters = new EncoderParameters(1);
                    parameters.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
                    scaled.Save(temporary, codec, parameters);
                }

                File.Move(temporary, path, overwrite: true);
                return IsValid(path);
            }
            catch (Exception e)
                when (e
                        is ArgumentException
                            or IOException
                            or UnauthorizedAccessException
                            or InvalidOperationException
                            or System.Runtime.InteropServices.ExternalException
                )
            {
                AppLog.Shared.Warn(LogCategory.Data, $"皮肤浏览：图片写入失败 {Path.GetFileName(path)}", e);
                TryDelete(temporary);
                return false;
            }
        }

        // JPEG needs 24-bit pixels; transparent edges become black like the client's own background.
        private static Bitmap Scale(Image source, int maxEdge)
        {
            double ratio = Math.Min(1, Math.Min((double)maxEdge / source.Width, (double)maxEdge / source.Height));
            int width = Math.Max(1, (int)Math.Round(source.Width * ratio));
            int height = Math.Max(1, (int)Math.Round(source.Height * ratio));
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, 0, 0, width, height);
            return bitmap;
        }

        public static bool IsValid(string path)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length >= MinimumBytes;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temporary file only wastes space; the next download writes a new one.
                AppLog.Shared.Warn(LogCategory.Data, "皮肤浏览：临时文件删除失败", e);
            }
        }
    }
}
