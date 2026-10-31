using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="BoardLighting"/>: the pack's light data, turned into lights on a board, on hand-made maps and the real ones.</summary>
public sealed class BoardLightingTests
{
    private static readonly ImmutableSortedDictionary<string, ImmutableArray<Tile>> NoZones
        = ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty;

    /// <summary>A map with <paramref name="rows"/> of "height:material:flags" cells.</summary>
    private static BattleMap Map(string id, string[] materials, ImmutableArray<PropPlacement> props, params string[] rows)
    {
        var cells = rows.Select(r => r.Split(' ')).ToArray();
        var bytes = cells.SelectMany(row => row.SelectMany(cell => cell.Split(':').Select(byte.Parse))).ToArray();
        return BattleMap.FromBytes(id, cells[0].Length, cells.Length, bytes, [.. materials], props, NoZones);
    }

    private static PropPlacement Prop(string id, int x, int y) => new(id, new Tile(x, y), true, Cover.None);

    private static BoardPlan Plan(BattleMap map, BoardChanges? changes = null) => BoardPlan.Build(new BoardTerrain(map, changes), Pack.Art);

    private static BoardLighting Lit(BattleMap map, BoardChanges? changes = null) => BoardLighting.Of(Plan(map, changes), map.Id, Pack.Art.Lighting);

    private static BoardLight[] OnTile(BoardLighting lighting, int x, int y) => [.. lighting.Lights.Where(l => l.Tile == new Tile(x, y))];

    private static string[] Flat(int width, int height, string cell = "0:0:0")
        => [.. Enumerable.Repeat(string.Join(' ', Enumerable.Repeat(cell, width)), height)];

    private static JsonElement PropEntry(string set, string id) => Pack.Manifest(set).RootElement.GetProperty("props").GetProperty(id);

    private static JsonElement TextureRule(string family)
        => Pack.Manifest("terrain").RootElement.GetProperty("textures").GetProperty(family).GetProperty("emissive");

    private static Rgb Hex(string hex)
    {
        var value = Convert.ToInt32(hex[1..], 16);
        return new Rgb((value >> 16 & 255) / 255f, (value >> 8 & 255) / 255f, (value & 255) / 255f);
    }

    private static float Sum(Rgb c) => c.R + c.G + c.B;

    /// <summary>Most of the finale's lights by their step: what the real data gives, and it's more than half of the curve's eight.</summary>
    private const int LavaPhases = 5;

    /// <summary>Each map's light, stated here by hand: a room of the recipe, or null for open air. A new map fails until it's looked at.</summary>
    private static readonly Dictionary<string, string?> StatedLight = new()
    {
        ["crypt_hall"] = "crypt",
        ["crystal_cave"] = "cave",
        ["hall_of_the_flame"] = "hall",
        ["forest_ruins"] = null,
        ["keep_gate"] = null,
        ["river_crossing"] = null,
        ["warlord_bridge"] = null,
    };

    private static LitBoard LitOf(BattleMap map, BoardChanges? changes = null) => LitBoard.Build(Plan(map, changes), Lit(map, changes));

    private static IEnumerable<LitFace> TopsOf(LitBoard lit, int x) => lit.Items.OfType<LitFace>().Where(f => f.Face.Kind == FaceKind.Top && f.Face.Tile.X == x);

    private static BoardPlan Block(BoardPlan plan, int fromX, int toX)
        => plan with { Items = [.. plan.Items.Where(i => i.Tile.X >= fromX && i.Tile.X < toX)] };

    /// <summary>A world-sized map: 160 x 120, with 960 torches.</summary>
    private static BattleMap Busy()
    {
        ImmutableArray<PropPlacement> props = [.. Enumerable.Range(0, 960).Select(i => Prop("torch", i % 40 * 4, i / 40 * 5))];
        return Map("test", ["cobble/grey"], props, Flat(160, 120));
    }

    /// <summary>The recipe's sum, over every light, with no grid or probe.</summary>
    private static Rgb ByHand(BoardLighting lighting, Vector2 point, Func<BoardLight, float> energy)
    {
        var (r, g, b) = (lighting.Ambient.R, lighting.Ambient.G, lighting.Ambient.B);
        foreach (var light in lighting.Lights)
        {
            var share = light.Falloff(point) * energy(light);
            r += share * light.Colour.R;
            g += share * light.Colour.G;
            b += share * light.Colour.B;
        }
        return new Rgb(Math.Min(r, BoardLighting.Ceiling), Math.Min(g, BoardLighting.Ceiling), Math.Min(b, BoardLighting.Ceiling));
    }

    [Fact]
    public void A_brazier_lights_from_the_pixel_the_manifest_names_with_its_radius_colour_and_curve()
    {
        var entry = PropEntry("dungeon", "brazier");
        var light = entry.GetProperty("light");
        var (atX, atY) = (light.GetProperty("at")[0][0].GetSingle(), light.GetProperty("at")[0][1].GetSingle());
        var (anchorX, anchorY) = (entry.GetProperty("anchor")[0].GetSingle(), entry.GetProperty("anchor")[1].GetSingle());
        var map = Pack.Map("crypt_hall");
        var at = map.Props.First(p => p.Id == "brazier").At;
        var feet = new BoardTerrain(map, null).Centre(at);

        var lit = Assert.Single(OnTile(Lit(map), at.X, at.Y));

        Assert.Equal(feet, lit.Ground);
        Assert.Equal(feet + new Vector2(atX - anchorX, atY - anchorY), lit.Source);
        Assert.Equal(light.GetProperty("radius").GetSingle(), lit.Radius);
        Assert.Equal(Hex(light.GetProperty("colour").GetProperty("fire").GetString()!), lit.Colour);
        Assert.Equal(Pack.Manifest("dungeon").RootElement.GetProperty("lighting").GetProperty("flicker").GetProperty("fire").EnumerateArray().Select(f => f.GetSingle()), lit.Curve);
    }

