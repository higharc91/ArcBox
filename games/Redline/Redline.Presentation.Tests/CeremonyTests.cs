using System.Numerics;
using ArcBox.Sdk;
using Redline.Logic;
using Redline.Presentation.Audio;
using Redline.Presentation.Ceremony;
using Redline.Presentation.Demo;
using Redline.Presentation.Hud;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Tests;

public class CeremonyTests
{
    private static readonly CircuitData Cota = CircuitData.Load("cota");
    private static readonly IReadOnlyList<TrackSample> Samples = TrackGeometry.Resample(Cota);
    private static readonly double Lap = Samples.Count * TrackGeometry.Step;
    private static readonly PodiumLayout Layout = PodiumLayout.For(Samples, Lap);

    private static Podium Ceremony(params (int Step, int[] Seqs)[] sprays) => new("Great Britain", "gb",
    [
        .. new[] { ("JO", "NOR", "mclaren"), ("ALEX", "LEC", "ferrari"), ("RIA", "ANT", "mercedes") }.Select((p, i) =>
            new PodiumStep(i + 1, new PlayerId($"p{i + 1}"), p.Item1, p.Item2, p.Item3, i + 1, 0.5,
                [.. sprays.Where(s => s.Step == i + 1).SelectMany(s => s.Seqs).Select(q => new Spray(q, Math.Round(0.2 + (0.1 * q), 2)))])),
    ]);

    private static RedlineHostView View(RacePhase phase, Podium? podium) =>
        DemoRace.Steps().Last(s => s.View.Phase == RacePhase.Chequered).View with { Phase = phase, Finish = null, Podium = podium };

    // ---- the set

    [Fact]
    public void Steps_are_two_one_three_with_the_winner_highest()
    {
        Assert.True(PodiumLayout.StepX(2) < PodiumLayout.StepX(1));
        Assert.True(PodiumLayout.StepX(1) < PodiumLayout.StepX(3));
        Assert.True(PodiumLayout.StepTop(1) > PodiumLayout.StepTop(2));
        Assert.True(PodiumLayout.StepTop(2) > PodiumLayout.StepTop(3));
        Assert.True(PodiumLayout.StepTop(3) > PodiumLayout.StageHeight);
        Assert.Throws<ArgumentOutOfRangeException>(() => PodiumLayout.StepX(4));
        // Neighbouring steps don't overlap and all of them fit on the stage.
        Assert.True(PodiumLayout.StepPitch >= PodiumLayout.StepWidth);
        Assert.True(PodiumLayout.StepPitch + (PodiumLayout.StepWidth / 2) < PodiumLayout.StageWidth / 2);
    }

    [Fact]
    public void Set_stands_on_the_pit_side_of_the_straight_before_the_line_facing_the_grandstand()
    {
        Assert.Equal(Lap - PodiumLayout.SiteBeforeLine, Layout.SiteS, 3);
        var track = TrackPose.At(Samples, Layout.SiteS, 0);
        // Local +Z (towards the camera) points to the right of travel, where the main grandstand is.
        Assert.True(Vector3.Dot(Layout.Front, track.Left) < -0.99f);
        Assert.True(Vector3.Dot(Layout.Right, track.Forward) > 0.99f);
        // A right-handed frame: Right × Up = Front.
        Assert.True(Vector3.Distance(Vector3.Cross(Layout.Right, Layout.Up), Layout.Front) < 1e-4f);
        // The backdrop stays inside the pit wall (4 m beyond the asphalt) and off the grid slots.
        var backdropLateral = PodiumLayout.FrontLateral - PodiumLayout.BackdropZ;
        Assert.InRange(backdropLateral, TrackGeometry.Width(Layout.SiteS) / 2, (TrackGeometry.Width(Layout.SiteS) / 2) + TrackMeshBuilder.WallGap - 0.3);
    }

