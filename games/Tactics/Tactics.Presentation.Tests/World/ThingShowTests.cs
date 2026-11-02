using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;
using static Tactics.Presentation.Tests.World.WalkFeed;

namespace Tactics.Presentation.Tests.World;

/// <summary>
/// <see cref="ThingShow"/>: the region's things in the world, in their state, sorted among the walkers and props, and animated only from
/// their events (interactables design §2.8, §2.9, §4.3).
/// </summary>
public sealed class ThingShowTests
{
    private static readonly Lazy<UnitArt> Units = new(() => UnitArt.Load(Pack.Root));
    private static readonly Lazy<ThingArt> Things = new(() => ThingArt.Load(Pack.Root));
    private static readonly Lazy<FxArt> Fx = new(() => FxArt.Load(Pack.Root));
    private static readonly Lazy<SoundBank> Sounds = new(() => SoundBank.Load(Pack.Root));
    private static readonly BoardTerrain Ground = WalkFeed.Ground(30, 30);

    private static readonly Tile ChestAt = new(10, 10);
    private static readonly Tile GateAt = new(12, 10);
    private static readonly Tile LeverAt = new(12, 12);

    internal static ThingView Chest(string state = "closed", bool trapped = false)
        => new("chest", "chest", ChestAt, "sw", "interactables.chest", state, state == "open" ? "props.chest_open" : "props.chest", "Chest", true, trapped);

    internal static ThingView Gate(string state = "closed", string facing = "sw")
        => new("gate", "gate", GateAt, facing, "interactables.gate", state, null, "Gate", state == "closed", false);

    internal static ThingView Lever(string state = "off") => new("lever", "lever", LeverAt, "sw", "interactables.lever", state, null, "Lever", false, false);

    internal static ThingView Pickup() => new("key", "pickup", new Tile(8, 12), "sw", "interactables.pickup", "here", "pickups.loot_sack", "Iron key", false, false);

    internal static ThingView Cache() => new("cache", "cache", new Tile(14, 14), "sw", "interactables.cache", "closed", "pickups.loot_sack", "Hidden cache", false, false);

    internal static ThingView Trap(string state = "armed") => new("plate", "trap", new Tile(9, 14), "sw", "interactables.trap", state, null, "Pressure plate", state == "armed", false);

    internal static ExploreView With(IEnumerable<ThingView> things, params ThingEventView[] events)
        => View(new Tile(5, 5)) with { Things = [.. things], ThingEvents = [.. events] };

    internal static ThingEventView E(int seq, string id, string what) => new(seq, id, what, "Ana", null);

    private static ThingShow Show(ThingArt? art = null, bool sounds = false)
        => new(Ground, Units.Value, art ?? Things.Value, Fx.Value, sounds ? Sounds.Value : null);

    private static BoardUnit Of(ThingFrame frame, string id) => frame.Units.Single(u => u.Id.Value == $"thing:{id}");

    [Fact]
    public void A_region_with_no_things_draws_none_and_costs_nothing()
    {
        var show = Show();
        show.Receive(View(new Tile(5, 5)));
        var frame = show.Frame(Frame);
        Assert.Empty(frame.Units);
        Assert.Empty(frame.Effects);
        Assert.Empty(frame.Sounds);
    }

    [Fact]
    public void Each_thing_stands_on_its_tile_in_its_state_with_its_stand_in()
    {
        var show = Show();
        show.Receive(With([Chest("open"), Pickup()]));
        var frame = show.Frame(Frame);

        var chest = Of(frame, "chest");
        Assert.Equal(UnitPlace.On(Ground, ChestAt).Feet, chest.Feet);
        Assert.Equal("props/chest_open_oak.png", chest.Sprite.Path);
        Assert.Equal(0, chest.Ring);                              // dressing: no ring
        Assert.Null(chest.Thing!.Marker);
        Assert.Null(chest.Thing.Label);                           // a sprite says what it is
        Assert.Equal(Vector2.One, chest.Thing.Scale);
        Assert.Equal("pickups/loot_sack.png", Of(frame, "key").Sprite.Path);
    }

