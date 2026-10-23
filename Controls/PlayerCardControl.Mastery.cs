using League.Clients;
using League.Models;
using League.Services;

namespace League.Controls;

/// <summary>One champion of the mastery row: its icon, owned by the card once shown, and the hover text.</summary>
public sealed record MasteryIcon(int ChampionId, string ToolTip, Image? Icon);

/// <summary>
/// The champions the player has the most mastery points on, as a row of round icons between the ranks and the champion
/// bar (upstream 3d379ba). The summoner level is already on the avatar.
/// </summary>
public partial class PlayerCardControl
{
    private const int MasteryCount = 8;
    private readonly MasteryStrip _masteryStrip = new() { Visible = false };
    private string _masteryPuuid = "";

    public int MasteryCountShown => _masteryStrip.Count;

    /// <summary>Shows the icons, replacing and disposing the previous ones; an empty list hides the row.</summary>
    public void ShowMasteries(IReadOnlyList<MasteryIcon> masteries)
    {
        if (IsDisposed)
        {
            foreach (var mastery in masteries)
            {
                mastery.Icon?.Dispose();
            }

            return;
        }

        _masteryStrip.Show(masteries);
        _masteryStrip.Visible = masteries.Count > 0;
        ReflowSummaryHeight();
    }

    // Reloaded only when the card moves to another player; refreshes of the same player keep the row.
    private void LoadMasteryForPlayer()
    {
        if (CurrentPuuId == _masteryPuuid)
        {
            return;
        }

        _masteryPuuid = CurrentPuuId;
        ShowMasteries([]);
        if (CurrentPuuId.Length > 0)
        {
            _ = LoadMasteryAsync(CurrentPuuId);
        }
    }

    private async Task LoadMasteryAsync(string puuid)
    {
        var icons = new List<MasteryIcon>();
        try
        {
            var page = await ChampionMasteryService.Shared.GetAsync(puuid, CancellationToken.None);
            foreach (var mastery in page.Top(MasteryCount))
            {
                // The loader's icon is shared with every other view; the card keeps a copy of its own.
                var shared = await FormMain.Globals.resLoading.GetChampionIconAsync(mastery.ChampionId);
                string name =
                    FormMain.Globals.resLoading.GetChampionById(mastery.ChampionId)?.Name
                    ?? $"英雄 {mastery.ChampionId}";
                icons.Add(new(mastery.ChampionId, name + "\n" + mastery.Description, shared?.Clone() as Image));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The row is a hint; without it the card still shows the record.
            AppLog.Shared.Warn(LogCategory.Ui, "Room mastery", ex);
        }

        // A card that moved on to another player keeps that player's row.
        if (IsDisposed || CurrentPuuId != puuid)
        {
            icons.ForEach(icon => icon.Icon?.Dispose());
            return;
        }

        ShowMasteries(icons);
    }

    private sealed class MasteryStrip : Control
    {
        private const int IconSize = 22;
        private const int Gap = 4;
        private readonly ToolTip _tips = new();
        private MasteryIcon[] _icons = [];
        private int _hover = -1;

        public MasteryStrip()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint,
                true
            );
            Height = IconSize;
        }

        public int Count => _icons.Length;

        public void Show(IReadOnlyList<MasteryIcon> icons)
        {
            Release();
            _icons = icons.ToArray();
            _hover = -1;
            _tips.SetToolTip(this, null);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var placeholder = new SolidBrush(Theme.ControlFill);
            for (int i = 0; i < _icons.Length; i++)
            {
                var bounds = Bounds(i);
                if (bounds.Right > Width)
                {
                    break;
                }

                if (_icons[i].Icon is { } icon)
                {
                    RoundImage.Draw(e.Graphics, icon, bounds);
                }
                else
                {
                    e.Graphics.FillEllipse(placeholder, bounds);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = Enumerable.Range(0, _icons.Length).FirstOrDefault(i => Bounds(i).Contains(e.Location), -1);
            if (index != _hover)
            {
                _hover = index;
                _tips.SetToolTip(this, index >= 0 ? _icons[index].ToolTip : null);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Release();
                _tips.Dispose();
            }

            base.Dispose(disposing);
        }

        private static Rectangle Bounds(int index) => new(index * (IconSize + Gap), 0, IconSize, IconSize);

        private void Release()
        {
            foreach (var icon in _icons)
            {
                icon.Icon?.Dispose();
            }

            _icons = [];
        }
    }
}
