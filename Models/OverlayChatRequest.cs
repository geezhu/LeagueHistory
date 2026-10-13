using Newtonsoft.Json.Linq;

namespace League.Models;

// Statistics select existing roster entries; voice text requires an explicit confirmation.
public sealed record OverlayChatRequest(
    string Id,
    string Session,
    string Target,
    int Slot,
    int ChampionId,
    long CreatedAt,
    bool Dismissed,
    bool Cancelled,
    string Kind = "tier",
    string Text = "",
    bool Confirmed = false
)
{
    public bool IsVoice => Kind == "voice";

    /// <summary>Requests that do not select roster players: confirmed voice text and wave summaries.</summary>
    public bool IsRosterFree => Kind is "voice" or "waves";

    public static OverlayChatRequest? Parse(JObject value, string session, int[] heroes, long now)
    {
        var commandId = ReadCommandId(value, session);
        if (commandId == null)
        {
            return null;
        }
        Guid id = commandId.Value;

        if (
            !long.TryParse(value["updatedAt"]!.ToString(), out long stamp)
            || stamp < 0
            || stamp > now
            || now - stamp > 30000
            || !int.TryParse(value["slot"]!.ToString(), out int slot)
            || !int.TryParse(value["championId"]!.ToString(), out int hero)
        )
        {
            return null;
        }

        string target = (string)value["target"]!;
        if (
            value["kind"] != null
            && (
                value["kind"]!.Type != JTokenType.String
                || (string?)value["kind"] is not ("tier" or "mastery" or "deathStreak" or "voice" or "waves")
            )
        )
        {
            return null;
        }

        string kind = (string?)value["kind"] ?? "tier";
        if (kind == "voice")
        {
            return ReadVoice(value, session, id, target, slot, hero, stamp);
        }
        if (value["text"] != null || value["confirmed"] != null)
        {
            return null;
        }

        // Wave messages describe the whole game, so they name a content instead of players.
        bool waves = kind == "waves";
        if (
            waves ? !MinionWaveChatMessage.IsTarget(target) || slot != -1 || hero != 0
            : target == "single" ? slot < 0 || slot >= heroes.Length || hero <= 0 || heroes[slot] != hero
            : target is not ("own" or "enemy") || slot != -1 || hero != 0
        )
        {
            return null;
        }

        return new(
            id.ToString("N"),
            session,
            target,
            slot,
            hero,
            stamp,
            (bool)value["dismissed"]!,
            (bool)value["cancelled"]!,
            kind
        );
    }

    private static Guid? ReadCommandId(JObject value, string session)
    {
        if (
            value["protocol"]?.Type != JTokenType.Integer
            || value["protocol"]!.ToString() != "1"
            || value["session"]?.Type != JTokenType.String
            || (string?)value["session"] != session
            || session.Length == 0
            || value["id"]?.Type != JTokenType.String
            || !Guid.TryParse((string?)value["id"], out var id)
            || value["target"]?.Type != JTokenType.String
            || value["updatedAt"]?.Type != JTokenType.Integer
            || value["slot"]?.Type != JTokenType.Integer
            || value["championId"]?.Type != JTokenType.Integer
            || value["dismissed"]?.Type != JTokenType.Boolean
            || value["cancelled"]?.Type != JTokenType.Boolean
        )
        {
            return null;
        }

        return id;
    }

    private static OverlayChatRequest? ReadVoice(
        JObject value,
        string session,
        Guid id,
        string target,
        int slot,
        int hero,
        long stamp
    )
    {
        if (
            target != "text"
            || slot != -1
            || hero != 0
            || value["confirmed"]?.Type != JTokenType.Boolean
            || (bool?)value["confirmed"] != true
            || value["text"]?.Type != JTokenType.String
            || !OverlayVoiceChatText.TryNormalize((string?)value["text"], out string text)
        )
        {
            return null;
        }

        return new(
            id.ToString("N"),
            session,
            target,
            slot,
            hero,
            stamp,
            (bool)value["dismissed"]!,
            (bool)value["cancelled"]!,
            "voice",
            text,
            true
        );
    }

    public bool SameCommand(OverlayChatRequest other) =>
        Id == other.Id
        && Session == other.Session
        && Target == other.Target
        && Slot == other.Slot
        && ChampionId == other.ChampionId
        && CreatedAt == other.CreatedAt
        && Kind == other.Kind
        && Text == other.Text
        && Confirmed == other.Confirmed;
}

public static class OverlayVoiceChatText
{
    public const int MaxLength = 180;

    public static bool TryNormalize(string? input, out string text)
    {
        text = "";
        if (input == null || input.Length > MaxLength)
        {
            return false;
        }

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsControl(c) || c is '\u2028' or '\u2029')
            {
                return false;
            }

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= input.Length || !char.IsLowSurrogate(input[++i]))
                {
                    return false;
                }
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
        }
        text = input.Trim();
        // This feature sends one chat message, never a game slash command.
        if (text.Length == 0 || text.StartsWith('/'))
        {
            text = "";
            return false;
        }
        return true;
    }

    public static IReadOnlyList<string> Build(OverlayChatRequest request)
    {
        if (
            !request.IsVoice
            || !request.Confirmed
            || request.Target != "text"
            || request.Slot != -1
            || request.ChampionId != 0
            || !TryNormalize(request.Text, out string text)
        )
        {
            throw new InvalidOperationException("未发送：请确认一条不超过 180 字符的聊天文字；不支持换行或斜杠命令。");
        }

        return [text];
    }
}

// One click, at most one send. IDs stay consumed for the whole match, including failures.
public sealed class OverlayChatRequests
{
    private string _session = "";
    private readonly HashSet<string> _seen = new();

    public bool Take(OverlayChatRequest request)
    {
        if (_session != request.Session)
        {
            _seen.Clear();
            _session = request.Session;
        }
        return _seen.Count < 256 && _seen.Add(request.Id);
    }
}

public enum OverlayChatReadiness
{
    Waiting,
    Ready,
    Cancelled,
    TimedOut,
}

public sealed class OverlayChatReady(OverlayChatRequest request)
{
    private long? _stableSince;

    public OverlayChatReadiness Observe(OverlayChatRequest? latest, long now, bool gameReady)
    {
        if (latest == null || !request.SameCommand(latest) || latest.Cancelled)
        {
            return OverlayChatReadiness.Cancelled;
        }

        if (now < request.CreatedAt || now - request.CreatedAt >= 20000)
        {
            return OverlayChatReadiness.TimedOut;
        }

        if (!latest.Dismissed || !gameReady)
        {
            _stableSince = null;
            return OverlayChatReadiness.Waiting;
        }
        _stableSince ??= now;
        return now - _stableSince >= 600 ? OverlayChatReadiness.Ready : OverlayChatReadiness.Waiting;
    }
}
