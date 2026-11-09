using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Beats;

/// <summary>Maps a real beat's action markers onto its cinematic clock; movement before casting keeps its original timing.</summary>
public sealed record TelegraphWindow(double At, double Until, ImmutableArray<Tile> Tiles);

public sealed class CutInPlayback(BattleCutIn plan, double start, string name, ImmutableArray<TelegraphWindow> windows = default, bool open = false, CutInFrame? incoming = null)
{
    public ImmutableArray<Tile> Telegraph(double seconds)
    {
        var script = ScriptTime(seconds);
        return windows.IsDefaultOrEmpty ? [] : [.. windows.Where(w => w.At <= script && script < w.Until).SelectMany(w => w.Tiles).Distinct().Order()];
    }
    private double Lead => incoming is null ? plan.Lead : 0;
    private double Tail => open || plan.Extra == 0 && incoming is null ? 0 : BattleCutIn.SettleSeconds;
    public bool HoldsForReaction => open && (plan.Extra > 0 || incoming is not null);
    public double Length => start + Lead + plan.ScriptLength + Tail;
    public double Extra => Lead + Tail;
    public CutInPlayback WithoutExtra() => new(plan.WithoutExtra(), start, name, windows, open);
    public bool CanReach(double seconds, double cueAt) => cueAt < start || seconds >= start + Lead;
    /// <summary>Let the cast's existing frames play during the close-up without releasing projectiles or applying results early.</summary>
    public double PoseLead(double seconds, double cueAt) => cueAt >= start ? Math.Clamp(seconds - start, 0, Lead) : 0;
    public double ScriptTime(double seconds) => seconds < start ? seconds : start + Math.Clamp(seconds - start - Lead, 0, plan.ScriptLength);
    public bool Finished(double seconds) => seconds >= Length;
    public CutInFrame? Frame(double seconds)
    {
        if (seconds < start) return null;
        var time = seconds - start;
        var held = incoming ?? plan.At(plan.Lead + plan.ScriptLength - 1e-7);
        if (time < Lead + plan.ScriptLength) return incoming ?? plan.At(time);
        if (open) return held with { Banner = false };
        if (Tail == 0 || time >= Lead + plan.ScriptLength + Tail) return new(1, held.Focus, 0, false);
        var t = Math.Clamp((time - Lead - plan.ScriptLength) / Tail, 0, 1);
        var ease = (float)(t * t * (3 - 2 * t));
        return held with { Zoom = 1 + (held.Zoom - 1) * (1 - ease), Dim = held.Dim * (1 - ease), Banner = false };
    }
    public string? Banner(double seconds) => Frame(seconds) is { Banner: true } ? name : null;

    public static CutInPlayback? For(Beat beat, BattleView shown, BeatScript script, RulesPack rules,
        BoardTerrain terrain, BattleCinematics mode, CutInFrame? incoming = null)
    {
        if (mode == BattleCinematics.Off) incoming = null;
        var pending = shown.Reacting;
        foreach (var e in beat.Events)
        {
            if (e is ReactionOffered offered) pending = offered.Offer.Reactor;
            if (e is Reacted reacted && pending == reacted.Reactor) pending = null;
        }
        // Several attacks or reactions in the same engine beat share one push and one settle.
        var markers = script.Cues.OfType<AbilityCue>().Where(c => c.At < script.Length)
            .GroupBy(c => c.At).Select(g => g.First()).ToArray();
        if (markers.Length == 0)
            return incoming is null ? null : new(new BattleCutIn([new(CutInTier.None, script.Length, Vector2.Zero, Vector2.Zero)], BattleCinematics.Off),
                0, "", open: pending is not null, incoming: incoming);
        var actions = ImmutableArray.CreateBuilder<CutInAction>();
        var telegraphs = ImmutableArray.CreateBuilder<TelegraphWindow>();
        for (var i = 0; i < markers.Length; i++)
        {
            var marker = markers[i];
            if (!rules.Abilities.TryGetValue(marker.Used.Ability, out var ability)) return null;
            var end = i + 1 < markers.Length ? markers[i + 1].At : script.Length;
            var focus = new Vector2(0, -12);
            var tiles = AreaFor(marker, rules, terrain);
            var target = UnitPlace.On(terrain, marker.Toward).Feet + focus;
            // Keep the integer close-up for the wind-up, then pull back to a readable target/area frame.
            // Holding the entire board at 2x through impacts needlessly increases software raster cost.
            const int zoom = 1;
            Vector2? extent = null;
            if (!tiles.IsEmpty)
            {
                telegraphs.Add(new(marker.At, marker.At + BeatTiming.Windup, tiles));
                var points = tiles.Select(terrain.Centre).ToArray();
                var min = new Vector2(points.Min(p => p.X) - 40, points.Min(p => p.Y) - 60);
                var max = new Vector2(points.Max(p => p.X) + 40, points.Max(p => p.Y) + 40);
                target = (min + max) / 2;
                extent = (max - min) / 2;
            }
            actions.Add(new(BattleCutIn.Tier(ability, DowningCritical(beat, marker.Used.Source)), end - marker.At,
                UnitPlace.On(terrain, marker.From).Feet + focus, target, zoom, extent));
        }
        var plan = new BattleCutIn(actions.ToImmutable(), mode);
        return plan.Extra == 0 && telegraphs.Count == 0 && incoming is null ? null : new(plan, markers[0].At, $"{shown.Units.FirstOrDefault(u => u.Id == markers[0].Used.Source)?.Name} · {rules.Abilities[markers[0].Used.Ability].Name}", telegraphs.ToImmutable(), pending is not null, incoming);
    }
    public static ImmutableArray<Tile> AreaFor(AbilityCue marker, RulesPack rules, BoardTerrain terrain)
    {
        var area = rules.Abilities[marker.Used.Ability].Target.Area;
        return area.Shape is AreaShape.Burst or AreaShape.Cone or AreaShape.Line or AreaShape.Ring or AreaShape.Cross
            ? Areas.Tiles(area, marker.From, marker.Toward, terrain.Rules) : [];
    }

    private static bool DowningCritical(Beat beat, UnitId source)
    {
        var critical = new Dictionary<UnitId, bool>();
        foreach (var e in beat.Events)
        {
            if (e is AttackResolved attack) critical[attack.Target] = attack.Source == source && attack.Crit;
            if (e is Downed down && critical.GetValueOrDefault(down.Unit)) return true;
        }
        return false;
    }

}