    [Fact]
    public void A_lights_colour_is_of_the_variant_the_prop_is_drawn_in()
    {
        var map = Pack.Map("crystal_cave");
        var prop = map.Props.First(p => p.Id == "crystal_big");
        var drawn = Plan(map).Items.OfType<Sprite>().First(s => s.Tile == prop.At).Image.Path;   // dungeon/props/crystal_big_<variant>.png
        var variant = Path.GetFileNameWithoutExtension(drawn)["crystal_big_".Length..];

        var lit = Assert.Single(OnTile(Lit(map), prop.At.X, prop.At.Y));

        Assert.Equal(Hex(PropEntry("dungeon", "crystal_big").GetProperty("light").GetProperty("colour").GetProperty(variant).GetString()!), lit.Colour);
    }

    [Fact]
    public void A_prop_with_a_list_of_points_per_frame_sways_and_a_still_one_has_one_place()
    {
        var map = Map("test", ["cobble/grey"], [Prop("torch", 0, 0), Prop("crystals", 1, 0)], Flat(2, 1));
        var lighting = Lit(map);

        var torch = Assert.Single(OnTile(lighting, 0, 0));
        var crystals = Assert.Single(OnTile(lighting, 1, 0));

        Assert.Equal(4, torch.Sources.Length);
        Assert.NotEqual(torch.Sources[0], torch.Sources[1]);
        Assert.Equal(8, torch.Fps);
        Assert.Single(crystals.Sources);
    }

    [Fact]
    public void A_prop_with_two_points_in_one_frame_is_two_lights()
    {
        var map = Map("test", ["cobble/grey"], [Prop("desk", 0, 0)], Flat(1, 1));

        Assert.Equal(2, OnTile(Lit(map), 0, 0).Length);
    }

    [Fact]
    public void A_prop_with_no_light_gives_none()
    {
        var map = Map("test", ["cobble/grey"], [Prop("crate", 0, 0)], Flat(1, 1));

        Assert.Empty(Lit(map).Lights);
    }

    [Theory]
    [InlineData("crypt_hall", "crypt")]
    [InlineData("crystal_cave", "cave")]
    [InlineData("hall_of_the_flame", "hall")]
    public void A_room_takes_the_recipes_ambient_by_the_word_in_its_maps_name_lifted_for_a_board_with_few_lights(string map, string room)
    {
        var ambient = Pack.Manifest("dungeon").RootElement.GetProperty("lighting").GetProperty("ambient").GetProperty(room);
        float Lifted(int channel) => ambient[channel].GetSingle() + (1 - ambient[channel].GetSingle()) * BoardLighting.AmbientLift;

        var lighting = Lit(Pack.Map(map));

        Assert.Equal(new Rgb(Lifted(0), Lifted(1), Lifted(2)), lighting.Ambient);
        Assert.True(lighting.Active);
    }

    [Theory]
    [InlineData("river_crossing")]
    [InlineData("forest_ruins")]
    [InlineData("warlord_bridge")]
    [InlineData("keep_gate")]
    public void Open_air_keeps_its_daylight_and_with_no_lights_is_not_lit_at_all(string map)
    {
        var lighting = Lit(Pack.Map(map));

        Assert.Equal(new Rgb(1, 1, 1), lighting.Ambient);
        Assert.False(lighting.Active);
        Assert.Equal(new Rgb(1, 1, 1), lighting.At(new Vector2(10, 10)));
    }

    [Fact]
    public void Lava_scatters_a_light_on_every_other_tile_in_a_staggered_grid_by_its_rule()
    {
        var rule = TextureRule("lava");
        var light = rule.GetProperty("light");
        var map = Map("test", ["lava/molten"], [], Flat(4, 4));
        var terrain = new BoardTerrain(map, null);

        var tops = Lit(map).Lights.Where(l => l.Source == terrain.Centre(l.Tile) - new Vector2(0, light.GetProperty("lift").GetSingle())).ToArray();

        Assert.Equal(8, tops.Length);
        Assert.All(tops, l =>
        {
            Assert.Equal(0, (l.Tile.X + l.Tile.Y) % 2);
            Assert.Equal(light.GetProperty("radius").GetSingle(), l.Radius);
            Assert.Equal(light.GetProperty("power").GetSingle(), l.Power);
            Assert.Equal(Hex(rule.GetProperty("colour").GetProperty("molten").GetString()!), l.Colour);
            Assert.Null(l.Halo);
        });
    }

    [Fact]
    public void Ash_only_glimmers_one_tile_in_four()
    {
        var map = Map("test", ["ash/grey"], [], Flat(8, 8));

        Assert.Equal(16, Lit(map).Lights.Length);
    }

    [Fact]
    public void A_lava_tiles_exposed_sides_scatter_their_own_lights()
    {
        var radius = TextureRule("lava_side").GetProperty("light").GetProperty("radius").GetSingle();
        var map = Map("test", ["lava/molten"], [], Flat(1, 1, "2:0:0"));

        Assert.Equal(2, Lit(map).Lights.Count(l => l.Radius == radius));
    }