    [Fact]
    public void Cars_park_nose_in_in_front_of_their_steps()
    {
        for (var step = 1; step <= 3; step++)
        {
            var car = Layout.Car(step);
            // In front of the stage (towards the camera), on the asphalt.
            Assert.True(car.Lateral < PodiumLayout.FrontLateral - 2.9, $"car {step} lateral {car.Lateral}");
            Assert.True(Math.Abs(car.Lateral) < TrackGeometry.Width(Layout.SiteS) / 2);
            // Nose towards the stage (yaw near +90°: pointing left of travel).
            Assert.InRange(car.Yaw, Math.PI / 2 - 0.3, Math.PI / 2 + 0.3);
        }
        Assert.True(Layout.Car(2).RaceMeters < Layout.Car(1).RaceMeters);
        Assert.True(Layout.Car(1).RaceMeters < Layout.Car(3).RaceMeters);
        // The outer cars turn in towards the winner's car.
        Assert.True(Layout.Car(2).Yaw < Layout.Car(1).Yaw);
        Assert.True(Layout.Car(3).Yaw > Layout.Car(1).Yaw);
    }

    [Fact]
    public void Bottles_are_held_above_their_steps_and_names_float_above_the_heads()
    {
        for (var step = 1; step <= 3; step++)
        {
            var bottle = PodiumLayout.Bottle(step);
            Assert.True(bottle.Y > PodiumLayout.StepTop(step) + 0.8f);
            Assert.True(Math.Abs(bottle.X - PodiumLayout.StepX(step)) < PodiumLayout.StepWidth / 2);
            Assert.True(PodiumLayout.NameAnchor(step).Y > PodiumLayout.StepTop(step) + PodiumLayout.FigureHeight);
        }
    }

    // ---- the camera

