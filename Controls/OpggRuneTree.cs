using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using League.Infrastructure;
using League.Models;

namespace League.Controls;

internal sealed class OpggRuneTree : Control
{
    private sealed record Node(int Id, Rectangle Bounds, bool Selected, string Name, string Description, string Path);

    private readonly List<Node> _nodes = new();
    private readonly Dictionary<int, Image?> _images = new();
    private readonly ToolTip _tips = new();
    private int _hover = -1;
    private string? _notice;

    public OpggRuneTree()
    {
        Size = new Size(420, 222);
        Margin = Padding.Empty;
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
            true
        );
    }

    internal static bool IsSelected(OpggRunes plan, int id, int shardRow = -1) =>
        shardRow >= 0 ? plan.Perks.Length >= 9 && plan.Perks[6 + shardRow] == id : plan.Perks.Take(6).Contains(id);

    public async Task SetPlanAsync(OpggRunes plan, RuneResourceLoader? resources, CancellationToken token)
    {
        _nodes.Clear();
        _hover = -1;
        _tips.SetToolTip(this, null);
        void Add(int id, int x, int y, int size, bool selected, string? name = null, string? path = null)
        {
            var rune = resources?.GetById(id);
            _nodes.Add(
                new(
                    id,
                    new Rectangle(x, y, size, size),
                    selected,
                    name ?? rune?.name ?? $"符文 {id}",
                    rune?.longDesc ?? "",
                    path ?? rune?.iconPath ?? ""
                )
            );
        }
        var primary = resources?.Styles.GetValueOrDefault(plan.Primary);
        var secondary = resources?.Styles.GetValueOrDefault(plan.Secondary);
        _notice = primary == null || secondary == null ? "符文树资源暂不可用，显示当前方案" : null;
        if (primary != null && secondary != null)
        {
            Add(primary.Id, 65, 4, 30, true, primary.Name, primary.IconPath);
            Add(secondary.Id, 237, 4, 30, true, secondary.Name, secondary.IconPath);
            for (int row = 0; row < Math.Min(4, primary.Slots.Length); row++)
            {
                var ids = primary.Slots[row];
                int size = row == 0 ? 36 : 28,
                    step = row == 0 ? 39 : 38;
                for (int col = 0; col < ids.Length; col++)
                    Add(
                        ids[col],
                        (166 - ids.Length * step) / 2 + col * step,
                        46 + row * 40,
                        size,
                        IsSelected(plan, ids[col])
                    );
            }
            for (int row = 1; row < Math.Min(4, secondary.Slots.Length); row++)
            {
                var ids = secondary.Slots[row];
                for (int col = 0; col < ids.Length; col++)
                    Add(ids[col], 190 + col * 36, 46 + row * 40, 28, IsSelected(plan, ids[col]));
            }
            for (int row = 0; row < Math.Min(3, primary.Slots.Length - 4); row++)
            for (int col = 0; col < primary.Slots[row + 4].Length; col++)
            {
                int id = primary.Slots[row + 4][col];
                Add(id, 314 + col * 33, 88 + row * 40, 24, IsSelected(plan, id, row));
            }
        }
        else
            for (int i = 0; i < plan.Perks.Length; i++)
                Add(plan.Perks[i], 20 + (i % 5) * 60, 65 + (i / 5) * 50, 32, true);
        Invalidate();
        foreach (var node in _nodes.ToArray())
        {
            token.ThrowIfCancellationRequested();
            if (resources == null || _images.ContainsKey(node.Id))
                continue;
            var task = resources.GetIconAsync(node.Path);
            Image? image;
            try
            {
                image = await task.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                _ = task.ContinueWith(
                    t =>
                    {
                        if (t.Status == TaskStatus.RanToCompletion)
                            t.Result?.Dispose();
                    },
                    TaskScheduler.Default
                );
                throw;
            }
            if (IsDisposed || token.IsCancellationRequested)
            {
                image?.Dispose();
                token.ThrowIfCancellationRequested();
                return;
            }
            _images[node.Id] = image;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (_notice != null)
            TextRenderer.DrawText(e.Graphics, _notice, Font, new Rectangle(8, 8, 400, 35), Color.SlateGray);
        foreach (var node in _nodes)
        {
            var rect = node.Bounds;
            if (_images.GetValueOrDefault(node.Id) is { } image)
            {
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(new ColorMatrix { Matrix33 = node.Selected ? 1f : 0.15f });
                using var path = new GraphicsPath();
                path.AddEllipse(rect);
                var state = e.Graphics.Save();
                e.Graphics.SetClip(path);
                e.Graphics.DrawImage(image, rect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                e.Graphics.Restore(state);
            }
            else
            {
                using var brush = new SolidBrush(
                    node.Selected ? Color.FromArgb(175, 195, 210) : Color.FromArgb(228, 233, 237)
                );
                e.Graphics.FillEllipse(brush, rect);
            }
            if (node.Selected)
            {
                using var pen = new Pen(Color.FromArgb(173, 138, 63), 1);
                e.Graphics.DrawEllipse(pen, rect);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = _nodes.FindIndex(n => n.Bounds.Contains(e.Location));
        if (index == _hover)
            return;
        _hover = index;
        _tips.SetToolTip(this, index < 0 ? null : TooltipText.Format(_nodes[index].Name, _nodes[index].Description));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tips.Dispose();
            foreach (var image in _images.Values)
                image?.Dispose();
            _images.Clear();
        }
        base.Dispose(disposing);
    }
}