    [Fact]
    public void The_real_lava_map_has_lights_on_its_lava_and_its_braziers()
    {
        var map = Pack.Map("hall_of_the_flame");
        var terrain = new BoardTerrain(map, null);
        var lighting = Lit(map);
        var lava = terrain.Tiles.Where(t => terrain.Look(t).Top.Family == "lava").ToHashSet();

        Assert.NotEmpty(lava);
        Assert.Contains(lighting.Lights, l => lava.Contains(l.Tile));
        Assert.Equal(map.Props.Count(p => p.Id is "brazier" or "altar_runes"), lighting.Lights.Count(l => l.Halo is not null));
    }

    [Fact]
    public void A_sunk_tile_takes_its_light_with_it()
    {
        var map = Map("test", ["cobble/grey", "lava/molten", "void/deep"], [Prop("brazier", 0, 0)], "0:0:0 0:2:2", "0:1:0 0:1:0");
        var sunk = new BoardChanges([], [], [new Tile(0, 0), new Tile(0, 1)]);
        Assert.Single(OnTile(Lit(map), 0, 0));
        Assert.Contains(OnTile(Lit(map), 1, 1), l => l.Radius == TextureRule("lava").GetProperty("light").GetProperty("radius").GetSingle());

        var after = Lit(map, sunk);

        Assert.Empty(OnTile(after, 0, 0));
        Assert.Null(Plan(map, sunk).Items.OfType<Sprite>().Single(s => s.Tile == new Tile(0, 0)).Light);
    }

    [Fact]
    public void A_lava_tile_that_sinks_stops_glowing()
    {
        var map = Map("test", ["lava/molten", "void/deep"], [], "0:0:0 0:1:2", "0:1:2 0:0:0");
        Assert.NotEmpty(OnTile(Lit(map), 0, 0));

        var after = Lit(map, new BoardChanges([], [], [new Tile(0, 0)]));

        Assert.Empty(OnTile(after, 0, 0));
        Assert.NotEmpty(OnTile(after, 1, 1));
    }

    [Fact]
    public void A_raised_tile_lifts_its_light()
    {
        var map = Map("test", ["cobble/grey"], [Prop("brazier", 0, 0)], Flat(1, 1));
        var before = Assert.Single(Lit(map).Lights);

        var after = Assert.Single(Lit(map, new BoardChanges([], [new HeightChange(new Tile(0, 0), 2)], [])).Lights);

        Assert.Equal(before.Source - new Vector2(0, 2 * BoardProjection.HeightStep), after.Source);
        Assert.Equal(before.Ground - new Vector2(0, 2 * BoardProjection.HeightStep), after.Ground);
    }

    [Fact]
    public void A_light_is_full_at_its_foot_and_gone_at_its_radius_on_a_2_to_1_ellipse()
    {
        var map = Map("test", ["cobble/grey"], [Prop("lamp_post", 0, 0)], Flat(1, 1));
        var light = Assert.Single(Lit(map).Lights);
        var reach = light.Radius * BoardLight.PixelsPerTile;

        Assert.Equal(1, light.Falloff(light.Ground), 3);
        Assert.Equal(0, light.Falloff(light.Ground + new Vector2(reach + 80, 0)), 3);
        Assert.Equal(0, light.Falloff(light.Ground + new Vector2(0, reach / 2 + 80)), 3);
        Assert.Equal(light.Falloff(light.Ground + new Vector2(reach / 2, 0)), light.Falloff(light.Ground + new Vector2(0, reach / 4)), 3);
    }

    [Fact]
    public void Falloff_follows_the_recipes_curve_and_the_source_has_its_own_round_glow()
    {
        var map = Map("test", ["cobble/grey"], [Prop("lamp_post", 0, 0)], Flat(1, 1));
        var light = Assert.Single(Lit(map).Lights);
        var reach = light.Radius * BoardLight.PixelsPerTile;

        Assert.Equal(MathF.Pow(0.5f, 1.6f), light.Falloff(light.Ground + new Vector2(reach / 2, 0)), 3);
        Assert.Equal(0.85f, light.Falloff(light.Source), 3);
    }

    [Fact]
    public void Flicker_is_deterministic_and_runs_through_the_curve()
    {
        var map = Map("test", ["cobble/grey"], [Prop("campfire", 0, 0)], Flat(1, 1));
        var light = Assert.Single(Lit(map).Lights);
        var at = 1.3 * BoardLight.CurveStepsPerSecond + light.Phase;
        var step = (int)Math.Floor(at);
        var (from, to) = (light.Curve[step % light.Curve.Length], light.Curve[(step + 1) % light.Curve.Length]);

        Assert.Equal(from + (to - from) * (float)(at - step), Assert.Single(Lit(map).Lights).Flicker(1.3), 4);
        Assert.Equal(light.Flicker(0.25), light.Flicker(0.25 + light.Curve.Length / BoardLight.CurveStepsPerSecond), 3);
        var seen = Enumerable.Range(0, 800).Select(i => light.Flicker(i / 100.0)).ToArray();
        Assert.Equal(light.Curve.Max(), seen.Max(), 2);
        Assert.Equal(light.Curve.Min(), seen.Min(), 2);
    }

