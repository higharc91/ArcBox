using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="ArtIndex"/> and <see cref="MaterialLook"/>: everything a sample map asks for has real art.</summary>
public sealed class ArtIndexTests
{
    private static void AssertExists(ArtImage? image, string what)
    {
        Assert.True(image is not null, $"{what} has no art");
        Assert.True(File.Exists(Path.Combine(Pack.Root, image.Path)), $"{what}: {image.Path} is missing");
    }

    public static TheoryData<string> Materials()
        => new(Pack.Maps.SelectMany(id => Pack.Map(id).Materials).Distinct());

    [Theory]
    [MemberData(nameof(Materials))]
    public void Every_material_on_the_sample_maps_has_a_top_and_sides(string material)
    {
        var look = MaterialLook.Of(material);

        AssertExists(Pack.Art.Texture(look.Top), $"{material} top");
        AssertExists(Pack.Art.Texture(look.Side), $"{material} side");
        AssertExists(Pack.Art.Texture(look.Chasm), $"{material} chasm side");
        if (look.Lip is { } lip)
            AssertExists(Pack.Art.Texture(lip), $"{material} lip");
    }

    [Theory]
    [InlineData("lava/molten", "lava_side")]
    [InlineData("basalt/dark", "basalt_side")]
    [InlineData("crypt_tiles/fel", "dungeon_wall")]
    [InlineData("dungeon_floor_cracked/grey", "dungeon_wall")]
    public void A_material_of_the_new_maps_has_the_side_that_goes_with_it(string material, string side)
        => Assert.Equal(side, MaterialLook.Of(material).Side.Family);

    [Fact]
    public void Fel_crypt_tiles_stand_on_dark_walls_as_the_pack_has_no_fel_ones()
        => Assert.Equal(new ArtRef("dungeon_wall", "dark"), MaterialLook.Of("crypt_tiles/fel").Side);

    [Theory]
    [InlineData("path/packed", "dirt_side", "brown")]
    [InlineData("ash/grey", "dirt_side", "dark")]
    [InlineData("mud/swamp", "dirt_side", "dark")]
    [InlineData("bog/duckweed", "dirt_side", "dark")]
    [InlineData("forest_floor/leafy", "dirt_side", "brown")]
    [InlineData("forest_needles/pine", "dirt_side", "brown")]
    [InlineData("sand/desert", "sandstone_side", "desert")]
    [InlineData("dunes/gold", "sandstone_side", "desert")]
    [InlineData("sandstone/red", "sandstone_side", "red")]
    [InlineData("snow/fresh", "ice_side", "glacier")]
    [InlineData("ice/clear", "ice_side", "clear")]
    [InlineData("planks/dark", "plank_side", "dark")]
    [InlineData("cobble/warm", "ashlar_side", "warm")]
    [InlineData("flagstone/warm", "ashlar_side", "warm")]
    [InlineData("pavers/warm", "ashlar_side", "warm")]
    public void Landscape_surfaces_keep_their_own_material_at_exposed_edges(string material, string family, string variant)
    {
        var look=MaterialLook.Of(material);
        Assert.Equal(new ArtRef(family,variant),look.Side);
        AssertExists(Pack.Art.Texture(look.Side),material);
    }

    [Fact]
    public void Wooden_walkways_are_decks_and_snow_keeps_its_cap()
    {
        Assert.True(MaterialLook.Of("planks/oak").Deck);
        Assert.True(MaterialLook.Of("planks/dark").Deck);
        Assert.Equal(new ArtRef("snow_lip","fresh"),MaterialLook.Of("snow/fresh").Lip);
        Assert.Equal(new ArtRef("snow_lip","fresh"),MaterialLook.Of("grass/frost").Lip);
        Assert.Equal(new ArtRef("snow_lip","fresh"),MaterialLook.Of("forest_needles/snowy").Lip);
    }

    public static TheoryData<string> Props()
        => new(Pack.Maps.SelectMany(id => Pack.Map(id).Props).Select(p => p.Id).Distinct());

    [Theory]
    [MemberData(nameof(Props))]
    public void Every_prop_on_the_sample_maps_has_art(string id)
    {
        var prop = Pack.Art.Prop(id, []);

        Assert.NotNull(prop);
        AssertExists(prop.Image, id);
    }

    [Fact]
    public void A_variant_the_set_does_not_have_falls_back_to_its_first()
    {
        Assert.Equal("dungeon/textures/top/mine_boards_mine.png", Pack.Art.Texture(new ArtRef("mine_boards", "oak"))!.Path);
        Assert.Equal("terrain/top/flagstone_grey.png", Pack.Art.Texture(new ArtRef("flagstone", "mossy"))!.Path);
    }

    [Fact]
    public void Art_the_pack_does_not_have_is_null()
    {
        Assert.Null(Pack.Art.Texture(new ArtRef("no_such_family", "x")));
        Assert.Null(Pack.Art.Prop("no_such_prop", []));
        Assert.Null(Pack.Art.Hazard("no_such_hazard"));
    }

    [Fact]
    public void A_prop_takes_the_first_preferred_variant_it_has()
    {
        Assert.EndsWith("rock_large_grey.png", Pack.Art.Prop("rock_large", ["lush", "grey", "dark"])!.Image.Path);
        Assert.EndsWith("sarcophagus_dark.png", Pack.Art.Prop("sarcophagus", ["dark"])!.Image.Path);
        Assert.EndsWith("crate_oak.png", Pack.Art.Prop("crate", ["dark"])!.Image.Path);
    }

    [Fact]
    public void A_prop_keeps_its_size_anchor_and_frames()
    {
        var brazier = Pack.Art.Prop("brazier", [])!;

        Assert.Equal((32, 48), (brazier.Width, brazier.Height));
        Assert.Equal((16, 41), (brazier.AnchorX, brazier.AnchorY));
        Assert.Equal(8, brazier.Image.Frames);
    }

    [Fact]
    public void A_hazard_has_its_ground_frames_and_any_over_layer()
    {
        var burning = Pack.Art.Hazard("burning")!;

        AssertExists(burning.Ground, "burning");
        AssertExists(burning.Over, "burning over");
        Assert.Equal(8, burning.Ground.Frames);
        Assert.Null(Pack.Art.Hazard("ice")!.Over);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.17, 1)]
    [InlineData(1.34, 0)]
    public void An_animated_image_loops_at_its_fps(double seconds, int frame)
        => Assert.Equal(frame, new ArtImage("x.png", 8, 6).FrameAt(seconds));

    [Fact]
    public void A_still_image_is_always_frame_zero()
        => Assert.Equal(0, new ArtImage("x.png", 1, 0).FrameAt(100));

    [Theory]
    [InlineData(0f, 0, 0f)]
    [InlineData(1f, 0, 0.125f)]
    [InlineData(0.5f, 3, 0.4375f)]
    public void A_frames_u_lands_inside_its_part_of_the_strip(float u, int frame, float expected)
        => Assert.Equal(expected, new ArtImage("x.png", 8, 6).FrameU(u, frame));
}
