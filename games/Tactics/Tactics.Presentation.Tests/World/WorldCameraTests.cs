using System.Numerics;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;
using static Tactics.Presentation.Tests.World.WalkFeed;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="WorldCamera"/>: alone, and following a real <see cref="WalkTrack"/>.</summary>
public sealed class WorldCameraTests
{
    private const float TilesPerSecond = 1 / 0.3f;

    private static readonly BoardTerrain Flat = Ground(160, 120);
    private static readonly Tile Middle = new(80, 60);

    private static Vector2 Screen(WorldCamera camera, Vector2 map, float height = 0)
        => camera.Fit.ToScreen(BoardProjection.Corner(map.X, map.Y, height));

    private static void Walk(WalkTrack track, WorldCamera camera, BoardTerrain terrain, Script script, (int, int)[] steps, bool heldAtEnd = true, Action? onFrame = null)
        => WalkFeed.Walk(track, terrain, script, steps, heldAtEnd, () =>
        {
            camera.Follow(track.Focus.Map, track.Focus.Height, track.Heading, Frame);
            onFrame?.Invoke();
        });

    [Fact]
    public void A_point_on_pixels_lands_where_the_unrounded_camera_would_put_it()
    {
        var camera = new WorldCamera(160, 120);
        camera.Snap(new Vector2(80.3f, 60.7f), 0);
        var art = new Vector2(123.3f, 45.8f);

        var on = camera.Fit.ToScreen(camera.OnPixels(art));

        Assert.Equal(MathF.Round(on.X), on.X, 3);
        Assert.Equal(MathF.Round(on.Y), on.Y, 3);
        Assert.InRange(MathF.Abs(on.X - camera.Fit.ToScreen(art).X), 0, 1);
    }

    [Fact]
    public void A_point_on_the_ground_stays_on_the_ground_as_the_camera_moves()
    {
        var camera = new WorldCamera(160, 120);
        var art = new Vector2(123.3f, 45.8f);

        camera.Snap(new Vector2(81.1f, 59.2f), 0);

        var on = camera.Fit.ToScreen(WorldCamera.OnGround(art));
        Assert.Equal(MathF.Round(on.X), on.X, 3);
        Assert.Equal(MathF.Round(on.Y), on.Y, 3);
    }

    private static void Rest(WalkTrack track, WorldCamera camera, BoardTerrain terrain, double seconds, Action? onFrame = null)
    {
        for (var t = 0.0; t < seconds; t += Frame)
        {
            track.Advance(Frame);
            track.Places(terrain);
            camera.Follow(track.Focus.Map, track.Focus.Height, track.Heading, Frame);
            onFrame?.Invoke();
        }
    }

    [Fact]
    public void The_camera_stops_short_of_the_region_edge()
    {
        var camera = new WorldCamera(160, 120);

        for (var i = 0; i < 2000; i++)
            camera.Follow(new Vector2(6, 60), 0, Vector2.Zero, Frame);
        Assert.Equal(WorldTuning.EdgeMargin, camera.Centre.X, 3);

        for (var i = 0; i < 2000; i++)
            camera.Follow(new Vector2(80, 114), 0, Vector2.Zero, Frame);
        Assert.Equal(120 - WorldTuning.EdgeMargin, camera.Centre.Y, 3);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(159, 60)]
    [InlineData(80, 0)]
    [InlineData(80, 119)]
    [InlineData(0, 0)]
    [InlineData(159, 119)]
    [InlineData(159, 0)]
    [InlineData(0, 119)]
    public void The_party_outranks_the_margin(int x, int y)
    {
        var camera = new WorldCamera(160, 120);
        var map = new Vector2(x + 0.5f, y + 0.5f);

        for (var i = 0; i < 2000; i++)
            camera.Follow(map, 0, Vector2.Zero, Frame);

        var screen = Screen(camera, map);
        Assert.InRange(screen.X, 150, 1770);
        Assert.InRange(screen.Y, 170, 1000);
    }

    [Fact]
    public void Walking_along_a_region_edge_into_the_corner_moves_the_camera_smoothly()
    {
        var camera = new WorldCamera(160, 120);
        var last = default(Vector2);
        for (var i = 0; i < 4000; i++)
        {
            var map = new Vector2(150 + 9.5f * i / 4000, 40 - 39.5f * i / 4000);
            camera.Follow(map, 0, Vector2.Zero, Frame);
            if (i > 0)
                Assert.InRange(Vector2.Distance(camera.Centre, last), 0, 0.1f);
            last = camera.Centre;
        }
    }

    [Fact]
    public void A_small_region_is_centred()
    {
        var camera = new WorldCamera(30, 24);

        for (var i = 0; i < 2000; i++)
            camera.Follow(new Vector2(18, 10), 0, Vector2.UnitX, Frame);

        Assert.Equal(15, camera.Centre.X, 3);
        Assert.Equal(12, camera.Centre.Y, 3);
    }