    [Fact]
    public void A_light_blends_between_the_steps_of_its_curve()
    {
        var map = Map("test", ["cobble/grey"], [Prop("crystals", 0, 0)], Flat(1, 1));
        var light = Assert.Single(Lit(map).Lights) with { Phase = 0 };
        var step = 1 / BoardLight.CurveStepsPerSecond;

        Assert.Equal(light.Curve[3], light.Flicker(3 * step), 3);
        Assert.Equal((light.Curve[3] + light.Curve[4]) / 2, light.Flicker(3.5 * step), 3);
    }

    [Theory]
    [InlineData("crypt_hall")]
    [InlineData("crystal_cave")]
    [InlineData("hall_of_the_flame")]
    public void Lights_of_a_room_dont_pulse_in_step(string id)
    {
        var flames = Lit(Pack.Map(id)).Lights.Where(l => l.Halo is not null).ToArray();

        Assert.True(flames.Length > 1);
        var steps = flames.Select(l => l.Phase % l.Curve.Length).ToArray();
        Assert.True(steps.Distinct().Count() == flames.Length, string.Join(",", steps));   // where each is in its curve: the phase itself wraps
    }

    [Fact]
    public void The_lava_of_the_finale_doesnt_breathe_as_one()
    {
        var lava = Lit(Pack.Map("hall_of_the_flame")).Lights.Where(l => l.Halo is null).ToArray();

        var steps = lava.Select(l => l.Phase % l.Curve.Length).ToArray();
        Assert.True(steps.Distinct().Count() >= LavaPhases, string.Join(",", steps));
    }

    [Fact]
    public void A_light_that_goes_out_doesnt_shift_the_phase_of_the_rest()
    {
        var map = Map("test", ["cobble/grey", "lava/molten"], [Prop("brazier", 3, 0)], "0:1:0 0:0:0 0:0:0 0:0:0");

        var after = Lit(map, new BoardChanges([], [], [new Tile(0, 0)]));

        Assert.Equal(Lit(map).Lights.Single(l => l.Halo is not null).Phase, after.Lights.Single(l => l.Halo is not null).Phase);
    }

    [Fact]
    public void Two_lights_of_one_prop_dont_pulse_in_step()
    {
        var map = Map("test", ["cobble/grey"], [Prop("desk", 0, 0)], Flat(1, 1));

        Assert.Equal(2, Lit(map).Lights.Select(l => l.Phase % l.Curve.Length).Distinct().Count());
    }

    [Fact]
    public void Lights_are_out_of_step_in_time_and_not_only_on_paper()
    {
        var lights = Lit(Pack.Map("crypt_hall")).Lights;

        var spread = Enumerable.Range(0, 80).Max(i => lights.Max(l => l.Flicker(i / 10.0)) - lights.Min(l => l.Flicker(i / 10.0)));

        Assert.True(spread > 0.1f, $"{spread}");
    }

    [Fact]
    public void Every_map_says_what_light_it_has_so_a_new_one_cant_slip_in_by_a_word_in_its_name()
    {
        var maps = Directory.GetFiles(Path.Combine(Pack.Root, "rules", "maps"), "*.json").Select(f => Path.GetFileNameWithoutExtension(f)!).ToArray();
        var rooms = Pack.Manifest("dungeon").RootElement.GetProperty("lighting").GetProperty("ambient");

        Assert.Equal(StatedLight.Keys.Order(), maps.Order());
        foreach (var id in maps)
        {
            float Lifted(JsonElement a, int c) => a[c].GetSingle() + (1 - a[c].GetSingle()) * BoardLighting.AmbientLift;
            var stated = StatedLight[id] is { } room
                ? new Rgb(Lifted(rooms.GetProperty(room), 0), Lifted(rooms.GetProperty(room), 1), Lifted(rooms.GetProperty(room), 2))
                : new Rgb(1, 1, 1);

            Assert.Equal(stated, Lit(Pack.Map(id)).Ambient);
        }
    }

    [Fact]
    public void A_probe_through_the_grid_is_the_sum_of_every_light_by_hand()
    {
        var lighting = Lit(Busy());
        lighting.Step(0.7);
        var random = new Random(8);

        for (var i = 0; i < 2000; i++)
        {
            var point = new Vector2(random.NextSingle() * 16000 - 8000, random.NextSingle() * 9000 - 800);
            var byHand = ByHand(lighting, point, l => l.Flicker(0.7) * l.Power * BoardLighting.Strength);

            Assert.Equal(byHand, lighting.At(lighting.Probe(point)));
            Assert.Equal(byHand, lighting.At(point));
        }
    }

    [Theory]
    [InlineData("crypt_hall")]
    [InlineData("crystal_cave")]
    [InlineData("hall_of_the_flame")]
    public void The_grid_misses_nothing_on_the_real_rooms_up_to_their_edges(string id)
    {
        var lighting = Lit(Pack.Map(id));
        lighting.Step(0.2);
        var points = lighting.Lights.SelectMany(l => new[]
        {
            l.Ground, l.Source, l.Ground + new Vector2(l.Radius * BoardLight.PixelsPerTile - 0.5f, 0), l.Ground - new Vector2(l.Radius * BoardLight.PixelsPerTile - 0.5f, 0),
            l.Ground + new Vector2(0, l.Radius * BoardLight.PixelsPerTile / 2 - 0.5f), l.Source - new Vector2(0, l.Radius * BoardLight.PixelsPerTile * 0.8f - 0.5f),
        });

        foreach (var point in points)
            Assert.Equal(ByHand(lighting, point, l => l.Flicker(0.2) * l.Power * BoardLighting.Strength), lighting.At(lighting.Probe(point)));
    }

