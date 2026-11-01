using Tactics.Logic;

namespace Tactics.Game.Tests;

/// <summary><see cref="HostSprite"/>: the sprite key the TV gets for a unit.</summary>
public sealed class HostSpriteTests
{
    private static string? Of(string? sprite, params string[] tags) => HostSprite.Of(sprite, tags);

    [Fact]
    public void A_character_has_no_sprite_of_its_own_so_it_takes_its_classs_preset()
        => Assert.Equal("characters.class_wizard", Of(null, "humanoid", "human", SheetBuilder.ClassTag + "wizard"));

    [Fact]
    public void A_monster_or_a_wild_shape_keeps_its_own()
        => Assert.Equal("beasts.wolf.wildshape", Of("beasts.wolf.wildshape", SheetBuilder.ClassTag + "druid"));

    [Fact]
    public void A_sheet_with_neither_has_none_and_the_tv_falls_back()
        => Assert.Null(Of(null, "humanoid"));
}
