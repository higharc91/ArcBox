using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="WorldPeople"/>: who stands about in the world, drawn by the TV alone (spec §3.2).</summary>
public sealed class WorldPeopleTests
{
    [Fact]
    public void A_walking_person_stops_for_their_bark_and_resumes_without_sliding_or_jumping()
    {
        var person = Person("farmer", 5, 5, "se", (6,5), (6,6)) with { Id = "farmer" };
        var people = new WorldPeople([person], Ground, Art.Value);
        people.Units(0, Everywhere, []);
        var bark = new BarkView("farmer",1,DateTimeOffset.MaxValue,new StoryLineView("hello","townsfolk_farmer","Farmer",null,"idle","Hello."));
        var first = Assert.Single(people.Units(5, Everywhere, [], leader: new Tile(4,5), bark: bark));
        var held = Assert.Single(people.Units(7, Everywhere, [], leader: new Tile(4,5), bark: bark));
        Assert.Equal(first.Feet, held.Feet);
        Assert.Equal(Art.Value.Emote("question"), held.Emote);
        Assert.Equal(UnitPlan.Overhead(held.Feet, held.Sprite), held.EmoteAt);
        var resumed = Assert.Single(people.Units(7.01, Everywhere, [], leader: new Tile(4,5)));
        Assert.True(Vector2.Distance(held.Feet,resumed.Feet) < 1);
        Assert.Null(resumed.Emote);
    }

    [Theory]
    [InlineData("innkeeper", "bree", "bree_tallow")]
    [InlineData("mayor", "halden", "halden_varrow")]
    public void Approved_named_people_use_their_talk_clip_while_holding_ground_then_resume_walking(string preset, string id, string name)
    {
        var person = Person(preset, 5, 5, "se", (6, 5), (6, 6)) with { Id = id };
        var people = new WorldPeople([person], Ground, Art.Value);
        var time = Enumerable.Range(1, 1000).Select(i => i / 100d)
            .First(t => people.PlaceAt(person, t + people.Phase(person)).Walking);
        var before = Assert.Single(people.Units(time, Everywhere, []));
        var bark = new BarkView(id, 1, DateTimeOffset.MaxValue, new StoryLineView("hello", name, name, null, "idle", "Welcome."));
        var first = Assert.Single(people.Units(time + .02, Everywhere, [], leader: new Tile(4, 5), bark: bark));
        var held = Assert.Single(people.Units(time + 2, Everywhere, [], leader: new Tile(4, 5), bark: bark));
        Assert.Equal(before.Feet, first.Feet);
        Assert.Equal(first.Feet, held.Feet);
        Assert.Equal(first.Sprite.Anims["talk"], first.Anim);
        Assert.Equal(first.Anim, held.Anim);
        Assert.InRange(first.Row, 0, 7);
        Assert.False(first.Mirror);
        var resumed = Assert.Single(people.Units(time + 2.02, Everywhere, [], leader: new Tile(4, 5)));
        Assert.Equal(resumed.Sprite.Anims["walk"], resumed.Anim);
        Assert.True(Vector2.Distance(held.Feet, resumed.Feet) < 2);
    }

    [Fact]
    public void A_barking_person_without_a_talk_clip_uses_idle_instead_of_attack()
    {
        var people = new WorldPeople([Person("kobold", 5, 5) with { Id = "speaker" }], Ground, Art.Value);
        var bark = new BarkView("speaker", 1, DateTimeOffset.MaxValue, new StoryLineView("hello", "speaker", "Speaker", null, "idle", "Hello."));
        var unit = Assert.Single(people.Units(1, Everywhere, [], leader: new Tile(4, 5), bark: bark));
        Assert.False(unit.Sprite.Anims.ContainsKey("talk"));
        Assert.Equal(unit.Sprite.Idle, unit.Anim);
        Assert.NotEqual(unit.Sprite.Anims["attack"], unit.Anim);
    }