    [Fact]
    public void A_read_looks_only_at_the_lights_near_it_however_many_the_region_has()
    {
        var lighting = Lit(Busy());

        Assert.Equal(960, lighting.Lights.Length);
        Assert.True(lighting.NearAt(lighting.Lights[480].Ground) < 40, $"{lighting.NearAt(lighting.Lights[480].Ground)}");
        Assert.Equal(0, lighting.NearAt(new Vector2(-90000, -90000)));
    }

    [Fact]
    public void Baking_reads_each_light_at_its_mean_whatever_the_flicker_is_doing()
    {
        var lighting = Lit(Pack.Map("hall_of_the_flame"));
        var point = lighting.Lights[3].Ground + new Vector2(12, 6);
        var mean = ByHand(lighting, point, l => l.Curve.Average() * l.Power * BoardLighting.Strength);

        foreach (var seconds in new[] { 0.0, 0.31, 1.7 })
        {
            lighting.Step(seconds);
            Assert.Equal(mean, lighting.MeanAt(point));
            Assert.Equal(mean, lighting.MeanAt(lighting.Probe(point)));
        }
        lighting.Step(0);
        Assert.NotEqual(mean, lighting.At(point));
    }

    [Fact]
    public void One_lighting_runs_across_blocks_so_a_pool_doesnt_stop_at_an_edge()
    {
        var map = Map("test", ["cobble/grey"], [Prop("campfire", 3, 0)], Flat(8, 1));
        var plan = Plan(map);
        var (left, right) = (Block(plan, 0, 4), Block(plan, 4, 8));
        var lighting = BoardLighting.Of([left, right], map.Id, Pack.Art.Lighting);
        var nearEdge = new BoardTerrain(map, null).Centre(new Tile(4, 0));

        Assert.Single(lighting.Lights);
        Assert.True(lighting.Probe(nearEdge).Count > 0);
        Assert.Contains(LitBoard.Build(right, lighting).Items.OfType<LitFace>(), f => f.Face.Tile == new Tile(4, 0) && f.Art && f.Face.Grid == false && f.Probes.Length == 4);
    }

    [Fact]
    public void Blocks_can_be_built_on_other_threads_at_once()
    {
        var map = Pack.Map("hall_of_the_flame");
        var plan = Plan(map);
        var lighting = BoardLighting.Of([plan], map.Id, Pack.Art.Lighting);
        var one = LitBoard.Build(plan, lighting);

        var many = Enumerable.Range(0, 8).AsParallel().Select(_ => LitBoard.Build(plan, lighting).Items.Length).ToArray();

        Assert.All(many, count => Assert.Equal(one.Items.Length, count));
    }

    [Fact]
    public void A_top_in_reach_is_cut_into_cells_and_one_out_of_reach_isnt()
    {
        var map = Map("test", ["cobble/grey"], [Prop("torch", 0, 0)], Flat(14, 1));
        var lit = LitOf(map);

        Assert.Equal(4, TopsOf(lit, 1).Count(f => f.Art));
        Assert.Equal(1, TopsOf(lit, 13).Count(f => f.Art));
    }

    [Fact]
    public void Only_a_tops_cells_are_cut_as_sides_have_one_value_at_each_corner()
    {
        var map = Map("test", ["cobble/grey"], [Prop("torch", 0, 0)], Flat(2, 1));

        Assert.All(LitOf(map).Items.OfType<LitFace>().Where(f => f.Face.Kind == FaceKind.Side), f => Assert.Equal(4, f.Face.Points.Length));
        Assert.Equal(Plan(map).Items.OfType<Face>().Count(f => f.Kind == FaceKind.Side), LitOf(map).Items.OfType<LitFace>().Count(f => f.Face.Kind == FaceKind.Side));
    }

    [Fact]
    public void A_face_the_light_leaves_out_has_no_probes_even_in_reach()
    {
        var map = Map("test", ["cobble/grey"], [Prop("brazier", 0, 0)], Flat(2, 1));
        var changes = new BoardChanges([new ZoneView(1, "grand_sigil", [new Tile(0, 0)])], [], []);

        var tint = Assert.Single(LitOf(map, changes).Items.OfType<LitFace>(), f => f.Face.Kind == FaceKind.Tint);

        Assert.Empty(tint.Probes);
        Assert.Equal(tint.Face.Tint, tint.ColourAt(0, Lit(map, changes)));
    }

    [Fact]
    public void A_zone_sprite_reads_no_light_and_a_prop_reads_it_at_its_middle()
    {
        var map = Map("test", ["cobble/grey"], [Prop("crate", 0, 0)], Flat(2, 1));
        var changes = new BoardChanges([new ZoneView(1, "fire", [new Tile(1, 0)])], [], []);
        var lit = LitOf(map, changes);

        Assert.All(lit.Items.OfType<LitSprite>().Where(s => !s.Sprite.Lit), s => Assert.Null(s.Probe));

        var torchMap = Map("test", ["cobble/grey"], [Prop("torch", 0, 0), Prop("crate", 1, 0)], Flat(2, 1));
        var lighting = Lit(torchMap);
        lighting.Step(0);
        var crate = LitBoard.Build(Plan(torchMap), lighting).Items.OfType<LitSprite>().Single(s => s.Sprite.Tile == new Tile(1, 0));
        Assert.Equal(lighting.At(crate.Sprite.TopLeft + crate.Sprite.Size / 2), crate.LightNow(lighting));
    }

