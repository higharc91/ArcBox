using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;
using static Tactics.Presentation.Tests.World.WalkFeed;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="WalkTrack"/>: what the TV makes of a stream of views, which is what someone watches for hours.</summary>
public sealed class WalkTrackTests
{
    private const string Lead = "a";
    private const string Walk = "walk";
    private const string Idle = "idle";

    private static readonly BoardTerrain Flat = Ground(64, 64);
    private static readonly Tile Start = new(20, 20);

    private static Vector2 Leader(IReadOnlyDictionary<string, UnitPlace> places) => places[Lead].Map;

    [Fact]
    public void The_first_step_shows_at_once()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Run(track, Flat, 1);
        var before = Leader(track.Places(Flat));

        script.Step(1, 0);
        track.Receive(script.Now(true));
        track.Advance(Frame);

        Assert.True(Leader(track.Places(Flat)).X > before.X);
        Assert.Equal(Walk, track.Poses[Lead].Anim);
    }

    // The stream that matters: the server's own walking rule on ground with walls, views arriving late, in bunches, dropped, and
    // cut off altogether, at a wandering frame rate with hitches.
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(7, 6)]
    [InlineData(8, 2)]
    public void The_leader_never_moves_backward(int seed, int party)
    {
        var random = new Random(seed);
        var terrain = Ground(64, 64, (x, y) => x > 40 ? 1 : 0, (x, y) => (x * 7 + y * 13) % 11 == 0 || x is 0 or 63 || y is 0 or 63);
        var server = new Server(terrain, new Tile(20, 21), random, party);
        var track = new WalkTrack();
        var trail = new List<Tile> { server.At };
        var (clock, walked, cuts, skips, walkingFor, fastest) = (0.0, 0.0, 0, 0, 0.0, 0.0);
        var before = new Dictionary<string, Vector2>();
        var seen = new Dictionary<string, Vector2>();
        track.Receive(server.View());

        while (clock < 240)
        {
            var dt = random.NextDouble() < 0.01 ? 0.1 : Frame * (0.5 + random.NextDouble());
            clock += dt;
            server.Run(clock);
            trail.AddRange(server.TakeTiles());
            if (server.Arrived(clock, dt) is { } view)
                track.Receive(view);

            var queued = track.Queued;
            track.Advance(dt);
            var places = track.Places(terrain);

            var moved = track.Walked - walked;
            var jumped = track.Cuts != cuts || track.Skips != skips;
            Assert.True(moved >= 0, $"the leader went back {moved} at {clock}");
            if (!jumped)
            {
                Assert.True(moved / dt <= WorldTuning.RateMost / 0.3 + 0.02, $"{moved / dt} tiles a second at {clock}");
                fastest = Math.Max(fastest, moved / dt);
                if (queued > 1e-3)
                    Assert.True(moved > 0, $"{queued} s of path queued and nothing moved at {clock}");
            }

            var idle = track.Poses[Lead].Anim == Idle;
            walkingFor = idle || jumped || track.Queued <= 0 ? 0 : walkingFor + dt;
            for (var i = 1; i < party; i++)
            {
                var id = Id(i);
                var place = places[id];
                if (walkingFor > 4)
                {
                    var off = DistanceToPath(place.Map, trail.TakeLast(60).ToArray());
                    Assert.True(off < 0.13f, $"{id} is {off} off the path at {clock}");
                    if (before.TryGetValue(id, out var was) && !jumped)
                        Assert.True(Vector2.Distance(was, place.Map) / dt <= WorldTuning.RateMost / 0.3 + 0.1, $"{id} too fast at {clock}");
                }
                before[id] = place.Map;
            }
            foreach (var (id, place) in places)
            {
                if (seen.TryGetValue(id, out var last) && !jumped)
                    Assert.True(Vector2.Distance(last, place.Map) / dt <= WorldTuning.RateMost / 0.3 + WorldTuning.FanMost, $"{id} popped {Vector2.Distance(last, place.Map)} at {clock}");
                seen[id] = place.Map;
            }
            foreach (var (id, place) in places)
                Assert.True(terrain.Rules.Walkable(TileUnder(place.Map)), $"{id} is on {TileUnder(place.Map)} at {clock}, and {(idle ? "standing" : "walking")}");

            (walked, cuts, skips) = (track.Walked, track.Cuts, track.Skips);
        }

        Assert.True(walked > 50, $"only {walked} tiles walked");
        Assert.True(fastest > 3, $"never got going: {fastest}");
    }

    [Fact]
    public void Missed_views_are_filled_in_from_the_legs()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Run(track, Flat, 0.1);

        script.Step(1, 0);
        script.Step(1, 0);
        script.Step(0, 1);
        script.Step(0, 1);
        track.Receive(script.Now(false));
        var seen = new List<Vector2>();
        for (var t = 0.0; t < 2; t += Frame)
        {
            track.Advance(Frame);
            seen.Add(Leader(track.Places(Flat)));
        }

        Assert.Equal(0, track.Cuts - 1);    // the first view only
        Assert.All(seen, p => Assert.True(DistanceToPath(p, [Start, new Tile(21, 20), new Tile(22, 20), new Tile(22, 21), new Tile(22, 22)]) < 0.1f));
        Assert.Equal(Centre(new Tile(22, 22)), seen[^1]);
    }

    [Fact]
    public void Late_views_dont_stop_the_walk_animation_while_held()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, Repeat(1, 0, 3));

        var seconds = new List<double>();
        for (var t = 0.0; t < 0.4; t += Frame)        // all of the path played, and the next step late
        {
            track.Advance(Frame);
            track.Places(Flat);
            Assert.Equal(Walk, track.Poses[Lead].Anim);
            seconds.Add(track.Poses[Lead].Seconds);
        }

        Assert.Equal(0, track.Queued, 3);
        Assert.Equal(Centre(script.At), Leader(track.Places(Flat)));
        Assert.True(seconds[^1] > seconds[0] + 0.05, "the feet stopped");
    }

    [Fact]
    public void Blocked_the_party_stands_and_turns_rather_than_running_on_the_spot()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, Repeat(1, 0, 3), heldAtEnd: false);
        Run(track, Flat, 0.2);

        Assert.Equal(Idle, track.Poses[Lead].Anim);
        track.Receive(View(script.At, new Tile(script.At.X, script.At.Y - 1), false, script.Legs, script.Seq));   // faces the wall
        Run(track, Flat, 0.05);

        Assert.Equal(Idle, track.Poses[Lead].Anim);
        Assert.Equal(new Vector2(0, -1), track.Poses[Lead].Facing);
        Assert.Equal(Centre(script.At), Leader(track.Places(Flat)));
    }

    // Interaction review 5b, item 2: screen up, down, left and right are the map's diagonals, which the art has no facing for. A press
    // that way into a wall used to keep the old facing whenever that was half of the diagonal, so nothing on the TV answered it.
    [Theory]
    [InlineData(1, 0, 1, -1, 0, -1)]        // facing se, pressing e: half of it already, so it turns to the other half, ne
    [InlineData(1, 0, 1, 1, 0, 1)]          // se, pressing s: sw
    [InlineData(0, -1, -1, -1, -1, 0)]      // ne, pressing n: nw
    [InlineData(1, 0, -1, -1, 0, -1)]       // se, pressing n: neither half, so the nearer turn, ne, not all the way round to nw
    [InlineData(0, 1, 1, -1, 1, 0)]         // sw, pressing e: se
    public void A_press_into_a_wall_on_a_diagonal_turns_the_leader(int walkX, int walkY, int pressX, int pressY, int faceX, int faceY)
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, [.. Repeat(walkX, walkY, 3)], heldAtEnd: false);
        Run(track, Flat, 0.5);
        Assert.Equal(new Vector2(walkX, walkY), track.Poses[Lead].Facing);

        var wall = View(script.At, new Tile(script.At.X + pressX, script.At.Y + pressY), false, script.Legs, script.Seq);
        track.Receive(wall);
        Run(track, Flat, 0.05);
        Assert.Equal(new Vector2(faceX, faceY), track.Poses[Lead].Facing);

        // The same view again, as the host view's other changes resend it: it stays turned, and doesn't flip back.
        track.Receive(wall);
        Run(track, Flat, 0.05);
        Assert.Equal(new Vector2(faceX, faceY), track.Poses[Lead].Facing);
        track.Receive(wall with { Party = [.. wall.Party.Select(w => w with { Here = w.Id != "c" })] });
        Run(track, Flat, 0.05);
        Assert.Equal(new Vector2(faceX, faceY), track.Poses[Lead].Facing);
        Assert.Equal(Centre(script.At), Leader(track.Places(Flat)));
    }

    // Review: at the spawn there are no legs to tell a turn from a step, so the very first press into a wall must be known by the view's
    // own word for it: Blocked, or a Bumps that went up.
    [Theory]
    [InlineData(1, 1, true, 0, 0, 1)]       // facing se at the spawn, pressing s, held against the wall
    [InlineData(1, -1, true, 0, 0, -1)]     // pressing e
    [InlineData(1, 1, false, 1, 0, 1)]      // the Blocked view was missed: only the release came, with the count gone up
    public void The_first_press_at_the_spawn_into_a_wall_turns_the_leader(int pressX, int pressY, bool blocked, int bumps, int faceX, int faceY)
    {
        var track = new WalkTrack();
        track.Receive(View(Start));
        Run(track, Flat, 0.5);
        Assert.Equal(new Vector2(1, 0), track.Poses[Lead].Facing);

        track.Receive(View(Start, new Tile(Start.X + pressX, Start.Y + pressY), false, [], 0) with { Blocked = blocked, Bumps = bumps });
        Run(track, Flat, 0.05);

        Assert.Equal(new Vector2(faceX, faceY), track.Poses[Lead].Facing);
    }

    // A diagonal that was walked, not pressed into a wall, keeps the facing it walked with at the stop, so a stairstep road doesn't turn
    // the party at every rest.
    [Fact]
    public void A_walked_diagonal_keeps_its_facing_at_the_stop()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, [.. Repeat(1, 0, 3), .. Repeat(1, -1, 3)], heldAtEnd: false);
        Run(track, Flat, 1);

        Assert.Equal(new Vector2(1, 0), track.Poses[Lead].Facing);
    }

    [Fact]
    public void A_server_gone_quiet_doesnt_run_on_the_spot_for_ever()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, Repeat(1, 0, 3));

        Run(track, Flat, WorldTuning.HeldPatience + 0.5);

        Assert.Equal(Idle, track.Poses[Lead].Anim);
    }

    [Fact]
    public void A_long_stall_catches_up_along_the_path()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, Repeat(1, 0, 2));

        Run(track, Flat, 3);          // nothing heard, while the server walked on
        for (var i = 0; i < 10; i++)
            script.Step(1, 0);
        var walked = track.Walked;
        track.Receive(script.Now(true));
        track.Advance(Frame);

        Assert.Equal(1, track.Skips);
        Assert.Equal(1, track.Cuts);
        Assert.True(track.Walked > walked + 4, "it should jump along the path");
        Assert.True(track.Queued <= WorldTuning.SnapLag, $"{track.Queued} s behind");
        Assert.True(track.Queued >= WorldTuning.TargetLag * 0.5, $"{track.Queued} s behind: it should land just behind");
        Assert.Equal(Start.Y + 0.5f, Leader(track.Places(Flat)).Y);
    }

    [Fact]
    public void A_reconnect_snaps_with_followers_on_the_trail()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, Repeat(1, 0, 2));

        for (var i = 0; i < 20; i++)    // more than a view's 12 legs: the path can't be filled in
            script.Step(0, 1);
        track.Receive(script.Now(true));
        var places = track.Places(Flat);

        Assert.Equal(2, track.Cuts);
        Assert.Equal(Centre(script.At), Leader(places));
        Assert.True(Vector2.Distance(Centre(script.At) - new Vector2(0, 0.9f), places["b"].Map) < 0.001f);
        Assert.True(Vector2.Distance(Centre(script.At) - new Vector2(0, 1.8f), places["c"].Map) < 0.001f);
    }

    [Fact]
    public void A_new_epoch_snaps()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, Repeat(1, 0, 3));

        var there = new Tile(40, 40);
        track.Receive(View(there, epoch: 2));
        var places = track.Places(Flat);

        Assert.Equal(2, track.Cuts);
        Assert.Equal(Centre(there), Leader(places));
        Assert.Equal(Idle, track.Poses[Lead].Anim);
        Assert.Equal(3, places.Values.Select(p => TileUnder(p.Map)).Distinct().Count());    // nobody stacked on the leader
    }

    [Fact]
    public void Seeded_followers_stand_where_they_were_put_and_fall_in_when_the_party_walks()
    {
        var track = new WalkTrack();
        track.Receive(View(Start, epoch: 2));
        var put = new UnitPlace(Centre(new Tile(Start.X + 3, Start.Y + 2)), 0);
        track.Seed(new Dictionary<string, UnitPlace> { ["b"] = put });

        Assert.Equal(put.Map, Run(track, Flat, 1)["b"].Map);

        var script = new Script(Start, epoch: 2);
        Walk(track, Flat, script, Repeat(1, 0, 8));
        Assert.True(DistanceToPath(track.Places(Flat)["b"].Map, [Start, script.At]) < 0.01f);
    }

    [Fact]
    public void Followers_trail_at_the_spacing_and_stop_when_the_leader_does()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, Repeat(1, 0, 8), onFrame: () =>
        {
            var places = track.Places(Flat);
            if (track.Walked > 7)
            {
                Assert.Equal(WorldTuning.Spacing, Vector2.Distance(places[Lead].Map, places["b"].Map), 2);
                Assert.Equal(WorldTuning.Spacing, Vector2.Distance(places["b"].Map, places["c"].Map), 2);
            }
        });

        track.Receive(script.Now(false));
        Run(track, Flat, 3);
        var settled = track.Places(Flat).ToDictionary(p => p.Key, p => p.Value.Map);
        Run(track, Flat, 1);

        Assert.All(settled, p => Assert.Equal(p.Value, track.Places(Flat)[p.Key].Map));
        Assert.All(track.Poses.Values, p => Assert.Equal(Idle, p.Anim));
    }

    // The flood bots.cjs --walk-flood makes: the leader's phone pressing and releasing at 60 messages a second, through the server's
    // limiter of 20 a second, refilled once a second. A third of each second gets through, flickering, and ends on a release, so the party
    // stops for most of every second. Each rest fans the followers out by the leader, and before Rejoin each set-off sent them back to
    // where they'd left the trail: 20 tiles behind after 12 s, and walking backward to get there.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_flood_of_stops_doesnt_leave_the_followers_behind(int seed)
    {
        var ground = Ground(200, 64);
        var track = new WalkTrack();
        var script = new Script(new Tile(5, 20));
        track.Receive(script.Now(false));
        Run(track, ground, 1);

        var random = new Random(seed);
        var (tokens, refill, nextMessage, press) = (20, 0.0, 0.0, true);
        var (held, stepDue, leaseUntil) = (false, 0.0, 0.0);
        var wire = new List<(double At, bool Held)>();
        var furthest = new Dictionary<string, float>();
        var (widest, backward) = (0f, 0f);
        for (var now = 0.0; now < 12; now += Frame)
        {
            if (now >= refill)
                (tokens, refill) = (20, refill + 1);
            if (now >= nextMessage)
            {
                nextMessage += 1.0 / 60;
                if (tokens > 0)
                {
                    tokens--;
                    if (press)
                    {
                        leaseUntil = now + 0.75;
                        if (!held && now >= stepDue)
                            stepDue = now + script.Step(1, 0) / 1000.0;
                        held = true;
                    }
                    else
                        held = false;
                    wire.Add((now + 0.005 + random.NextDouble() * 0.03, held));
                }
                press = !press;
            }
            if (held && now >= leaseUntil)
                held = false;
            if (held && now >= stepDue)
            {
                stepDue += script.Step(1, 0) / 1000.0;
                wire.Add((now + 0.005 + random.NextDouble() * 0.03, true));
            }
            if (wire.Count > 0 && wire[0].At <= now)
            {
                var newest = wire.Last(w => w.At <= now).Held;
                wire.RemoveAll(w => w.At <= now);
                track.Receive(script.Now(newest));
            }

            track.Advance(Frame);
            var places = track.Places(ground);
            if (now < 3)
                continue;     // they start fanned out from the opening rest
            widest = Math.Max(widest, Vector2.Distance(places[Lead].Map, places["c"].Map));
            foreach (var id in new[] { "b", "c" })
            {
                var x = places[id].Map.X;
                furthest[id] = Math.Max(furthest.GetValueOrDefault(id, x), x);
                backward = Math.Max(backward, furthest[id] - x);
            }
        }

        Assert.True(script.Seq >= 20, $"the flood walked {script.Seq} steps");
        Assert.True(widest < 2 * WorldTuning.Spacing + 2, $"the last follower got {widest:0.00} tiles from the leader");
        Assert.True(backward < 0.3f, $"a follower walked {backward:0.00} tiles back the way the party came");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void A_stopped_party_fans_out_on_ground_it_can_stand_on(int size)
    {
        var track = new WalkTrack();
        var script = new Script(Start, size);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, Repeat(1, 0, 8), heldAtEnd: false);
        var places = Run(track, Flat, 3);

        Assert.Equal(size, places.Count);
        var tiles = places.Values.Select(p => TileUnder(p.Map)).ToArray();
        Assert.Equal(size, tiles.Distinct().Count());
        Assert.All(tiles, t => Assert.True(Flat.Rules.Walkable(t)));
        Assert.All(places.Values, p => Assert.Equal(0, p.Height));
        if (size > 2)
            Assert.True(places.Values.Select(p => p.Map.Y).Max() - places.Values.Select(p => p.Map.Y).Min() > 0.9f, "that's a queue, not a formation");
    }

    [Fact]
    public void In_a_corridor_they_stay_in_line_on_whole_tiles()
    {
        var corridor = Ground(64, 64, wall: (_, y) => y != 20);
        var track = new WalkTrack();
        var script = new Script(Start, 4);
        track.Receive(script.Now(false));
        Walk(track, corridor, script, Repeat(1, 0, 8), heldAtEnd: false);
        var places = Run(track, corridor, 3);

        Assert.All(places.Values, p => Assert.True(corridor.Rules.Walkable(TileUnder(p.Map))));
        Assert.Equal(4, places.Values.Select(p => TileUnder(p.Map)).Distinct().Count());
        Assert.All(places.Values, p => Assert.True(TileUnder(p.Map).X <= script.At.X));
    }

    [Fact]
    public void A_cliff_top_party_doesnt_fan_down_the_cliff()
    {
        var cliff = Ground(64, 64, (_, y) => y > 21 ? 0 : 6);
        var track = new WalkTrack();
        var script = new Script(Start, 6);
        track.Receive(script.Now(false));
        Walk(track, cliff, script, Repeat(1, 0, 8), heldAtEnd: false);

        Assert.All(Run(track, cliff, 3).Values, p => Assert.Equal(6, p.Height));
    }

    [Fact]
    public void Followers_dont_bunch_at_a_corner()
    {
        var track = new WalkTrack();
        var script = new Script(Start, 6);
        track.Receive(script.Now(false));
        var closest = float.MaxValue;
        Walk(track, Flat, script, [.. Repeat(1, 0, 8), .. Repeat(0, 1, 8)], onFrame: () =>
        {
            if (track.Walked < 7)
                return;
            var places = track.Places(Flat);
            for (var i = 1; i < 6; i++)
                closest = Math.Min(closest, Vector2.Distance(places[Id(i - 1)].Map, places[Id(i)].Map));
        });

        Assert.True(closest >= 0.8f, $"as close as {closest}");
    }

    [Fact]
    public void A_corner_turns_without_a_kink()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        var was = Vector2.Zero;
        var (last, turn) = (Vector2.Zero, 0f);
        Walk(track, Flat, script, [.. Repeat(1, 0, 4), .. Repeat(0, 1, 4)], onFrame: () =>
        {
            var at = Leader(track.Places(Flat));
            var move = at - was;
            if (last != Vector2.Zero && move != Vector2.Zero && track.Walked > 1)
                turn = Math.Max(turn, MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(move), Vector2.Normalize(last)), -1, 1)));
            (was, last) = (at, move);
        });

        Assert.True(turn < 0.12f, $"{turn} radians in one frame");
    }

    [Fact]
    public void The_feet_go_with_the_distance_walked()
    {
        var track = new WalkTrack();
        var script = new Script(Start, 1);
        track.Receive(script.Now(false));
        Run(track, Flat, 0.5);
        var (from, seconds) = (0.0, 0.0);
        var first = true;
        var ratios = new List<double>();
        void Check()
        {
            var pose = track.Poses[Lead];
            if (pose.Anim != Walk)
                return;
            if (first)
            {
                (from, seconds, first) = (track.Walked, pose.Seconds, false);
                return;
            }
            if (track.Walked - from > 0.25)
                ratios.Add((pose.Seconds - seconds) / (track.Walked - from));
        }

        Walk(track, Flat, script, [.. Repeat(1, 0, 3), (1, 1), .. Repeat(0, 1, 2)], heldAtEnd: false, onFrame: Check);
        for (var t = 0.0; t < 0.4; t += Frame)
        {
            track.Advance(Frame);
            track.Places(Flat);
            Check();
        }

        Assert.NotEmpty(ratios);
        Assert.All(ratios, r => Assert.Equal(WorldTuning.StrideSeconds, r, 2));
    }

    [Fact]
    public void A_stop_settles_rather_than_snaps()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, Repeat(1, 0, 6));
        script.Step(1, 0);
        track.Receive(script.Now(false));

        var speeds = new List<double>();
        var walked = track.Walked;
        for (var t = 0.0; t < 1; t += Frame)
        {
            track.Advance(Frame);
            speeds.Add((track.Walked - walked) / Frame);
            walked = track.Walked;
        }
        speeds.RemoveAll(s => s == 0);
        speeds.RemoveAt(speeds.Count - 1);      // the last frame ends part way through
        var drop = speeds.Zip(speeds.Skip(1), (a, b) => a - b).Max();

        Assert.True(speeds[^1] < 1.5, $"last frame at {speeds[^1]} tiles a second");
        Assert.True(drop < 0.3, $"lost {drop} tiles a second in a frame");
    }

    [Fact]
    public void A_diagonal_takes_the_servers_time()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, Repeat(1, 0, 8));

        var (legs, played) = (6, 0.0);
        var from = track.Walked;
        Walk(track, Flat, script, Repeat(1, -1, legs), onFrame: () => played += Frame);

        // Six legs of a whole number of frames, so a frame's grace on each.
        Assert.True(Math.Abs(track.Walked - from - legs * Math.Sqrt(2)) < 0.1, $"{track.Walked - from} tiles");
        Assert.True(Math.Abs((track.Walked - from) / played - 1 / 0.3) < 0.1, $"{(track.Walked - from) / played} tiles a second");
    }

    [Fact]
    public void Heights_step_as_the_battle_walk_does()
    {
        var steps = Ground(64, 64, (x, _) => x > 21 ? 1 : 0);
        var track = new WalkTrack();
        var script = new Script(Start, 1);
        track.Receive(script.Now(false));
        Run(track, steps, 0.2);
        Walk(track, steps, script, Repeat(1, 0, 4), onFrame: () =>
        {
            var place = track.Places(steps)[Lead];
            var along = place.Map.X - 0.5f - Start.X;
            var leg = (int)MathF.Floor(along);
            var share = along - leg;
            var ground = leg + Start.X < 21 ? 0 : leg + Start.X > 21 ? 1 : share;
            Assert.True(Math.Abs(ground + BeatPlayer.StepHop * MathF.Sin(share * MathF.PI) - place.Height) < 0.05f, $"{place.Height} at {place.Map.X}");
        });
    }

    [Fact]
    public void A_long_drop_reads_as_a_drop()
    {
        var cliff = Ground(64, 64, (x, _) => x > 21 ? 0 : 6);
        var track = new WalkTrack();
        var script = new Script(Start, 1);
        track.Receive(script.Now(false));
        Run(track, cliff, 0.2);
        var heights = new List<(float X, float H)>();
        var jumped = false;
        for (var i = 0; i < 3; i++)
        {
            var ms = script.Step(1, 0, i == 1 ? 540 : 300);
            track.Receive(script.Now(true));
            for (var t = 0.0; t < ms / 1000.0; t += Frame)
            {
                track.Advance(Frame);
                var place = track.Places(cliff)[Lead];
                heights.Add((place.Map.X, place.Height));
                jumped |= track.Poses[Lead].Anim == "jump";
            }
        }

        var drop = heights.Where(h => h.X >= 21.5f && h.X <= 22.5f).ToArray();
        Assert.True(jumped, "it should leave its feet");
        Assert.Equal(6, drop.First(h => h.X >= 21.9f).H, 1);                               // it walks to the edge first
        var quarter = drop.First(h => h.X >= 22.25f).H;
        Assert.True(quarter > 3.5f, $"{quarter}: gravity starts slow");                    // a straight ramp would be at 3
        Assert.True(drop[^1].H < 0.7f);
    }

    [Fact]
    public void Each_faces_the_way_it_is_walking()
    {
        var track = new WalkTrack();
        var script = new Script(Start, 4);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, [.. Repeat(1, 0, 8), .. Repeat(0, 1, 1)]);

        Assert.Equal(new Vector2(0, 1), track.Poses[Lead].Facing);       // round the corner
        Assert.Equal(new Vector2(1, 0), track.Poses["d"].Facing);       // not there yet
    }

    [Fact]
    public void They_turn_to_the_leaders_way_once_settled()
    {
        var track = new WalkTrack();
        var script = new Script(Start, 4);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, Repeat(0, 1, 8), heldAtEnd: false);
        Run(track, Flat, 3);

        Assert.All(track.Poses.Values, p => Assert.Equal(new Vector2(0, 1), p.Facing));
    }

    [Fact]
    public void A_stairstep_road_doesnt_flip_the_sprite_every_tile()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        var faces = new List<Vector2>();
        Walk(track, Flat, script, [.. Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? (1, 0) : (0, -1))], onFrame: () => faces.Add(track.Poses[Lead].Facing!.Value));

        var flips = faces.Zip(faces.Skip(1), (a, b) => a != b).Count(f => f);
        Assert.True(flips <= 2, $"{flips} flips");
    }

    [Fact]
    public void A_handover_doesnt_teleport_anyone()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, Repeat(1, 0, 6), heldAtEnd: false);
        var was = Run(track, Flat, 3).ToDictionary(p => p.Key, p => p.Value.Map);

        var view = script.Now(false);
        track.Receive(view with { Party = [.. view.Party.Select(w => w with { Leads = w.Id == "b" })] });
        var now = track.Places(Flat);

        Assert.All(was, p => Assert.Equal(p.Value, now[p.Key].Map));
        now = Run(track, Flat, 3);
        Assert.Equal(Centre(script.At), now["b"].Map);
        Assert.NotEqual(Centre(script.At), now[Lead].Map);
    }

    [Fact]
    public void A_stale_view_changes_nothing()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Walk(track, Flat, script, Repeat(1, 0, 4));
        var old = script.Now(true) with { Seq = 2, At = new Tile(22, 20), Legs = [.. script.Legs.Take(2)] };

        var cuts = track.Cuts;
        track.Receive(old);

        Assert.Equal(cuts, track.Cuts);
    }

    [Fact]
    public void Heading_is_nothing_at_rest_and_on_a_tap()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        Run(track, Flat, 0.2);
        Assert.Equal(Vector2.Zero, track.Heading);

        Walk(track, Flat, script, Repeat(1, 0, 1), heldAtEnd: false, onFrame: () => Assert.True(track.Heading.Length() < 0.01f));
        Run(track, Flat, 0.5);
        Assert.Equal(Vector2.Zero, track.Heading);

        Walk(track, Flat, script, Repeat(1, 0, 6));
        Assert.Equal(new Vector2(1, 0), track.Heading);
    }

    [Fact]
    public void Focus_is_the_leader_without_the_hop()
    {
        var track = new WalkTrack();
        var script = new Script(Start);
        track.Receive(script.Now(false));
        script.Step(1, 0);
        track.Receive(script.Now(true));
        Run(track, Flat, 0.15);

        var places = track.Places(Flat);
        Assert.Equal(places[Lead].Map, track.Focus.Map);
        Assert.True(places[Lead].Height > 0);
        Assert.Equal(0, track.Focus.Height);
    }

    /// <summary>The server's walking, with the view stream a TV really gets: latency, jitter, dropped and bunched views, and outages.</summary>
    private sealed class Server(BoardTerrain terrain, Tile start, Random random, int party)
    {
        private readonly List<WalkLeg> _legs = [];
        private readonly List<Tile> _tiles = [];
        private readonly List<(double At, ExploreView View)> _wire = [];
        private readonly Terrain _rules = terrain.Rules;
        private WalkDir? _dir;
        private bool _blocked;
        private bool? _alongX;
        private double _stepDue;
        private double _changeAt;
        private double _outageUntil;
        private int _seq;
        private int _epoch = 1;

        public Tile At { get; private set; } = start;

        public ExploreView View()
            => WalkFeed.View(At, new Tile(At.X + 1, At.Y), _dir is not null && !_blocked, _legs, _seq, _epoch, party);

        public IReadOnlyList<Tile> TakeTiles()
        {
            var taken = _tiles.ToArray();
            _tiles.Clear();
            return taken;
        }

        public void Run(double now)
        {
            if (now >= _changeAt)
            {
                _changeAt = now + (random.NextDouble() < 0.3 ? 0.1 + random.NextDouble() * 0.3 : 0.5 + random.NextDouble() * 4);
                _dir = random.NextDouble() < 0.25 ? null : (WalkDir)random.Next(8);
                _blocked = false;
                if (random.NextDouble() < 0.03)
                    _outageUntil = now + 1 + random.NextDouble() * 3;
                if (random.NextDouble() < 0.01)
                    {
                    _epoch++;
                    _legs.Clear();
                }
                Send(now);
            }
            if (_dir is not { } dir || _blocked || now < _stepDue)
                return;
            if (WorldWalk.Next(_rules, At, dir, 2, _alongX) is not { } move)
            {
                _blocked = true;
                Send(now);
                return;
            }
            var drop = _rules.HeightAt(At) - _rules.HeightAt(move.To);
            var ms = (int)Math.Round(WalkFeed.StraightMs * move.Cost * (move.Diagonal ? Math.Sqrt(2) : 1) + 60 * Math.Max(0, drop - 2));
            if (!move.Diagonal)
                _alongX = move.To.Y == At.Y;
            At = move.To;
            _stepDue = now + ms / 1000.0;
            _tiles.Add(At);
            _legs.Add(new WalkLeg(++_seq, At, ms));
            if (_legs.Count > 12)
                _legs.RemoveAt(0);
            Send(now);
        }

        private void Send(double now)
        {
            if (now < _outageUntil || random.NextDouble() < 0.2)
                return;
            var late = random.NextDouble() < 0.02 ? 0.4 : 0.005 + random.NextDouble() * 0.05;
            var at = Math.Max(now + late, _wire.Count > 0 ? _wire[^1].At : 0);    // order is kept
            _wire.Add((at, View()));
        }

        /// <summary>What reaches the TV this frame: only the newest, as MainThreadQueue keeps.</summary>
        public ExploreView? Arrived(double now, double dt)
        {
            ExploreView? newest = null;
            while (_wire.Count > 0 && _wire[0].At <= now)
            {
                newest = _wire[0].View;
                _wire.RemoveAt(0);
            }
            return newest;
        }
    }
}
