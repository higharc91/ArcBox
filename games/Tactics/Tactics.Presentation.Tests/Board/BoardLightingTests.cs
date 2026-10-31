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

        Assert.Equal(light.Flicker(1.3), light.Flicker(1.3));
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
    public void Lights_of_a_room_dont_pulse_in_step(string id)
    {
        var flames = Lit(Pack.Map(id)).Lights;

        Assert.True(flames.Length > 1);
        Assert.True(flames.Select(l => l.Phase).Distinct().Count() == flames.Length, string.Join(",", flames.Select(l => l.Phase)));
    }

    [Fact]
    public void The_lava_of_the_finale_doesnt_breathe_as_one()
    {
        var lava = Lit(Pack.Map("hall_of_the_flame")).Lights.Where(l => l.Halo is null).ToArray();

        Assert.True(lava.Select(l => l.Phase).Distinct().Count() >= 4);
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
