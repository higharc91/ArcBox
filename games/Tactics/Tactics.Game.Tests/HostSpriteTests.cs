using Tactics.Logic;

namespace Tactics.Game.Tests;

/// <summary><see cref="HostSprite"/>: the sprite key the TV gets for a unit.</summary>
public sealed class HostSpriteTests
{
    private static string? Of(string? sprite, params string[] tags) => HostSprite.Of(sprite, tags);

    [Fact]
    public void A_character_has_no_sprite_of_its_own_so_it_takes_its_classs_preset()
        => Assert.Equal("characters.human_wizard", Of(null, "humanoid", "human", SheetBuilder.ClassTag + "wizard"));

    [Fact]
    public void Legacy_saved_race_tags_keep_their_identity()
        => Assert.Equal("characters.elf_ranger", Of(null, "humanoid", "elf", SheetBuilder.ClassTag + "ranger"));

    [Fact]
    public void A_class_without_race_metadata_still_uses_the_class_alias()
        => Assert.Equal("characters.class_fighter", Of(null, SheetBuilder.ClassTag + "fighter"));

    [Fact]
    public void A_monster_or_a_wild_shape_keeps_its_own()
        => Assert.Equal("beasts.wolf.wildshape", Of("beasts.wolf.wildshape", SheetBuilder.ClassTag + "druid"));

    [Fact]
    public void Race_identity_selects_the_matching_tactical_sheet()
        => Assert.Equal("characters.tiefling_warlock", Of(null, "humanoid", "tiefling", SheetBuilder.RaceTag + "tiefling", SheetBuilder.ClassTag + "warlock"));

    [Fact]
    public void A_sheet_with_neither_has_none_and_the_tv_falls_back()
        => Assert.Null(Of(null, "humanoid"));
}