    [Fact]
    public void A_thing_sorts_among_the_walkers_and_props_by_its_tile_and_a_flat_one_lies_under_them()
    {
        var show = Show();
        show.Receive(With([Chest(), Trap()]));
        var frame = show.Frame(Frame);

        Assert.Equal(BoardOrder.Key(ChestAt, BoardLayer.Prop), Of(frame, "chest").Order);
        Assert.Equal(BoardOrder.Key(new Tile(9, 14), BoardLayer.Ground), Of(frame, "plate").Order);
        var behind = UnitPlan.Figure("a", "characters.class_fighter", UnitPlace.On(Ground, new Tile(9, 10)), Units.Value);
        var front = UnitPlan.Figure("b", "characters.class_fighter", UnitPlace.On(Ground, new Tile(11, 10)), Units.Value);
        var beside = UnitPlan.Figure("c", "characters.class_fighter", UnitPlace.On(Ground, ChestAt), Units.Value);
        Assert.True(behind.Order < Of(frame, "chest").Order);
        Assert.True(front.Order > Of(frame, "chest").Order);
        Assert.True(beside.Order > Of(frame, "chest").Order);     // someone on its tile (a sack, a lever) stands in front of it
    }

    [Fact]
    public void Facing_se_or_nw_mirrors_the_sprite_and_turns_a_passages_slab_along_its_wall()
    {
        var show = Show();
        show.Receive(With([Chest() with { Facing = "se" }, Gate("closed", "sw")]));
        var frame = show.Frame(Frame);
        Assert.True(Of(frame, "chest").Mirror);
        var along = Of(frame, "gate").Thing!.Marker!.Slab!.Value;
        Assert.True(along[1].X > along[0].X && along[1].Y > along[0].Y);          // sw: along x, down to the right

        show.Receive(With([Gate("closed", "nw")]));
        var across = Of(show.Frame(Frame), "gate").Thing!.Marker!.Slab!.Value;
        Assert.True(across[1].X < across[0].X && across[1].Y > across[0].Y);      // nw: along y, down to the left
    }

    [Fact]
    public void A_shut_passage_blocks_visibly_and_an_open_one_lies_flat()
    {
        var show = Show();
        show.Receive(With([Gate("closed")]));
        var shut = Of(show.Frame(Frame), "gate").Thing!;
        Assert.NotNull(shut.Marker!.Slab);
        Assert.True(shut.Marker.FillAlpha >= 0.4f);
        Assert.Equal(["Gate"], shut.Label!.Lines);                 // the marker says what it is

        show.Receive(With([Gate("open")]));
        var open = Of(show.Frame(Frame), "gate").Thing!;
        Assert.Null(open.Marker!.Slab);
        Assert.True(open.Marker.FillAlpha < 0.2f);
    }

    [Fact]
    public void The_first_view_plays_nothing_it_brings()
    {
        var show = Show(sounds: true);
        show.Receive(With([Chest("open")], E(1, "chest", ThingEvents.Opened), E(2, "key", ThingEvents.Taken)));
        var frame = show.Frame(Frame);
        Assert.Empty(show.Playing);
        Assert.Empty(frame.Sounds);
        Assert.Equal(Vector2.One, Of(frame, "chest").Thing!.Scale);
    }

    [Fact]
    public void Opening_the_chest_swaps_its_stand_in_with_a_squash_that_settles()
    {
        var show = Show();
        show.Receive(With([Chest()]));
        show.Frame(Frame);
        show.Receive(With([Chest("open")], E(1, "chest", ThingEvents.Opened)));

        var mid = Of(show.Frame(ThingShow.ChangeSeconds / 2), "chest");
        Assert.Equal("props/chest_open_oak.png", mid.Sprite.Path);
        Assert.True(mid.Thing!.Scale.X > 1.05f && mid.Thing.Scale.Y < 0.95f);
        var after = Of(show.Frame(ThingShow.ChangeSeconds), "chest");
        Assert.Equal(Vector2.One, after.Thing!.Scale);
    }

