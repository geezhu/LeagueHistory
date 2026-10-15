using System.Media;
using League.Models;
using League.Services;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    // Players' new survival items play the item's own sound, from the main program like the respawn chimes, because
    // Windows mutes the widget while the game has focus.
    private readonly ItemSoundLibrary _itemSounds = new();
    private readonly SurvivalItemAlerts _survivalAlerts = new();
    private ControlSettings _controlSettings = ControlSettings.Default;
    private bool _readingControlSettings,
        _itemSoundsFetched;
    private SoundPlayer? _survivalPlayer;

    private void UpdateSurvivalSounds(LoadingRoster roster, LiveMatchInsight insight)
    {
        _ = ReadControlSettingsAsync(_generation);
        _ = FetchItemSoundsAsync();
        int own = roster.Players.FirstOrDefault(p => p.Self)?.Team ?? 0;
        // Allies, the player included, and enemies follow the widget's choice.
        var players = roster
            .Players.Where(p => p.ChampionId > 0 && own != 0)
            .Select(p =>
                insight.Players.GetValueOrDefault(p.Key) is { } live
                    ? new SurvivalInventory(
                        p.Key,
                        SurvivalItems.Find(live.Items).Select(item => SurvivalItems.BaseId(item.Id)).ToArray(),
                        p.Team == own ? _controlSettings.AllySound : _controlSettings.EnemySound
                    )
                    : null
            )
            .OfType<SurvivalInventory>();
        if (_survivalAlerts.Update(players) is { } alert)
        {
            var player = roster.Players.First(p => p.Key == alert.Key);
            string item = FormMain.Globals.resLoading?.GetItemById(alert.Item)?.Name ?? alert.Item.ToString();
            // Each sound is logged with whose purchase it was, so a sound nobody expected can be traced.
            ChatSendDiagnostics.Shared.Report(
                "item-sound",
                $"保命提示音：{(player.Team == own ? "我方" : "敌方")} {Champion(player.ChampionId)} 做出 {item}"
            );
            _ = PlayItemSoundAsync(alert.Item);
        }
    }

    private async Task ReadControlSettingsAsync(int generation)
    {
        if (_readingControlSettings || _disposed || !UseGameBar)
        {
            return;
        }

        _readingControlSettings = true;
        try
        {
            var settings = await _gameBar.ReadControlSettingsAsync();
            if (settings == null || _disposed || generation != _generation)
            {
                return;
            }

            _controlSettings = settings;
        }
        finally
        {
            _readingControlSettings = false;
        }
    }

    // The sounds are small; fetching them once early keeps the first alert from waiting for the Wiki.
    private async Task FetchItemSoundsAsync()
    {
        if (_itemSoundsFetched || _disposed || !(_controlSettings.EnemySound || _controlSettings.AllySound))
        {
            return;
        }

        _itemSoundsFetched = true;
        foreach (int item in ItemSoundLibrary.Files.Keys)
        {
            await LoadItemSoundAsync(item);
        }
    }

    private async Task PlayItemSoundAsync(int item)
    {
        if (await LoadItemSoundAsync(item) is not { } wave || _disposed)
        {
            return;
        }

        // SoundPlayer.Play returns at once and plays on its own thread; a new sound replaces one still playing.
        var player = new SoundPlayer(new MemoryStream(wave));
        _survivalPlayer?.Dispose();
        _survivalPlayer = player;
        player.Play();
    }

    private async Task<byte[]?> LoadItemSoundAsync(int item)
    {
        try
        {
            return await Task.Run(() => _itemSounds.LoadAsync(item, _life.Token));
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested)
        {
            // Shutting down; nothing to play.
            return null;
        }
        catch (Exception e)
            when (e
                    is HttpRequestException
                        or TaskCanceledException
                        or IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or ArgumentException
            )
        {
            // A missing sound only silences this alert; the next one tries the Wiki again.
            ChatSendDiagnostics.Shared.Report("item-sound", $"{item} {e.GetType().Name} {e.Message}");
            return null;
        }
    }
}
