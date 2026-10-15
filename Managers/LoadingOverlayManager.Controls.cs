using League.Services;

namespace League.Managers;

internal sealed partial class LoadingOverlayManager
{
    // Control skills per champion, read once from the client's champion data and kept across games.
    private readonly Dictionary<int, ControlSkill[]> _controls = new();
    private readonly Dictionary<int, long> _controlRetry = new();
    private readonly HashSet<int> _controlRequests = new();
    private readonly Dictionary<int, SpellCooldown[]> _cooldowns = new();
    private readonly ChampionSpellData _spellData = new();

    /// <summary>Starts reading the control skills of roster champions that have none yet.</summary>
    private void RequestControls()
    {
        if (!UseGameBar || _roster == null)
        {
            return;
        }

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
            var data = await _championDetails.ChampionDataAsync(id, _life.Token);
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

    // Slow amounts and cooldowns come from CommunityDragon after the client's control skills are already shown.
    private async Task LoadSpells(int id, ControlSkill[] controls, Newtonsoft.Json.Linq.JObject champion)
    {
        try
        {
            var facts = await _spellData.LoadAsync(champion, _life.Token);
            if (!_disposed)
            {
                _controls[id] = ChampionSlowSkills.Merge(controls, facts.Slows, champion);
                _cooldowns[id] = facts.Cooldowns;
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
