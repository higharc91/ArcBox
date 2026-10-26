using ArcBox.Sdk;
using Redline.Logic;
using Redline.Presentation.Audio;
using Redline.Presentation.Demo;

namespace Redline.Presentation.Tests.Audio;

public class CommentaryTests
{
    private static readonly Grid Grid = Grid2026.Load();

    private static readonly QuestionCard Card = new("Geography", Difficulty.Medium,
        "Which U.S. state has the longest coastline?", ["Florida", "California", "Alaska", "Hawaii"]);

    /// <summary>A row for a real 2026 driver code; <paramref name="player"/> makes it a human seat.</summary>
    private static DriverRow Row(
        string code, int pos, int dist, int? prevDist = null, int change = 0, string? player = null,
        RoundOutcome? outcome = null) =>
        new(code, 1, Grid.ByCode(code).Team.Id, player is null ? null : new PlayerId(player.ToLowerInvariant()), player,
            pos, change, dist, prevDist ?? dist, DriverStatus.Ready, null, null, outcome, false, true);

    private static RedlineHostView View(RacePhase phase, int round, params DriverRow[] rows) =>
        new(phase, round, 15, 28, "cota",
            phase switch
            {
                RacePhase.Grid or RacePhase.Chequered => null,
                RacePhase.Throttle => Card with { Text = null, Answers = null },
                _ => Card,
            },
            phase == RacePhase.Reveal ? 2 : null, rows, null, null, "attribution", Payouts.From(new RedlineRules()));

    private static RedlineHostView Question(int round = 4) => View(RacePhase.Question, round, Row("NOR", 1, 10));

    private static IReadOnlyList<string> Texts(RedlineHostView view, RedlineHostView? previous) =>
        [.. Commentary.Lines(view, previous, Grid).Select(l => l.Text)];

    [Fact]
    public void Grid_intro_counts_players()
    {
        var rows = Grid.Seats.Select((s, i) =>
            Row(s.Driver.Code, i + 1, 0, player: s.Driver.Code is "LEC" or "VER" or "NOR" ? s.Driver.Code.ToLowerInvariant() : null)).ToArray();
        var lines = Commentary.Lines(View(RacePhase.Grid, 1, rows), View(RacePhase.DriverSelect, 0, rows), Grid);

        var line = Assert.Single(lines);
        Assert.Equal("Welcome to the Circuit of the Americas. Twenty-two cars, 3 players. Lights out in a moment.", line.Text);
        Assert.Equal(CommentaryKind.Intro, line.Kind);

        var solo = rows.Select(r => r.Code == "LEC" ? r : r with { Player = null, PlayerName = null }).ToArray();
        Assert.Equal(["Welcome to the Circuit of the Americas. Twenty-two cars, 1 player. Lights out in a moment."],
            Texts(View(RacePhase.Grid, 1, solo), null));
    }

    [Fact]
    public void First_throttle_says_lights_out_then_round_intro()
    {
        var grid = View(RacePhase.Grid, 1, Row("NOR", 1, 0));
        var lines = Commentary.Lines(View(RacePhase.Throttle, 1, Row("NOR", 1, 0)), grid, Grid);

        Assert.Equal(["And it's lights out!", "Round 1. Geography, medium."], lines.Select(l => l.Text));
        Assert.Equal([CommentaryKind.LightsOut, CommentaryKind.RoundIntro], lines.Select(l => l.Kind));
    }

    [Fact]
    public void Later_throttles_only_round_intro()
    {
        var reveal = View(RacePhase.Reveal, 3, Row("NOR", 1, 10));
        var lines = Commentary.Lines(View(RacePhase.Throttle, 4, Row("NOR", 1, 10)), reveal, Grid);

        var line = Assert.Single(lines);
        Assert.Equal("Round 4. Geography, medium.", line.Text);
        Assert.Equal(CommentaryKind.RoundIntro, line.Kind);
    }

