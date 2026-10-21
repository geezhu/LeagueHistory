using League.Controls;
using League.Models;
using League.Services;

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

    // Glyphs from Segoe Fluent Icons / MDL2 Assets: AreaChart, LightningBolt.
    private void ConfigureOpgg()
    {
        AddGuidePage("OP.GG 英雄攻略", new RailIcon("\uE9D2", "攻略"), new OpggView());
        AddGuidePage(
            "arammeta · 海克斯大乱斗",
            new RailIcon("\uE945", "大乱斗"),
            new AramMetaView(MainWindowAramMetaLifetime)
        );
    }

    private TimeSpan MainWindowAramMetaLifetime() =>
        TimeSpan.FromMinutes(Math.Clamp(_appConfig?.AramMetaCacheMinutes ?? 1440, 1, 10080));

    private void AddGuidePage(string title, RailIcon icon, Control content)
    {
        var page = new TabPage(title) { Tag = icon };
        page.Controls.Add(content);
        imageTabControl1.TabPages.Add(page);
    }
}
