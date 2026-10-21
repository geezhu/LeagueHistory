using League.Infrastructure;
using League.Models;

namespace League.Controls;

/// <summary>Owns scheme selection and previews; the containing page authorizes and applies a scheme.</summary>
internal sealed class OpggRuneRecommendations : FlowLayoutPanel
{
    private readonly OpggRunes[] _plans;
    private readonly Func<OpggRunes, CancellationToken, Task<Image?>> _readKeystone;
    private readonly Func<RuneResourceLoader?> _resources;
    private readonly Func<bool> _canApply;
    private readonly Func<OpggRunes, CancellationToken, Task> _applyPlan;
    private readonly Action<string> _showError;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _token;
    private readonly Panel _preview = new() { Size = new Size(420, 222), Margin = Padding.Empty };
    private readonly FlowLayoutPanel _choices = new()
    {
        Width = 254,
        AutoSize = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Margin = new Padding(12, 0, 0, 0),
    };
    private readonly Button _apply = new()
    {
        Text = "应用到工具专用符文页",
        Size = new Size(250, 34),
        Margin = new Padding(0, 6, 0, 0),
    };
    private readonly ToolTip _tips = new();
    private readonly List<Button> _buttons = new();
    private readonly HashSet<Task> _work = new();
    private int _selected;
    private int _previewRevision;
    private bool _applying;
    private bool _closing;
    private Task _cleanupWork = Task.CompletedTask;

    public OpggRuneRecommendations(
        OpggRunes[] plans,
        Func<OpggRunes, CancellationToken, Task<Image?>> readKeystone,
        Func<RuneResourceLoader?> resources,
        Func<bool> canApply,
        Func<OpggRunes, CancellationToken, Task> applyPlan,
        Action<string> showError,
        CancellationToken token
    )
    {
        _plans = plans;
        _readKeystone = readKeystone;
        _resources = resources;
        _canApply = canApply;
        _applyPlan = applyPlan;
        _showError = showError;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        _token = _lifetime.Token;
        AutoSize = true;
        WrapContents = true;
        Margin = new Padding(0, 2, 0, 2);
        MinimumSize = new Size(200, 0);
        Controls.AddRange(new Control[] { _preview, _choices });
        ButtonStyles.Primary(_apply);
        _apply.Click += async (_, _) => await Track(ApplySelectedAsync());
    }

    internal Task InitializeAsync() => Track(InitializeChoicesAsync());

    private async Task InitializeChoicesAsync()
    {
        await Task.Yield();
        for (int i = 0; i < _plans.Length; i++)
        {
            _token.ThrowIfCancellationRequested();
            await AddChoiceAsync(i);
        }
        _token.ThrowIfCancellationRequested();
        _choices.Controls.Add(_apply);
        await SelectPlanAsync(0);
        _token.ThrowIfCancellationRequested();
    }

