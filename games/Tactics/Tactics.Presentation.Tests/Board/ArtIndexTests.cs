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