    [Fact]
    public void A_lever_moves_and_its_gate_follows_a_moment_later_its_slab_sinking()
    {
        var show = Show();
        show.Receive(With([Lever(), Gate()]));
        show.Frame(Frame);
        show.Receive(With([Lever("on"), Gate("open")], E(1, "lever", ThingEvents.Toggled), E(2, "gate", ThingEvents.Toggled)));

        Assert.Equal(["toggled lever", "toggled gate"], show.Playing);
        var first = show.Frame(Frame);
        var handle = Of(first, "lever").Thing!.Marker!.Handle!.Value;
        Assert.True(handle[3].X < handle[1].X);                      // just started: still up and to the left
        Assert.Equal(44f, Height(Of(first, "gate")), 1);             // the gate hasn't started

        var leverDone = show.Frame(ThingShow.ChangeSeconds);
        Assert.True(Of(leverDone, "lever").Thing!.Marker!.Handle!.Value[3].X > handle[1].X);   // thrown to the right
        var sinking = Height(Of(leverDone, "gate"));
        Assert.InRange(sinking, 1, 43);

        var done = show.Frame(ThingShow.Stagger);
        Assert.Null(Of(done, "gate").Thing!.Marker!.Slab);
    }

    private static float Height(BoardUnit unit)
    {
        var slab = unit.Thing!.Marker!.Slab;
        return slab is null ? 0 : slab.Value.Max(p => p.Y) - slab.Value.Min(p => p.Y) - BoardProjection.HalfHeight * 0.84f;
    }

    [Fact]
    public void A_found_thing_pops_in_with_a_puff_and_a_bang_over_it()
    {
        var show = Show();
        show.Receive(With([Chest()]));
        show.Frame(Frame);
        show.Receive(With([Chest(), Cache()], E(1, "cache", ThingEvents.Revealed)));

        var start = show.Frame(Frame);
        var cache = Of(start, "cache");
        Assert.True(cache.Thing!.Alpha < 0.2f && cache.Thing.Scale.X < 0.3f);
        Assert.Equal(Units.Value.Emote("surprise"), cache.Emote);
        Assert.Contains(start.Effects, e => e.Path.StartsWith("fx/poof", StringComparison.Ordinal));

        var over = Of(show.Frame(ThingShow.PopSeconds * 0.7), "cache").Thing!;
        Assert.True(over.Scale.X > 1);                               // a touch too big, then it settles
        var settled = Of(show.Frame(ThingShow.PopSeconds), "cache");
        Assert.Equal(Vector2.One, settled.Thing!.Scale);
        Assert.Equal(1, settled.Thing.Alpha);
        Assert.Null(Of(show.Frame(ThingShow.FoundSeconds), "cache").Emote);
    }

    [Fact]
    public void A_second_find_in_the_same_view_waits_its_turn_unseen()
    {
        var show = Show();
        show.Receive(With([]));
        show.Frame(Frame);
        show.Receive(With([Cache(), Trap()], E(1, "cache", ThingEvents.Revealed), E(2, "plate", ThingEvents.Spotted)));

        var frame = show.Frame(Frame);
        Assert.Equal(0, Of(frame, "plate").Thing!.Alpha);
        Assert.Null(Of(frame, "plate").Emote);                       // its skull waits too, or it would give it away
        Assert.True(Of(show.Frame(ThingShow.Stagger + ThingShow.PopSeconds), "plate").Thing!.Alpha > 0.99f);
    }

    [Fact]
    public void A_taken_pickup_rises_and_fades_where_it_was_then_is_gone()
    {
        var show = Show(sounds: true);
        show.Receive(With([Pickup()]));
        show.Frame(Frame);
        show.Receive(With([], E(1, "key", ThingEvents.Taken)));

        var first = show.Frame(Frame);
        Assert.Equal(["ui_loot"], first.Sounds);                     // the pack's own loot sound
        var mid = Of(show.Frame(ThingShow.VanishSeconds / 2), "key");
        Assert.InRange(mid.Thing!.Alpha, 0.05f, 0.95f);
        Assert.True(mid.Feet.Y < UnitPlace.On(Ground, new Tile(8, 12)).Feet.Y);
        Assert.Empty(show.Frame(ThingShow.VanishSeconds).Units);
        Assert.Empty(show.Frame(Frame).Sounds);                      // once
    }

