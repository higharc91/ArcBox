using System.Collections.Immutable;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;
using Xunit;

namespace Tactics.Presentation.Tests.World;

/// <summary>The world's animals: drawn at the world's scale, and kept to ground that makes sense (<see cref="WorldHerd"/>, <see cref="FaunaRange"/>).</summary>
public sealed class FaunaTests
{
    private static readonly ImmutableSortedDictionary<string, ImmutableArray<Tile>> NoZones =
        ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty;

    private static BattleMap Map(int width, int height, Func<int, int, (int Height, TileFlags Flags)> tileAt, params PropPlacement[] props)
    {
        var bytes = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var (h, f) = tileAt(x, y);
                bytes[(y * width + x) * 3] = (byte)h;
                bytes[(y * width + x) * 3 + 2] = (byte)f;
            }
        return BattleMap.FromBytes("test", width, height, bytes, ["grass/lush"], [.. props], NoZones);
    }

    private static PropPlacement Prop(string id, int x, int y) => new(id, new Tile(x, y), true, Cover.Half);

    private static int Low(string id) => 1;
    [Fact]
    public void Every_animal_in_the_pack_is_drawn_at_about_its_size_beside_a_person()
    {
        // Keep the original animal/person ratios when the party art changes its authored density.
        var art = ArtIndex.Load(Pack.Root);
        var referenceScale = UnitArt.Load(Pack.Root).Sprite("characters.class_fighter")!.HeadLift / 29f;
        (string Id, string Variant, int Least, int Most)[] animals =
        [
            ("pack_mule_anim", "grey", 22, 32),      // shoulder height at the back, the head above it
            ("farm_goat_anim", "rust", 11, 18),      // waist high
            ("red_fox_anim", "russet", 7, 13),       // smaller than a dog
            ("river_duck_anim", "mallard", 5, 9),
            ("field_crow_anim", "charcoal", 4, 8),   // tiny
            ("farm_dog_anim", "brown", 9, 17),
            ("farm_sheep_anim", "cream", 10, 18),
        ];
        foreach (var (id, variant, least, most) in animals)
        {
            var prop = art.Prop(id, [variant])!;
            var sprite = WorldHerd.StripSprite(prop);
            var (rgba, width, height) = PngPixels.Read(Path.Combine(Pack.Root, prop.Image.Path));
            var rows = Enumerable.Range(0, height).Where(y => Enumerable.Range(0, prop.Width).Any(x => rgba[(y * width + x) * 4 + 3] != 0)).ToArray();
            var drawn = (rows.Max() - rows.Min() + 1) * sprite.PixelScale;
            Assert.InRange(drawn, MathF.Round(least * referenceScale) - 0.5f, MathF.Ceiling(most * referenceScale) + 0.5f);
            Assert.InRange(sprite.AnchorY - rows.Max(), 0, 1);      // feet on the ground
        }
    }

    [Fact]
    public void A_strip_made_bigger_than_the_world_is_drawn_smaller_about_its_feet()
    {
        var art = ArtIndex.Load(Pack.Root);
        var prop = art.Prop("pack_mule_anim", ["grey"])! with { Scale = 0.5 };
        var sprite = WorldHerd.StripSprite(prop);
        var place = new UnitPlace(new System.Numerics.Vector2(10.5f, 10.5f), 0);
        var unit = UnitPlan.Animal("mule", sprite, place, 0, 0, false);
        var area = WorldPlan.AreaOf(unit);

        Assert.Equal(0.5f, sprite.PixelScale);
        Assert.Equal(prop.Width / 2f, sprite.WorldWidth);
        Assert.Equal(prop.Height / 2f, sprite.WorldHeight);
        // The anchor stays the file's, and lands on the feet: the drawn box is half the size, its anchor pixel where the feet are.
        Assert.Equal(place.Feet.Y - prop.AnchorY / 2f, area.Min.Y, 0.01);
        Assert.Equal(1f, WorldHerd.StripSprite(prop with { Scale = 1 }).PixelScale);
    }

    [Fact]
    public void An_animal_doesnt_climb_a_cliff_or_go_down_one()
    {
        // A field at height 3 with a cliff to height 6 from x 6 on, and a drop to 1 below y 6.
        var map = Map(12, 10, (x, y) => (x >= 6 ? 6 : y >= 6 ? 1 : 3, TileFlags.None));
        var free = FaunaRange.Free(map, Low);

        var domain = FaunaRange.Domain(map, free, new Tile(2, 2), 8, water: false);

        Assert.All(domain, t => Assert.Equal(3, map[t].Height));
        Assert.Contains(new Tile(5, 5), domain);
        Assert.Empty(FaunaRange.Path(map, new Tile(5, 2), new Tile(6, 2), domain.ToHashSet()));
    }

    [Fact]
    public void A_gentle_slope_is_fine()
    {
        var map = Map(8, 3, (x, y) => (x / 2, TileFlags.None));
        var free = FaunaRange.Free(map, Low);

        Assert.Contains(new Tile(7, 1), FaunaRange.Domain(map, free, new Tile(0, 1), 10, water: false));
    }

    [Fact]
    public void A_land_animal_stays_dry_and_a_duck_stays_wet()
    {
        var map = Map(10, 6, (x, y) => (1, x >= 5 ? TileFlags.Water | (x >= 7 ? TileFlags.Deep : 0) : TileFlags.None));
        var free = FaunaRange.Free(map, Low);

        Assert.All(FaunaRange.Domain(map, free, new Tile(2, 2), 9, water: false), t => Assert.True(t.X < 5));
        var duck = FaunaRange.Domain(map, free, new Tile(8, 2), 9, water: true);
        Assert.All(duck, t => Assert.True(t.X >= 5));
        Assert.Contains(new Tile(9, 5), duck);
    }

    [Fact]
    public void An_animal_never_stands_on_a_prop_but_may_go_behind_one()
    {
        // A haystack at (4, 4), and a building's blocked tile at (8, 8). Animals are drawn in painter's order now (OW-14), so what's in front hides them.
        var map = Map(12, 12, (x, y) => (0, TileFlags.None), Prop("haystack", 4, 4), Prop("building:cottage", 8, 8));
        var free = FaunaRange.Free(map, Low);

        Assert.DoesNotContain(new Tile(4, 4), free);
        Assert.Contains(new Tile(3, 4), free);           // behind it along x
        Assert.Contains(new Tile(4, 3), free);           // along y
        Assert.Contains(new Tile(3, 3), free);           // and both
        Assert.Contains(new Tile(5, 5), free);           // in front
        Assert.DoesNotContain(new Tile(7, 7), free);     // within a tile of a building
        Assert.Contains(new Tile(6, 6), free);           // two tiles behind it
        Assert.Contains(new Tile(10, 10), free);
    }

    [Fact]
    public void A_farm_animal_stays_in_its_pen()
    {
        // A pen of fence from (2, 2) to (8, 8), a gap at (5, 8).
        var fence = new List<PropPlacement>();
        for (var i = 2; i <= 8; i++)
        {
            fence.Add(Prop("fence", i, 2));
            fence.Add(Prop("fence", 2, i));
            fence.Add(Prop("fence", 8, i));
            if (i != 5)
                fence.Add(Prop(i == 8 ? "fence_join" : "fence", i, 8));
        }
        var map = Map(14, 14, (x, y) => (0, TileFlags.None), [.. fence.Distinct()]);
        var free = FaunaRange.Free(map, Low);

        var domain = FaunaRange.Domain(map, free, new Tile(5, 5), 12, water: false);

        Assert.NotEmpty(domain);
        Assert.All(domain, t => Assert.True(t.X is > 2 and < 8 && t.Y is > 2 and < 8, $"{t} is outside the pen"));
    }
    [Fact]
    public void Every_live_overworld_animal_starts_on_clear_ground_in_its_habitat()
    {
        var art = ArtIndex.Load(Pack.Root);
        var map = Pack.Ground("ashen_ford").Map;
        var free = FaunaRange.Free(map, art.PropHeight);
        var animals = map.Props.Where(p => WorldAnimalIds.Contains(p.Id)).ToArray();
        Assert.Contains(animals, a => a.Id == "farm_dog");
        Assert.Equal(2, animals.Count(a => a.Id == "farm_sheep"));
        foreach (var animal in animals)
        {
            Assert.True(free.Contains(animal.At), $"{animal.Id} at {animal.At} is covered by a prop or building");
            var water = (map[animal.At].Flags & TileFlags.Water) != 0;
            Assert.Equal(animal.Id == "river_duck", water);
            Assert.True(FaunaRange.Domain(map, free, animal.At, 3, water).Length > 1, $"{animal.Id} has nowhere to wander");
        }
    }

    private static readonly Lazy<UnitArt> Units = new(() => UnitArt.Load(Pack.Root));

    [Fact]
    public void The_fords_herd_wanders_the_same_way_every_time_and_keeps_to_its_ground()
    {
        var ground = Pack.Ground("ashen_ford");
        var terrain = new BoardTerrain(ground.Map, null);
        var (one, two) = (WorldHerd.Build(terrain, Pack.Art, Units.Value), WorldHerd.Build(terrain, Pack.Art, Units.Value));
        var homes = ground.Map.Props.Where(p => WorldAnimalIds.Contains(p.Id)).ToArray();
        var free = FaunaRange.Free(ground.Map, Pack.Art.PropHeight);
        Assert.Equal(homes.Length, one.Count);
        var moved = 0;
        for (var frame = 0; frame < 60 * 40; frame++)
        {
            one.Step(1 / 60.0);
            two.Step(1 / 60.0);
            if (frame % 30 != 0)
                continue;
            var (a, b) = (one.Units(), two.Units());
            Assert.Equal(a.Select(u => (u.Feet, u.Order, u.Anim.Frames[0])), b.Select(u => (u.Feet, u.Order, u.Anim.Frames[0])));
            moved += homes.Count(h => one.PlaceOf(h.At) is { } place && place.Over != h.At);
            foreach (var home in homes)
            {
                var at = one.PlaceOf(home.At)!.Value.Over;
                var water = (ground.Map[at].Flags & TileFlags.Water) != 0;
                Assert.True(at == home.At || free.Contains(at) && water == (home.Id == "river_duck"), $"{home.Id} from {home.At} is at {at}, off its ground: free {free.Contains(at)}, flags {ground.Map[at].Flags}");
            }
        }
        Assert.True(moved > 0, "nothing wandered");
    }

    [Fact]
    public void A_pen_is_found_quickly_however_much_fence_is_about()
    {
        // A field of fences in rows, as a farm drawn without care might have: the pen search tries every rectangle within reach.
        var fences = new List<PropPlacement>();
        for (var y = 0; y < 40; y += 3)
            for (var x = 0; x < 40; x++)
                fences.Add(Prop("fence", x, y));
        var map = Map(40, 40, (x, y) => (0, TileFlags.None), [.. fences]);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 10; i++)
            FaunaRange.Pen(new Tile(20, 20), fences.Select(f => f.At).ToHashSet(), map);

        Assert.True(clock.ElapsedMilliseconds < 2000, $"{clock.ElapsedMilliseconds} ms for ten pens");
    }

    /// <summary>The pen as it was found before OW-12, counting each side tile by tile: what the running counts have to agree with.</summary>
    private static HashSet<Tile>? SlowPen(Tile animal, HashSet<Tile> fences, BattleMap map)
    {
        (int Left, int Top, int Right, int Bottom, int Area)? best = null;
        int Count(int fromX, int toX, int fromY, int toY)
        {
            var n = 0;
            for (var y = fromY; y <= toY; y++)
                for (var x = fromX; x <= toX; x++)
                    n += fences.Contains(new Tile(x, y)) ? 1 : 0;
            return n;
        }
        for (var left = Math.Max(0, animal.X - 18); left < animal.X; left++)
            for (var right = animal.X + 1; right <= Math.Min(map.Width - 1, animal.X + 18); right++)
                for (var top = Math.Max(0, animal.Y - 18); top < animal.Y; top++)
                    for (var bottom = animal.Y + 1; bottom <= Math.Min(map.Height - 1, animal.Y + 18); bottom++)
                    {
                        var (w, h) = (right - left - 1, bottom - top - 1);
                        if (w < 3 || h < 3 || best is { Area: var area } && w * h >= area)
                            continue;
                        var (across, along) = ((right - left + 1) * 0.62, (bottom - top + 1) * 0.62);
                        if (Count(left, right, top, top) < across || Count(left, right, bottom, bottom) < across
                            || Count(left, left, top, bottom) < along || Count(right, right, top, bottom) < along)
                            continue;
                        best = (left, top, right, bottom, w * h);
                    }
        if (best is not { } pen)
            return null;
        var inside = new HashSet<Tile>();
        for (var y = pen.Top + 1; y < pen.Bottom; y++)
            for (var x = pen.Left + 1; x < pen.Right; x++)
                inside.Add(new Tile(x, y));
        return inside;
    }

    [Fact]
    public void Pens_are_the_same_as_counting_tile_by_tile_found()
    {
        var random = new Random(78);
        for (var trial = 0; trial < 12; trial++)
        {
            var fences = new HashSet<Tile>();
            for (var pen = 0; pen < 3; pen++)
            {
                var (x0, y0) = (random.Next(0, 18), random.Next(0, 18));
                var (x1, y1) = (x0 + random.Next(3, 9), y0 + random.Next(3, 9));
                for (var x = x0; x <= x1; x++)
                    for (var y = y0; y <= y1; y++)
                        if ((x == x0 || x == x1 || y == y0 || y == y1) && random.NextDouble() < 0.85)
                            fences.Add(new Tile(x, y));
            }
            var map = Map(30, 30, (x, y) => (0, TileFlags.None), [.. fences.Select(f => Prop("fence", f.X, f.Y))]);
            for (var i = 0; i < 6; i++)
            {
                var animal = new Tile(random.Next(1, 28), random.Next(1, 28));
                var (slow, fast) = (SlowPen(animal, fences, map), FaunaRange.Pen(animal, fences, map));
                Assert.Equal(slow?.Order().ToArray(), fast?.Order().ToArray());
            }
        }
        // And the region's own pens.
        var ford = Pack.Ground("ashen_ford").Map;
        var fordFences = ford.Props.Where(p => FaunaRange.IsFence(p.Id)).Select(p => p.At).ToHashSet();
        foreach (var animal in ford.Props.Where(p => WorldAnimalIds.Contains(p.Id)))
            Assert.Equal(SlowPen(animal.At, fordFences, ford)?.Order().ToArray(), FaunaRange.Pen(animal.At, fordFences, ford)?.Order().ToArray());
    }
}