    [Fact]
    public void A_corner_is_the_faces_own_shade_times_the_light_there()
    {
        var map = Map("test", ["cobble/grey"], [Prop("torch", 0, 0)], Flat(2, 1));
        var lighting = Lit(map);
        lighting.Step(0);
        var face = LitBoard.Build(Plan(map), lighting).Items.OfType<LitFace>().First(f => f.Art && f.Face.Kind == FaceKind.Top && f.Face.Tile == new Tile(0, 0));

        for (var corner = 0; corner < 4; corner++)
            Assert.Equal(face.Face.Tint.Times(lighting.At(face.Face.Points[corner])), face.ColourAt(corner, lighting));
    }

    [Fact]
    public void A_faces_tint_is_its_fill_or_white_at_its_shade()
    {
        var map = Map("test", ["cobble/grey"], [], Flat(1, 1));
        var top = Plan(map).Items.OfType<Face>().Single(f => f.Kind == FaceKind.Top);

        Assert.Equal(new Rgb(0.5f, 0.5f, 0.5f), (top with { Fill = null, Shade = 0.5f }).Tint);
        Assert.Equal(new Rgb(0.1f, 0.25f, 0.5f), (top with { Fill = new Rgb(0.2f, 0.5f, 1), Shade = 0.5f }).Tint);
    }

    [Fact]
    public void Colours_multiply_channel_by_channel()
    {
        Assert.Equal(new Rgb(0.5f, 0.25f, 2), new Rgb(1, 0.5f, 4).Times(new Rgb(0.5f, 0.5f, 0.5f)));
    }

    [Fact]
    public void A_figure_reads_the_light_a_share_of_the_way_up_its_sprite()
    {
        Assert.Equal(new Vector2(10, 100 - 40 * BoardLighting.BodyHeight), BoardLighting.BodyOf(new Vector2(10, 100), 40));
    }

    [Fact]
    public void A_glowing_top_in_reach_draws_its_glow_once_after_its_cells()
    {
        var map = Map("test", ["lava/molten"], [], Flat(2, 2));
        var faces = LitOf(map).Items.OfType<LitFace>().Where(f => f.Face.Kind == FaceKind.Top && f.Face.Tile == new Tile(0, 0)).ToArray();

        Assert.Equal(5, faces.Length);
        Assert.All(faces[..4], f => Assert.True(f.Art && !f.Glow && f.Probes.Length == 4));
        Assert.False(faces[4].Art);
        Assert.True(faces[4].Glow);
        Assert.Empty(faces[4].Probes);
        Assert.True(faces[4].Face.Grid);
    }

    [Fact]
    public void A_plain_top_in_reach_keeps_its_outline_over_all_its_cells()
    {
        var map = Map("test", ["cobble/grey"], [Prop("torch", 0, 0)], Flat(1, 1));

        var last = LitOf(map).Items.OfType<LitFace>().Last(f => f.Face.Kind == FaceKind.Top);

        Assert.False(last.Art);
        Assert.False(last.Glow);
        Assert.True(last.Face.Grid);
        Assert.Equal(Plan(map).Items.OfType<Face>().Single(f => f.Kind == FaceKind.Top).Points, last.Face.Points);
    }

    [Fact]
    public void A_top_with_no_outline_or_glow_has_nothing_to_add_after_its_cells()
    {
        var map = Map("test", ["cobble/grey"], [Prop("torch", 0, 0)], Flat(1, 1, "0:0:4"));   // impassable: no grid

        Assert.Equal(4, LitOf(map).Items.OfType<LitFace>().Count(f => f.Face.Kind == FaceKind.Top));
    }

    [Fact]
    public void A_lit_board_keeps_the_plans_order()
    {
        var lit = LitOf(Pack.Map("hall_of_the_flame"));

        Assert.Equal(lit.Items.Select(i => i.Order).Order(), lit.Items.Select(i => i.Order));
    }

    [Fact]
    public void In_the_open_air_a_board_draws_as_it_is_with_nothing_to_read()
    {
        var map = Pack.Map("river_crossing");

        var lit = LitOf(map);

        Assert.Equal(Plan(map).Items.Length, lit.Items.Length);
        Assert.All(lit.Items.OfType<LitFace>(), f => Assert.Empty(f.Probes));
        Assert.All(lit.Items.OfType<LitSprite>(), s => Assert.Null(s.Probe));
        Assert.All(lit.Items.OfType<LitSprite>(), s => Assert.Equal(new Rgb(1, 1, 1), s.LightNow(lit.Lighting)));
    }

    [Fact]
    public void A_prop_with_a_light_glows_and_one_without_doesnt()
    {
        var map = Map("test", ["cobble/grey"], [Prop("brazier", 0, 0), Prop("crate", 1, 0)], Flat(2, 1));

        var sprites = LitOf(map).Items.OfType<LitSprite>().ToArray();

        Assert.True(sprites.Single(s => s.Sprite.Tile == new Tile(0, 0)).Glow);
        Assert.False(sprites.Single(s => s.Sprite.Tile == new Tile(1, 0)).Glow);
    }

