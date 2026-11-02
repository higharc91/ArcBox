using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>
/// The people of a region (spec §3.2): dressing the TV draws from the region's own file, standing idle or walking a short loop, sorted among the party by their tiles and passing
/// through the same overlay. The server never hears of them, so there's no phone change and no state, and nobody can be walled in by one. Everything here is worked out from a clock.
/// <para>
/// When a beat's figure is someone who's already standing about, it replaces them: of everyone in that preset, the one nearest the beat steps out of the picture, so nobody shows twice.
/// Only those near the screen are drawn: each one costs the overlay its closure, and the rest of a region's people have no reason to.
/// </para>
/// </summary>
public sealed class WorldPeople
{
    private const float SecondsPerTile = 0.9f;      // a stroll, slower than the party
    private const double Rest = 4;                  // what they stand at home for, between loops
    private const float ScreenX = 620;              // half what the TV can show, in art pixels, with a sprite's width to spare
    private const float ScreenY = 400;
    private const string Walk = "walk";

    private static readonly ImmutableDictionary<string, Vector2> Facing = new Dictionary<string, Vector2>(StringComparer.Ordinal)
    {
        ["se"] = new(1, 0),
        ["sw"] = new(0, 1),
        ["nw"] = new(-1, 0),
        ["ne"] = new(0, -1),
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private readonly ImmutableArray<RegionPerson> _people;
    private readonly BoardTerrain _terrain;
    private readonly UnitArt _art;
    private readonly string[] _sprites;
    private readonly string[] _ids;
    private readonly Vector2[] _feet;

    /// <summary>The people of <paramref name="people"/> on <paramref name="terrain"/>. Anyone the pack has no sprite for is left out, as a stand-in would read as someone who's there.</summary>
    public WorldPeople(ImmutableArray<RegionPerson> people, BoardTerrain terrain, UnitArt art)
    {
        (_terrain, _art) = (terrain, art);
        _people = [.. people.Where(p => art.Sprite(SpriteOf(p)) is not null && terrain.Map.InBounds(p.At))];
        _sprites = [.. _people.Select(SpriteOf)];
        _ids = [.. _people.Select((p, i) => $"person:{i}:{p.Preset}")];
        _feet = [.. _people.Select(p => UnitPlace.On(terrain, p.At).Feet)];
    }

    /// <summary>Where someone is, and whether they're on their way.</summary>
    public readonly record struct Stance(UnitPlace Place, bool Walking, Vector2 Heading);

    private static string SpriteOf(RegionPerson person) => $"characters.{person.Preset}";

    private static ImmutableArray<Tile> Route(RegionPerson person) => person.Walk.IsDefaultOrEmpty ? [person.At] : [person.At, .. person.Walk];

    /// <summary>How long one time round their loop takes, rest and all.</summary>
    public double Cycle(RegionPerson person) => person.Walk.IsDefaultOrEmpty ? Rest : Rest + (person.Walk.Length + 1) * SecondsPerTile;

    /// <summary>How far through their day someone starts, from where they stand, so no two go in step.</summary>
    public double Phase(RegionPerson person) => (person.At.X * 7.3 + person.At.Y * 3.7) % Cycle(person);

    /// <summary>Where <paramref name="person"/> is, <paramref name="seconds"/> into their loop.</summary>
    public Stance PlaceAt(RegionPerson person, double seconds)
    {
        var home = UnitPlace.On(_terrain, person.At);
        var face = Facing.GetValueOrDefault(person.Face, Facing["sw"]);
        var cycle = Cycle(person);
        if (person.Walk.IsDefaultOrEmpty || seconds == cycle)
            return new Stance(home, false, face);
        var walked = (seconds % cycle) - Rest;
        if (walked < 0)
            return new Stance(home, false, face);

        var route = Route(person);
        var along = (float)(walked / SecondsPerTile);
        var step = Math.Min((int)along, route.Length - 1);
        var (from, to) = (UnitPlace.On(_terrain, route[step]), UnitPlace.On(_terrain, route[(step + 1) % route.Length]));
        var share = along - step;
        var place = new UnitPlace(Vector2.Lerp(from.Map, to.Map, share), from.Height + (to.Height - from.Height) * share);
        return new Stance(place, true, to.Map - from.Map);
    }

    /// <summary>
    /// Everyone to draw at <paramref name="clock"/> near <paramref name="around"/> (art pixels), in painter's order, less those <paramref name="markers"/> have taken over.
    /// <paramref name="pixels"/> is the walk's, as for the party, so a figure stands still against the ground as the camera moves.
    /// </summary>
    public ImmutableArray<BoardUnit> Units(double clock, Vector2 around, ImmutableArray<StoryMarker> markers, Func<Vector2, bool, Vector2>? pixels = null)
    {
        if (_people.IsEmpty)
            return [];
        var taken = Taken(markers);
        var units = ImmutableArray.CreateBuilder<BoardUnit>();
        for (var i = 0; i < _people.Length; i++)
        {
            if (Array.IndexOf(taken, i) >= 0 || Math.Abs(_feet[i].X - around.X) > ScreenX || Math.Abs(_feet[i].Y - around.Y) > ScreenY)
                continue;
            var person = _people[i];
            var stance = PlaceAt(person, clock + Phase(person));
            units.Add(UnitPlan.Person(_ids[i], _sprites[i], stance.Place, stance.Heading, stance.Walking ? Walk : null, _art, pixels));
        }
        units.Sort((a, b) => a.Order.CompareTo(b.Order));
        return units.ToImmutable();
    }

    /// <summary>Which people a beat's figure stands in for. The markers are few.</summary>
    private int[] Taken(ImmutableArray<StoryMarker> markers)
    {
        if (markers.IsDefaultOrEmpty)
            return [];
        var taken = new int[markers.Length];
        for (var m = 0; m < markers.Length; m++)
        {
            var marker = markers[m];
            var (nearest, best) = (-1, int.MaxValue);
            for (var i = 0; i < _people.Length; i++)
            {
                var distance = Math.Abs(_people[i].At.X - marker.At.X) + Math.Abs(_people[i].At.Y - marker.At.Y);
                if (_sprites[i] == marker.Sprite && distance < best)
                    (nearest, best) = (i, distance);
            }
            taken[m] = nearest;
        }
        return taken;
    }
}
