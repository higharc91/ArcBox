using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;
using static Tactics.Presentation.Tests.World.WalkFeed;

namespace Tactics.Presentation.Tests.World;

/// <summary>
/// A scene's cast in the world on the TV (<see cref="ExploreView.Cast"/>, the user's answer of 2026-11-01, item 21): drawn among the party on
/// their tiles, facing the leader, gone when the scene ends, there for a TV that has only just come, and never under a follower.
/// </summary>
public sealed class SceneCastShowTests
{
    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));
    private static readonly BoardTerrain Ground = WalkFeed.Ground(40, 40);

    private static CastView Member(string id, Tile at, Tile facing) => new(id, id, "characters.mayor", at, facing);

    private static BoardUnit[] CastOf(WalkFrame frame) => [.. frame.Units.Where(u => u.Id.Value.StartsWith("cast:", StringComparison.Ordinal))];

    [Fact]
    public void A_TV_that_has_just_come_draws_the_cast_among_the_party_on_their_tiles()
    {
        var at = new Tile(10, 10);
        var show = new WalkShow(Ground, Art.Value, null);       // a new TV, or one that came back: its first view is mid-scene
        show.Receive(View(at) with { Cast = [Member("mayor", new Tile(12, 10), at), Member("pip", new Tile(12, 11), at)] });

        var frame = show.Frame(1 / 60.0);

        var cast = CastOf(frame);
        Assert.Equal(["cast:mayor", "cast:pip"], cast.Select(u => u.Id.Value).Order());
        Assert.Equal(WorldCamera.OnGround(UnitPlace.On(Ground, new Tile(12, 10)).Feet), cast.Single(u => u.Id.Value == "cast:mayor").Feet);
        Assert.Equal(WorldCamera.OnGround(UnitPlace.On(Ground, new Tile(12, 11)).Feet), cast.Single(u => u.Id.Value == "cast:pip").Feet);
        Assert.Equal(Art.Value.Sprite("characters.mayor"), cast[0].Sprite);
        Assert.All(cast, u => Assert.Equal(0, u.Ring));                         // dressing: no ring, so nobody takes them for the party
        Assert.Equal(5, frame.Units.Length);                                    // the party of three and the two of the cast
        Assert.Equal(frame.Units.OrderBy(u => u.Order).Select(u => u.Id), frame.Units.Select(u => u.Id));
        Assert.Equal(2, show.Cast.Count);
    }

    [Fact]
    public void The_cast_looks_towards_the_leader()
    {
        var at = new Tile(10, 10);
        BoardUnit Looking(Tile from)
        {
            var show = new WalkShow(Ground, Art.Value, null);
            show.Receive(View(at) with { Cast = [Member("m", from, at)] });
            return Assert.Single(CastOf(show.Frame(1 / 60.0)));
        }

        // The art's four facings: towards the viewer (row 0) or away (row 1), mirrored or not. Each side of the leader looks back at them.
        var (east, west, south, north) = (Looking(new Tile(12, 10)), Looking(new Tile(8, 10)), Looking(new Tile(10, 12)), Looking(new Tile(10, 8)));
        Assert.Equal((1, false), (east.Row, east.Mirror));          // looking along -x
        Assert.Equal((0, false), (west.Row, west.Mirror));          // along +x
        Assert.Equal((1, true), (south.Row, south.Mirror));         // along -y
        Assert.Equal((0, true), (north.Row, north.Mirror));         // along +y
        Assert.Equal(4, new[] { east, west, south, north }.Select(u => (u.Row, u.Mirror)).Distinct().Count());
    }

    [Fact]
    public void The_cast_leaves_when_the_scene_ends()
    {
        var at = new Tile(10, 10);
        var show = new WalkShow(Ground, Art.Value, null);
        show.Receive(View(at) with { Cast = [Member("mayor", new Tile(12, 10), at)] });
        Assert.Single(CastOf(show.Frame(1 / 60.0)));

        show.Receive(View(at) with { Cast = [] });
        Assert.Empty(CastOf(show.Frame(1 / 60.0)));
        Assert.Empty(show.Cast);

        show.Receive(View(at) with { Cast = [Member("mayor", new Tile(12, 10), at)] });
        show.Receive(View(at));                                         // a view with no cast at all (a pack with no story)
        Assert.Empty(CastOf(show.Frame(1 / 60.0)));
    }

    [Fact]
    public void Someone_off_the_map_isnt_drawn()
        => Assert.Empty(StoryMarks.Cast([Member("m", new Tile(400, 3), new Tile(5, 5))], Ground, Art.Value));

    /// <summary>
    /// Model test: a party walks a random way and stops, and a cast comes on random tiles round it, including the very tiles the followers
    /// stand on; or the party arrives with its cast in one view, as after travel. Once they've settled, no follower stands on a cast tile, and
    /// only those who had to moved. With <see cref="WalkTrack.Keep"/>'s tiles left out of a rest's formation, or nobody stepping aside, it fails.
    /// </summary>
    [Fact]
    public void No_follower_ever_stands_on_the_cast()
    {
        var walls = WalkFeed.Ground(40, 40, wall: (x, y) => (x * 7 + y * 13) % 11 == 0 && (x, y) != (20, 20));
        for (var seed = 0; seed < 60; seed++)
        {
            var random = new Random(seed);
            var party = random.Next(2, 7);
            var script = new Script(new Tile(20, 20), party);
            var track = new WalkTrack();
            track.Receive(script.Now(false));
            Run(track, walls, 0.5);
            var steps = new List<(int, int)>();
            var at = script.At;
            for (var i = 0; i < random.Next(2, 9); i++)
            {
                var (dx, dy) = (random.Next(-1, 2), random.Next(-1, 2));
                var to = new Tile(at.X + dx, at.Y + dy);
                if ((dx, dy) == (0, 0) || !walls.Rules.Walkable(to) || (dx != 0 && dy != 0))
                    continue;
                steps.Add((dx, dy));
                at = to;
            }
            if (steps.Count == 0)
                steps.Add((0, 1));
            Walk(track, walls, script, [.. steps], heldAtEnd: false);
            var settled = Run(track, walls, 4);

            // The cast: some on followers' tiles, the rest at random round the leader, never on the leader's.
            var taken = settled.Where(p => p.Key != Id(0)).Select(p => TileUnder(p.Value.Map)).ToList();
            var tiles = new HashSet<Tile>(taken.Where(_ => random.Next(2) == 0));
            for (var i = 0; i < random.Next(1, 5); i++)
            {
                var t = new Tile(script.At.X + random.Next(-3, 4), script.At.Y + random.Next(-3, 4));
                if (t != script.At && walls.Rules.Walkable(t))
                    tiles.Add(t);
            }
            tiles.Remove(script.At);
            var before = settled.ToDictionary(p => p.Key, p => p.Value.Map);
            var arrived = random.Next(3) == 0;
            var view = arrived
                ? View(script.At, script.Facing, false, [], 0, epoch: 2, party: party)          // a new epoch: travelled here with the cast
                : script.Now(false);
            track.Keep([.. tiles]);
            track.Receive(view with { Cast = [.. tiles.Select(t => Member($"c{t.X}_{t.Y}", t, script.At))] });
            var after = Run(track, walls, 5);

            foreach (var (id, place) in after.Where(p => p.Key != Id(0)))
            {
                Assert.False(tiles.Contains(TileUnder(place.Map)), $"Seed {seed}: follower {id} stands on the cast at {TileUnder(place.Map)}.");
                if (!arrived && !tiles.Contains(TileUnder(before[id])))
                    Assert.True(System.Numerics.Vector2.Distance(before[id], place.Map) < 0.05f, $"Seed {seed}: follower {id} moved though nobody stood on it.");
            }
        }
    }
}
