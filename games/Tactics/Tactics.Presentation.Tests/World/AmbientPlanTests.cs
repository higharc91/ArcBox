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
}
