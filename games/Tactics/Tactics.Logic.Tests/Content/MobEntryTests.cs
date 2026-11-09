using System.Collections.Immutable;
namespace Tactics.Logic.Tests;

public sealed class MobEntryTests
{
    private static RulesPack Rules => BasePackTests.Rules;
    public static IEnumerable<object[]> Arenas() => Rules.Mobs.Values.SelectMany(m => m).Select(m => new object[] { m.Encounter, m.Arena });

    [Theory]
    [MemberData(nameof(Arenas))]
    public void Every_entry_keeps_one_to_six_heroes_together_on_dry_ground_and_reaches_the_arena(string encounter, string id)
    {
        var map = Rules.MapFor(id)!;
        var terrain = new Terrain(map, ImmutableSortedDictionary<Tile, int>.Empty, []);
        var entries = map.Zones.Where(z => z.Key == "party_spawn" || z.Key.StartsWith(MobSpawns.Prefix, StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(entries);
        foreach (var (zone, tiles) in entries)
        {
            Assert.Equal(6, tiles.Length);
            Assert.All(tiles, t => { Assert.True(terrain.Walkable(t)); Assert.False(map[t].Flags.HasFlag(TileFlags.Water)); });
            var reached = WorldWalk.Reach(terrain, tiles[0], ClassDef.DefaultJump);
            var walkable = Enumerable.Range(0, map.Width).SelectMany(x => Enumerable.Range(0, map.Height).Select(y => new Tile(x, y))).Where(terrain.Walkable).ToArray();
            Assert.Empty(walkable.Where(t => !reached.Contains(t)));
            var pending = tiles.ToHashSet(); var queue = new Queue<Tile>(); queue.Enqueue(tiles[0]); pending.Remove(tiles[0]);
            while (queue.TryDequeue(out var at))
                foreach (var next in new[] { new Tile(at.X + 1, at.Y), new Tile(at.X - 1, at.Y), new Tile(at.X, at.Y + 1), new Tile(at.X, at.Y - 1) })
                    if (pending.Remove(next)) queue.Enqueue(next);
            Assert.Empty(pending);
            for (var size = 1; size <= 6; size++)
            {
                ImmutableArray<PartyMember> party = [.. Enumerable.Range(0, size).Select(i => new PartyMember(new("hero" + i),
                    SheetBuilder.DefaultCharacter("Hero" + i, "human", "fighter", null, 3, Rules), new AiControlled("companion")))];
                var setup = Encounters.Setup(encounter, party, Difficulty.Normal, Rules, zone);
                Assert.Equal(tiles.Take(size), setup.Units.Where(u => u.Sheet.Side == Side.Party).Select(u => u.At));
                Assert.Equal(setup.Units.Length, setup.Units.Select(u => u.At).Distinct().Count());
            }
        }
    }

    [Theory]
    [MemberData(nameof(Arenas))]
    public void Contact_selects_the_closest_authored_entry_with_deterministic_ties(string encounter, string id)
    {
        Assert.Equal(id, Rules.Encounters[encounter].Map);
        var map = Rules.MapFor(id)!;
        foreach (var contact in Enumerable.Range(-1, map.Width + 2).SelectMany(x => Enumerable.Range(-1, map.Height + 2).Select(y => new Tile(x, y))))
        {
            var selected = MobSpawns.For(map, contact);
            int Distance(string zone) => map.Zones[zone].Min(t => Math.Abs(t.X - contact.X) + Math.Abs(t.Y - contact.Y));
            Assert.Equal(map.Zones.Keys.Where(k => k.StartsWith(MobSpawns.Prefix, StringComparison.Ordinal)).Min(Distance), Distance(selected));
            Assert.Equal(selected, MobSpawns.For(map, contact));
        }
    }
}