    private static bool InFrame(CameraPose cam, Vector3 p, float margin = 0.92f)
    {
        var forward = Vector3.Normalize(cam.Target - cam.Eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        var d = p - cam.Eye;
        var z = Vector3.Dot(d, forward);
        if (z <= 0) return false;
        var tanV = MathF.Tan(cam.Fov / 2 * MathF.PI / 180);
        var x = Vector3.Dot(d, right) / z / (tanV * 16 / 9);
        var y = Vector3.Dot(d, up) / z / tanV;
        return Math.Abs(x) < margin && Math.Abs(y) < margin;
    }

    [Theory]
    [InlineData(false, 0.0)]
    [InlineData(false, 8.0)]
    [InlineData(false, 16.0)]
    [InlineData(true, 0.0)]
    [InlineData(true, 16.0)]
    public void Camera_frames_all_three_drivers_and_their_names(bool champagne, double seconds)
    {
        var cam = PodiumFraming.Pose(Layout, champagne, seconds);
        for (var step = 1; step <= 3; step++)
        {
            Assert.True(InFrame(cam, Layout.World(PodiumLayout.StepTopCentre(step))), $"step {step} feet");
            Assert.True(InFrame(cam, Layout.World(PodiumLayout.NameAnchor(step))), $"step {step} name");
        }
        // In front of the podium, inside the grandstand-side catch fence.
        var lateral = Vector3.Dot(cam.Eye - TrackPose.At(Samples, Layout.SiteS, 0).Position, TrackPose.At(Samples, Layout.SiteS, 0).Left);
        Assert.InRange(lateral, -TrackMeshBuilder.FenceOffset + 0.5, PodiumLayout.FrontLateral - 8);
    }

    [Fact]
    public void Anthem_pushes_in_and_champagne_goes_wider()
    {
        var feet = Layout.World(PodiumLayout.StepTopCentre(1));
        var start = PodiumFraming.Pose(Layout, false, 0);
        var mid = PodiumFraming.Pose(Layout, false, 8);
        var end = PodiumFraming.Pose(Layout, false, 16);
        var wide = PodiumFraming.Pose(Layout, true, 0);

        Assert.True(Vector3.Distance(mid.Eye, feet) < Vector3.Distance(start.Eye, feet));
        Assert.True(Vector3.Distance(end.Eye, feet) < Vector3.Distance(mid.Eye, feet));
        Assert.Equal(end.Eye, PodiumFraming.Pose(Layout, false, 30).Eye);
        Assert.True(wide.Fov > end.Fov);
        Assert.True(Vector3.Distance(wide.Eye, feet) > Vector3.Distance(end.Eye, feet));
        Assert.NotEqual(wide.Eye, PodiumFraming.Pose(Layout, true, 10).Eye);
    }

    // ---- sprays

    [Fact]
    public void Spray_size_speed_and_duration_grow_with_strength()
    {
        var looks = Enumerable.Range(3, 18).Select(i => SprayLook.For(i * 0.05)).ToList();
        for (var i = 1; i < looks.Count; i++)
        {
            Assert.True(looks[i].Seconds > looks[i - 1].Seconds);
            Assert.True(looks[i].Lifetime > looks[i - 1].Lifetime);
            Assert.True(looks[i].SpeedMax > looks[i - 1].SpeedMax);
            Assert.True(looks[i].Size > looks[i - 1].Size);
            Assert.True(looks[i].Blobs >= looks[i - 1].Blobs);
            Assert.True(looks[i].CorkSpeed > looks[i - 1].CorkSpeed);
            Assert.True(looks[i].HissDb > looks[i - 1].HissDb);
        }
        var full = SprayLook.For(1);
        Assert.InRange(full.Seconds, 2.5, 4);
        Assert.True(full.SpeedMin < full.SpeedMax);
        Assert.True(full.Blobs <= SprayLook.MaxBlobs);
        // A full bottle carries well past the cars (flat-ground range at 35° for the fastest blob).
        Assert.True(full.SpeedMax * full.SpeedMax * Math.Sin(70 * Math.PI / 180) / 9.81 > PodiumLayout.CarZ + 3);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(double.NaN)]
    public void A_flat_or_bad_strength_still_fizzes_at_the_minimum(double strength)
    {
        Assert.Equal(SprayLook.For(Celebration.MinSprayStrength), SprayLook.For(strength));
        Assert.True(SprayLook.For(strength).Blobs > 50);
    }

    [Fact]
    public void Strength_above_one_is_a_full_bottle()
    {
        Assert.Equal(SprayLook.For(1), SprayLook.For(4));
    }

    [Fact]
    public void Jets_aim_up_and_out_with_the_outer_steps_turned_in_and_sweep()
    {
        for (var step = 1; step <= 3; step++)
        {
            for (var t = 0.0; t < 3; t += 0.1)
            {
                var aim = SprayLook.Aim(step, t);
                Assert.Equal(1, aim.Length(), 3);
                Assert.True(aim.Z > 0.35f, "towards the crowd");
                Assert.True(aim.Y > 0.3f, "upwards");
            }
        }
        var two = Enumerable.Range(0, 30).Average(i => SprayLook.Aim(2, i * 0.1).X);
        var three = Enumerable.Range(0, 30).Average(i => SprayLook.Aim(3, i * 0.1).X);
        Assert.True(two > 0.3 && three < -0.3);
        Assert.NotEqual(SprayLook.Aim(1, 0), SprayLook.Aim(1, 0.3));
    }

    [Fact]
    public void Tracker_launches_each_spray_once_in_seq_order_and_flags_the_first()
    {
        var tracker = new SprayTracker();
        var chequered = View(RacePhase.Chequered, null);
        var anthem = View(RacePhase.Anthem, Ceremony());
        var c0 = View(RacePhase.Champagne, Ceremony());
        var c1 = View(RacePhase.Champagne, Ceremony((1, [1])));
        var c2 = View(RacePhase.Champagne, Ceremony((1, [1]), (3, [3]), (2, [2])));

        Assert.Empty(tracker.Update(chequered, null));
        Assert.Empty(tracker.Update(anthem, chequered));
        Assert.Empty(tracker.Update(c0, anthem));
        Assert.Equal([new NewSpray(1, 1, 0.3, true)], tracker.Update(c1, c0));
        Assert.Empty(tracker.Update(c1, c1));
        var both = tracker.Update(c2, c1);
        Assert.Equal([(2, 2, false), (3, 3, false)], both.Select(s => (s.Step, s.Seq, s.First)));
        Assert.Equal(3, tracker.LastSeq);
    }

    [Fact]
    public void Tracker_does_not_replay_sprays_a_late_tv_missed_and_restarts_for_a_new_ceremony()
    {
        var tracker = new SprayTracker();
        var mid = View(RacePhase.Champagne, Ceremony((1, [1, 4]), (2, [2]), (3, [3])));

        Assert.Empty(tracker.Update(mid, null));
        var next = View(RacePhase.Champagne, Ceremony((1, [1, 4]), (2, [2, 5]), (3, [3])));
        Assert.Equal([new NewSpray(2, 5, 0.7, false)], tracker.Update(next, mid));

        // The race ends and a new one runs to its podium: Seq starts again at 1.
        var lobby = View(RacePhase.Chequered, null);
        Assert.Empty(tracker.Update(lobby, next));
        var again = View(RacePhase.Champagne, Ceremony((2, [1])));
        Assert.Equal([new NewSpray(2, 1, 0.3, true)], tracker.Update(again, View(RacePhase.Anthem, Ceremony())));
    }

    // ---- the bottle

    [Fact]
    public void Bottle_is_a_closed_upright_lathe_with_the_foil_over_the_neck()
    {
        var bottle = BottleMesh.Build("#ff8000");

        Assert.Equal(BottleMesh.Height, bottle.Glass.Positions.Max(p => p.Y), 3);
        Assert.Equal(0, bottle.Glass.Positions.Min(p => p.Y), 3);
        Assert.InRange(bottle.Glass.Positions.Max(p => new Vector2(p.X, p.Z).Length()), BottleMesh.BodyRadius - 0.001, BottleMesh.BodyRadius + 0.001);
        Assert.True(bottle.Foil.Positions.Min(p => p.Y) >= BottleMesh.FoilFrom - 0.01f);
        Assert.True(bottle.Foil.Positions.Max(p => p.Y) >= BottleMesh.Height);
        Assert.Contains(bottle.Foil.Colors, c => Math.Abs(c.X - 1) < 1e-3 && Math.Abs(c.Y - (0x80 / 255f)) < 1e-3);
        // A label round the body: some cream, some green.
        Assert.Contains(bottle.Glass.Colors, c => c.X > 0.9f);
        Assert.Contains(bottle.Glass.Colors, c => c.Y > c.X * 1.3f && c.X < 0.2f);
        foreach (var mesh in new[] { bottle.Glass, bottle.Foil })
        {
            Assert.Equal(0, mesh.Indices.Count % 3);
            // Clockwise from the front: the cross product points against the vertex normals.
            for (var i = 0; i < mesh.Indices.Count; i += 3)
            {
                var (a, b, c) = (mesh.Indices[i], mesh.Indices[i + 1], mesh.Indices[i + 2]);
                var cross = Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                Assert.True(Vector3.Dot(cross, mesh.Normals[a] + mesh.Normals[b] + mesh.Normals[c]) <= 0);
            }
        }
        // Outward normals on the body.
        var side = bottle.Glass.Positions.Select((p, i) => (p, n: bottle.Glass.Normals[i])).Where(x => x.p.Y is > 0.1f and < 0.25f).ToList();
        Assert.All(side, x => Assert.True(Vector3.Dot(x.n, new Vector3(x.p.X, 0, x.p.Z)) > 0));
    }

    // ---- music

    [Theory]
    [InlineData("gb", true)]
    [InlineData("fr", true)]
    [InlineData("mc", false)]   // public domain, melody not in yet
    [InlineData("ar", false)]
    [InlineData("nz", false)]
    [InlineData("zz", false)]
    [InlineData(null, false)]   // Brazil, Mexico, Thailand
    public void Anthem_plays_only_with_a_melody(string? id, bool anthem)
    {
        Assert.Equal(anthem, PodiumMusic.Anthem(id) is not null);
    }

    [Fact]
    public void Anthem_is_predicted_from_the_best_placed_player_at_the_flag()
    {
        var grid = Grid2026.Load();
        var chequered = DemoRace.Steps().Last(s => s.View.Phase == RacePhase.Chequered).View;

        Assert.Equal("gb", PodiumMusic.PredictAnthemId(chequered, grid));
        Assert.Null(PodiumMusic.PredictAnthemId(DemoRace.Steps(winner: "PER").Last(s => s.View.Phase == RacePhase.Chequered).View, grid));
        var aiOnly = chequered with { Drivers = [.. chequered.Drivers.Select(d => d with { Player = null, PlayerName = null })] };
        Assert.Null(PodiumMusic.PredictAnthemId(aiOnly, grid));
        // The prediction agrees with the podium the ceremony then shows.
        Assert.Equal(DemoRace.Steps().First(s => s.View.Phase == RacePhase.Anthem).View.Podium!.AnthemId, PodiumMusic.PredictAnthemId(chequered, grid));
    }

    [Fact]
    public void The_band_waits_for_the_line_unless_the_anthem_would_overrun()
    {
        var line = PodiumMusic.LineSeconds("The national anthem of France, for ALEX in the Ferrari!");
        Assert.InRange(line, 3, 4.5);
        Assert.Equal(0, PodiumMusic.LineSeconds(""));

        // A short anthem: after the line.
        Assert.Equal(line + PodiumMusic.AfterLine, PodiumMusic.Start(line, 10, 16, loops: false), 6);
        // A long one: early enough to end within the overrun, under the end of the line.
        var start = PodiumMusic.Start(line, 14.5, 16, loops: false);
        Assert.Equal(16 + PodiumMusic.MaxOverrun - 14.5, start, 6);
        Assert.True(start < line);
        // Never before the phase.
        Assert.Equal(0, PodiumMusic.Start(line, 18, 16, loops: false));
        // The looping theme: after the line.
        Assert.Equal(line + PodiumMusic.AfterLine, PodiumMusic.Start(line, 16, 16, loops: true), 6);
    }

    // ---- HUD

    [Fact]
    public void Anthem_caption_names_the_country_the_player_the_driver_and_the_team()
    {
        var anthem = DemoRace.Steps().First(s => s.View.Phase == RacePhase.Anthem).View;
        var caption = PodiumText.Caption(anthem)!;

        Assert.Equal("NATIONAL ANTHEM · GREAT BRITAIN", caption.Title);
        Assert.Equal("for JO · Lando Norris, McLaren", caption.Subtitle);
        Assert.Equal("CHAMPAGNE!", PodiumText.Caption(anthem with { Phase = RacePhase.Champagne })!.Title);
        Assert.Null(PodiumText.Caption(anthem with { Podium = null }));

        // A theme-only country is still named.
        var mexico = DemoRace.Steps(winner: "PER").First(s => s.View.Phase == RacePhase.Anthem).View;
        Assert.Equal("NATIONAL ANTHEM · MEXICO", PodiumText.Caption(mexico)!.Title);
        Assert.Null(mexico.Podium!.AnthemId);
    }

    [Fact]
    public void Labels_carry_name_code_team_colour_charge_and_spray_count_in_step_order()
    {
        var view = View(RacePhase.Champagne, Ceremony((1, [1, 3]), (3, [2])) with { Steps = [.. Ceremony((1, [1, 3]), (3, [2])).Steps.Reverse()] });
        var labels = PodiumText.Labels(view);

        Assert.Equal([1, 2, 3], labels.Select(l => l.Step));
        Assert.Equal(("JO", "NOR", 1, TeamAccent.ById("mclaren"), 0.5, 2), (labels[0].Name, labels[0].Code, labels[0].Number, labels[0].Accent, labels[0].Charge, labels[0].Sprays));
        Assert.Equal(1, labels[2].Sprays);
        Assert.Empty(PodiumText.Labels(view with { Podium = null }));
        Assert.Equal("#1ed760", PodiumText.GaugeColour(0.1));
        Assert.Equal("#ff3b3b", PodiumText.GaugeColour(0.95));
    }

    [Fact]
    public void Labels_far_apart_stay_over_their_anchors()
    {
        Assert.Equal([900.0, 300.0, 1500.0], PodiumLabelLayout.Spread([900, 300, 1500], 200, 10, 0, 1920));
    }

    [Fact]
    public void Close_labels_spread_evenly_around_their_anchors_without_overlap()
    {
        var x = PodiumLabelLayout.Spread([960, 800, 1120], 262, 12, 0, 1920);

        Assert.Equal(960, x[0], 6);
        Assert.Equal(960 - 274, x[1], 6);
        Assert.Equal(960 + 274, x[2], 6);
    }

    [Fact]
    public void The_label_row_is_pushed_back_on_screen()
    {
        var x = PodiumLabelLayout.Spread([20, 60], 200, 10, 0, 1920);

        Assert.True(x.Min() - 100 >= -1e-9);
        Assert.True(Math.Abs(x[1] - x[0]) >= 210 - 1e-9);
        Assert.Empty(PodiumLabelLayout.Spread([], 200, 10, 0, 1920));
    }
}
