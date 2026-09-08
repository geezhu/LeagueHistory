using League.Models;
using Newtonsoft.Json.Linq;

namespace League.Controls;

/// <summary>A player entry point; profile lookup and navigation belong to its host.</summary>
public sealed class PlayerNameLink : LinkLabel
{
    public string PlayerName { get; init; } = "";
    public string SummonerId { get; init; } = "";
    public Func<string, Task<JObject?>>? ResolveProfile { get; init; }
    public event Action<string>? ProfileRequested;
    private bool _loaded;

    public PlayerNameLink()
    {
        LinkBehavior = LinkBehavior.HoverUnderline;
        AutoEllipsis = true;
        BackColor = Color.Transparent;
        TextAlign = ContentAlignment.MiddleLeft;
    }

    protected override void OnLinkClicked(LinkLabelLinkClickedEventArgs e)
    {
        base.OnLinkClicked(e);
        if (!string.IsNullOrWhiteSpace(SummonerId) && SummonerId != "0") ProfileRequested?.Invoke(SummonerId);
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_loaded) return;
        _loaded = true;
        Text = PlayerName;
        bool canNavigate = !string.IsNullOrWhiteSpace(SummonerId) && SummonerId != "0";
        if (!canNavigate) { Links.Clear(); TabStop = false; return; }
        if (ResolveProfile == null) return;
        try
        {
            var profile = await ResolveProfile(SummonerId);
            if (IsDisposed || Disposing) return;
            League.UIHelpers.PlayerNamePresentation.Apply(this, PlayerName, PlayerPrivacy.IsPublic(profile));
            foreach (Label badge in Controls.OfType<Label>())
                badge.Click += (_, _) => ProfileRequested?.Invoke(SummonerId);
            AccessibleDescription = $"{PlayerName}，隐私：{PlayerPrivacy.Describe(profile)}，点击查看主页";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"玩家隐私查询失败: {ex.Message}");
        }
    }
}
