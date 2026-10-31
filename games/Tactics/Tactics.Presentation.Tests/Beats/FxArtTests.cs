using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Beats;

/// <summary><see cref="FxArt"/> against the real pack: every key the rules use has art behind it.</summary>
public sealed class FxArtTests
{
    private static readonly Lazy<FxArt> Fx = new(() => FxArt.Load(Pack.Root));

    private static readonly Lazy<RulesPack> Rules = new(() => RulesPack.Load([Path.Combine(Pack.Root, "rules")]));

    private static bool OnDisk(string path) => File.Exists(Path.Combine(Pack.Root, path));

    [Fact]
    public void An_effect_key_names_its_set_and_variant()
    {
        var clip = Fx.Value.Clip("fx.slash.fire")!;

        Assert.Equal("fx/slash_fire.png", clip.Path);
        Assert.Equal((32, 32, 7, 18.0, false), (clip.Width, clip.Height, clip.Frames, clip.Fps, clip.Loop));
        Assert.Equal((16, 16), (clip.AnchorX, clip.AnchorY));
        Assert.Equal(15, clip.Lift);      // "body"
        Assert.True(clip.Additive);
    }

    [Fact]
    public void A_key_with_no_variant_takes_the_default()
        => Assert.Equal("fx/slash_steel.png", Fx.Value.Clip("fx.slash")!.Path);

    [Fact]
    public void A_signature_effect_reads_the_same_way()
    {
        var clip = Fx.Value.Clip("signature.geomancer_lower.grass")!;

        Assert.Equal("signature/geomancer_lower_grass.png", clip.Path);
        Assert.False(clip.Additive);
        Assert.Equal(0, clip.Lift);       // "feet"
    }

    [Theory]
    [InlineData("fx.nonsense")]
    [InlineData("signature.nonsense")]
    [InlineData("nonsense")]
    [InlineData("")]
    public void An_unknown_key_is_null(string key) => Assert.Null(Fx.Value.Clip(key));

    [Fact]
    public void A_projectile_has_a_row_per_heading()
    {
        var arrow = Fx.Value.Shot("projectiles.arrow")!;

        Assert.Equal("projectiles/arrow.png", arrow.Path);
        Assert.Equal((24, 24, 16), (arrow.Width, arrow.Height, arrow.Headings));
    }

    [Fact]
    public void An_effect_used_as_a_projectile_is_a_turning_strip()
    {
        var ball = Fx.Value.Shot("fx.fireball.fire")!;

        Assert.Equal("fx/fireball_fire.png", ball.Path);
        Assert.Equal(1, ball.Headings);
    }

    [Theory]
    [InlineData(0, 0)]     // screen right
    [InlineData(90, 12)]   // screen down: three quarters of the way round
    [InlineData(-90, 4)]   // screen up
    [InlineData(180, 8)]
    public void The_heading_row_goes_counter_clockwise_from_the_right(float degreesDown, int row)
        => Assert.Equal(row, ShotArt.Row(degreesDown * MathF.PI / 180, 16));

    [Fact]
    public void Every_key_in_the_rules_has_art_on_disk()
    {
        var missing = new List<string>();
        foreach (var ability in Rules.Value.Abilities.Values)
        {
            if (ability.Presentation is not { } p)
                continue;
            if (p.Fx is { } fx && (Fx.Value.Clip(fx) is not { } clip || !OnDisk(clip.Path)))
                missing.Add(fx);
            if (p.Projectile is { } shot && (Fx.Value.Shot(shot) is not { } art || !OnDisk(art.Path)))
                missing.Add(shot);
        }

        Assert.Empty(missing.Distinct());
    }
}
