using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="ThingsDemo"/>, the art check: every kind in every state, on ground that can hold them, and its scenes in order.</summary>
public sealed class ThingsDemoTests
{
    private static readonly Lazy<WorldGround> Ground = new(ThingsDemo.Ground);

    [Fact]
    public void The_galleries_hold_every_kind_in_every_state_the_art_brief_draws_each_on_its_own_tile()
    {
        var gallery = ThingsDemo.Gallery();
        foreach (var kind in InteractableKinds.All.Values)
            foreach (var state in ThingsDemo.StatesOf(kind))
                Assert.Contains(gallery, t => t.Kind == kind.Id && t.State == state);
        Assert.Equal(InteractableKinds.All.Keys, ThingsDemo.Kinds.Order(StringComparer.Ordinal));
        Assert.Equal(gallery.Length, gallery.Select(t => t.At).Distinct().Count());
        Assert.Equal(gallery.Length, gallery.Select(t => t.Id).Distinct().Count());
        Assert.Contains(gallery, t => t.Trapped);
        Assert.Equal(["ne", "nw", "se", "sw"], gallery.Where(t => t.Kind == "door").Select(t => t.Facing).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_thing_and_every_leader_stands_on_open_flat_ground_and_no_leader_on_a_thing()
    {
        var terrain = Ground.Value.Terrain;
        var steps = ThingsDemo.Steps();
        foreach (var thing in steps.SelectMany(s => s.View.Explore!.Things!.Value))
        {
            Assert.True(terrain.Walkable(thing.At), $"{thing.Id} at {thing.At}");
            Assert.Equal(1, terrain.HeightAt(thing.At));
        }
        foreach (var step in steps)
        {
            var explore = step.View.Explore!;
            Assert.True(terrain.Walkable(explore.At), $"{step.Label}: the leader at {explore.At}");
            Assert.DoesNotContain(explore.Things!.Value, t => t.At == explore.At);
            Assert.Equal(explore.Things!.Value.Length, explore.Things.Value.Select(t => t.At).Distinct().Count());
        }
    }

    [Fact]
    public void A_door_a_gate_and_a_secret_door_each_stand_in_a_gap_of_a_wall_that_blocks()
    {
        var terrain = Ground.Value.Terrain;
        var last = ThingsDemo.Steps()[^1].View.Explore!.Things!.Value;
        foreach (var id in new[] { "door", "gate", "secret" })
        {
            var thing = last.Single(t => t.Id == id);
            var at = thing.At;
            var (a, b) = thing.Facing is "se" or "nw" ? (new Tile(at.X, at.Y - 1), new Tile(at.X, at.Y + 1)) : (new Tile(at.X - 1, at.Y), new Tile(at.X + 1, at.Y));
            Assert.False(terrain.Walkable(a), $"{id}: {a}");
            Assert.False(terrain.Walkable(b), $"{id}: {b}");
        }
    }

    [Fact]
    public void The_scenes_play_each_kind_of_event_and_name_every_shot_once()
    {
        var steps = ThingsDemo.Steps();
        Assert.Equal(steps.Select(s => s.At).Order(), steps.Select(s => s.At));
        var events = steps.SelectMany(s => s.View.Explore!.ThingEvents!.Value).DistinctBy(e => e.Seq).Select(e => e.What).ToList();
        foreach (var what in new[] { ThingEvents.Opened, ThingEvents.Revealed, ThingEvents.Spotted, ThingEvents.Sprung, ThingEvents.Disarmed,
                     ThingEvents.Toggled, ThingEvents.Unlocked, ThingEvents.Taken, ThingEvents.Read, ThingEvents.Used })
            Assert.Contains(what, events);
        var shots = steps.SelectMany(s => s.Shots.Select(x => x.Name)).ToList();
        Assert.Equal(shots.Count, shots.Distinct().Count());
        Assert.True(steps[0].Shots[0].After >= 8, "the first shot waits for the world to open");
        Assert.All(steps.Where(s => s.Label.StartsWith("gallery", StringComparison.Ordinal)), s => Assert.True(s.Captions));
        var art = ThingArt.Load(Pack.Root);
        Assert.All(steps.SelectMany(s => s.View.Explore!.Things!.Value), t => Assert.True(t.StandIn is null || art.Sprite(t.StandIn) is not null, t.Id));
    }

    [Fact]
    public void The_secret_door_is_not_sent_until_it_is_found()
    {
        var steps = ThingsDemo.Steps();
        var found = steps.ToList().FindIndex(s => s.Label == "secret-found");
        Assert.All(steps.Take(found), s => Assert.DoesNotContain(s.View.Explore!.Things!.Value, t => t.Id == "secret"));
        Assert.Contains(steps[found].View.Explore!.Things!.Value, t => t.Id == "secret" && t.State == "closed" && t.Blocks);
    }

    /// <summary>The hidden things' scenes (design §7): nothing is sent before it's found, and the search's check has no DC.</summary>
    [Fact]
    public void The_hidden_things_are_not_sent_until_found_and_the_search_shows_no_dc()
    {
        var steps = ThingsDemo.Steps();
        int At(string label) => steps.ToList().FindIndex(s => s.Label == label);
        foreach (var (id, label) in new[] { ("tracks", "notice"), ("track_cache", "notice"), ("rune", "rune"), ("goat_track", "path"), ("loose_stone", "search-found") })
        {
            var found = steps.ToList().FindLastIndex(s => s.Label == label);
            Assert.All(steps.Take(At(label)), s => Assert.DoesNotContain(s.View.Explore!.Things!.Value, t => t.Id == id));
            Assert.Contains(steps[found].View.Explore!.Things!.Value, t => t.Id == id);
        }
        var events = steps.SelectMany(s => s.View.Explore!.ThingEvents!.Value).DistinctBy(e => e.Seq).ToList();
        Assert.Contains(events, e => e.Id == "tracks" && e.What == ThingEvents.Noticed);
        Assert.Contains(events, e => e.Id == "track_cache" && e.What == ThingEvents.Revealed);
        var path = steps[^1].View.Explore!.Things!.Value.Single(t => t.Id == "goat_track");
        Assert.Equal(("found", false), (path.State, path.Blocks));
        Assert.True(Ground.Value.Terrain.Walkable(path.At));
        Assert.False(Ground.Value.Terrain.Walkable(new Tile(path.At.X - 1, path.At.Y)));        // the hedge either side of it
        Assert.False(Ground.Value.Terrain.Walkable(new Tile(path.At.X + 1, path.At.Y)));
        Assert.All(steps.Where(s => s.Label.StartsWith("search-", StringComparison.Ordinal) && s.View.Story is not null), s => Assert.Null(s.View.Story!.Check!.Dc));
        Assert.Contains(steps, s => s.View.Story?.Check?.Roll is { Vs: 0, Success: true });
    }

    [Fact]
    public void The_TV_opens_the_courtyard_for_the_demo_region()
    {
        Assert.Equal(ThingsDemo.Region, DemoRegion.Load(Pack.Root, ThingsDemo.Region).Map.Id);
        Assert.Equal("ashen_ford", DemoRegion.Load(Pack.Root, "ashen_ford").Map.Id);
    }
}