    [Fact]
    public void Reveal_says_answer_then_one_player_event()
    {
        var view = View(RacePhase.Reveal, 4,
            Row("NOR", 1, 13, 10),
            Row("LEC", 2, 12, 9, change: 3, player: "Alex", outcome: RoundOutcome.Correct),
            Row("VER", 9, 7, 9, change: -6, player: "Sam", outcome: RoundOutcome.SpunOut));

        var lines = Commentary.Lines(view, Question(), Grid);

        Assert.Equal(["The answer is Alaska.", "Sam spins the Red Bull!"], lines.Select(l => l.Text));
        Assert.Equal([CommentaryKind.Answer, CommentaryKind.Event], lines.Select(l => l.Kind));
        Assert.True(lines[0].Priority > lines[1].Priority);
    }

    [Fact]
    public void Player_spin_beats_lead_change_beats_gain_beats_near_flag()
    {
        var spin = Row("VER", 6, 7, 9, change: -2, player: "Sam", outcome: RoundOutcome.SpunOut);
        var lead = Row("LEC", 1, 15, 11, change: 1, player: "Alex", outcome: RoundOutcome.Correct);
        var gain = Row("ANT", 3, 13, 9, change: 4, player: "Ria", outcome: RoundOutcome.Correct);
        var flag = Row("HUL", 2, 24, 23, change: 0, player: "Christabella", outcome: RoundOutcome.Correct);
        var ai = Row("NOR", 4, 12, 12, change: -3, outcome: RoundOutcome.Wrong);

        string Event(params DriverRow[] rows) => Texts(View(RacePhase.Reveal, 4, rows), Question())[1];

        // Positions are deliberately loose: only the precedence is under test.
        Assert.Equal("Sam spins the Red Bull!", Event(lead, flag, gain, spin, ai));
        Assert.Equal("Alex takes the lead!", Event(flag, gain, lead, ai));
        Assert.Equal("Ria storms up 4 places!", Event(flag, gain, ai));
        Assert.Equal("Christabella can see the flag!", Event(flag, ai));

        // Below the thresholds there is no player event: +2 places, already inside the last 4 before this round.
        var small = gain with { PositionChange = 2 };
        var stillNear = flag with { PreviousDistance = 24, Distance = 25 };
        Assert.Equal(["The answer is Alaska."], Texts(View(RacePhase.Reveal, 4, small, stillNear, ai), Question()));
    }

    [Fact]
    public void Reveal_without_player_event_uses_ai_lead_change_or_nothing()
    {
        var newLeader = Row("RUS", 1, 14, 9, change: 3, outcome: RoundOutcome.Correct);
        var player = Row("LEC", 5, 10, 9, change: 0, player: "Alex", outcome: RoundOutcome.Correct);
        var oldLeader = Row("NOR", 2, 13, 13, change: -1, outcome: RoundOutcome.Wrong);

        Assert.Equal(["The answer is Alaska.", "Russell leads the race."],
            Texts(View(RacePhase.Reveal, 4, newLeader, oldLeader, player), Question()));

        // The AI who led before still leads: nothing to add.
        var steady = View(RacePhase.Reveal, 4, oldLeader with { Position = 1, PositionChange = 0 }, player);
        Assert.Equal(["The answer is Alaska."], Texts(steady, Question()));

        // An AI spin or AI gain is never called.
        var aiSpin = Row("VER", 7, 7, 9, change: -4, outcome: RoundOutcome.SpunOut);
        Assert.Equal(["The answer is Alaska."], Texts(View(RacePhase.Reveal, 4, oldLeader with { Position = 1, PositionChange = 0 }, aiSpin), Question()));
    }

