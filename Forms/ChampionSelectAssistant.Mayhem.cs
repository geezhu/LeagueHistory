using League.Models;
using Newtonsoft.Json.Linq;

namespace League;

internal sealed partial class ChampionSelectAssistant
{
    private void AddMayhemBrowser(
        AramMetaPage page,
        int hero,
        CancellationToken token,
        FlowLayoutPanel host,
        int initialTab = 0,
        int initialRarity = 0
    )
    {
        if (!page.Index.Champions.Any(c => c.Id == hero))
        {
            throw new InvalidDataException("该英雄暂无海克斯推荐。");
        }
        _apply.Text = "应用符文";
        _apply.Enabled = !_applying;
        var browser = new Controls.AssistantMayhemBrowser(
            page,
            hero,
            token,
            host,
            initialTab,
            initialRarity,
            _tips,
            ForeColor,
            lifetime => _am.GetAsync(hero, false, lifetime),
            ApplySpellsAsync
        );
        browser.WorkRetired += completion => TrackAssistantWork(completion);
        host.Disposed += (_, _) =>
        {
            browser.Dispose();
            TrackAssistantWork(browser.Completion);
        };
    }
}
