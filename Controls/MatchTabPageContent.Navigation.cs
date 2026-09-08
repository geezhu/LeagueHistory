using League.Models;

namespace League.Controls;

public partial class MatchTabPageContent
{
    private Panel _profileSurface = null!, _contentSurface = null!, _historySurface = null!;
    private MatchDetailView? _detailView;
    private MatchListPanel? _selectedCard;
    private Button _backToProfile = null!;
    private Point _profileHistoryScroll;
    private ProfileMasteryView _masteryView = null!;
    private readonly Dictionary<string, RankedStats> _profileRanks = new();
    private Label _highestRank = null!;

    private void SetRankHistory(string name, RankedStats stats)
    {
        _profileRanks[name] = stats;
        if (Controls.Find(name.Replace("History", "Emblem"), true).FirstOrDefault() is RankEmblem emblem)
            emblem.SetTier(stats.Tier);
        var highest = _profileRanks.Values.OrderByDescending(rank => rank.HighestTierOrder).First();
        _highestRank.Text = highest.HighestTierOrder > 0 ? highest.HighestTierDisplay : "—";
        if (Controls.Find(name,true).FirstOrDefault() is Label label)
        {
            label.Text = string.Join("\n",new[] {
                stats.HighestTierDisplay == "—" ? "" : $"赛季最高  {stats.HighestTierDisplay}",
                stats.PreviousSeasonDisplay == "—" ? "" : $"上赛季结算  {stats.PreviousSeasonDisplay}"
            }.Where(text => text.Length > 0));
            label.Visible = false;
        }
    }

    private void ConfigureProfileNavigation(Control profile, Control toolbar)
    {
        _profileSurface = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _masteryView = new ProfileMasteryView { Dock = DockStyle.Top };
        _masteryView.ProfileLoaded += picChampionId.SetExperience;
        _profileSurface.Controls.Add(_masteryView);
        _profileSurface.Controls.Add(profile);
        _contentSurface = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0,0,12,0) };
        _contentSurface.Controls.Add(_profileSurface);
        _historySurface = new Panel { Dock = DockStyle.Right, Width = 300 };
        _backToProfile = new Button { Text = "← 返回玩家主页", Dock = DockStyle.Top, Height = 32, FlatStyle = FlatStyle.Flat, Visible = false };
        _backToProfile.Click += (_, _) => ReturnToProfile();
        _historySurface.Controls.Add(flowLayoutPanelRight);
        _historySurface.Controls.Add(toolbar);
        _historySurface.Controls.Add(_backToProfile);
        Controls.Add(_contentSurface);
        Controls.Add(_historySurface);
    }

    private void ConfigureCompactMatch(Control? control)
    {
        if (control is not MatchListPanel card) return;
        card.Compact = true;
        card.DetailsClicked -= OpenInlineMatch;
        card.DetailsClicked += OpenInlineMatch;
        ResizeMatchCards();
    }

    private void OpenInlineMatch(MatchInfo match)
    {
        var card = flowLayoutPanelRight.Controls.OfType<MatchListPanel>().FirstOrDefault(c => ReferenceEquals(c.MatchInfo, match));
        if (card == null || card == _selectedCard) return;
        if (match.AllParticipants == null || !match.AllParticipants.Any(p => (int?)p["teamId"] is 100 or 200))
        {
            MessageBox.Show("这场比赛暂不支持计分板详情。"); return;
        }
        if (_detailView == null) _profileHistoryScroll = new Point(0,-flowLayoutPanelRight.AutoScrollPosition.Y);
        _detailView?.Dispose();
        if (_selectedCard != null) { _selectedCard.SelectedMatch = false; _selectedCard.Invalidate(); }
        _selectedCard = card; card.SelectedMatch = true; card.Invalidate();
        _historySurface.Dock = DockStyle.Left; _historySurface.Width = 230;
        _contentSurface.Padding = new Padding(12,0,0,0);
        _profileSurface.Visible = false; _backToProfile.Visible = true;
        _detailView = new MatchDetailView(match) { Dock = DockStyle.Fill };
        _detailView.PlayerProfileRequested += async id =>
        {
            if (FindForm() is FormMain main) await main.OpenPlayerProfileAsync(id);
        };
        _detailView.ReplayRequested += () => card.RequestReplay();
        _contentSurface.Controls.Add(_detailView); _detailView.BringToFront();
        ResizeMatchCards();
    }

    private void ReturnToProfile()
    {
        _detailView?.Dispose(); _detailView = null;
        if (_selectedCard != null && !_selectedCard.IsDisposed) { _selectedCard.SelectedMatch = false; _selectedCard.Invalidate(); }
        _selectedCard = null;
        if (_historySurface == null || _historySurface.IsDisposed) return;
        _historySurface.Dock = DockStyle.Right; _historySurface.Width = 300;
        _contentSurface.Padding = new Padding(0,0,12,0);
        _profileSurface.Visible = true; _backToProfile.Visible = false;
        ResizeMatchCards();
        flowLayoutPanelRight.AutoScrollPosition = _profileHistoryScroll;
    }
}