    [Fact]
    public void Never_more_than_two_reveal_lines()
    {
        var rows = new[]
        {
            Row("LEC", 1, 27, 23, change: 1, player: "Alex", outcome: RoundOutcome.Correct),
            Row("NOR", 2, 26, 22, change: 5, player: "Jo", outcome: RoundOutcome.Correct),
            Row("ANT", 3, 25, 21, change: 6, player: "Ria", outcome: RoundOutcome.Correct),
            Row("VER", 4, 21, 23, change: -3, player: "Sam", outcome: RoundOutcome.SpunOut),
            Row("PER", 5, 20, 22, change: -1, player: "Kai", outcome: RoundOutcome.SpunOut),
            Row("RUS", 6, 19, 15, change: 4, outcome: RoundOutcome.Correct),
        };

        var lines = Commentary.Lines(View(RacePhase.Reveal, 9, rows), View(RacePhase.Question, 9, rows), Grid);

        Assert.Equal(2, lines.Count);
        Assert.Equal(CommentaryKind.Answer, lines[0].Kind);
        // The best-placed spin, matching the camera's trackside shot.
        Assert.Equal("Sam spins the Red Bull!", lines[1].Text);
    }

    [Fact]
    public void Chequered_player_winner()
    {
        var view = View(RacePhase.Chequered, 11,
            Row("LEC", 1, 29, player: "Alex"), Row("NOR", 2, 28, player: "Jo"), Row("PIA", 3, 27));

        var line = Assert.Single(Commentary.Lines(view, View(RacePhase.Reveal, 11, view.Drivers.ToArray()), Grid));

        Assert.Equal("Chequered flag! Alex wins at the Circuit of the Americas!", line.Text);
        Assert.Equal(CommentaryKind.Finish, line.Kind);
    }

    [Fact]
    public void Chequered_ai_winner_names_best_player()
    {
        var view = View(RacePhase.Chequered, 15,
            Row("PIA", 1, 29), Row("RUS", 2, 27), Row("LEC", 3, 26, player: "Alex"), Row("NOR", 4, 25, player: "Jo"));

        Assert.Equal(["Piastri takes the win. Best of the players, Alex in P3."], Texts(view, null));

        // No players at all: just the winner.
        var aiOnly = View(RacePhase.Chequered, 15, Row("PIA", 1, 29), Row("RUS", 2, 27));
        Assert.Equal(["Piastri takes the win."], Texts(aiOnly, null));
    }

    [Fact]
    public void No_lines_without_phase_change()
    {
        var rows = new[] { Row("LEC", 1, 12, 9, change: 3, player: "Alex", outcome: RoundOutcome.Correct) };
        var throttle = View(RacePhase.Throttle, 4, rows);
        var reveal = View(RacePhase.Reveal, 4, rows);
        var grid = View(RacePhase.Grid, 1, rows);

        Assert.Empty(Commentary.Lines(throttle, throttle with { Drivers = [rows[0] with { Status = DriverStatus.Locked }] }, Grid));
        Assert.Empty(Commentary.Lines(reveal, reveal, Grid));
        Assert.Empty(Commentary.Lines(grid, grid, Grid));
        Assert.Empty(Commentary.Lines(View(RacePhase.Question, 4, rows), throttle, Grid));
        Assert.Empty(Commentary.Lines(View(RacePhase.DriverSelect, 0, rows), null, Grid));

        // A new round is an entry even if the phase is the same (a missed Reveal view).
        Assert.Single(Commentary.Lines(View(RacePhase.Throttle, 5, rows), throttle, Grid));
    }

    [Fact]
    public void Demo_race_calls_the_spin_and_the_win()
    {
        var steps = DemoRace.Steps();
        var said = new List<string>();
        RedlineHostView? previous = null;
        foreach (var step in steps)
        {
            said.AddRange(Commentary.Lines(step.View, previous, Grid).Select(l => l.Text));
            previous = step.View;
        }

        Assert.Equal(
        [
            "Welcome to the Circuit of the Americas. Twenty-two cars, 7 players. Lights out in a moment.",
            "Round 4. Geography, medium.",
            "The answer is Alaska.",
            "SAM spins the Red Bull!",
            "Chequered flag! ALEX wins at the Circuit of the Americas!",
        ], said);
    }
}
