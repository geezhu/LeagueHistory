using League.Models;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    // The augment page's records, read one poll behind the snapshot they are counted against.
    private AugmentSelection[] _augmentSelections = [];
    private IReadOnlyDictionary<string, double> _ultimateHaste = new Dictionary<string, double>();
    private bool _readingAugments;

    // Selections name roster players; an unnamed one is the widget's own player. Takedown augments are counted from
    // the events; a player the snapshot cannot match keeps the anchor's stacks and is marked incomplete. The other
    // augments carry the user's count, or none.
    private LiveAugmentStack[] CountAugmentStacks(LoadingRoster roster, LiveMatchSnapshot snapshot)
    {
        string self = roster.Players.FirstOrDefault(p => p.Self)?.Key ?? "";
        return _augmentSelections
            .Select(selection => selection.Player.Length > 0 ? selection : selection with { Player = self })
            .Where(selection => selection.Player.Length > 0)
            .Select(selection =>
            {
                var kind = AugmentCatalog.Get(selection.Augment).Kind;
                if (kind != AugmentKind.Takedowns)
                {
                    int stacks = kind == AugmentKind.Manual ? selection.Anchor.Stacks : 0;
                    return new LiveAugmentStack(
                        selection,
                        new(stacks, 0, 0, true, kind == AugmentKind.Manual ? "手动记录的层数。" : "固定加成。")
                    );
                }

                var player = roster.Players.FirstOrDefault(p => p.Key == selection.Player);
                string live = player != null ? snapshot.Find(player)?.Key ?? "" : "";
                return new LiveAugmentStack(
                    selection,
                    AugmentStacks.Count(snapshot, live, selection.Augment, selection.Anchor)
                );
            })
            .ToArray();
    }

    private Dictionary<string, AugmentEffects> SumAugmentEffects(LoadingRoster roster, LiveAugmentStack[] stacks) =>
        roster
            .Players.Where(player => player.ChampionId > 0)
            .ToDictionary(
                player => player.Key,
                player =>
                    AugmentEffects.From(
                        stacks.Where(stack => stack.Selection.Player == player.Key),
                        _ultimateHaste.GetValueOrDefault(player.Key)
                    )
            );

    private async Task ReadAugmentSelectionsAsync(int generation)
    {
        if (_readingAugments || _disposed || !UseGameBar)
        {
            return;
        }

        _readingAugments = true;
        try
        {
            var selections = await _gameBar.ReadAugmentSelectionsAsync();
            var ultimateHaste = await _gameBar.ReadUltimateHasteAsync();
            // A missing or half-written file keeps the last values; the widget rewrites it on every change.
            if (!_disposed && generation == _generation)
            {
                _augmentSelections = selections ?? _augmentSelections;
                _ultimateHaste = ultimateHaste ?? _ultimateHaste;
            }
        }
        finally
        {
            _readingAugments = false;
        }
    }
}
