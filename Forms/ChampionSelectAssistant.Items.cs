using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    internal static Rectangle EquipmentEditorBounds(Rectangle client, Rectangle work, Size size)
    {
        int width = Math.Min(size.Width, work.Width),
            height = Math.Min(size.Height, work.Height);
        return new Rectangle(
            Math.Clamp(client.Left + (client.Width - width) / 2, work.Left, work.Right - width),
            Math.Clamp(client.Top + (client.Height - height) / 2, work.Top, work.Bottom - height),
            width,
            height
        );
    }

    private void PositionEquipmentEditor(Form editor, Size? minimum = null)
    {
        Rectangle work = Screen.FromControl(this).WorkingArea,
            client = work;
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("LeagueClientUx"))
        {
            using (process)
            {
                try
                {
                    var handle = process.MainWindowHandle;
                    if (
                        handle == IntPtr.Zero
                        || !IsWindowVisible(handle)
                        || IsIconic(handle)
                        || !GetWindowRect(handle, out var rect)
                    )
                    {
                        continue;
                    }

                    client = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                    work = Screen.FromHandle(handle).WorkingArea;
                    break;
                }
                catch
                { /* Fall back to the current monitor if the client exits. */
                }
            }
        }
        var minimumSize = minimum ?? Dip(530, 460);
        editor.MinimumSize = new Size(
            Math.Min(minimumSize.Width, work.Width),
            Math.Min(minimumSize.Height, work.Height)
        );
        editor.Bounds = EquipmentEditorBounds(client, work, editor.Size);
    }

    private string ItemOption(JObject option)
    {
        string names = string.Join(
            " → ",
            OpggItemSetService
                .Ids(option)
                .Select(id => FormMain.Globals.resLoading.GetItemById(id)?.Name ?? id.ToString())
        );
        double? win = OpggBuild.Ratio(option["win"], option["play"]),
            pick = (double?)option["pick_rate"];
        return $"{names} · 胜率 {(win.HasValue ? win.Value.ToString("P2") : "—")} / 使用率 {(pick.HasValue ? pick.Value.ToString("P2") : "—")} · {(long?)option["play"] ?? 0:N0} 场";
    }

    private void AddEquipmentChoices(OpggBuild build, CancellationToken token)
    {
        var cores = OpggItemSetService.Options(build.Data, "core_items");
        if (cores.Length == 0)
        {
            Line("暂无核心装备方案");
            return;
        }
        var section = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = Padding.Empty,
        };
        var select = new Controls.EquipmentComboBox(cores, token) { Width = Dip(190), Margin = DipPadding(0, 0, 4, 0) };
        var configure = new Button
        {
            Text = "配置出装",
            Width = Dip(82),
            Height = select.Height,
            FlatStyle = FlatStyle.Flat,
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(16, 45, 57),
            ForeColor = ForeColor,
        };
        configure.FlatAppearance.BorderColor = Color.FromArgb(42, 72, 83);
        configure.FlatAppearance.MouseOverBackColor = Color.FromArgb(23, 65, 78);
        configure.FlatAppearance.MouseDownBackColor = Color.FromArgb(23, 82, 95);
        section.Controls.AddRange(new Control[] { select, configure });
        section.SetFlowBreak(configure, true);
        _body.Controls.Add(section);
        int starterIndex = 0,
            bootsIndex = 0;
        void Refresh() => _tips.SetToolTip(select, ItemOption(cores[select.SelectedIndex]));
        select.SelectedIndexChanged += (_, _) =>
        {
            if (!token.IsCancellationRequested)
            {
                Refresh();
            }
        };
        Refresh();
        configure.Click += (_, _) =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            ShowEquipmentEditor(
                build,
                cores,
                select.SelectedIndex,
                starterIndex,
                bootsIndex,
                token,
                (core, starter, boots) =>
                {
                    if (token.IsCancellationRequested || select.IsDisposed)
                    {
                        return;
                    }

                    starterIndex = starter;
                    bootsIndex = boots;
                    select.SelectedIndex = core;
                }
            );
        };
    }

    private void ShowEquipmentEditor(
        OpggBuild build,
        JObject[] cores,
        int coreIndex,
        int starterIndex,
        int bootsIndex,
        CancellationToken token,
        Action<int, int, int> changed,
        string source = "OP.GG"
    )
    {
        using var editor = new Controls.AssistantEquipmentEditor(
            build,
            cores,
            coreIndex,
            starterIndex,
            bootsIndex,
            token,
            changed,
            source,
            this,
            _tips,
            ItemOption,
            form => PositionEquipmentEditor(form)
        );
        editor.ShowDialog(this);
    }
}
