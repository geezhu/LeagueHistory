using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed partial class AramMetaView
{
    private readonly HttpClient _imageHttp;
    private readonly SemaphoreSlim _imageGate = new(4, 4);
    private readonly Dictionary<string, Image> _images = new();
    private readonly Dictionary<string, Task<Image?>> _imageLoads = new();

    private Task<Image?> ImageAsync(string? url, string kind = "", int id = 0)
    {
        // ID-based recommendations use the existing game resources without a public URL.
        string? fallback = null;
        if (
            !string.IsNullOrWhiteSpace(url)
            && Uri.TryCreate(new Uri("https://arammeta.com/"), url, out var uri)
            && uri.Scheme == "https"
            && new[] { "arammeta.com", "ddragon.leagueoflegends.com" }.Contains(uri.Host)
        )
        {
            fallback = uri.AbsoluteUri;
        }

        bool local = id > 0 && kind is "champion" or "item" or "spell" or "augment";
        if (!local && fallback == null)
        {
            return Task.FromResult<Image?>(null);
        }

        string key = local ? $"{kind}:{id}" : fallback!;
        if (_images.TryGetValue(key, out var cached))
        {
            return Task.FromResult<Image?>(cached);
        }

        if (_imageLoads.TryGetValue(key, out var pending))
        {
            return pending;
        }

        return _imageLoads[key] = FetchImageAsync(key, fallback, kind, id);
    }

    private async Task<Image?> FetchImageAsync(string key, string? url, string kind, int id)
    {
        await _imageGate.WaitAsync();
        try
        {
            if (IsDisposed)
            {
                return null;
            }

            var resources = FormMain.Globals.resLoading;
            Image? image = null;
            if (resources != null)
            {
                try
                {
                    // Disk hits still decode bitmaps and draw augment frames; do that off the UI thread.
                    image = await Task.Run(async () =>
                        kind switch
                        {
                            "champion" => (await resources.GetChampionInfoAsync(id)).image,
                            "item" => (await resources.GetItemInfoAsync(id)).image,
                            "spell" => (await resources.GetSpellInfoAsync(id)).image,
                            "augment" => (await resources.GetAugmentInfoAsync(id)).image,
                            _ => null,
                        }
                    );
                }
                catch
                { /* Missing client resources fall back to the public image cache. */
                }
            }
            if (IsDisposed)
            {
                image?.Dispose();
                return null;
            }
            if (image == null && url != null)
            {
                var filename =
                    Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url))
                    ) + ".png";
                image = await Task.Run(() =>
                    new League.Infrastructure.ImageCacheService(_imageHttp).GetOrDownloadImageAsync(
                        url,
                        "arammeta",
                        filename
                    )
                );
            }
            if (IsDisposed)
            {
                image?.Dispose();
                return null;
            }
            if (image != null)
            {
                _images[key] = image;
            }

            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            _imageGate.Release();
        }
    }

    private async Task SetGridImageAsync(DataGridViewRow row, AramMetaChampion champion, CancellationToken token)
    {
        try
        {
            var image = await ImageAsync((string?)champion.Data["image"], "champion", champion.Id).WaitAsync(token);
            if (image != null && !IsDisposed && !token.IsCancellationRequested && row.DataGridView == _grid)
            {
                row.Cells[0].Value = image;
            }
        }
        catch (OperationCanceledException) { }
    }

    private PictureBox Icon(
        Control parent,
        string? url,
        string tooltip,
        CancellationToken token,
        int size = 32,
        string kind = "",
        int id = 0
    )
    {
        var picture = new PictureBox
        {
            Size = new Size(size, size),
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(2),
            BackColor = Color.FromArgb(233, 239, 245),
        };
        parent.Controls.Add(picture);
        _tips.SetToolTip(picture, tooltip);
        _ = Load();
        return picture;
        async Task Load()
        {
            try
            {
                var image = await ImageAsync(url, kind, id).WaitAsync(token);
                if (!picture.IsDisposed && !token.IsCancellationRequested)
                {
                    picture.Image = image;
                }
            }
            catch (OperationCanceledException) { }
        }
    }

    private FlowLayoutPanel Section(string title)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 10),
        };
        panel.Controls.Add(
            new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 8),
            }
        );
        _detail.Controls.Add(panel);
        ResizeSections();
        return panel;
    }

    private void ResizeSections()
    {
        int width = Math.Max(360, _detail.ClientSize.Width - 32);
        foreach (Control section in _detail.Controls)
        {
            section.MinimumSize = new Size(width, 0);
            section.MaximumSize = new Size(width, 0);
            section.Width = width;
            foreach (Control child in section.Controls)
            {
                if (child is FlowLayoutPanel || child is Label)
                {
                    child.MaximumSize = new Size(width - 24, 0);
                }
            }

            foreach (var table in section.Controls.OfType<TableLayoutPanel>())
            {
                table.Width = Math.Max(510, width - 24);
            }

            ResizeComparison(section, width - 24);
        }
    }

    private FlowLayoutPanel Row(Control parent) => AddRow(parent);

    private static FlowLayoutPanel AddRow(Control parent)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 2, 0, 2),
            MaximumSize = new Size(Math.Max(330, parent.Width - 24), 0),
        };
        parent.Controls.Add(row);
        return row;
    }

    private Label Text(string text, Color? color = null) =>
        new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = color ?? Color.FromArgb(43, 65, 83),
            Margin = new Padding(6, 6, 6, 4),
        };
}