    [Fact]
    public void The_camera_origin_is_on_whole_pixels()
    {
        var camera = new WorldCamera(160, 120);
        var random = new Random(4);

        for (var i = 0; i < 3000; i++)
        {
            camera.Follow(new Vector2(70 + i * 0.013f, 60 + i * 0.007f), i * 0.002f, Vector2.Zero, Frame * (0.5 + random.NextDouble()));
            Assert.Equal(MathF.Round(camera.Fit.Origin.X), camera.Fit.Origin.X);
            Assert.Equal(MathF.Round(camera.Fit.Origin.Y), camera.Fit.Origin.Y);
            Assert.Equal(WorldTuning.Scale, camera.Fit.Scale);
        }
    }

    [Fact]
    public void A_party_at_rest_stands_in_the_middle_of_the_board_area()
    {
        var camera = new WorldCamera(160, 120);
        camera.Snap(Centre(Middle), 3);

        var at = Screen(camera, Centre(Middle), 3);

        Assert.Equal(BoardFit.Area.X + BoardFit.Area.Width / 2, at.X, 0);
        Assert.Equal(BoardFit.Area.Y + BoardFit.Area.Height / 2, at.Y, 0);
    }

    // Pixel-perfect origin steps on a steady walk: one step a frame at most in the walk's direction, and no step back, whatever the
    // frame times, hitch included. The party stays put on screen to within the origin's own rounding.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void No_jitter_at_144_hz_with_a_hitch(int seed)
    {
        var random = new Random(seed);
        var camera = new WorldCamera(800, 800);
        var (start, way) = (new Vector2(100, 400), new Vector2(1, 0));
        camera.Snap(start, 0);
        var (t, wasOrigin) = (0.0, camera.Fit.Origin);
        var seen = new List<float>();
        for (var i = 0; i < 2000; i++)
        {
            var dt = i == 1000 ? 0.1 : (1.0 / 144) * (0.6 + 0.8 * random.NextDouble());
            t += dt;
            var map = start + way * TilesPerSecond * (float)t;
            camera.Follow(map, 0, way, dt);
            var origin = camera.Fit.Origin;
            if (i > 1200 || (i > 600 && i < 1000))
            {
                Assert.True(origin.X <= wasOrigin.X, $"backed up at {i}");
                seen.Add(Screen(camera, map).X);
            }
            wasOrigin = origin;
        }

        Assert.True(seen.Max() - seen.Min() <= 2.5f, $"the party swam {seen.Max() - seen.Min()} px on screen");
    }

    // The real thing at its worst: a view stream with jitter and a wandering frame rate, with a hitch. The party mustn't swim on screen.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_real_walk_holds_steady_on_screen(int seed)
    {
        var random = new Random(seed);
        var (track, camera, script) = Rested();
        var (next, t) = (0.0, 0.0);
        var seen = new List<float>();
        while (t < 10)
        {
            var dt = Math.Abs(t - 7) < 0.004 ? 0.1 : (1.0 / 144) * (0.6 + 0.8 * random.NextDouble());
            t += dt;
            if (t >= next)
            {
                next += 0.3 + (random.NextDouble() - 0.5) * 0.04;
                script.Step(1, -1, 300);
                track.Receive(script.Now(true));
            }
            track.Advance(dt);
            track.Places(Flat);
            camera.Follow(track.Focus.Map, track.Focus.Height, track.Heading, dt);
            if (t > 4)
                seen.Add(Screen(camera, track.Focus.Map).X);
        }

        Assert.True(seen.Max() - seen.Min() <= 8, $"swam {seen.Max() - seen.Min()} px");
    }

    [Fact]
    public void A_tap_doesnt_swing_the_look_ahead()
    {
        var (track, camera, script) = Rested();
        var rest = camera.Fit.Origin;

        var farthest = 0f;
        Walk(track, camera, Flat, script, Repeat(1, -1, 1), heldAtEnd: false, onFrame: () => farthest = Math.Max(farthest, Beyond()));
        Rest(track, camera, Flat, 2, () => farthest = Math.Max(farthest, Beyond()));

        // The tap moves the party a diagonal; the camera may go there, but no further.
        Assert.True(farthest < 1, $"{farthest} px past the party");
        Assert.NotEqual(rest, camera.Fit.Origin);

        float Beyond() => Vector2.Distance(camera.Centre, Centre(script.At)) is var off && Vector2.Dot(camera.Centre - Centre(script.At), new Vector2(1, -1)) > 0 ? off * 32 : 0;
    }

    [Fact]
    public void A_blocked_press_doesnt_move_the_camera()
    {
        var (track, camera, script) = Rested();
        var rest = camera.Fit.Origin;

        track.Receive(View(script.At, new Tile(script.At.X + 1, script.At.Y), true, script.Legs, script.Seq));
        Rest(track, camera, Flat, 0.02);
        track.Receive(View(script.At, new Tile(script.At.X, script.At.Y - 1), false, script.Legs, script.Seq));
        Rest(track, camera, Flat, 2, () => Assert.Equal(rest, camera.Fit.Origin));
    }

