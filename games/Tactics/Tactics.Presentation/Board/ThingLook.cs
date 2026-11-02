using System.Collections.Immutable;
using System.Numerics;

namespace Tactics.Presentation.Board;

/// <summary>
/// How a thing to interact with is drawn this frame (<see cref="World.ThingShow"/>), in art pixels: everything is worked out here, and the TV
/// only draws it.
/// </summary>
/// <param name="Marker">The plain marker, for a thing with no art yet; null when it has a sprite.</param>
/// <param name="Label">Words under it: a marker's name, or in the art check (<c>--world-demo=things</c>) its key and where its picture came from.</param>
/// <param name="Scale">The sprite's scale about its feet, across and down: a squash as it opens, a pop as it's found.</param>
/// <param name="Tint">What the sprite's light is multiplied by: above 1 brightens it (a flash), and its alpha fades it.</param>
/// <param name="Alpha">How much of it shows, 0 to 1.</param>
public sealed record ThingLook(ThingMarker? Marker, ThingLabel? Label, Vector2 Scale, Rgb Tint, float Alpha)
{
    /// <summary>No tween: as it stands.</summary>
    public static readonly Rgb Plain = new(1, 1, 1);
}

/// <summary>
/// The plain marker (interactables design §4.3): a diamond on the tile, filled while the thing stands in the way, with a slab standing along
/// its wall for a shut passage, and a post and handle for a lever, so a door or a gate blocks visibly before its art exists.
/// </summary>
/// <param name="Diamond">The diamond's four points.</param>
/// <param name="Slab">A shut passage's slab, four points; null for none. It sinks as the passage opens.</param>
/// <param name="Handle">A lever's post and handle: the post's foot and top, then the handle's pivot and end; null for none.</param>
/// <param name="Line">The outline's colour.</param>
/// <param name="Fill">The fill's colour.</param>
/// <param name="FillAlpha">How much fill shows: 0 for a thing that doesn't stand in the way.</param>
public sealed record ThingMarker(ImmutableArray<Vector2> Diamond, ImmutableArray<Vector2>? Slab, ImmutableArray<Vector2>? Handle, Rgb Line, Rgb Fill, float FillAlpha);

/// <summary>Words drawn under a thing, unlit, over everything.</summary>
/// <param name="Lines">Each line, top first.</param>
/// <param name="At">The middle of the first line's top, in art pixels.</param>
/// <param name="Colour">Its colour.</param>
public sealed record ThingLabel(ImmutableArray<string> Lines, Vector2 At, Rgb Colour);