    [Fact]
    public void A_trap_seen_and_set_shows_a_skull_until_it_is_dealt_with()
    {
        var show = Show();
        show.Receive(With([Trap(), Chest(trapped: true)]));
        var frame = show.Frame(Frame);
        Assert.Equal(Units.Value.Emote("skull"), Of(frame, "plate").Emote);
        Assert.Equal(Units.Value.Emote("skull"), Of(frame, "chest").Emote);
        Assert.Equal(ThingShow.Danger, Of(frame, "plate").Thing!.Marker!.Line);

        show.Receive(With([Trap("disarmed"), Chest()]));
        frame = show.Frame(Frame);
        Assert.Null(Of(frame, "plate").Emote);
        Assert.Null(Of(frame, "chest").Emote);
        Assert.Equal(ThingShow.Spent, Of(frame, "plate").Thing!.Marker!.Line);
    }

    [Fact]
    public void A_trap_going_off_shakes_red_in_smoke()
    {
        var show = Show();
        show.Receive(With([Trap()]));
        show.Frame(Frame);
        show.Receive(With([Trap("sprung")], E(1, "plate", ThingEvents.Sprung)));
        var frame = show.Frame(0.05);
        var plate = Of(frame, "plate");
        Assert.True(plate.Thing!.Tint.R > 1.2f && plate.Thing.Tint.G < 1);
        Assert.NotEqual(UnitPlace.On(Ground, new Tile(9, 14)).Feet, plate.Feet);
        Assert.Contains(frame.Effects, e => e.Path.StartsWith("fx/smoke", StringComparison.Ordinal));
    }

    [Fact]
    public void An_event_for_a_thing_this_TV_was_never_shown_plays_nothing()
    {
        var show = Show();
        show.Receive(With([Lever()]));
        show.Frame(Frame);
        show.Receive(With([Lever("on")], E(1, "lever", ThingEvents.Toggled), E(2, "unseen_gate", ThingEvents.Toggled)));
        Assert.Equal(["toggled lever"], show.Playing);
        Assert.DoesNotContain(show.Frame(1).Units, u => u.Id.Value == "thing:unseen_gate");
    }

    [Fact]
    public void Numbers_that_start_again_after_a_restart_replay_nothing()
    {
        var show = Show();
        show.Receive(With([Chest()], E(40, "chest", ThingEvents.Looked)));
        show.Receive(With([Chest("open")], E(1, "chest", ThingEvents.Opened)));
        Assert.Empty(show.Playing);
        show.Receive(With([Chest("open")], E(1, "chest", ThingEvents.Opened), E(2, "chest", ThingEvents.Looked)));
        Assert.Equal(["looked chest"], show.Playing);
    }

    [Fact]
    public void An_event_already_seen_never_plays_twice()
    {
        var show = Show();
        show.Receive(With([Chest()]));
        var opened = With([Chest("open")], E(1, "chest", ThingEvents.Opened));
        show.Receive(opened);
        show.Receive(opened);
        show.Receive(opened with { Seq = 9 });
        Assert.Single(show.Playing);
    }

    [Fact]
    public void Every_moment_ends_within_two_seconds_as_the_thing_stands()
    {
        string[] all = [ThingEvents.Opened, ThingEvents.Unlocked, ThingEvents.Failed, ThingEvents.Revealed, ThingEvents.Spotted, ThingEvents.Sprung,
            ThingEvents.Disarmed, ThingEvents.Toggled, ThingEvents.Read, ThingEvents.Used, ThingEvents.Looked];
        foreach (var what in all)
        {
            var show = Show();
            show.Receive(With([Chest()]));
            show.Frame(Frame);
            show.Receive(With([Chest("open")], E(1, "chest", what)));
            for (var t = 0.0; t < 2; t += 1.0 / 30)
            {
                var look = Of(show.Frame(1.0 / 30), "chest").Thing!;
                Assert.InRange(look.Alpha, 0, 1);
                Assert.InRange(look.Scale.X, 0, 1.3f);
            }
            var end = Of(show.Frame(Frame), "chest");
            Assert.Equal((Vector2.One, ThingLook.Plain, 1f), (end.Thing!.Scale, end.Thing.Tint, end.Thing.Alpha));
            Assert.Equal(UnitPlace.On(Ground, ChestAt).Feet, end.Feet);
            Assert.Empty(show.Playing);
        }
    }