    [Fact]
    public void A_long_walk_looks_ahead_and_a_stop_comes_back()
    {
        var (track, camera, script) = Rested();

        Walk(track, camera, Flat, script, Repeat(1, 0, 30));
        var ahead = Vector2.Distance(camera.Centre, track.Focus.Map);

        Assert.True(camera.Centre.X > track.Focus.Map.X, "the camera should lead");
        Assert.True(ahead is > 0.8f and < 1.5f, $"{ahead} tiles ahead");

        script.Step(1, 0);
        track.Receive(script.Now(false));
        Rest(track, camera, Flat, 5);
        Assert.True(Vector2.Distance(Centre(script.At), camera.Centre) < 0.01f);
    }

    [Fact]
    public void A_stairstep_road_doesnt_zigzag_the_camera()
    {
        var (track, camera, script) = Rested();

        var ys = new List<float>();
        Walk(track, camera, Flat, script, [.. Enumerable.Range(0, 24).Select(i => i % 2 == 0 ? (1, 0) : (0, -1))], onFrame: () =>
        {
            if (track.Walked > 12)
                ys.Add(camera.Fit.Origin.Y);
        });

        // A road running along the screen: the origin's Y should stay put, though the leader's swings 32 px every tile.
        Assert.True(ys.Max() - ys.Min() <= 6, $"swung {ys.Max() - ys.Min()} px");
    }

    [Fact]
    public void A_real_corner_is_still_followed()
    {
        var (track, camera, script) = Rested();

        Walk(track, camera, Flat, script, Repeat(1, 0, 16));
        Walk(track, camera, Flat, script, Repeat(0, 1, 16));

        Assert.True(Vector2.Distance(camera.Centre, track.Focus.Map) < 2.5f);
        Assert.True(camera.Centre.Y > track.Focus.Map.Y, "the look-ahead should have come round");
    }

    [Fact]
    public void Coming_up_to_the_edge_the_camera_eases_to_a_stop()
    {
        var camera = new WorldCamera(160, 120);
        camera.Snap(new Vector2(130, 60), 0);
        var map = new Vector2(130, 60);
        var (was, lastMoved, biggestDrop) = (camera.Centre.X, 0f, 0f);
        var speeds = new List<float>();
        for (var i = 0; i < 2000; i++)
        {
            map.X = Math.Min(map.X + TilesPerSecond * (float)Frame, 155);
            camera.Follow(map, 0, Vector2.UnitX, Frame);
            var moved = (camera.Centre.X - was) / (float)Frame;
            if (i > 200)
                biggestDrop = Math.Max(biggestDrop, lastMoved - moved);
            (was, lastMoved) = (camera.Centre.X, moved);
            speeds.Add(moved);
        }

        Assert.True(biggestDrop < 0.05f, $"lost {biggestDrop} tiles a second in a frame");
        Assert.True(speeds[^1] < 0.01f);
        Assert.Equal(160 - WorldTuning.EdgeMargin, camera.Centre.X, 2);
    }

    [Fact]
    public void A_hold_ignores_the_clamp_and_the_follow_takes_it_back_gently()
    {
        var camera = new WorldCamera(160, 120);
        var stage = new Vector2(6, 60);
        camera.Snap(new Vector2(20, 60), 0);

        for (var i = 0; i < 2000; i++)
            camera.Hold(stage, 2, Frame);

        Assert.Equal(stage.X, camera.Centre.X, 2);

        var was = camera.Centre.X;
        for (var i = 0; i < 3000; i++)
        {
            camera.Follow(stage, 2, Vector2.Zero, Frame);
            Assert.True(Math.Abs(camera.Centre.X - was) < 0.2f, $"{camera.Centre.X} from {was}");
            was = camera.Centre.X;
        }

        Assert.Equal(WorldTuning.EdgeMargin, camera.Centre.X, 2);
    }

    [Fact]
    public void A_snap_is_a_cut()
    {
        var camera = new WorldCamera(160, 120);
        camera.Follow(new Vector2(40, 40), 0, Vector2.Zero, Frame);

        camera.Snap(new Vector2(100, 90), 4);

        Assert.Equal(new Vector2(100, 90), camera.Centre);
        Assert.Equal(new Vector2(960, 568), Screen(camera, new Vector2(100, 90), 4));
    }

    [Fact]
    public void The_first_follow_is_a_cut()
    {
        var camera = new WorldCamera(160, 120);

        camera.Follow(new Vector2(100, 90), 0, Vector2.Zero, Frame);

        Assert.Equal(new Vector2(100, 90), camera.Centre);
    }

    private static (WalkTrack, WorldCamera, Script) Rested()
    {
        var track = new WalkTrack();
        var camera = new WorldCamera(160, 120);
        var script = new Script(Middle);
        track.Receive(script.Now(false));
        Rest(track, camera, Flat, 3);
        return (track, camera, script);
    }
}
