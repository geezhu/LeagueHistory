using League.Models;
using League.Services;
using Newtonsoft.Json.Linq;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    // Control skills per champion, read once from the champion data in Chinese and kept across games.
    private readonly Dictionary<int, ControlSkill[]> _controls = new();
    private readonly Dictionary<int, long> _controlRetry = new();
    private readonly HashSet<int> _controlRequests = new();
    private readonly Dictionary<int, SpellCooldown[]> _cooldowns = new();
    private readonly Dictionary<int, (ControlSkill[] Controls, ChampionSpellFacts Facts, JObject Champion)> _spells =
        new();
    private readonly ChampionSpellData _spellData = new();

    // ARAM, Mayhem and other modes change some slows and cooldowns; 2999's mode wins over the queue's once a game runs.
    private string _liveMode = "",
        _controlsMode = "";

    private string CurrentMode() => GameModes.Of(_liveMode, _roster?.Queue ?? 0);

    // A game in another mode merges the slows again; the client's control skills do not depend on it.
    private void RefreshModeControls()
    {
        string mode = CurrentMode();
        if (mode == _controlsMode)
        {
            return;
        }

        _controlsMode = mode;
        foreach (var (id, spell) in _spells)
        {
            _controls[id] = ChampionSlowSkills.Merge(spell.Controls, spell.Facts.SlowsFor(mode), spell.Champion);
        }
    }

    private Dictionary<int, SpellCooldown[]> ModeCooldowns()
    {
        string mode = CurrentMode();
        return _cooldowns.ToDictionary(entry => entry.Key, entry => entry.Value.Select(c => c.ForMode(mode)).ToArray());
    }

    /// <summary>Starts reading the control skills of roster champions that have none yet.</summary>
    private void RequestControls()
    {
        if (!UseGameBar || _roster == null)
        {
            return;
        }

        RefreshModeControls();
        foreach (int id in _roster.Players.Select(p => p.ChampionId).Where(id => id > 0).Distinct())
        {
            _ = LoadControls(id);
        }
    }

    private async Task LoadControls(int id)
    {
        if (
            _disposed
            || _controls.ContainsKey(id)
            || Environment.TickCount64 < _controlRetry.GetValueOrDefault(id)
            || !_controlRequests.Add(id)
        )
        {
            return;
        }

        try
        {
            var data = await _championDetails.RecognitionDataAsync(id, _life.Token);
            if (_disposed)
            {
                return;
            }

            var controls = ChampionControlSkills.Parse(data);
            _controls[id] = controls;
            SchedulePaint();
            await LoadSpells(id, controls, data);
        }
        catch (OperationCanceledException)
        {
            // Closing the overlay cancels the read; nothing is published afterwards.
        }
        catch (Exception e)
            when (e
                    is HttpRequestException
                        or InvalidOperationException
                        or InvalidDataException
                        or Newtonsoft.Json.JsonException
            )
        {
            // The client may still be starting: retry this champion later rather than on every refresh.
            _controlRetry[id] = Environment.TickCount64 + 30000;
        }
        finally
        {
            _controlRequests.Remove(id);
        }
    }

    // Slow amounts and cooldowns come from CommunityDragon after the control skills are already shown.
    private async Task LoadSpells(int id, ControlSkill[] controls, JObject champion)
    {
        try
        {
            var facts = await _spellData.LoadAsync(champion, _life.Token);
            if (!_disposed)
            {
                _spells[id] = (controls, facts, champion);
                _controls[id] = ChampionSlowSkills.Merge(controls, facts.SlowsFor(_controlsMode), champion);
                _cooldowns[id] = facts.Cooldowns;
                RefreshModeControls();
                SchedulePaint();
            }
        }
        catch (Exception e)
            when (e
                    is HttpRequestException
                        or IOException
                        or Newtonsoft.Json.JsonException
                        or UnauthorizedAccessException
                || (e is OperationCanceledException && !_life.IsCancellationRequested)
            )
        {
            // Slows and cooldowns are optional: the page keeps the client's hard control and tries again in the
            // next game.
            System.Diagnostics.Debug.WriteLine($"[控制保命] 减速数据读取失败 {id}: {e.GetType().Name}");
        }
    }
}
