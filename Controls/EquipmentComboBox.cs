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

    private static readonly Color DarkBack = Color.FromArgb(16, 45, 57);
    private static readonly Color DarkInk = Color.FromArgb(215, 225, 226);
    private bool _light;

    /// <summary>
    /// The ARAM guide sits on the light theme while the champion-select assistant keeps the dark one. Light mode also
    /// draws a choice's numbers in the guide's statistics cell instead of two lines of text.
    /// </summary>
    public bool Light
    {
        get => _light;
        set
        {
            _light = value;
            BackColor = value ? Theme.Surface : DarkBack;
            ForeColor = value ? Theme.Ink : DarkInk;
            ItemHeight = LogicalToDeviceUnits(value ? 44 : 34);
            Invalidate();
        }
    }

    protected override Color FrameColor => _light ? Theme.DividerStrong : base.FrameColor;

    protected override Color FocusFrameColor => _light ? Theme.Accent : base.FocusFrameColor;

    protected override Color ArrowColor => _light ? Theme.Muted : base.ArrowColor;

    protected override Color DisabledArrowColor => _light ? Theme.Divider : base.DisabledArrowColor;

    public EquipmentComboBox(JObject[] options, CancellationToken token, string kind = "item")
    {
        _options = options;
        _kind = kind;
        _images = CancellationTokenSource.CreateLinkedTokenSource(token);
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = LogicalToDeviceUnits(34);
        DropDownWidth = LogicalToDeviceUnits(440);
        MaxDropDownItems = 8;
        IntegralHeight = false;
        DropDownHeight = LogicalToDeviceUnits(280);
        BackColor = DarkBack;
        ForeColor = DarkInk;
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
                AppLog.Shared.Warn(LogCategory.Ui, "Selection icon", ex);
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
        var selectedBack = _light ? Theme.AccentSoft : Color.FromArgb(23, 82, 95);
        using var background = new SolidBrush(selected ? selectedBack : BackColor);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (e.Index < 0 || e.Index >= _options.Length)
        {
            TextRenderer.DrawText(e.Graphics, "暂无数据", Font, e.Bounds, ForeColor);
            return;
        }
        var option = _options[e.Index];
        int x = DrawChoiceIcons(e, OpggItemSetService.Ids(option));
        DrawChoiceRates(e, option, x);
    }

    // Draws the choice's icons from the left and returns where its numbers may start.
    private int DrawChoiceIcons(DrawItemEventArgs e, int[] ids)
    {
        int iconSize = LogicalToDeviceUnits(_light ? 32 : 26),
            space = LogicalToDeviceUnits(_light ? 36 : 29),
            maxIcons = Math.Max(1, (e.Bounds.Width - LogicalToDeviceUnits(65)) / space);
        int x = e.Bounds.Left + LogicalToDeviceUnits(_light ? 6 : 4),
            y = _light ? e.Bounds.Top + (e.Bounds.Height - iconSize) / 2 : e.Bounds.Top + LogicalToDeviceUnits(4);
        foreach (int id in ids.Take(maxIcons))
        {
            var rect = new Rectangle(x, y, iconSize, iconSize);
            if (_icons.TryGetValue(id, out var icon))
            {
                e.Graphics.DrawImage(icon, rect);
            }
            else
            {
                using var fallback = new SolidBrush(_light ? Theme.ControlFill : Color.FromArgb(38, 64, 75));
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
            TextRenderer.DrawText(
                e.Graphics,
                "+",
                Font,
                new Point(x - LogicalToDeviceUnits(5), y + LogicalToDeviceUnits(6)),
                ForeColor
            );
        }
        return x;
    }

    private void DrawChoiceRates(DrawItemEventArgs e, JObject option, int x)
    {
        double? win = OpggBuild.Ratio(option["win"], option["play"]),
            pick = (double?)option["pick_rate"];
        if (_light && e.Bounds.Width > LogicalToDeviceUnits(300))
        {
            int cellWidth = LogicalToDeviceUnits(OpggStatCell.CellWidth),
                cellHeight = LogicalToDeviceUnits(OpggStatCell.CellHeight);
            var cell = new Rectangle(
                Math.Max(x + LogicalToDeviceUnits(12), e.Bounds.Right - cellWidth - LogicalToDeviceUnits(8)),
                e.Bounds.Top + (e.Bounds.Height - cellHeight) / 2,
                cellWidth,
                cellHeight
            );
            OpggStatCell.Draw(e.Graphics, cell, Font, win, (long?)option["play"], pick);
            return;
        }
        var text = new Rectangle(
            x + LogicalToDeviceUnits(4),
            e.Bounds.Top + 1,
            Math.Max(0, e.Bounds.Right - x - LogicalToDeviceUnits(6)),
            e.Bounds.Height - 2
        );
        string rates =
            $"胜 {(win.HasValue ? win.Value.ToString("P1") : "—")}\n选 {(pick.HasValue ? pick.Value.ToString("P1") : "—")}";
        if (e.Bounds.Width > LogicalToDeviceUnits(300))
        {
            text.X = Math.Max(x + LogicalToDeviceUnits(12), e.Bounds.Right - LogicalToDeviceUnits(255));
            text.Width = e.Bounds.Right - text.X - LogicalToDeviceUnits(8);
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
