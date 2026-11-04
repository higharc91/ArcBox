using System.Collections.Immutable;
using System.Numerics;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;
using Xunit;

namespace Tactics.Presentation.Tests.World;

public sealed class AmbientPlanTests
{
    private static readonly ImmutableSortedDictionary<string, ImmutableArray<Tile>> NoZones =
        ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty;

    private static BattleMap Map(int width, int height, Func<int, int, (int Height, byte Material, byte Flags)> tileAt)
    {
        var bytes = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var (h, m, f) = tileAt(x, y);
                bytes[(y * width + x) * 3] = (byte)h;
                bytes[(y * width + x) * 3 + 1] = m;
                bytes[(y * width + x) * 3 + 2] = f;
            }
        return BattleMap.FromBytes("test", width, height, bytes, ["grass/lush", "dirt/brown", "water/clear"], [], NoZones);
    }

    [Fact]
    public void At_dusk_there_are_a_few_fireflies_over_the_fields_and_no_butterflies()
    {
        var map = Map(64, 64, (x, y) => (0, (byte)1, (byte)0));
        var ambient = new AmbientPlan(new BoardTerrain(map, null), map, AmbientManifest.Load(Pack.Root), seed: 42, dusk: true);
        var items = ambient.ActiveAt(1, new ArtArea(new Vector2(-4000), new Vector2(4000)));

        Assert.DoesNotContain(items, i => i.Kind == "butterfly");
        Assert.All(items.Where(i => i.Kind == "fireflies"), f => Assert.True(f.Add));
        Assert.InRange(ambient.FlittingCount, 20, 110);
    }

    [Fact]
    public void Steam_is_thickest_where_the_river_is_warm_and_thins_away_from_it()
    {
        // A river down the map, its stamped water at the top.
        ImmutableHashSet<Tile> warm = [.. Enumerable.Range(0, 4).SelectMany(y => Enumerable.Range(14, 4).Select(x => new Tile(x, y)))];
        var bytes = new byte[32 * 96 * 3];
        for (var y = 0; y < 96; y++)
            for (var x = 14; x < 18; x++)
                (bytes[(y * 32 + x) * 3 + 1], bytes[(y * 32 + x) * 3 + 2]) = (2, (byte)(TileFlags.Water | TileFlags.Deep));
        var map = BattleMap.FromBytes("test", 32, 96, bytes, ["grass/lush", "dirt/brown", "water/clear"], [], NoZones, warm);
        var ambient = new AmbientPlan(new BoardTerrain(map, null), map, AmbientManifest.Load(Pack.Root), seed: 42, dusk: true);

        var steam = ambient.ActiveAt(1, new ArtArea(new Vector2(-8000), new Vector2(8000))).Where(i => i.Kind == "river_steam").ToArray();

        Assert.NotEmpty(steam);
        Assert.True(steam.Count(s => s.Tile.Y < 32) > steam.Count(s => s.Tile.Y >= 32) * 2);
    }

    [Fact]
    public void Nothing_steams_with_no_stamped_water()
    {
        var map = Map(32, 32, (x, y) => (0, (byte)2, (byte)(TileFlags.Water | TileFlags.Deep)));
        Assert.Equal(0, new AmbientPlan(new BoardTerrain(map, null), map, AmbientManifest.Load(Pack.Root), seed: 42).SteamCount);
    }

    [Fact]
    public void Butterflies_stay_over_grass()
    {
        // Half grass, half dirt
        var map = Map(32, 32, (x, y) => (0, (byte)(x < 16 ? 0 : 1), (byte)0));
        var terrain = new BoardTerrain(map, null);
        var manifest = AmbientManifest.Load(Pack.Root);
        var ambient = new AmbientPlan(terrain, map, manifest, seed: 42);

        var view = new ArtArea(new Vector2(0, 0), new Vector2(1000, 1000));
        for (var t = 0.0; t < 5.0; t += 0.5)
        {
            var items = ambient.ActiveAt(t, view);
            var butterflies = items.Where(i => i.Kind == "butterfly").ToArray();
            Assert.NotEmpty(butterflies);
            foreach (var b in butterflies)
            {
                var tile = b.Tile;
                Assert.True(tile.X < 16, $"Butterfly at {tile} should be over grass (x < 16)");
                Assert.Contains("grass", map.Materials[map[tile].Material]);
            }
        }
    }

    [Fact]
    public void Fish_stay_in_deep_water()
    {
        // Half deep water, half grass
        var map = Map(32, 32, (x, y) => x < 16
            ? (0, (byte)2, (byte)(TileFlags.Water | TileFlags.Deep))
            : (0, (byte)0, (byte)0));
        var terrain = new BoardTerrain(map, null);
        var manifest = AmbientManifest.Load(Pack.Root);
        var ambient = new AmbientPlan(terrain, map, manifest, seed: 42);

        var view = new ArtArea(new Vector2(0, 0), new Vector2(1000, 1000));
        var seenFish = false;
        for (var t = 0.0; t < 10.0; t += 0.25)
        {
            var items = ambient.ActiveAt(t, view);
            var fish = items.Where(i => i.Kind == "fish").ToArray();
            if (fish.Length > 0)
            {
                seenFish = true;
                foreach (var f in fish)
                {
                    var tile = f.Tile;
                    Assert.True(tile.X < 16, $"Fish at {tile} should be in deep water (x < 16)");
                    Assert.True(map[tile].Flags.HasFlag(TileFlags.Water | TileFlags.Deep));
                }
            }
        }
        Assert.True(seenFish, "Expected to see fish jump over the observation period");
    }

    [Fact]
    public void The_same_region_and_seed_give_the_same_life()
    {
        var map = Map(32, 32, (x, y) => (0, (byte)(x % 3 == 0 ? 0 : x % 3 == 1 ? 1 : 2), (byte)(x % 3 == 2 ? TileFlags.Water | TileFlags.Deep : 0)));
        var terrain = new BoardTerrain(map, null);
        var manifest = AmbientManifest.Load(Pack.Root);

        var a = new AmbientPlan(terrain, map, manifest, seed: 12345);
        var b = new AmbientPlan(terrain, map, manifest, seed: 12345);

        var view = new ArtArea(new Vector2(0, 0), new Vector2(600, 600));
        for (var t = 0.0; t < 3.0; t += 0.5)
        {
            var itemsA = a.ActiveAt(t, view);
            var itemsB = b.ActiveAt(t, view);
            Assert.Equal(itemsA.Length, itemsB.Length);
            for (var i = 0; i < itemsA.Length; i++)
            {
                Assert.Equal(itemsA[i].Kind, itemsB[i].Kind);
                Assert.Equal(itemsA[i].Variant, itemsB[i].Variant);
                Assert.Equal(itemsA[i].Tile, itemsB[i].Tile);
                Assert.Equal(itemsA[i].Position, itemsB[i].Position);
                Assert.Equal(itemsA[i].Frame, itemsB[i].Frame);
            }
        }
    }

    [Fact]
    public void Nothing_runs_away_from_the_camera()
    {
        var map = Map(64, 64, (x, y) => (0, 0, (byte)0));
        var terrain = new BoardTerrain(map, null);
        var manifest = AmbientManifest.Load(Pack.Root);
        var ambient = new AmbientPlan(terrain, map, manifest, seed: 99);

        // View A at the top left
        var viewA = new ArtArea(new Vector2(0, 0), new Vector2(300, 300));
        var itemsA = ambient.ActiveAt(1.0, viewA);
        Assert.All(itemsA, item =>
        {
            // Position must be near the view margin (within 128 px of view)
            Assert.InRange(item.Position.X, viewA.Min.X - 128, viewA.Max.X + 128);
            Assert.InRange(item.Position.Y, viewA.Min.Y - 128, viewA.Max.Y + 128);
        });

        // View B moved far away to the bottom right
        var viewB = new ArtArea(new Vector2(1000, 1000), new Vector2(1300, 1300));
        var itemsB = ambient.ActiveAt(2.0, viewB);
        Assert.All(itemsB, item =>
        {
            Assert.InRange(item.Position.X, viewB.Min.X - 128, viewB.Max.X + 128);
            Assert.InRange(item.Position.Y, viewB.Min.Y - 128, viewB.Max.Y + 128);
        });
    }

    [Fact]
    public void A_world_with_no_ambient_art_still_plays()
    {
        var map = Map(16, 16, (_, _) => (0, 0, (byte)0));
        var terrain = new BoardTerrain(map, null);
        var ambient = new AmbientPlan(terrain, map, manifest: null);

        var view = new ArtArea(new Vector2(0, 0), new Vector2(500, 500));
        var items = ambient.ActiveAt(1.0, view);

        Assert.Empty(items);
    }

    private static readonly Lazy<(BattleMap Map, AmbientPlan Plan)> Ford = new(() =>
    {
        var map = Pack.Ground("ashen_ford").Map;
        return (map, new AmbientPlan(new BoardTerrain(map, null), map, AmbientManifest.Load(Pack.Root), seed: map.Id.Sum(c => c), dusk: true));
    });

    private static IEnumerable<AmbientItem> Watched(AmbientPlan plan, double seconds, ArtArea view)
    {
        for (var t = 0.0; t < seconds; t += 0.25)
            foreach (var item in plan.ActiveAt(t, view))
                yield return item;
    }

    private static readonly ArtArea Everywhere = new(new Vector2(-20000), new Vector2(20000));

    [Fact]
    public void The_fords_river_steams_with_its_own_art()
    {
        var (map, plan) = Ford.Value;
        var steam = plan.ActiveAt(3, Everywhere).Where(i => i.Kind == "river_steam").ToArray();

        Assert.NotEmpty(steam);
        Assert.All(steam, s => Assert.True((map[s.Tile].Flags & TileFlags.Water) != 0, $"steam on {s.Tile}"));
        Assert.All(steam, s => Assert.Equal("terrain/river_steam_mist.png", s.Path));
        Assert.DoesNotContain(plan.ActiveAt(3, Everywhere), i => i.Kind == "chimney_smoke");     // the town kit's chimneys smoke for themselves
    }

    [Fact]
    public void Leaves_fall_from_the_fords_trees_in_their_colours_and_land_near_them()
    {
        var (map, plan) = Ford.Value;
        var leaves = Watched(plan, 12, Everywhere).Where(i => i.Kind == "leaf").ToArray();
        var trees = map.Props.Where(p => p.Id == "tree").ToDictionary(p => p.At);

        Assert.True(plan.PropLife.Leaves > 0);
        Assert.NotEmpty(leaves);
        foreach (var leaf in leaves)
        {
            Assert.True(trees.ContainsKey(leaf.Tile), $"a leaf from {leaf.Tile}, which has no tree");
            Assert.Equal("autumn", leaf.Variant);
            var foot = BoardProjection.Corner(leaf.Tile.X, leaf.Tile.Y, map[leaf.Tile].Height) + new Vector2(16, 8);
            Assert.InRange(leaf.Position.Y, foot.Y - 60, foot.Y + 4);
            Assert.InRange(leaf.Position.X, foot.X - 40, foot.X + 40);
        }
    }

    [Fact]
    public void Fires_throw_embers_that_glow_and_lamps_hold_motes_only_at_dusk()
    {
        var (map, plan) = Ford.Value;
        var items = plan.ActiveAt(2, Everywhere);
        var fires = map.Props.Where(p => p.Id is "campfire" or "torch").Select(p => p.At).ToHashSet();

        Assert.Equal(fires.Count, items.Count(i => i.Kind == "embers"));
        Assert.All(items.Where(i => i.Kind is "embers" or "motes"), i => Assert.True(i.Add));
        Assert.All(items.Where(i => i.Kind == "embers"), i => Assert.Contains(i.Tile, fires));
        Assert.Contains(items, i => i.Kind == "motes");

        var day = new AmbientPlan(new BoardTerrain(map, null), map, AmbientManifest.Load(Pack.Root), seed: 1, dusk: false);
        Assert.DoesNotContain(day.ActiveAt(2, Everywhere), i => i.Kind == "motes");
    }

    [Fact]
    public void Birds_cross_the_sky_now_and_then_crows_at_dusk_and_stay_by_the_view()
    {
        var (_, plan) = Ford.Value;
        var view = new ArtArea(new Vector2(1000, 2000), new Vector2(1640, 2360));
        var birds = Watched(plan, 120, view).Where(i => i.Kind is "bird" or "flock").ToArray();

        Assert.NotEmpty(birds);
        Assert.All(birds, b => Assert.Equal("crow", b.Variant));
        Assert.All(birds, b => Assert.InRange(b.Position.X, view.Min.X - 128, view.Max.X + 128));
        Assert.All(birds, b => Assert.InRange(b.Position.Y, view.Min.Y, view.Min.Y + (view.Max.Y - view.Min.Y) * 0.5f));
        // Not all the time: a crossing takes a few seconds of each beat.
        Assert.True(Enumerable.Range(0, 480).Count(i => plan.ActiveAt(i * 0.25, view).Any(b => b.Kind is "bird" or "flock")) < 480);
    }

    [Fact]
    public void Everything_it_draws_is_in_its_paths_and_on_disk()
    {
        var (_, plan) = Ford.Value;
        var drawn = Watched(plan, 60, Everywhere).Select(i => i.Path).Distinct().ToArray();

        Assert.All(drawn, p => Assert.Contains(p, plan.Paths));
        Assert.All(plan.Paths, p => Assert.True(File.Exists(Path.Combine(Pack.Root, "ambient", p)), p));
    }
}
