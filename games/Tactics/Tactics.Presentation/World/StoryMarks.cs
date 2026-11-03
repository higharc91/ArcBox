using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>A story beat's marker, ready to draw in the world, in the same pixels as the party's units.</summary>
/// <param name="Beat">The beat it offers.</param>
/// <param name="At">Its spot.</param>
/// <param name="Figure">Who stands there, sorted among the party by its tile; null with nobody to show.</param>
/// <param name="Emote">The emote over the spot (the "!"), or null while the beat's scene plays.</param>
/// <param name="EmoteAt">Where the emote's tail goes (<see cref="EmoteStrip.AnchorX"/>, <see cref="EmoteStrip.AnchorY"/>); null with no emote.</param>
/// <param name="Unit">
/// What the world draws, sorted among the party: the figure with the "!" over its head, or with nobody standing there, the "!" alone on the
/// spot (<see cref="BoardUnit.MarksOnly"/>); null with nothing to draw.
/// </param>
public sealed record StoryMark(string Beat, Tile At, BoardUnit? Figure, EmoteStrip? Emote, Vector2? EmoteAt, BoardUnit? Unit = null);

/// <summary>
/// The story's markers in the world (overworld 5d §2, default 12), from <see cref="ExploreView.Markers"/>: the first of the opening scene's cast
/// standing at the beat's spot, with the <c>alert</c> emote, the "!", over their head while the beat waits. Pure dressing: the figure doesn't
/// block the party, and the server changes a marker only when what's on offer does, so it never moves with a step. Godot only draws these.
/// </summary>
public static class StoryMarks
{
    /// <summary>
    /// The markers of <paramref name="explore"/> on <paramref name="terrain"/>: one for each on the map. <paramref name="pixels"/> is the walk's
    /// (<see cref="WalkShow"/>): where feet go on the screen's pixels, so the figure stands still against the ground as the camera moves.
    /// </summary>
    public static ImmutableArray<StoryMark> Of(ExploreView explore, BoardTerrain terrain, UnitArt art, Func<Vector2, bool, Vector2>? pixels = null)
        => Of(explore.Markers ?? [], terrain, art, pixels);

    /// <summary>The markers <paramref name="markers"/> on <paramref name="terrain"/>, as <see cref="Of(ExploreView, BoardTerrain, UnitArt, Func{Vector2, bool, Vector2}?)"/>.</summary>
    public static ImmutableArray<StoryMark> Of(ImmutableArray<StoryMarker> markers, BoardTerrain terrain, UnitArt art, Func<Vector2, bool, Vector2>? pixels = null)
    {
        if (markers.IsDefaultOrEmpty)
            return [];
        return [.. markers.Where(m => terrain.Map.InBounds(m.At)).Select(m => Of(m, terrain, art, pixels))];
    }

    /// <summary>
    /// The scene's cast (<see cref="ExploreView.Cast"/>), ready to draw among the party: each idle on their tile, looking towards the tile
    /// the server says (the leader's), with no ring and no marks. Anyone off the map isn't drawn.
    /// </summary>
    public static ImmutableArray<BoardUnit> Cast(ImmutableArray<CastView> cast, BoardTerrain terrain, UnitArt art, Func<Vector2, bool, Vector2>? pixels = null)
    {
        if (cast.IsDefaultOrEmpty)
            return [];
        return [.. cast.Where(c => terrain.Map.InBounds(c.At)).Select(c => UnitPlan.Figure($"cast:{c.Id}", c.Sprite, UnitPlace.On(terrain, c.At), art, pixels,
            new Vector2(c.Facing.X - c.At.X, c.Facing.Y - c.At.Y)))];
    }

    private static StoryMark Of(StoryMarker marker, BoardTerrain terrain, UnitArt art, Func<Vector2, bool, Vector2>? pixels)
    {
        var place = UnitPlace.On(terrain, marker.At);
        var figure = marker.Sprite is null ? null : UnitPlan.Figure($"marker:{marker.Beat}", marker.Sprite, place, art, pixels);
        var emote = marker.Emote is { } name ? name == "shop" ? art.Emote("trader") ?? art.Emote("notice") ?? art.Emote("alert") : art.Emote(name) : null;
        // With nobody standing there, the "!" floats where a head would be, so it reads the same from the sofa.
        var head = figure?.Sprite ?? art.StandIn(UnitTone.Neutral);
        var feet = figure?.Feet ?? pixels?.Invoke(place.Feet, true) ?? place.Feet;
        var at = emote is null ? (Vector2?)null : UnitPlan.Overhead(feet, head);
        var unit = figure is not null ? figure with { Emote = emote, EmoteAt = at }
            : emote is null ? null : UnitPlan.Figure($"marker:{marker.Beat}", null, place, art, pixels) with { MarksOnly = true, Emote = emote, EmoteAt = at };
        return new StoryMark(marker.Beat, marker.At, figure, emote, at, unit);
    }
}
