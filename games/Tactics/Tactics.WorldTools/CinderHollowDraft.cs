using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>The Hollow's four forced chambers, with a safe rest before the lair and two reserved future encounter clearings.</summary>
public static class CinderHollowDraft
{
    /// <summary>Builds the authored cave using the existing Ashen Ford material palette and exact battle-map stamps.</summary>
    public static RegionSource Build(RulesPack rules)
    {
        var cave = new RegionSource("cinder_hollow", "The Cinder Hollow", 1, 56, 44)
        {
            Start = new Tile(5, 2), Recovery = new Tile(5, 2),
        };
        cave.NamedSpawns.Add("entrance", cave.Start);
        for (var y = 0; y < cave.Height; y++)
            for (var x = 0; x < cave.Width; x++)
                    cave.Set(new Tile(x, y), 2, "rock_ford/bank", TileFlags.Impassable);
        Floor(4, 1, 2, 11);
        Floor(20, 9, 14, 3);
        Floor(22, 6, 10, 8);       // #79: first reserved wandering arena
        Floor(39, 16, 3, 10);
        Floor(35, 17, 12, 8);      // #79: second reserved wandering arena
        Floor(20, 31, 14, 3);
        Floor(23, 29, 10, 8);      // safe camp: never a wandering arena
        foreach (var (map, at, entry) in new[]
        {
            ("hollow_throat", new Tile(6, 4), new Tile(6, 10)),
            ("hollow_diggings", new Tile(34, 4), new Tile(34, 10)),
            ("hollow_nest", new Tile(34, 26), new Tile(40, 26)),
            ("hollow_lair", new Tile(6, 26), new Tile(19, 32)),
        })
        {
            cave.Stamp(rules.MapFor(map)!, at, 0);
            cave.Beats.Add(new BeatSpot(map, entry));
        }
        // The existing Ford rock gives natural cliff faces; keep a low shelf beside the path,
        // then a higher roof beyond it, without opening shortcuts around the forced chambers.
        var floors = cave.Tiles.Where(p => p.Value.Flags == TileFlags.None).Select(p => p.Key).ToArray();
        foreach (var (at, cell) in cave.Tiles.ToArray())
        {
            if (cave.Stamps.Any(s => at.X >= s.At.X && at.X < s.At.X + 14 && at.Y >= s.At.Y && at.Y < s.At.Y + 12)) continue;
            if (cell.Flags.HasFlag(TileFlags.Impassable) && !floors.Any(f => Math.Abs(at.X - f.X) + Math.Abs(at.Y - f.Y) <= 2))
                cave.Set(at, 3, cell.Material, cell.Flags);
        }
        // Workers keep small lights near the entrance and the mine. Beyond the safe nook,
        // old animal remains and dried stains lead into the bear's bed without blocking the route.
        cave.Props.Add(new RegionProps("candle_stubs", "default", [new(5, 3), new(21, 10), new(33, 10), new(41, 23)]));
        cave.Props.Add(new RegionProps("animal_bones", "default", [new(21, 31)]));
        cave.Props.Add(new RegionProps("dried_blood", "default", [new(21, 33)]));
        return cave;

        void Floor(int left, int top, int width, int height)
        {
            for (var y = top; y < top + height; y++)
                for (var x = left; x < left + width; x++)
                    cave.Set(new Tile(x, y), 0, "rock_ford/bank", TileFlags.None);
        }
    }
}
