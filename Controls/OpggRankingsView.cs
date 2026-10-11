using System.Drawing.Drawing2D;
using League.Models;

namespace League.Controls;

internal sealed class OpggRankingsView : UserControl
{
    private const int RankColumn = 0;
    private const int ChampionColumn = 1;
    private const int TierColumn = 2;
    private const int WinRateColumn = 3;
    private const int PickRateColumn = 4;
    private const int BanRateColumn = 5;
    private const int CountersColumn = 6;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BackgroundColor = Color.FromArgb(246, 248, 251),
        BorderStyle = BorderStyle.None,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        ColumnHeadersHeight = 42,
        EnableHeadersVisualStyles = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
    };
    private readonly Dictionary<int, Image?> _icons = new();
    private readonly HashSet<int> _pending = new();
    private readonly SemaphoreSlim _imageGate = new(4, 4);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _imageWork = new();
    private readonly Font _textFont = new("Microsoft YaHei UI", 9f);
    private bool _closing;
    private Task _cleanupWork = Task.CompletedTask;
    private Func<int, Task<Image?>> _iconLoader = id =>
        Task.Run(
            () => FormMain.Globals.resLoading?.GetChampionIconAsync(id) ?? Task.FromResult<Image?>(null),
            CancellationToken.None
        );
    private OpggTier[] _rows = Array.Empty<OpggTier>();
    private int _sortColumn = RankColumn;
    private bool _descending;
    public event Action<OpggTier>? ChampionRequested;
    public bool HasRows => _rows.Length > 0;

    private static string ChampionName(int id) =>
        FormMain.Globals.resLoading?.GetChampionById(id)?.Name ?? $"英雄 {id}";

    private static string Rate(double? value) => value is double rate ? $"{rate:P2}" : "—";

    public OpggRankingsView()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(12, 0, 12, 12);
        ConfigureGridStyle();
        ConfigureColumns();
        BindGridEvents();
        _grid.Cursor = Cursors.Hand;
        Controls.Add(_grid);
    }

    private void ConfigureGridStyle()
    {
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            Font = _textFont,
            ForeColor = Color.FromArgb(40, 60, 80),
            BackColor = Color.White,
            SelectionBackColor = Color.FromArgb(218, 238, 251),
            SelectionForeColor = Color.FromArgb(27, 75, 106),
            Alignment = DataGridViewContentAlignment.MiddleCenter,
        };
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(238, 243, 249),
            ForeColor = Color.FromArgb(85, 106, 129),
            Font = _textFont,
        };
        _grid.GridColor = Color.FromArgb(231, 237, 244);
        _grid.RowTemplate.Height = 52;
    }

    private void ConfigureColumns()
    {
        foreach (
            var (title, weight, min) in new[]
            {
                ("排名", 45, 45),
                ("英雄", 200, 175),
                ("梯队", 65, 55),
                ("胜率", 100, 80),
                ("登场率", 100, 80),
                ("禁用率", 100, 80),
                ("劣势对局", 130, 112),
            }
        )
        {
            _grid.Columns.Add(
                new DataGridViewTextBoxColumn
                {
                    HeaderText = title,
                    FillWeight = weight,
                    MinimumWidth = min,
                    SortMode = DataGridViewColumnSortMode.Programmatic,
                }
            );
        }
    }

    private void BindGridEvents()
    {
        _grid.CellPainting += PaintCell;
        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].Tag is OpggTier row)
            {
                ChampionRequested?.Invoke(row);
            }
        };
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _grid.CurrentRow?.Tag is OpggTier row)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ChampionRequested?.Invoke(row);
            }
        };
        _grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.ColumnIndex == CountersColumn)
            {
                return;
            }

            _descending =
                _sortColumn == e.ColumnIndex
                    ? !_descending
                    : e.ColumnIndex is WinRateColumn or PickRateColumn or BanRateColumn;
            _sortColumn = e.ColumnIndex;
            FillRows();
        };
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].Tag is OpggTier row)
            {
                e.ToolTipText =
                    e.ColumnIndex == CountersColumn
                        ? "劣势对局："
                            + (
                                row.Counters.Length > 0
                                    ? string.Join("、", row.Counters.Select(ChampionName))
                                    : "暂无数据"
                            )
                        : "点击查看 " + ChampionName(row.ChampionId) + " 的攻略";
            }
        };
    }

    public void ClearRows()
    {
        _rows = Array.Empty<OpggTier>();
        _grid.Rows.Clear();
    }

    public void SetRows(OpggTier[] rows)
    {
        _rows = rows;
        _sortColumn = RankColumn;
        _descending = false;
        FillRows();
    }

    private void FillRows()
    {
        var sorted =
            _sortColumn == ChampionColumn
                ? (
                    _descending
                        ? _rows.OrderByDescending(r => ChampionName(r.ChampionId))
                        : _rows.OrderBy(r => ChampionName(r.ChampionId))
                )
            : _descending ? _rows.OrderByDescending(Value)
            : _rows.OrderBy(Value);
        double Value(OpggTier r) =>
            _sortColumn switch
            {
                TierColumn => r.Tier ?? 99,
                WinRateColumn => r.WinRate ?? -1,
                PickRateColumn => r.PickRate ?? -1,
                BanRateColumn => r.BanRate ?? -1,
                _ => r.Rank ?? 9999,
            };
        _grid.Rows.Clear();
        foreach (var row in sorted)
        {
            int index = _grid.Rows.Add(
                row.Rank?.ToString() ?? "—",
                ChampionName(row.ChampionId),
                row.Tier?.ToString() ?? "—",
                Rate(row.WinRate),
                Rate(row.PickRate),
                Rate(row.BanRate),
                ""
            );
            _grid.Rows[index].Tag = row;
        }
        foreach (DataGridViewColumn col in _grid.Columns)
        {
            col.HeaderCell.SortGlyphDirection =
                col.Index == _sortColumn ? (_descending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
        }

        _grid.ClearSelection();
    }

    private void PaintCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (
            e.RowIndex < 0
            || _grid.Rows[e.RowIndex].Tag is not OpggTier row
            || e.ColumnIndex is not (ChampionColumn or TierColumn or CountersColumn)
        )
        {
            return;
        }

        e.PaintBackground(e.ClipBounds, true);
        var g = e.Graphics!;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (e.ColumnIndex == ChampionColumn)
        {
            DrawHero(g, row.ChampionId, new Rectangle(e.CellBounds.X + 10, e.CellBounds.Y + 9, 34, 34));
            TextRenderer.DrawText(
                g,
                ChampionName(row.ChampionId),
                _grid.Font,
                new Rectangle(e.CellBounds.X + 52, e.CellBounds.Y, e.CellBounds.Width - 56, e.CellBounds.Height),
                e.CellStyle!.ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            );
        }
        else if (e.ColumnIndex == TierColumn)
        {
            var color = row.Tier switch
            {
                1 => Color.FromArgb(0, 160, 206),
                2 => Color.FromArgb(49, 171, 149),
                3 => Color.FromArgb(91, 139, 202),
                _ => Color.FromArgb(136, 146, 165),
            };
            int x = e.CellBounds.X + (e.CellBounds.Width - 24) / 2,
                y = e.CellBounds.Y + 12;
            using var brush = new SolidBrush(color);
            g.FillPolygon(
                brush,
                new[]
                {
                    new Point(x, y),
                    new Point(x + 24, y),
                    new Point(x + 24, y + 24),
                    new Point(x + 12, y + 30),
                    new Point(x, y + 24),
                }
            );
            TextRenderer.DrawText(
                g,
                row.Tier?.ToString() ?? "—",
                _grid.Font,
                new Rectangle(x, y, 24, 25),
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
        }
        else if (row.Counters.Length == 0)
        {
            TextRenderer.DrawText(
                g,
                "—",
                _grid.Font,
                e.CellBounds,
                Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
        }
        else
        {
            for (int i = 0; i < row.Counters.Length; i++)
            {
                DrawHero(g, row.Counters[i], new Rectangle(e.CellBounds.X + 8 + i * 30, e.CellBounds.Y + 13, 26, 26));
            }
        }

        e.Handled = true;
    }

    private void DrawHero(Graphics g, int id, Rectangle rect)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(rect);
        var state = g.Save();
        g.SetClip(path);
        if (_icons.TryGetValue(id, out var icon) && icon != null)
        {
            g.DrawImage(icon, rect);
        }
        else
        {
            using var brush = new SolidBrush(Color.FromArgb(219, 229, 241));
            g.FillEllipse(brush, rect);
        }
        g.Restore(state);
        if (!_closing && !_icons.ContainsKey(id) && _pending.Add(id))
        {
            _ = LoadIconAsync(id);
        }
    }

    private Task LoadIconAsync(int id)
    {
        if (_closing)
        {
            return Task.CompletedTask;
        }
        var work = LoadIconCoreAsync(id, _lifetime.Token);
        lock (_imageWork)
        {
            _imageWork.Add(work);
        }
        _ = work.ContinueWith(
            completed =>
            {
                lock (_imageWork)
                {
                    _imageWork.Remove(completed);
                }
                _ = completed.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        return work;
    }

    private async Task LoadIconCoreAsync(int id, CancellationToken token)
    {
        await Task.Yield();
        bool entered = false;
        Image? image = null;
        try
        {
            await _imageGate.WaitAsync(token);
            entered = true;
            image = await _iconLoader(id);

            if (_closing || token.IsCancellationRequested)
            {
                image?.Dispose();
                return;
            }
            _icons[id] = image;
            _grid.Invalidate();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Closing cancels queued gate waiters; in-flight borrowed resource tasks retain their own result cleanup.
            image?.Dispose();
        }
        catch (Exception ex)
        {
            image?.Dispose();
            System.Diagnostics.Debug.WriteLine($"OP.GG ranking icon: {ex.GetType().Name}");
            if (!_closing)
            {
                _icons[id] = null;
            }
        }
        finally
        {
            _pending.Remove(id);
            if (entered)
            {
                _imageGate.Release();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            Task[] pending;
            lock (_imageWork)
            {
                pending = _imageWork.ToArray();
            }
            var cancellation = _lifetime.CancelAsync();
            _cleanupWork = Task.WhenAll(pending.Append(cancellation))
                .ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        _lifetime.Dispose();
                        _imageGate.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            foreach (var icon in _icons.Values)
            {
                icon?.Dispose();
            }

            _icons.Clear();
            _textFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