    [Fact]
    public void A_prop_on_a_sunk_tile_goes_out_where_the_maps_chasm_is_lava()
    {
        var map = Map("test", ["cobble/grey", "lava/molten"], [Prop("brazier", 0, 0)], "0:0:0 0:1:16");   // the first deep tile is lava
        var sunk = new BoardChanges([], [], [new Tile(0, 0)]);
        Assert.False(new BoardTerrain(map, sunk).Look(new Tile(0, 0)).Void);

        Assert.Null(Plan(map, sunk).Items.OfType<Sprite>().Single().Light);
        Assert.DoesNotContain(Lit(map, sunk).Lights, l => l.Halo is not null);
    }

    [Fact]
    public void On_the_finale_itself_a_prop_that_sinks_goes_out()
    {
        var map = Pack.Map("hall_of_the_flame");
        var brazier = map.Props.First(p => p.Id == "brazier").At;
        var sunk = new BoardChanges([], [], [brazier]);

        Assert.Null(Plan(map, sunk).Items.OfType<Sprite>().Single(s => s.Tile == brazier && s.Image.Path.Contains("brazier")).Light);
    }

    [Fact]
    public void A_prop_on_a_map_void_it_was_built_on_keeps_its_light()
    {
        var map = Map("test", ["void/deep"], [Prop("will_o_wisp", 0, 0)], Flat(1, 1, "0:0:16"));

        Assert.NotNull(Plan(map).Items.OfType<Sprite>().Single().Light);
    }

    [Fact]
    public void Light_is_ambient_plus_every_lights_share_clamped_to_the_recipes_ceiling()
    {
        var lighting = Lit(Pack.Map("crypt_hall"));
        lighting.Step(0);
        var brazier = lighting.Lights.First(l => l.Radius == 3.5f);

        var far = lighting.At(new Vector2(-5000, -5000));
        var near = lighting.At(brazier.Ground);

        Assert.Equal(lighting.Ambient, far);
        Assert.True(near.R > lighting.Ambient.R + 0.5f);
        Assert.True(near.R <= BoardLighting.Ceiling);
    }

    [Fact]
    public void Ten_fires_in_a_row_stop_at_the_ceiling()
    {
        var map = Map("test", ["cobble/grey"], [.. Enumerable.Range(0, 10).Select(i => Prop("campfire", i, 0))], Flat(10, 1));
        var lighting = Lit(map);
        lighting.Step(0);

        Assert.Equal(BoardLighting.Ceiling, lighting.At(lighting.Lights[5].Ground).R, 3);
    }

    [Fact]
    public void A_probe_kept_for_a_point_reads_as_the_point_does_at_any_moment()
    {
        var lighting = Lit(Pack.Map("hall_of_the_flame"));
        var point = lighting.Lights[3].Ground + new Vector2(12, 6);
        var probe = lighting.Probe(point);

        foreach (var seconds in new[] { 0.0, 0.31, 1.7 })
        {
            lighting.Step(seconds);
            Assert.Equal(lighting.At(point), lighting.At(probe));
        }
    }

    [Fact]
    public void A_probe_lists_only_the_lights_in_reach()
    {
        var lighting = Lit(Map("test", ["cobble/grey"], [Prop("torch", 0, 0), Prop("torch", 13, 0)], Flat(14, 1)));

        Assert.Equal(0, lighting.Probe(new Vector2(-5000, -5000)).Count);
        Assert.Equal(1, lighting.Probe(lighting.Lights[0].Ground).Count);
    }

    [Fact]
    public void A_figure_never_falls_below_the_floor_that_keeps_it_readable_and_lights_still_raise_it()
    {
        var lighting = Lit(Pack.Map("crypt_hall"));
        lighting.Step(0);
        var brazier = lighting.Lights.First(l => l.Radius == 3.5f);

        var dark = lighting.ForFigure(new Vector2(-5000, -5000));
        var lit = lighting.ForFigure(brazier.Ground);

        Assert.True(dark.R >= BoardLighting.FigureFloor);
        Assert.True(dark.G >= BoardLighting.FigureFloor);
        Assert.True(dark.B >= BoardLighting.FigureFloor);
        Assert.True(Sum(lit) > Sum(dark));
    }

    [Fact]
    public void In_the_open_air_a_figure_stays_as_drawn()
    {
        var lighting = Lit(Pack.Map("river_crossing"));

        Assert.Equal(new Rgb(1, 1, 1), lighting.ForFigure(new Vector2(0, 0)));
    }

    [Fact]
    public void A_halo_comes_from_the_packs_halos_in_the_nearest_colour()
    {
        var crypt = Lit(Pack.Map("crypt_hall"));
        var cave = Lit(Pack.Map("crystal_cave"));

        Assert.Equal("dungeon/light/halo_torch.png", crypt.Lights.First(l => l.Radius == 3.5f).Halo!.Path);
        Assert.Equal("dungeon/light/halo_fel.png", crypt.Lights.First(l => l.Radius == 2.5f).Halo!.Path);
        Assert.Equal("dungeon/light/halo_arcane.png", cave.Lights.First(l => l.Halo is not null).Halo!.Path);
    }

    [Fact]
    public void Halos_are_additive_on_their_source_and_breathe_with_the_light()
    {
        var lighting = Lit(Pack.Map("crypt_hall"));
        var light = lighting.Lights.First(l => l.Halo is not null);

        var halo = lighting.Halos(0).Single(d => d.TopLeft + d.Size / 2 == light.Source);
        var moments = Enumerable.Range(0, 80).Select(i => lighting.Halos(i / 10.0)[0].Alpha).ToArray();

        Assert.True(halo.Additive);
        Assert.Equal(light.Halo!.Path, halo.Path);
        Assert.True(moments.Max() > moments.Min());
    }

