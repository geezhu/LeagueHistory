using League.Controls;
using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League;

public partial class FormMain
{
    internal async void OpenAssistantGuide(int hero, int queue, string? position, OpggQuery? query = null)
    {
        try
        {
            await OpenAssistantGuideAsync(hero, queue, position, query);
        }
        catch (OperationCanceledException)
        {
            // Navigating or closing a guide retires its outstanding request.
        }
        catch (Exception error)
        {
            AppLog.Shared.Warn(LogCategory.Ui, "Assistant guide", error);
            if (!IsDisposed && !Disposing)
            {
                MessageBox.Show(this, "英雄攻略打开失败，请稍后重试。", "英雄攻略");
            }
        }
    }

    internal async Task OpenAssistantGuideAsync(int hero, int queue, string? position, OpggQuery? query = null)
    {
        if (IsDisposed || Disposing || hero <= 0)
        {
            return;
        }
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        if (queue == 2400)
        {
            imageTabControl1.SelectedIndex = 4;
            await imageTabControl1.TabPages[4].Controls.OfType<AramMetaView>().Single().OpenChampionAsync(hero);
        }
        else
        {
            imageTabControl1.SelectedIndex = 3;
            var view = imageTabControl1.TabPages[3].Controls.OfType<OpggView>().Single();
            if (query != null)
            {
                await view.OpenQueryAsync(query);
            }
            else
            {
                await view.OpenChampionAsync(hero, queue, position);
            }
        }
    }

    private void ConfigureOpgg()
    {
        AddGuidePage("OP.GG 英雄攻略", "OP\nGG", Color.FromArgb(48, 118, 218), 10, new OpggView());
        AddGuidePage(
            "arammeta · 海克斯大乱斗",
            "AM",
            Color.FromArgb(0, 145, 151),
            12,
            new AramMetaView(MainWindowAramMetaLifetime)
        );
        AddGuidePage(
            "当前对局",
            "对局",
            Color.FromArgb(16, 70, 85),
            10,
            new LiveMatchView(
                CurrentMatchQueueAsync,
                (hero, queue) => OpenAssistantGuide(hero, queue, "none"),
                MainWindowAramMetaLifetime
            )
        );
    }

    private TimeSpan MainWindowAramMetaLifetime() =>
        TimeSpan.FromMinutes(Math.Clamp(_appConfig?.AramMetaCacheMinutes ?? 1440, 1, 10080));

    private static async Task<int> CurrentMatchQueueAsync()
    {
        var client = Globals.lcuClient?.Client;
        if (client == null)
        {
            return 0;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var flow = JObject.Parse(await client.GetStringAsync("/lol-gameflow/v1/session", timeout.Token));
        return (int?)flow["gameData"]?["queue"]?["id"] ?? 0;
    }

    private void AddGuidePage(string title, string label, Color color, float fontSize, Control content)
    {
        var icon = new Bitmap(40, 40);
        using (var graphics = Graphics.FromImage(icon))
        using (var font = new Font("Microsoft YaHei UI", fontSize, FontStyle.Bold))
        {
            graphics.Clear(color);
            TextRenderer.DrawText(
                graphics,
                label,
                font,
                new Rectangle(0, 0, 40, 40),
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            );
        }
        var page = new TabPage(title) { Tag = icon };
        page.Controls.Add(content);
        page.Disposed += (_, _) => icon.Dispose();
        imageTabControl1.TabPages.Add(page);
    }
}
