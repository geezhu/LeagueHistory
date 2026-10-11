using League.Models;

namespace League.Controls;

internal sealed class RuneSchemeComboBox : DarkSelectionComboBox
{
    private readonly OpggRunes[] _schemes;
    private readonly Func<OpggRunes, string[]> _highlights;
    private readonly Dictionary<int, Image> _icons = new();
    private readonly CancellationTokenSource _images;

    public RuneSchemeComboBox(OpggRunes[] schemes, Func<OpggRunes, string[]> highlights, CancellationToken token)
    {
        _schemes = schemes;
        _highlights = highlights;
        _images = CancellationTokenSource.CreateLinkedTokenSource(token);
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 38;
        DropDownWidth = 420;
        IntegralHeight = false;
        DropDownHeight = 304;
        BackColor = Color.FromArgb(16, 45, 57);
        ForeColor = Color.FromArgb(215, 225, 226);
        _ = LoadIconsAsync();
    }

    private async Task LoadIconsAsync()
    {
        foreach (int id in _schemes.Select(s => s.Perks[0]).Distinct())
        {
            if (_images.IsCancellationRequested)
                return;
            try
            {
                var info = await FormMain.Globals.resLoading.GetRuneInfoAsync(id);
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
            catch
            {
                if (IsDisposed || _images.IsCancellationRequested)
                    return;
            }
        }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        using var fill = new SolidBrush(
            (e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(23, 82, 95) : BackColor
        );
        e.Graphics.FillRectangle(fill, e.Bounds);
        if (e.Index < 0 || e.Index >= _schemes.Length)
            return;
        var scheme = _schemes[e.Index];
        int id = scheme.Perks[0];
        var icon = new Rectangle(e.Bounds.Left + 5, e.Bounds.Top + 3, 32, 32);
        if (_icons.TryGetValue(id, out var image))
            e.Graphics.DrawImage(image, icon);
        else
            TextRenderer.DrawText(
                e.Graphics,
                "◆",
                Font,
                icon,
                Color.FromArgb(208, 185, 130),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
        var rune = FormMain.Globals.resLoading.GetRuneById(id);
        string name = rune?.name ?? rune?.Name ?? "推荐符文";
        string[] highlights = _highlights(scheme);
        string title = $"{name} · {e.Index + 1}";
        string subtitle = highlights.Length > 0 ? string.Join(" / ", highlights) : "可选方案";
        if (e.Bounds.Width > 300)
        {
            string Rate(double? n) => n.HasValue ? n.Value.ToString("P1") : "—";
            subtitle = $"胜率 {Rate(scheme.WinRate)} · 使用率 {Rate(scheme.PickRate)}";
            if (highlights.Length > 0)
                title += " · " + string.Join(" / ", highlights);
        }
        var text = new Rectangle(e.Bounds.Left + 44, e.Bounds.Top + 1, Math.Max(0, e.Bounds.Width - 48), 19);
        TextRenderer.DrawText(
            e.Graphics,
            title,
            Font,
            text,
            ForeColor,
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine
        );
        text.Y += 18;
        TextRenderer.DrawText(
            e.Graphics,
            subtitle,
            Font,
            text,
            Color.FromArgb(208, 185, 130),
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            _images.Cancel();
            _images.Dispose();
            foreach (var icon in _icons.Values)
                icon.Dispose();
            _icons.Clear();
        }
        base.Dispose(disposing);
    }
}