    [Fact]
    public void A_board_with_no_light_has_no_halos()
    {
        Assert.Empty(Lit(Pack.Map("river_crossing")).Halos(1));
    }

    [Fact]
    public void Zones_are_left_out_of_the_light_so_what_the_rules_mean_stays_clear()
    {
        var map = Map("test", ["cobble/grey"], [Prop("brazier", 0, 0)], Flat(2, 1));
        var changes = new BoardChanges([new ZoneView(1, "fire", [new Tile(1, 0)]), new ZoneView(2, "grand_sigil", [new Tile(0, 0)])], [], []);

        var plan = Plan(map, changes);

        Assert.All(plan.Items.OfType<Sprite>().Where(s => s.Light is null), s => Assert.False(s.Lit));
        Assert.All(plan.Items.OfType<Face>().Where(f => f.Kind == FaceKind.Tint), f => Assert.False(f.Lit));
        Assert.All(plan.Items.OfType<Face>().Where(f => f.Kind != FaceKind.Tint), f => Assert.True(f.Lit));
        Assert.True(plan.Items.OfType<Sprite>().Single(s => s.Light is not null).Lit);
    }

    [Fact]
    public void A_prop_with_no_light_of_its_own_is_lit_like_the_ground()
    {
        var map = Map("test", ["cobble/grey"], [Prop("crate", 0, 0)], Flat(1, 1));

        Assert.True(Plan(map).Items.OfType<Sprite>().Single().Lit);
    }

    [Fact]
    public void Only_the_textures_that_carry_a_rule_have_a_glow()
    {
        Assert.NotNull(Pack.Art.Texture(new ArtRef("lava", "molten"))!.Glow);
        Assert.NotNull(Pack.Art.Texture(new ArtRef("lava_side", "molten"))!.Glow);
        Assert.NotNull(Pack.Art.Texture(new ArtRef("ash", "grey"))!.Glow);
        Assert.Null(Pack.Art.Texture(new ArtRef("cobble", "grey"))!.Glow);
    }

    [Fact]
    public void A_glow_is_coloured_by_the_variant_of_the_texture()
    {
        var rule = TextureRule("lava_side").GetProperty("colour");

        Assert.Equal(Hex(rule.GetProperty("cooling").GetString()!), Pack.Art.Texture(new ArtRef("lava_side", "cooling"))!.Glow!.Colour);
    }

    [Fact]
    public void Only_outdoor_props_name_a_mask_and_it_is_a_file_of_the_pack()
    {
        var torch = Pack.Art.Prop("torch", [])!.Light!;
        var brazier = Pack.Art.Prop("brazier", [])!.Light!;

        Assert.True(File.Exists(Path.Combine(Pack.Root, torch.Mask!)));
        Assert.Null(brazier.Mask);
    }

    [Fact]
    public void A_top_splits_into_four_cells_that_keep_its_corners_and_texture()
    {
        var map = Map("test", ["cobble/grey"], [], Flat(1, 1));
        var top = Plan(map).Items.OfType<Face>().Single(f => f.Kind == FaceKind.Top);

        var cells = top.Cells();

        Assert.Equal(4, cells.Length);
        Assert.Equal(top.Points[0], cells[0].Points[0]);
        Assert.Equal(top.Points[1], cells[1].Points[1]);
        Assert.Equal(top.Points[2], cells[2].Points[2]);
        Assert.Equal(top.Points[3], cells[3].Points[3]);
        Assert.Equal(top.Uvs[2], cells[2].Uvs[2]);
        Assert.Equal((top.Points[0] + top.Points[2]) / 2, cells[0].Points[2]);
        Assert.Equal((top.Uvs[0] + top.Uvs[2]) / 2, cells[3].Uvs[1]);
        Assert.All(cells, c => Assert.Equal(top.Order, c.Order));
        Assert.All(cells, c => Assert.False(c.Grid));
    }

    [Fact]
    public void Glow_from_a_mask_keeps_the_pixels_colour_and_takes_the_masks_coverage()
    {
        byte[] sprite = [10, 20, 30, 255, 200, 100, 50, 255, 1, 2, 3, 0];
        byte[] mask = [0, 128, 255];

        var glow = GlowMask.FromMask(sprite, mask);

        Assert.Equal<byte>([10, 20, 30, 0, 200, 100, 50, 128, 1, 2, 3, 0], glow);
    }

    [Theory]
    [InlineData(255, 210, 99, true)]      // the light's own colour
    [InlineData(255, 150, 40, true)]      // a hot orange
    [InlineData(255, 250, 210, true)]     // white hot
    [InlineData(120, 120, 125, false)]    // grey stone
    [InlineData(200, 200, 205, false)]    // pale stone
    [InlineData(60, 40, 20, false)]       // a dim shade of the same hue
    [InlineData(60, 200, 90, false)]      // another colour
    public void Glow_with_no_mask_is_the_brightest_shades_of_the_lights_colour(byte r, byte g, byte b, bool glows)
    {
        var fire = Hex("#ffd263");

        var glow = GlowMask.FromColour([r, g, b, 255], fire);

        Assert.Equal(glows ? (byte)255 : (byte)0, glow[3]);
        Assert.Equal(new[] { r, g, b }, glow[..3]);
    }

    [Fact]
    public void An_empty_pixel_never_glows()
    {
        Assert.Equal(0, GlowMask.FromColour([255, 210, 99, 0], Hex("#ffd263"))[3]);
    }
}
