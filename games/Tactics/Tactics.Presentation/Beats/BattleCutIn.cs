using System.Collections.Immutable;
using System.Numerics;
using Tactics.Logic;

namespace Tactics.Presentation.Beats;

/// <summary>Presentation intensity; explicit Off never adds time or moves the board.</summary>
public enum BattleCinematics { Full, Short, Off }

/// <summary>A content tier, independent of the viewer's cinematic preference.</summary>
public enum CutInTier { None, Short, Full }

/// <summary>One linked action's original timing and framing, in board art pixels.</summary>
public sealed record CutInAction(CutInTier Tier, double Seconds, Vector2 Caster, Vector2 Target);

/// <summary>Board framing layered over BoardFit. The HUD keeps its ordinary screen transform.</summary>
public sealed record CutInFrame(float Zoom, Vector2 Focus, float Dim, bool Banner);

/// <summary>
/// Pure cut-in clock for a linked action/reaction chain. Playback uses ScriptTime to advance the existing BeatPlayer;
/// completion uses Length, so a shown signal cannot precede settling. No gameplay clocks or events are changed here.
/// </summary>
public sealed class BattleCutIn
{
    public const double PushSeconds = 0.3;
    public const double SettleSeconds = 0.3;
    public const double FullHoldSeconds = 0.3;
    private readonly ImmutableArray<CutInAction> _actions;
    private readonly CutInTier _tier;
    private readonly int _zoom;

    public BattleCutIn(ImmutableArray<CutInAction> actions, BattleCinematics mode, int zoom = 2)
    {
        if (actions.IsDefaultOrEmpty || actions.Any(a => !double.IsFinite(a.Seconds) || a.Seconds <= 0
            || !float.IsFinite(a.Caster.X) || !float.IsFinite(a.Caster.Y) || !float.IsFinite(a.Target.X) || !float.IsFinite(a.Target.Y)
            || !Enum.IsDefined(a.Tier)))
            throw new ArgumentException("A chain needs finite positions and positive action lengths.", nameof(actions));
        if (zoom is not (2 or 3)) throw new ArgumentOutOfRangeException(nameof(zoom));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _actions = actions;
        _zoom = zoom;
        var tier = actions.Max(a => a.Tier);
        _tier = mode == BattleCinematics.Off ? CutInTier.None
            : mode == BattleCinematics.Short && tier == CutInTier.Full ? CutInTier.Short : tier;
        ScriptLength = actions.Sum(a => a.Seconds);
    }

    public double ScriptLength { get; }
    public double Lead => _tier == CutInTier.None ? 0 : PushSeconds + (_tier == CutInTier.Full ? FullHoldSeconds : 0);
    public double Extra => _tier == CutInTier.None ? 0 : Lead + SettleSeconds;
    public double Length => ScriptLength + Extra;

    /// <summary>Automatic Short at five or six players, unless explicitly overridden.</summary>
    public static BattleCinematics Preference(int players, BattleCinematics? chosen = null)
    {
        if (players is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(players));
        if (chosen is { } mode && !Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(chosen));
        return chosen ?? (players >= 5 ? BattleCinematics.Short : BattleCinematics.Full);
    }

    /// <summary>Content overrides win; otherwise paid features/spells are short, signatures and downing criticals full.</summary>
    public static CutInTier Tier(AbilityDef ability, bool downingCritical = false, bool bossMove = false)
    {
        if (ability.Presentation?.CutIn is { } authored)
            return authored switch { "none" => CutInTier.None, "short" => CutInTier.Short, "full" => CutInTier.Full,
                _ => throw new ArgumentException($"Unknown cut-in tier {authored}.", nameof(ability)) };
        if (downingCritical || bossMove) return CutInTier.Full;
        if (ability.Cost.Reaction > 0) return CutInTier.None;
        if (new[] { ability.Presentation?.Anim, ability.Presentation?.Fx, ability.Presentation?.Projectile }
            .Any(key => key?.StartsWith("signature.", StringComparison.Ordinal) == true)) return CutInTier.Full;
        return ability.Tier > 0 || ability.Cost.Uses is not null || ability.Cost.Resource?.Any(p => p.Value > 0) == true
            ? CutInTier.Short : CutInTier.None;
    }

    public double ScriptTime(double seconds) => Math.Clamp(seconds - Lead, 0, ScriptLength);
    public bool Finished(double seconds) => seconds >= Length;

    public CutInFrame At(double seconds)
    {
        if (_tier == CutInTier.None || seconds < 0 || seconds >= Length) return new(1, _actions[0].Caster, 0, false);
        var t = ScriptTime(seconds);
        var action = _actions[^1];
        for (var i = 0; i < _actions.Length; i++)
        {
            action = _actions[i];
            if (t < action.Seconds || i == _actions.Length - 1) break;
            t -= action.Seconds;
        }
        var release = Ease(Math.Clamp(t / Math.Min(0.3, action.Seconds), 0, 1));
        var focus = Vector2.Lerp(action.Caster, action.Target, (float)release);
        var weight = seconds < PushSeconds ? Ease(seconds / PushSeconds)
            : seconds > Length - SettleSeconds ? Ease((Length - seconds) / SettleSeconds) : 1;
        return new((float)(1 + (_zoom - 1) * weight), focus, (float)(0.35 * weight), seconds < Lead);
    }

    private static double Ease(double t) => t * t * (3 - 2 * t);
}