    private async Task AddChoiceAsync(int index)
    {
        var plan = _plans[index];
        var button = new Button
        {
            Text = "",
            Size = new Size(250, 54),
            Tag = index,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 0, 4),
            AccessibleName = $"方案 {index + 1}，胜率 {Percent(plan.WinRate)}，选用率 {Percent(plan.PickRate)}",
        };
        _buttons.Add(button);
        _choices.Controls.Add(button);
        var image = await _readKeystone(plan, _token);
        if (_closing || _token.IsCancellationRequested)
        {
            image?.Dispose();
            _token.ThrowIfCancellationRequested();
            return;
        }
        button.Disposed += (_, _) => image?.Dispose();
        button.Paint += (_, e) => DrawChoice(e.Graphics, plan, image);
        _tips.SetToolTip(button, "中间为胜率和统计场次，右侧为选用率。点击切换符文方案。");
        button.Click += async (_, _) => await Track(SelectFromClickAsync(index));
    }

    private static string Percent(double? value) => value.HasValue ? $"{value:P1}" : "—";

    private void DrawChoice(Graphics graphics, OpggRunes plan, Image? icon)
    {
        if (icon != null)
        {
            graphics.DrawImage(icon, new Rectangle(8, 11, 32, 32));
        }
        TextRenderer.DrawText(
            graphics,
            Percent(plan.WinRate),
            Font,
            new Rectangle(46, 6, 110, 21),
            Color.FromArgb(45, 61, 75),
            TextFormatFlags.HorizontalCenter
        );
        TextRenderer.DrawText(
            graphics,
            $"{plan.Plays:N0} 场",
            Font,
            new Rectangle(46, 28, 110, 20),
            Color.Gray,
            TextFormatFlags.HorizontalCenter
        );
        TextRenderer.DrawText(
            graphics,
            Percent(plan.PickRate),
            Font,
            new Rectangle(158, 16, 85, 22),
            Theme.Heading,
            TextFormatFlags.HorizontalCenter
        );
    }

    private async Task SelectFromClickAsync(int index)
    {
        // Register click work before cache hits or host callbacks can close this component.
        await Task.Yield();
        try
        {
            await SelectPlanAsync(index);
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested)
        {
            // The containing page no longer owns this preview.
        }
        catch (Exception ex)
        {
            if (!_closing && !_token.IsCancellationRequested)
            {
                _showError("符文预览加载失败：" + ex.Message);
            }
        }
    }

    private async Task SelectPlanAsync(int index)
    {
        if (_closing || _token.IsCancellationRequested)
        {
            return;
        }
        _selected = index;
        int revision = ++_previewRevision;
        _apply.Enabled = false;
        UpdateSelectionColors(index);
        var tree = new OpggRuneTree();
        bool published = false;
        try
        {
            await tree.SetPlanAsync(_plans[index], _resources(), _token);
            if (revision != _previewRevision || _closing || _token.IsCancellationRequested)
            {
                return;
            }
            foreach (Control old in _preview.Controls.Cast<Control>().ToArray())
            {
                old.Dispose();
            }
            _preview.Controls.Add(tree);
            published = true;
            _apply.Enabled = !_applying;
        }
        finally
        {
            if (!published)
            {
                tree.Dispose();
            }
        }
    }

    private void UpdateSelectionColors(int index)
    {
        foreach (var button in _buttons)
        {
            bool active = (int)button.Tag! == index;
            button.BackColor = active ? Theme.AccentSoft : Theme.SurfaceSubtle;
            button.FlatAppearance.BorderColor = active ? Theme.Accent : Theme.Divider;
        }
    }

    private async Task ApplySelectedAsync()
    {
        await Task.Yield();
        if (_closing || _token.IsCancellationRequested || _applying || !_canApply())
        {
            return;
        }
        var plan = _plans[_selected];
        _applying = true;
        SetApplicationEnabled(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await _applyPlan(plan, timeout.Token);
            if (!_closing && !_token.IsCancellationRequested)
            {
                _apply.Text = "已应用到工具专用符文页";
            }
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested)
        {
            // Closing or replacing the page cancels application without showing a stale error dialog.
        }
        catch (Exception ex)
        {
            if (!_closing && !_token.IsCancellationRequested)
            {
                MessageBox.Show(
                    FindForm(),
                    ex is OperationCanceledException ? "请求超时，请在客户端确认是否生效。" : ex.Message,
                    "应用符文"
                );
            }
        }
        finally
        {
            _applying = false;
            if (!_closing && !_token.IsCancellationRequested)
            {
                SetApplicationEnabled(true);
            }
        }
    }

    private void SetApplicationEnabled(bool enabled)
    {
        _apply.Enabled = enabled;
        foreach (var button in _buttons)
        {
            button.Enabled = enabled;
        }
    }

    private Task Track(Task task)
    {
        lock (_work)
        {
            _work.Add(task);
        }
        _ = task.ContinueWith(
            completed =>
            {
                lock (_work)
                {
                    _work.Remove(completed);
                }
                _ = completed.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        return task;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closing)
        {
            _closing = true;
            Task[] pending;
            lock (_work)
            {
                pending = _work.ToArray();
            }
            var cancellation = _lifetime.CancelAsync();
            _cleanupWork = Task.WhenAll(pending.Append(cancellation))
                .ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        _lifetime.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
