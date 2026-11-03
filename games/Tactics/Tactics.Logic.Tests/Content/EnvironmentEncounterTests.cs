namespace Tactics.Logic.Tests;

public sealed class EnvironmentEncounterTests
{
    [Fact]
    public void Ford_stormcaller_is_isolated_to_the_assault_and_keeps_its_fire_spell()
    {
        var rules = BasePackTests.Rules;
        Assert.True(rules.Encounters["river_crossing"].Environment);
        Assert.True(rules.Encounters["ford_assault"].Environment);
        var spawn = Assert.Single(rules.Encounters["ford_assault"].Enemies, e => e.Unit == "goblin_ford_shaman");
        Assert.Equal(new Tile(9, 7), spawn.At);
        Assert.DoesNotContain(rules.Encounters.Where(e => e.Key != "ford_assault").SelectMany(e => e.Value.Enemies), e => e.Unit == "goblin_ford_shaman");
        var shaman = rules.Monsters[spawn.Unit];
        Assert.Contains(new AbilityId("goblin_shaman.cinder_bolt"), shaman.Abilities);
        Assert.Contains(new AbilityId("goblin_shaman.storm_hex"), shaman.Abilities);
        Assert.DoesNotContain(new AbilityId("goblin_shaman.storm_hex"), rules.Monsters["goblin_shaman"].Abilities);
        Assert.Equal("characters.goblin_shaman", shaman.Sprite);
        var storm = rules.Abilities[new AbilityId("goblin_shaman.storm_hex")];
        Assert.Equal(0, storm.Tier);
        Assert.Equal(1, storm.Cost.Action);
        Assert.Equal(6, storm.Target.Range);
        var hit = Assert.IsType<AttackEffect>(Assert.Single(storm.Effects));
        var damage = Assert.IsType<DamageEffect>(Assert.Single(hit.OnHit));
        Assert.Equal("lightning", damage.Type);
        Assert.Equal(4.5, damage.Dice!.At(1).Average);
        Assert.Equal(4.5, damage.Dice!.At(10).Average);
    }

    [Fact]
    public void Warlord_bridge_is_enabled_and_its_existing_oak_deck_is_fuel()
    {
        var rules = BasePackTests.Rules;
        var state = Battle.Start(Encounters.Setup("warlord_bridge", BasePackTests.Party(4, rules), Difficulty.Normal, rules), rules, 7).State;
        Assert.True(state.Environment);
        var writer = new BattleWriter(state, rules);
        Assert.All(rules.MapFor("warlord_bridge")!.Zones["bridge"], tile => Assert.True(Surfaces.Fuel(writer, rules.Environment, tile)));
        Assert.Contains(rules.Encounters["warlord_bridge"].Triggers, trigger => trigger.Id == "bridge_collapse");
    }

    [Fact]
    public void Forest_ruins_enables_only_the_small_dry_patch_as_grass_fuel()
    {
        var rules = BasePackTests.Rules;
        var state = Battle.Start(Encounters.Setup("forest_ruins", BasePackTests.Party(4, rules), Difficulty.Normal, rules), rules, 7).State;
        Assert.True(state.Environment);
        var map = rules.MapFor("forest_ruins")!;
        var dry = from y in Enumerable.Range(0, map.Height) from x in Enumerable.Range(0, map.Width)
                  let tile = new Tile(x, y) where map.Materials[map[tile].Material] == "grass/dry" select tile;
        var expected = from y in Enumerable.Range(3, 3) from x in Enumerable.Range(4, 3) select new Tile(x, y);
        Assert.Equal(expected, dry);
        var writer = new BattleWriter(state, rules);
        Assert.All(dry, tile => Assert.True(Surfaces.Fuel(writer, rules.Environment, tile)));
        Assert.False(Surfaces.Fuel(writer, rules.Environment, new Tile(3, 4)));
    }

    [Fact]
    public void Fords_local_autumn_palette_keeps_the_same_fuel_property_as_autumn_grass()
    {
        var rules = BasePackTests.Rules;
        var state = Battle.Start(Encounters.Setup("river_crossing", BasePackTests.Party(4, rules), Difficulty.Normal, rules), rules, 7).State;
        Assert.Contains("grass_ford/autumn", rules.Environment.Fuel);
        Assert.True(Surfaces.Fuel(new BattleWriter(state, rules), rules.Environment, new Tile(2, 2)));
    }
}
