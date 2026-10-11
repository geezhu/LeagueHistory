using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Controls;

internal sealed class EquipmentComboBox : DarkSelectionComboBox
{
    private readonly JObject[] _options;
    private readonly Dictionary<int, Image> _icons = new();
    private readonly CancellationTokenSource _images;
    private readonly Task _imageWork;
    private readonly string _kind;

    public EquipmentComboBox(JObject[] options, CancellationToken token, string kind = "item")
    {
        _options = options;
        _kind = kind;
        _images = CancellationTokenSource.CreateLinkedTokenSource(token);
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 34;
        DropDownWidth = 440;
        MaxDropDownItems = 8;
        IntegralHeight = false;
        DropDownHeight = 280;
        BackColor = Color.FromArgb(16, 45, 57);
        ForeColor = Color.FromArgb(215, 225, 226);
        Items.AddRange(
            options.Length > 0
                ? options
                    .Select(
                        (option, index) =>
                            $"方案 {index + 1}: "
                            + string.Join(
                                " / ",
                                OpggItemSetService
                                    .Ids(option)
                                    .Select(id =>
                                        (
                                            _kind == "spell"
                                                ? FormMain.Globals.resLoading.GetSpellById(id)?.Name
                                                : FormMain.Globals.resLoading.GetItemById(id)?.Name
                                        ) ?? id.ToString()
                                    )
                            )
                    )
                    .ToArray()
                : new[] { "暂无数据" }
        );
        Enabled = options.Length > 0;
        SelectedIndex = 0;
        _imageWork = LoadImagesAsync();
    }

    private async Task LoadImagesAsync()
    {
        foreach (int id in _options.SelectMany(OpggItemSetService.Ids).Distinct())
        {
            if (_images.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var info =
                    _kind == "spell"
                        ? await FormMain.Globals.resLoading.GetSpellInfoAsync(id)
                        : await FormMain.Globals.resLoading.GetItemInfoAsync(id);
                if (IsDisposed || _images.IsCancellationRequested)
                {
                    info.image?.Dispose();
                    return;
                }
                if (info.image != null)
                {
                    _icons[id] = info.image;
                    Invalidate();
                }
            }
            catch (Exception ex)
            {
                // A failed optional icon leaves its placeholder usable; continue loading the other choices.
                System.Diagnostics.Debug.WriteLine($"Selection icon: {ex.GetType().Name}");
                if (IsDisposed || _images.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? Color.FromArgb(23, 82, 95) : BackColor);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (e.Index < 0 || e.Index >= _options.Length)
        {
            TextRenderer.DrawText(e.Graphics, "暂无数据", Font, e.Bounds, ForeColor);
            return;
        }
        var option = _options[e.Index];
        int x = e.Bounds.Left + 4,
            y = e.Bounds.Top + 4;
        var ids = OpggItemSetService.Ids(option);
        int iconSize = 26,
            space = 29,
            maxIcons = Math.Max(1, (e.Bounds.Width - 65) / space);
        foreach (int id in ids.Take(maxIcons))
        {
            var rect = new Rectangle(x, y, iconSize, iconSize);
            if (_icons.TryGetValue(id, out var icon))
            {
                e.Graphics.DrawImage(icon, rect);
            }
            else
            {
                using var fallback = new SolidBrush(Color.FromArgb(38, 64, 75));
                e.Graphics.FillRectangle(fallback, rect);
                TextRenderer.DrawText(
                    e.Graphics,
                    "?",
                    Font,
                    rect,
                    ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                );
            }
            x += space;
        }
        if (ids.Length > maxIcons)
        {
            TextRenderer.DrawText(e.Graphics, "+", Font, new Point(x - 5, y + 6), ForeColor);
        }
        double? win = OpggBuild.Ratio(option["win"], option["play"]),
            pick = (double?)option["pick_rate"];
        var text = new Rectangle(x + 4, e.Bounds.Top + 1, Math.Max(0, e.Bounds.Right - x - 6), e.Bounds.Height - 2);
        string rates =
            $"胜 {(win.HasValue ? win.Value.ToString("P1") : "—")}\n选 {(pick.HasValue ? pick.Value.ToString("P1") : "—")}";
        if (e.Bounds.Width > 300)
        {
            text.X = Math.Max(x + 12, e.Bounds.Right - 255);
            text.Width = e.Bounds.Right - text.X - 8;
            rates =
                $"胜率 {(win.HasValue ? win.Value.ToString("P1") : "—")}  ·  选取率 {(pick.HasValue ? pick.Value.ToString("P1") : "—")}\n{((long?)option["play"])?.ToString("N0") ?? "—"} 场";
        }
        TextRenderer.DrawText(
            e.Graphics,
            rates,
            Font,
            text,
            ForeColor,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            // Keep the linked source alive while the current resource load and cancellation callbacks finish.
            var cancellation = _images.CancelAsync();
            _ = Task.WhenAll(_imageWork, cancellation)
                .ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        _images.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            foreach (var image in _icons.Values)
            {
                image.Dispose();
            }

            _icons.Clear();
        }
        base.Dispose(disposing);
    }
}
