using System.Text.Json;
using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class EnvironmentCoverageTests
{
    [Fact]
    public void Reactive_encounters_are_enabled_or_have_an_exact_documented_exception()
    {
        var rules = BasePackTests.Rules;
        using var policy = JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(BaseContent.Root,
            "../../../tools/tactics-sim/environment-policy.json"))));
        var exceptions = policy.RootElement.GetProperty("exceptions");
        foreach (var encounter in rules.Encounters.Values)
        {
            var map = rules.MapFor(encounter.Map)!;
            var tiles = from y in Enumerable.Range(0, map.Height) from x in Enumerable.Range(0, map.Width) select new Tile(x, y);
            var fuel = tiles.Count(t => (map[t].Flags & TileFlags.Water) == 0 && rules.Environment.IsFuelMaterial(map.Materials[map[t].Material]));
            var shallows = tiles.Count(t => (map[t].Flags & (TileFlags.Water | TileFlags.Deep)) == TileFlags.Water);
            var documented = exceptions.TryGetProperty(encounter.Id, out var reason);
            if (documented)
            {
                Assert.False(encounter.Environment);
                Assert.Equal(fuel, reason.GetProperty("fuel").GetInt32());
                Assert.Equal(shallows, reason.GetProperty("shallows").GetInt32());
                Assert.True(reason.GetProperty("reason").GetString()!.Length > 40);
            }
            Assert.True(fuel < 10 && shallows < 10 || encounter.Environment || documented,
                $"{encounter.Id}: {fuel} fuel / {shallows} shallows needs reactions or an explicit policy exception");
        }
        Assert.All(exceptions.EnumerateObject(), p => Assert.True(rules.Encounters.ContainsKey(p.Name)));
    }

    [Fact]
    public void Reactive_landings_reach_the_actual_enemy_positions()
    {
        var rules = BasePackTests.Rules;
        var setup = Encounters.Setup("mob_rooting_boars", BasePackTests.Party(4, rules), Difficulty.Normal, rules);
        var writer = new BattleWriter(Battle.Start(setup, rules, 7).State, rules);
        Assert.All(setup.Units.Where(u => u.Sheet.Side == Side.Enemy), u => Assert.True(Surfaces.Fuel(writer, rules.Environment, u.At)));
        var cave = rules.MapFor("crystal_cave")!;
        Assert.All(cave.Zones["chasm_ledge"], t => Assert.True(rules.Environment.IsFuelMaterial(cave.Materials[cave[t].Material])));
    }

    [Fact]
    public void Molten_lava_is_a_deep_fall_not_an_unstated_fire_surface()
    {
        var rules = BasePackTests.Rules;
        var map = rules.MapFor("hall_of_the_flame")!;
        var terrain = new Terrain(map, ImmutableSortedDictionary<Tile, int>.Empty, ImmutableSortedSet<Tile>.Empty);
        var lava = (from y in Enumerable.Range(0, map.Height) from x in Enumerable.Range(0, map.Width)
                    let t = new Tile(x, y) where map.Materials[map[t].Material].StartsWith("lava/", StringComparison.Ordinal) select t).ToArray();
        Assert.NotEmpty(lava);
        Assert.All(lava, t =>
        {
            Assert.True(terrain.Deep(t));
            Assert.False(terrain.Walkable(t));
            Assert.False(rules.Environment.IsFuelMaterial(map.Materials[map[t].Material]));
        });
    }
}
