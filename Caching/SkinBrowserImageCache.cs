namespace League.Caching
{
    /// <summary>
    /// 皮肤浏览器专用：图片只解码一次，由缓存统一释放。
    /// </summary>
    public sealed class SkinBrowserImageCache : IDisposable
    {
        private readonly Dictionary<string, Image> _map = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public Image? GetOrLoad(string key, Func<Image?> loader)
        {
            if (_disposed)
            {
                return null;
            }

            if (_map.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var img = loader();
            if (img == null)
            {
                return null;
            }

            _map[key] = img;
            return img;
        }

        public Image? Get(string key) => _map.TryGetValue(key, out var img) ? img : null;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var img in _map.Values)
            {
                try
                {
                    img.Dispose();
                }
                catch { }
            }
            _map.Clear();
        }

        public static Image? LoadScaledFromFile(string path, int maxW, int maxH)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using var fs = File.OpenRead(path);
                using var src = Image.FromStream(fs);
                return Scale(src, maxW, maxH);
            }
            catch
            {
                return null;
            }
        }

        public static Image Scale(Image src, int maxW, int maxH)
        {
            if (src.Width <= maxW && src.Height <= maxH)
            {
                return new Bitmap(src);
            }

            double ratio = Math.Min(maxW / (double)src.Width, maxH / (double)src.Height);
            int w = Math.Max(1, (int)(src.Width * ratio));
            int h = Math.Max(1, (int)(src.Height * ratio));
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
            return bmp;
        }
    }
}
