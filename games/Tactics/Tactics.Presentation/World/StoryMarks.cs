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
public sealed record StoryMark(string Beat, Tile At, BoardUnit? Figure, EmoteStrip? Emote, Vector2? EmoteAt)
{
    /// <summary>The figure as the world draws it, among the party, with the "!" over its head; null with nobody standing there.</summary>
    public BoardUnit? Unit => Figure is null ? null : Figure with { Emote = Emote, EmoteAt = EmoteAt };
}

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

    private static StoryMark Of(StoryMarker marker, BoardTerrain terrain, UnitArt art, Func<Vector2, bool, Vector2>? pixels)
    {
        var place = UnitPlace.On(terrain, marker.At);
        var figure = marker.Sprite is null ? null : UnitPlan.Figure($"marker:{marker.Beat}", marker.Sprite, place, art, pixels);
        var emote = marker.Emote is { } name ? art.Emote(name) : null;
        // With nobody standing there, the "!" floats where a head would be, so it reads the same from the sofa.
        var head = figure?.Sprite ?? art.StandIn(UnitTone.Neutral);
        var feet = figure?.Feet ?? pixels?.Invoke(place.Feet, true) ?? place.Feet;
        return new StoryMark(marker.Beat, marker.At, figure, emote, emote is null ? null : UnitPlan.Overhead(feet, head));
    }
}