    [Theory]
    [InlineData("bree", "tallow", "creatures/bree_tallow.png")]
    [InlineData("innkeeper", "missing", "creatures/bree_tallow.png")]
    [InlineData("innkeeper", null, "creatures/bree_tallow.png")]
    public void Placed_art_variants_and_region_preloading_select_the_same_sheet(string preset, string? variant, string path)
    {
        var person = Person(preset, 5, 5) with { Variant = variant };
        var key = WorldPeople.SpriteKey(person, Art.Value);
        var loaded = Art.Value.Sprite(key);
        Assert.EndsWith(path, loaded!.Path.Replace('\\', '/'), StringComparison.Ordinal);
        var drawn = Assert.Single(new WorldPeople([person], Ground, Art.Value).Units(0, Everywhere, []));
        Assert.Same(loaded, drawn.Sprite);
    }

    [Fact]
    public void A_departing_person_fades_without_renumbering_the_people_who_remain()
    {
        var people = new WorldPeople([Person("guard", 5, 5) with { Id = "watch", Unless = ["gone"] }, Person("farmer", 6, 5)], Ground, Art.Value);
        var before = people.Units(0, Everywhere, [], flags: new HashSet<string>());
        var survivor = before.Single(u => u.Id.Value != "watch").Id;
        Assert.Equal(2, before.Length);
        people.Units(1, Everywhere, [], flags: new HashSet<string> { "gone" });
        var fading = people.Units(1.1, Everywhere, [], flags: new HashSet<string> { "gone" });
        Assert.InRange(fading.Single(u => u.Id.Value == "watch").Opacity, 0.01f, 0.99f);
        Assert.Equal(survivor, Assert.Single(people.Units(2, Everywhere, [], flags: new HashSet<string> { "gone" })).Id);
    }