    [Fact]
    public void With_its_own_art_a_state_change_plays_its_strip_once_and_holds()
    {
        var show = Show(ThingArt.Load(ThingArtTests.WithArt.Value));
        show.Receive(With([Chest()]));
        Assert.Equal("interactables/chest_closed.png", Of(show.Frame(Frame), "chest").Sprite.Path);
        show.Receive(With([Chest("open")], E(1, "chest", ThingEvents.Opened)));

        var playing = Of(show.Frame(0.15), "chest");
        Assert.Equal("interactables/chest_opening.png", playing.Sprite.Path);
        Assert.Equal([1], playing.Anim.Frames);
        Assert.Equal(Vector2.One, playing.Thing!.Scale);              // the strip shows it: no squash on top
        Assert.Equal("interactables/chest_open.png", Of(show.Frame(0.3), "chest").Sprite.Path);
    }

    [Fact]
    public void The_art_check_labels_every_thing_with_its_key_and_where_its_picture_came_from()
    {
        var show = new ThingShow(Ground, Units.Value, Things.Value, captions: true);
        show.Receive(With([Chest(), Gate()]));
        var frame = show.Frame(Frame);
        Assert.Equal(["chest.closed", "stand-in: props.chest"], Of(frame, "chest").Thing!.Label!.Lines);
        Assert.Equal(["gate.closed", "marker"], Of(frame, "gate").Thing!.Label!.Lines);
    }

    [Fact]
    public void Things_follow_the_walks_pixels()
    {
        var show = Show();
        show.Receive(With([Chest()]));
        var feet = UnitPlace.On(Ground, ChestAt).Feet;
        Assert.Equal(feet + new Vector2(3, 4), Of(show.Frame(Frame, (f, _) => f + new Vector2(3, 4)), "chest").Feet);
    }
    [Fact]
    public void An_unlock_then_an_opening_in_one_view_flash_the_shut_door_then_open_it()
    {
        var door = Gate() with { Id = "door", Kind = "door", Art = "interactables.door", Name = "Door" };
        var show = Show();
        show.Receive(With([door]));
        show.Frame(Frame);
        show.Receive(With([door with { State = "open", Blocks = false }], E(1, "door", ThingEvents.Unlocked), E(2, "door", ThingEvents.Opened)));

        var flash = Of(show.Frame(0.05), "door").Thing!;
        Assert.True(flash.Tint.R > 1.2f);                            // the lock gives
        Assert.NotNull(flash.Marker!.Slab);                          // and the door is still shut
        Assert.Null(Of(show.Frame(ThingShow.Stagger + ThingShow.ChangeSeconds), "door").Thing!.Marker!.Slab);
    }
    [Fact]
    public void A_shut_passage_sorts_after_the_next_tile_of_its_wall_and_an_open_one_on_its_own_tile()
    {
        var show = Show();
        show.Receive(With([Gate("closed", "sw")]));
        Assert.Equal(BoardOrder.Key(new Tile(GateAt.X + 1, GateAt.Y), BoardLayer.Prop), Of(show.Frame(Frame), "gate").Order);
        show.Receive(With([Gate("closed", "nw")]));
        Assert.Equal(BoardOrder.Key(new Tile(GateAt.X, GateAt.Y + 1), BoardLayer.Prop), Of(show.Frame(Frame), "gate").Order);
        var front = UnitPlan.Figure("b", "characters.class_fighter", UnitPlace.On(Ground, new Tile(GateAt.X + 1, GateAt.Y)), Units.Value);
        Assert.True(front.Order > Of(show.Frame(Frame), "gate").Order);      // whoever stands in front of the gap is drawn over it
        show.Receive(With([Gate("open", "nw")]));
        Assert.Equal(BoardOrder.Key(GateAt, BoardLayer.Prop), Of(show.Frame(Frame), "gate").Order);
    }
}
