using ArcBox.Sdk;
using Redline.Logic;

namespace Redline.Presentation.Tests;

public class CameraDirectorTests
{
    private static DriverRow Row(
        string code, int pos, int dist, int? prevDist = null, bool human = false, RoundOutcome? outcome = null) =>
        new(code, 1, "team", human ? new PlayerId(code.ToLowerInvariant()) : null, human ? code : null,
            pos, 0, dist, prevDist ?? dist, DriverStatus.Ready, null, null, outcome, false, true);

    private static RedlineHostView View(RacePhase phase, params DriverRow[] rows) =>
        new(phase, 1, 15, 20, "cota", null, null, rows, null, null, "attribution");

    private static IReadOnlyList<string> HeliSubjects(Shot shot) =>
        Assert.IsType<HeliShot>(shot).Subjects;

    [Fact]
    public void DriverSelect_follows_the_new_claim()
    {
        var before = View(RacePhase.DriverSelect, Row("VER", 1, 0, human: true), Row("HAM", 2, 0), Row("NOR", 3, 0));
        var after = View(RacePhase.DriverSelect, Row("VER", 1, 0, human: true), Row("HAM", 2, 0), Row("NOR", 3, 0, human: true));

        Assert.Equal("NOR", Assert.IsType<LineupShot>(CameraDirector.Pick(after, before)).Subject);
        Assert.Null(Assert.IsType<LineupShot>(CameraDirector.Pick(after, after)).Subject);
    }

    [Fact]
    public void Grid_and_chequered()
    {
        var rows = new[] { Row("VER", 1, 0, human: true), Row("HAM", 2, 0) };

        Assert.IsType<GridShot>(CameraDirector.Pick(View(RacePhase.Grid, rows), null));
        Assert.IsType<FinishShot>(CameraDirector.Pick(View(RacePhase.Chequered, rows), null));
    }

    [Fact]
    public void Throttle_heli_on_best_placed_human()
    {
        var view = View(RacePhase.Throttle,
            Row("AIA", 1, 9), Row("HAM", 2, 8, human: true), Row("NOR", 3, 7, human: true), Row("AIB", 4, 6));

        Assert.Equal(["HAM"], HeliSubjects(CameraDirector.Pick(view, null)));
    }

    [Fact]
    public void Throttle_without_humans_falls_back_to_the_leader()
    {
        var view = View(RacePhase.Throttle, Row("AIA", 1, 9), Row("AIB", 2, 8));

        Assert.Equal(["AIA"], HeliSubjects(CameraDirector.Pick(view, null)));
    }

    [Fact]
    public void Question_closest_battle_with_a_human()
    {
        var view = View(RacePhase.Question,
            Row("AIA", 1, 12), Row("HAM", 2, 8, human: true), Row("AIB", 3, 7), Row("NOR", 4, 6, human: true), Row("AIC", 5, 1));

        // gaps: AIA-HAM 4, HAM-AIB 1, AIB-NOR 1, NOR-AIC 5 → HAM-AIB is first of the 1s
        Assert.Equal(["HAM", "AIB"], HeliSubjects(CameraDirector.Pick(view, null)));
    }

    [Fact]
    public void Question_tie_goes_to_higher_position()
    {
        var view = View(RacePhase.Question,
            Row("AIA", 1, 10), Row("HAM", 2, 9, human: true), Row("AIB", 3, 5), Row("NOR", 4, 4, human: true));

        // gaps: AIA-HAM 1, HAM-AIB 4, AIB-NOR 1 → both 1; AIA-HAM is the higher pair
        Assert.Equal(["AIA", "HAM"], HeliSubjects(CameraDirector.Pick(view, null)));
    }

    [Fact]
    public void Ai_only_battle_never_chosen_while_a_human_battle_exists()
    {
        var view = View(RacePhase.Question,
            Row("AIA", 1, 10), Row("AIB", 2, 10), Row("HAM", 3, 9, human: true), Row("AIC", 4, 5));

        // AI-only gap 0, human gap 1 (AIB-HAM)
        Assert.Equal(["AIB", "HAM"], HeliSubjects(CameraDirector.Pick(view, null)));
    }

    [Fact]
    public void Question_without_humans_uses_the_closest_ai_battle()
    {
        var view = View(RacePhase.Question, Row("AIA", 1, 10), Row("AIB", 2, 9), Row("AIC", 3, 2));

        Assert.Equal(["AIA", "AIB"], HeliSubjects(CameraDirector.Pick(view, null)));
    }

    [Fact]
    public void Reveal_spin_out_beats_biggest_gainer()
    {
        var view = View(RacePhase.Reveal,
            Row("HAM", 1, 10, prevDist: 5, human: true),
            Row("NOR", 3, 4, prevDist: 4, human: true, outcome: RoundOutcome.SpunOut),
            Row("LEC", 4, 3, prevDist: 3, human: true, outcome: RoundOutcome.SpunOut),
            Row("AIA", 2, 5, prevDist: 5, outcome: RoundOutcome.SpunOut));

        var shot = Assert.IsType<TracksideShot>(CameraDirector.Pick(view, null));
        Assert.Equal("NOR", shot.Subject);
    }

    [Fact]
    public void Reveal_biggest_human_gainer()
    {
        var view = View(RacePhase.Reveal,
            Row("AIA", 1, 12, prevDist: 6),
            Row("HAM", 2, 8, prevDist: 6, human: true),
            Row("NOR", 3, 8, prevDist: 6, human: true),
            Row("LEC", 4, 4, prevDist: 3, human: true));

        // HAM and NOR both gain 2 (AI gain of 6 ignored); the better position wins
        var shot = Assert.IsType<TracksideShot>(CameraDirector.Pick(view, null));
        Assert.Equal("HAM", shot.Subject);
    }

    [Fact]
    public void Reveal_follow_up_is_leader()
    {
        var view = View(RacePhase.Reveal, Row("HAM", 2, 8, human: true), Row("AIA", 1, 9));

        Assert.Equal(["AIA"], HeliSubjects(CameraDirector.RevealFollowUp(view)));
    }
}
