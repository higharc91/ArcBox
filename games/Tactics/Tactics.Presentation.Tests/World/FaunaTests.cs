using System.Collections.Immutable;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;
using Xunit;

namespace Tactics.Presentation.Tests.World;

/// <summary>The world's animals: drawn at the world's scale, and kept to ground that makes sense (<see cref="SpriteShrink"/>, <see cref="FaunaRange"/>).</summary>
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
            var (rgba, width, height) = PngPixels.Read(Path.Combine(Pack.Root, prop.Image.Path));
            var strip = SpriteShrink.Shrink(rgba, width, height, prop.Image.Frames, prop.Scale);
            var rows = Enumerable.Range(0, strip.Height).Where(y => Enumerable.Range(0, strip.Width).Any(x => strip.Rgba[(y * strip.Width + x) * 4 + 3] != 0)).ToArray();
            Assert.InRange(rows.Max() - rows.Min() + 1, (int)MathF.Round(least * referenceScale), (int)MathF.Ceiling(most * referenceScale));
            Assert.Equal(SpriteShrink.Anchor(prop.AnchorX, prop.AnchorY, prop.Width, prop.Height, prop.Scale).Y, rows.Max());   // feet on the ground
        }
    }

    [Fact]
    public void A_shrunk_strip_keeps_its_frames_apart_and_its_alpha_whole()
    {
        // Two 4x4 frames: the first solid red, the second clear but for one blue pixel.
        var rgba = new byte[8 * 4 * 4];
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
                (rgba[(y * 8 + x) * 4], rgba[(y * 8 + x) * 4 + 3]) = (255, 255);
        (rgba[(0 * 8 + 7) * 4 + 2], rgba[(0 * 8 + 7) * 4 + 3]) = (255, 255);

        var strip = SpriteShrink.Shrink(rgba, 8, 4, 2, 0.5);

        Assert.Equal((2, 2, 4), (strip.FrameWidth, strip.Height, strip.Width));
        for (var y = 0; y < 2; y++)
            for (var x = 0; x < 4; x++)
            {
                var i = (y * 4 + x) * 4;
                Assert.True(strip.Rgba[i + 3] is 0 or 255);
                Assert.Equal(x < 2 ? 255 : 0, (int)strip.Rgba[i + 3]);      // red stays red, and one pixel of four is too little to draw
                if (x < 2)
                    Assert.Equal((255, 0, 0), (strip.Rgba[i], strip.Rgba[i + 1], strip.Rgba[i + 2]));
            }
    }

    [Fact]
    public void The_matte_fringe_is_dropped_and_an_anchor_on_the_bottom_row_stays_there()
    {
        Assert.True(SpriteShrink.Matte(255, 0, 255));
        Assert.True(SpriteShrink.Matte(180, 20, 160));
        Assert.False(SpriteShrink.Matte(40, 0, 40));        // dark: it reads as outline
        Assert.False(SpriteShrink.Matte(200, 120, 60));     // a fox
        Assert.False(SpriteShrink.Matte(60, 100, 60));      // a duck's head

        Assert.Equal((9, 22), SpriteShrink.Anchor(44, 103, 88, 104, 0.22));
        Assert.Equal(SpriteShrink.Size(88, 104, 0.38).Height - 1, SpriteShrink.Anchor(44, 103, 88, 104, 0.38).Y);
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
    public void An_animal_never_stands_on_a_prop_or_just_behind_one()
    {
        // A haystack at (4, 4), and a building's blocked tile at (8, 8), which is tall.
        var map = Map(12, 12, (x, y) => (0, TileFlags.None), Prop("haystack", 4, 4), Prop("building:cottage", 8, 8));
        var free = FaunaRange.Free(map, Low);

        Assert.DoesNotContain(new Tile(4, 4), free);
        Assert.DoesNotContain(new Tile(3, 4), free);     // behind it along x
        Assert.DoesNotContain(new Tile(4, 3), free);     // along y
        Assert.DoesNotContain(new Tile(3, 3), free);     // and both
        Assert.Contains(new Tile(5, 5), free);           // in front is fine
        Assert.DoesNotContain(new Tile(6, 6), free);     // two tiles behind a building
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
}
