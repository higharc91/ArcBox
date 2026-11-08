using System.Collections.Immutable;
using System.Numerics;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Tests.Board;

public sealed class SpellLightingTests
{
    [Fact]
    public void A_real_lightning_beam_lights_its_length_instead_of_only_the_casters_feet()
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var sample = FxShowcase.Sample("wizard", new("wizard.lightning_bolt"), 5, rules, 1);
        var player = new BeatPlayer(FxArt.Load(Pack.Root), UnitArt.Load(Pack.Root)) { EnhancedEffects = true };
        var terrain = new BoardTerrain(Pack.Map(sample.Before.Map), sample.Before.Board);
        player.Receive(sample.Before); player.Receive(sample.After);
        var reached = false;
        for (var i = 0; i < 200; i++)
        {
            player.Tick(1.0 / 60);
            var sources = player.Frame(terrain).Lights.Where(l => l.Radius > 3 && l.Colour.B > l.Colour.R && l.Power > .1f).ToArray();
            if (sources.Length == 3)
            {
                Assert.True(Vector2.Distance(sources[0].Source, sources[2].Source) > 50);
                reached = true;
            }
        }
        Assert.True(reached);
        Assert.Empty(player.Frame(terrain).Lights);
    }

    [Fact]
    public void Spell_fire_keeps_lighting_the_cave_between_turns_until_the_zone_is_removed()
    {
        var map = Pack.Map("crypt_hall");
        var tile = new Tile(5, 5);
        var burning = new BoardTerrain(map, new BoardChanges([new ZoneView(1, "burning", [tile])], [], []));
        var lights = SpellLighting.ForZones(burning, 100, false);
        var light = Assert.Single(lights);
        Assert.True(light.Colour.R > light.Colour.G);
        Assert.Equal(burning.Centre(tile), light.Ground);
        Assert.NotEmpty(SpellLighting.ForZones(burning, 1000, true));
        Assert.Empty(SpellLighting.ForZones(new BoardTerrain(map, BoardChanges.None), 1001, false));
    }

    [Fact]
    public void Fire_warms_nearby_surfaces_and_bodies_but_not_distant_tiles()
    {
        var dark = new Rgb(.1f, .1f, .15f);
        var ground = new Vector2(100, 100);
        var fire = SpellEmission.ForAsset("signature.spell_fireball.fire", true)!;
        ImmutableArray<SpellLight> lights = [fire.At(ground - new Vector2(0, 16), ground)];
        var floor = SpellLighting.Apply(dark, ground, lights);
        var body = SpellLighting.Apply(dark, ground - new Vector2(0, 24), lights);
        Assert.True(floor.R > floor.G && floor.G > floor.B);
        Assert.True(body.R > dark.R);
        Assert.Equal(dark, SpellLighting.Apply(dark, ground + new Vector2(500, 0), lights));
        Assert.Equal(dark, SpellLighting.Apply(dark, ground, []));
        Assert.Equal(new Rgb(BoardLighting.Ceiling, BoardLighting.Ceiling, BoardLighting.Ceiling),
            SpellLighting.Apply(new(2, 2, 2), ground, lights));
    }

    [Fact]
    public void Ordinary_arrows_dust_and_poison_emit_no_light_but_magic_has_its_own_colour()
    {
        Assert.Null(SpellEmission.ForAsset("projectiles.arrow", false));
        Assert.Null(SpellEmission.ForAsset("fx.dust.sand", false));
        Assert.Null(SpellEmission.ForAsset("projectiles.poison", false));
        Assert.Null(SpellEmission.ForAsset("signature.duelist_riposte.silver", true));
        var storm = SpellEmission.ForAsset("projectiles.lightning", true)!;
        Assert.True(storm.Colour.B > storm.Colour.R);
        var holy = SpellEmission.ForAsset("fx.heal.holy", true)!;
        Assert.True(holy.Colour.R > holy.Colour.B);
        var spark = SpellEmission.ForAsset("fx.hit.spark", true)!;
        Assert.True(spark.Power < holy.Power / 4);
        Assert.True(spark.Colour.R > spark.Colour.B);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_fire_spell_light_moves_with_its_projectile_then_clears_without_changing_the_board(bool reduced)
    {
        var sample = FxShowcase.StepZero(Pack.Root)[3];
        var player = new BeatPlayer(FxArt.Load(Pack.Root), UnitArt.Load(Pack.Root))
        { EnhancedEffects = true, ReducedEffects = reduced };
        var terrain = new BoardTerrain(Pack.Map(sample.Before.Map), sample.Before.Board);
        player.Receive(sample.Before);
        Assert.Empty(player.Frame(terrain).Lights);
        player.Receive(sample.After);
        var positions = new HashSet<Vector2>();
        var litFrames = 0;
        for (var i = 0; i < 400; i++)
        {
            player.Tick(1.0 / 60);
            foreach (var light in player.Frame(terrain).Lights)
            {
                if (light.Radius == 2)
                {
                    Assert.True(light.Colour.R > light.Colour.G);
                    positions.Add(light.Source);
                }
                if (light.Colour.R > light.Colour.G) litFrames++;
            }
        }
        Assert.True(positions.Count > 3);
        Assert.True(litFrames > 5);
        Assert.Empty(player.Frame(terrain).Lights);
        Assert.Equal(sample.After.Board, player.Shown!.Board);
    }
}