    [Fact]
    public void An_arrival_waits_until_behind_the_party_and_never_appears_on_the_leader()
    {
        var people = new WorldPeople([Person("guard", 5, 5) with { Requires = ["arrived"] }], Ground, Art.Value);
        var flags = new HashSet<string>();
        Assert.Empty(people.Units(0, Everywhere, [], flags: flags, leader: new Tile(5, 5)));
        flags.Add("arrived");
        Assert.Empty(people.Units(1, Everywhere, [], flags: flags, leader: new Tile(5, 5)));
        Assert.Empty(people.Units(2, Everywhere, [], flags: flags, leader: new Tile(4, 4)));
        people.Units(3, Everywhere, [], flags: flags, leader: new Tile(8, 8));
        Assert.InRange(Assert.Single(people.Units(3.1, Everywhere, [], flags: flags, leader: new Tile(8, 8))).Opacity, 0.01f, 0.99f);
        Assert.Equal(1, Assert.Single(people.Units(4, Everywhere, [], flags: flags, leader: new Tile(8, 8))).Opacity);
    }

    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));
    private static readonly BoardTerrain Ground = WalkFeed.Ground(60, 60, (x, y) => x >= 30 ? 4 : 0);
    private static readonly Vector2 Everywhere = BoardProjection.Corner(5, 5, 0);

    private static RegionPerson Person(string preset, int x, int y, string face = "sw", params (int X, int Y)[] walk)
        => new(preset, new Tile(x, y), face, walk.Length == 0 ? default : [.. walk.Select(t => new Tile(t.X, t.Y))]);

    private static ImmutableArray<BoardUnit> At(double seconds, ImmutableArray<RegionPerson> people, ImmutableArray<StoryMarker> markers = default, Vector2? around = null)
        => new WorldPeople(people, Ground, Art.Value).Units(seconds, around ?? Everywhere, markers.IsDefault ? [] : markers);

    [Fact]
    public void Someone_stands_on_their_tile_at_its_height_in_their_own_sprite()
    {
        var unit = Assert.Single(At(0, [Person("guard", 31, 5)], around: BoardProjection.Corner(31, 5, 4)));

        Assert.Equal(UnitPlace.On(Ground, new Tile(31, 5)).Feet, unit.Feet);
        Assert.Equal(Art.Value.Sprite("characters.guard"), unit.Sprite);
        Assert.Equal(Art.Value.Sprite("characters.guard")!.Idle, unit.Anim);
    }

    [Fact]
    public void They_are_dressing_with_no_ring_or_marks_or_tone_of_a_side()
    {
        var unit = Assert.Single(At(0, [Person("guard", 5, 5)]));

        Assert.Equal(UnitTone.Neutral, unit.Tone);
        Assert.Equal(0, unit.Ring);
        Assert.Null(unit.Bar);
        Assert.Null(unit.CrownAt);
        Assert.True(unit.Dressing);
    }

    [Theory]
    [InlineData("se", 0, false)]
    [InlineData("sw", 0, true)]
    [InlineData("nw", 1, false)]
    [InlineData("ne", 1, true)]
    public void They_look_the_way_they_were_told(string face, int row, bool mirror)
    {
        var unit = Assert.Single(At(0, [Person("guard", 5, 5, face)]));

        Assert.Equal(row, unit.Row);
        Assert.Equal(mirror, unit.Mirror);
    }

    [Fact]
    public void They_sort_among_the_party_by_their_tile()
    {
        var unit = Assert.Single(At(0, [Person("guard", 5, 6)]));

        Assert.Equal(BoardOrder.Key(new Tile(5, 6), BoardLayer.Unit), unit.Order);
    }

    [Fact]
    public void Someone_on_a_walk_rests_at_home_then_walks_the_loop_and_comes_back()
    {
        var person = Person("farmer", 5, 5, "se", (6, 5), (6, 6), (5, 6));
        var world = new WorldPeople([person], Ground, Art.Value);
        var cycle = world.Cycle(person);

        var seen = Enumerable.Range(0, 400).Select(i => world.PlaceAt(person, cycle * i / 400)).ToArray();

        Assert.Equal(new Vector2(5.5f, 5.5f), world.PlaceAt(person, 0).Place.Map);
        Assert.False(world.PlaceAt(person, 0).Walking);
        Assert.Contains(seen, s => s.Walking);
        Assert.Contains(seen, s => Vector2.Distance(s.Place.Map, new Vector2(6.5f, 6.5f)) < 0.05f);
        Assert.Equal(new Vector2(5.5f, 5.5f), world.PlaceAt(person, cycle).Place.Map);

        // No tile is cut across: they keep to the lines between them.
        Assert.All(seen, s => Assert.True(Math.Min(MathF.Abs(s.Place.Map.X % 1 - 0.5f), MathF.Abs(s.Place.Map.Y % 1 - 0.5f)) < 0.001f, $"{s.Place.Map}"));
    }

    [Fact]
    public void Walking_they_play_their_walk_and_look_where_theyre_going()
    {
        var person = Person("farmer", 5, 5, "sw", (5, 4), (6, 4), (6, 5));
        var world = new WorldPeople([person], Ground, Art.Value);
        var when = Enumerable.Range(0, 2000).Select(i => world.Cycle(person) * i / 2000).First(t => world.PlaceAt(person, t).Walking);

        var unit = Assert.Single(world.Units(when - world.Phase(person), Everywhere, []));

        Assert.Equal(Art.Value.Sprite("characters.farmer")!.Anim("walk"), unit.Anim);
        Assert.Equal(1, unit.Row);    // heading -y, away from the viewer
    }

    [Fact]
    public void Two_people_dont_walk_in_step()
    {
        var (a, b) = (Person("farmer", 5, 5, "se", (6, 5)), Person("merchant", 15, 5, "se", (16, 5)));
        var world = new WorldPeople([a, b], Ground, Art.Value);

        Assert.NotEqual(world.Phase(a), world.Phase(b));
    }

    [Fact]
    public void Height_follows_them_up_a_step()
    {
        var person = Person("farmer", 29, 5, "se", (30, 5), (31, 5));
        var world = new WorldPeople([person], Ground, Art.Value);
        var heights = Enumerable.Range(0, 400).Select(i => world.PlaceAt(person, world.Cycle(person) * i / 400).Place.Height).ToArray();

        Assert.Equal(0, heights.Min());
        Assert.Equal(4, heights.Max());
    }

    [Fact]
    public void A_beats_figure_replaces_the_person_it_is_so_nobody_shows_twice()
    {
        var people = ImmutableArray.Create(Person("mayor", 5, 5), Person("guard", 6, 6));

        var units = At(0, people, [new StoryMarker("ch1_granary", new Tile(20, 20), "characters.mayor", "alert")]);

        Assert.DoesNotContain(units, u => u.Sprite == Art.Value.Sprite("characters.mayor"));
        Assert.Contains(units, u => u.Sprite == Art.Value.Sprite("characters.guard"));
    }

    [Fact]
    public void Of_several_in_one_preset_only_the_nearest_to_the_beat_goes()
    {
        var people = ImmutableArray.Create(Person("guard", 5, 5), Person("guard", 8, 8));

        var unit = Assert.Single(At(0, people, [new StoryMarker("beat", new Tile(9, 9), "characters.guard", null)]));

        Assert.Equal(UnitPlace.On(Ground, new Tile(5, 5)).Feet, unit.Feet);
    }

    [Fact]
    public void A_scenes_cast_replaces_the_person_it_is_as_a_beats_figure_does()
    {
        // Pip stands at the well; once the scene starts the marker's gone, and Pip speaks from beside the party instead.
        var people = ImmutableArray.Create(Person("child", 5, 5), Person("guard", 6, 6));
        ImmutableArray<CastView> cast = [new CastView("pip", "Pip Ashdown", "characters.child", new Tile(9, 9), new Tile(8, 8))];

        var units = new WorldPeople(people, Ground, Art.Value).Units(0, Everywhere, [], cast: cast);

        Assert.DoesNotContain(units, u => u.Sprite == Art.Value.Sprite("characters.child"));
        Assert.Contains(units, u => u.Sprite == Art.Value.Sprite("characters.guard"));
    }

    [Fact]
    public void A_beat_with_nobody_to_show_replaces_nobody()
        => Assert.Single(At(0, [Person("guard", 5, 5)], [new StoryMarker("beat", new Tile(9, 9), null, "alert")]));

    [Fact]
    public void Nobody_far_off_the_screen_is_drawn_so_they_cost_the_overlay_nothing()
    {
        var people = ImmutableArray.Create(Person("guard", 5, 5), Person("guard", 50, 50));

        var unit = Assert.Single(At(0, people, around: BoardProjection.Corner(5, 5, 0)));

        Assert.Equal(UnitPlace.On(Ground, new Tile(5, 5)).Feet, unit.Feet);
    }

    [Fact]
    public void A_region_with_nobody_in_it_draws_nobody() => Assert.Empty(At(0, []));

    [Fact]
    public void What_is_drawn_is_in_painters_order()
    {
        var units = At(0, [Person("guard", 9, 9), Person("guard", 5, 5), Person("guard", 7, 5)]);

        Assert.Equal(units.Select(u => u.Order).Order(), units.Select(u => u.Order));
    }

    [Fact]
    public void A_preset_the_pack_cant_draw_is_left_out_rather_than_shown_as_a_stand_in()
        => Assert.Empty(At(0, [Person("nobody_at_all", 5, 5)]));

    [Fact]
    public void Everyone_in_ashen_ford_can_be_drawn()
    {
        var ground = Pack.Ground("ashen_ford");
        Assert.True(ground.People.Length >= 30, $"{ground.People.Length} people.");
        Assert.All(ground.People, p => Assert.NotNull(Art.Value.Sprite($"characters.{p.Preset}")));
    }

    [Fact]
    public void The_show_puts_people_on_the_ground_with_the_party_and_only_the_party_fades_what_they_stand_behind()
    {
        var show = new WalkShow(Ground, Art.Value, null, [Person("guard", 6, 5)]);
        show.Receive(WalkFeed.View(new Tile(5, 5)));

        var frame = show.Frame(0.016);

        Assert.Contains(frame.Units, u => u.Dressing);
        Assert.DoesNotContain(frame.Walkers, u => u.Dressing);
        Assert.Contains(frame.Walkers, u => u.Tone == UnitTone.Party);
    }
}
