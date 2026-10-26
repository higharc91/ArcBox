namespace Redline.Presentation.Tests;

public class RevealMotionTests
{
    private static readonly CarSlot From = new(1000, -4.4);
    private static readonly CarSlot To = new(1590, 0);

    [Fact]
    public void Drive_starts_at_the_old_slot_and_ends_at_the_new_one()
    {
        var start = RevealMotion.Sample(From, To, false, 0);
        var end = RevealMotion.Sample(From, To, false, RevealMotion.Duration);
        var later = RevealMotion.Sample(From, To, false, 99);

        Assert.Equal(From.RaceMeters, start.RaceMeters, 9);
        Assert.Equal(From.Lateral, start.Lateral, 9);
        Assert.Equal(To.RaceMeters, end.RaceMeters, 9);
        Assert.Equal(To.Lateral, end.Lateral, 9);
        Assert.Equal(end, later);
        Assert.Equal(0, end.Speed);
    }

    [Fact]
    public void Drive_eases_out_and_never_overshoots()
    {
        var previous = From.RaceMeters;
        for (var t = 0.1; t <= RevealMotion.Duration; t += 0.1)
        {
            var s = RevealMotion.Sample(From, To, false, t).RaceMeters;
            Assert.InRange(s, previous, To.RaceMeters);
            previous = s;
        }
        // Ease-out: more than half the way at a quarter of the time.
        Assert.True(RevealMotion.Sample(From, To, false, RevealMotion.Duration / 4).RaceMeters > (From.RaceMeters + To.RaceMeters) / 2);
    }

    [Fact]
    public void Bigger_gains_drive_faster()
    {
        var small = RevealMotion.Sample(From, From with { RaceMeters = 1200 }, false, 1).Speed;
        var big = RevealMotion.Sample(From, To, false, 1).Speed;

        Assert.True(big > small * 2);
    }

    [Fact]
    public void Spin_turns_540_degrees_in_the_spin_window_with_smoke()
    {
        var back = new CarSlot(600, 4.4);
        var mid = RevealMotion.Sample(From, back, true, RevealMotion.SpinDuration / 2);
        var spun = RevealMotion.Sample(From, back, true, RevealMotion.SpinDuration);

        Assert.Equal(1, mid.Smoke);
        Assert.InRange(mid.Yaw, 0.1, 3 * Math.PI);
        Assert.Equal(3 * Math.PI, spun.Yaw, 6);
        Assert.True(spun.RaceMeters > From.RaceMeters, "the car carries on while it spins");
    }

    [Fact]
    public void Spin_slides_back_and_ends_facing_forwards_without_smoke()
    {
        var back = new CarSlot(600, 4.4);

        var end = RevealMotion.Sample(From, back, true, RevealMotion.Duration);

        Assert.Equal(back.RaceMeters, end.RaceMeters, 9);
        Assert.Equal(back.Lateral, end.Lateral, 9);
        Assert.Equal(0, Math.IEEERemainder(end.Yaw, 2 * Math.PI), 6);
        Assert.Equal(0, end.Smoke);
        Assert.True(RevealMotion.Sample(From, back, true, 3).Speed < 0, "sliding back");
    }

    [Fact]
    public void Spin_motion_is_continuous()
    {
        var back = new CarSlot(600, 4.4);
        var previous = RevealMotion.Sample(From, back, true, 0);
        for (var t = 0.01; t <= RevealMotion.Duration + 0.5; t += 0.01)
        {
            var now = RevealMotion.Sample(From, back, true, t);
            Assert.True(Math.Abs(now.RaceMeters - previous.RaceMeters) < 6, $"jump in s at {t:0.00}");
            Assert.True(Math.Abs(now.Yaw - previous.Yaw) < 0.2, $"jump in yaw at {t:0.00}");
            Assert.True(Math.Abs(now.Lateral - previous.Lateral) < 0.2, $"jump in lateral at {t:0.00}");
            previous = now;
        }
    }
}
