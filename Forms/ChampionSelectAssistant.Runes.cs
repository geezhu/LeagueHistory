using League.Models;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    internal static string[] RuneHighlights(OpggRunes scheme, OpggRunes[] schemes)
    {
        bool Valid(double? value) => value is double n && double.IsFinite(n) && n >= 0 && n <= 1;
        var labels = new List<string>();
        foreach (
            var (name, value, values) in new[]
            {
                ("胜率最高", scheme.WinRate, schemes.Select(s => s.WinRate)),
                ("使用率最高", scheme.PickRate, schemes.Select(s => s.PickRate)),
            }
        )
        {
            var available = values.Where(Valid).Select(v => v!.Value).ToArray();
            if (!Valid(value) || available.Length == 0)
            {
                continue;
            }

            double maximum = available.Max();
            if (Math.Abs(value!.Value - maximum) < 1e-9)
            {
                labels.Add(name + (available.Count(v => Math.Abs(v - maximum) < 1e-9) > 1 ? "（并列）" : ""));
            }
        }
        return labels.ToArray();
    }

    private void AddRuneChoices(OpggRunes[] schemes, CancellationToken token)
    {
        if (schemes.Length == 0)
        {
            return;
        }
        var (section, select, icons) = CreateRuneChoices(schemes, token);
        CancellationTokenSource? images = null;
        Task imageWork = Task.CompletedTask;
        void RetireImages()
        {
            var retired = images;
            images = null;
            if (retired != null)
            {
                retired.Cancel();
                _ = ReleaseRuneImagesAsync(retired, imageWork);
            }
        }
        section.Disposed += (_, _) => RetireImages();
        void RefreshChoice()
        {
            if (token.IsCancellationRequested || section.IsDisposed || select.SelectedIndex < 0)
            {
                return;
            }
            RetireImages();
            images = CancellationTokenSource.CreateLinkedTokenSource(token);
            foreach (Control child in icons.Controls.Cast<Control>().ToArray())
            {
                child.Dispose();
            }
            _runeChoice = select.SelectedIndex;
            _runes = schemes[_runeChoice];
            UpdateRuneChoiceText(select, _runes, schemes);
            _apply.Enabled = !_applying;
            _apply.Text = $"应用方案 {_runeChoice + 1}";
            imageWork = LoadRuneIconsAsync(_runes, icons, images.Token);
        }
        select.SelectedIndexChanged += (_, _) => RefreshChoice();
        RefreshChoice();
    }

    // The selector already shows each scheme's rates, so no separate statistics line is added.
    private (FlowLayoutPanel Section, ComboBox Select, FlowLayoutPanel Icons) CreateRuneChoices(
        OpggRunes[] schemes,
        CancellationToken token
    )
    {
        var section = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = Padding.Empty,
        };
        var select = new Controls.RuneSchemeComboBox(schemes, scheme => RuneHighlights(scheme, schemes), token)
        {
            Width = ContentWidth - Dip(6),
        };
        PopulateRuneChoices(select, schemes);
        select.SelectedIndex = 0;
        var icons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = Padding.Empty,
        };
        icons.Margin = DipPadding(0, 4, 0, 2);
        section.Controls.AddRange(new Control[] { select, icons });
        _body.Controls.Add(section);
        return (section, select, icons);
    }

    private static void PopulateRuneChoices(ComboBox select, OpggRunes[] schemes)
    {
        foreach (var (scheme, index) in schemes.Select((scheme, index) => (scheme, index)))
        {
            var rune = FormMain.Globals.resLoading?.GetRuneById(scheme.Perks[0]);
            var name = rune?.name ?? rune?.Name ?? "推荐符文";
            var primary = FormMain.Globals.resLoading?.Runes.Styles.GetValueOrDefault(scheme.Primary)?.Name ?? "主系";
            var highlights = RuneHighlights(scheme, schemes);
            select.Items.Add(
                $"方案 {index + 1} · {primary} · {name}"
                    + (highlights.Length > 0 ? " · " + string.Join(" / ", highlights) : "")
            );
        }
    }

    private void UpdateRuneChoiceText(ComboBox select, OpggRunes scheme, OpggRunes[] schemes)
    {
        var highlights = RuneHighlights(scheme, schemes);
        var recommendation = string.Join(
            "\n",
            highlights.Select(label =>
                label.StartsWith("胜率")
                    ? label + "：当前返回方案中获胜比例最高。"
                    : label + "：当前返回方案中采用比例最高。"
            )
        );
        _tips.SetToolTip(
            select,
            recommendation
                + "\n"
                + $"仅比较当前英雄、模式、分路和段位下返回的方案。\n本方案样本：{(scheme.Plays.HasValue ? scheme.Plays.Value.ToString("N0") + " 场" : "未知")}。样本量较小时胜率可能波动较大。"
        );
    }

    private async Task LoadRuneIconsAsync(OpggRunes scheme, FlowLayoutPanel icons, CancellationToken token)
    {
        try
        {
            // Keystone, primary and secondary runes; the three stat shards add noise without a choice.
            await IconRow(scheme.Perks.Take(6), "rune", token, icons);
        }
        catch (OperationCanceledException)
        {
            // Changing a scheme or closing its section retires this icon batch.
        }
        catch (Exception error)
        {
            // Missing optional icons leave the selector and apply action usable.
            System.Diagnostics.Debug.WriteLine($"Rune icons: {error}");
        }
    }

    private static async Task ReleaseRuneImagesAsync(CancellationTokenSource source, Task work)
    {
        try
        {
            await work.ConfigureAwait(false);
        }
        finally
        {
            source.Dispose();
        }
    }
}
