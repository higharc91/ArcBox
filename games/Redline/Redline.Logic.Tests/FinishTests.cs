using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Testing;
using Redline.Logic;

namespace Redline.Logic.Tests;

public class FinishTests
{
    private static readonly string[] Codes = ["NOR", "VER", "LEC", "ANT"];

    /// <summary>AI never answers correctly (P = 0), so AI cars never gain distance.</summary>
    private static readonly RedlineRules HopelessAi = new() { Ai = new AiRules { Easy = 0, Medium = 0, Hard = 0, MinP = 0 } };

    /// <summary>AI always answers correctly (P = 1).</summary>
    private static readonly RedlineRules PerfectAi = new() { Ai = new AiRules { MinP = 1, MaxP = 1 } };

    private static string Snapshot(GameHarness<RedlineGame> h) => JsonSerializer.Serialize(h.Game.HostView(), ArcJson.Options);

    private static string Surname(string code) => Grid2026.Load().ByCode(code).Driver.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];

    private static Driver Human(string code, int n, int distance, int correct, int redlines = 0, int spinOuts = 0) =>
        new(Grid2026.Load().ByCode(code))
        {
            PlayerId = new PlayerId($"p{n}"), PlayerName = $"Player{n}",
            Distance = distance, Correct = correct, Redlines = redlines, SpinOuts = spinOuts,
        };

    private static Driver Ai(string code, int distance, int correct, int redlines = 0, int spinOuts = 0) =>
        new(Grid2026.Load().ByCode(code)) { Distance = distance, Correct = correct, Redlines = redlines, SpinOuts = spinOuts };

    /// <summary>Plays one full round: throttles (null = no press), then answers (true right, false wrong, null none).</summary>
    private static void PlayRound(GameHarness<RedlineGame> h, string?[] throttles, bool?[] answers)
    {
        Assert.Equal(RacePhase.Throttle, h.Game.Phase);
        for (var i = 0; i < throttles.Length; i++)
            if (throttles[i] is { } t) RedlineKit.Throttle(h, i, t);
        if (h.Game.Phase == RacePhase.Throttle) h.Advance(h.Game.Rules.Throttle);

        Assert.Equal(RacePhase.Question, h.Game.Phase);
        for (var i = 0; i < answers.Length; i++)
        {
            if (answers[i] == true) RedlineKit.AnswerRight(h, i);
            else if (answers[i] == false) RedlineKit.AnswerWrong(h, i);
        }
        if (h.Game.Phase == RacePhase.Question) h.Advance(h.Game.Rules.Question);
        Assert.Equal(RacePhase.Reveal, h.Game.Phase);
    }

    [Fact]
    public void Race_ends_after_reveal_when_a_car_reaches_the_finish_line()
    {
        var (h, _) = RedlineKit.Start();
        RedlineKit.SeatAllAndGoToRound1(h, Codes);

        // 7 correct Redlines (+4) reach the 28-unit line exactly.
        for (var round = 1; round <= 7; round++)
        {
            Assert.Equal(round, h.Game.Round);
            PlayRound(h, ["redline", "cruise", "cruise", "cruise"], [true, false, false, false]);
            Assert.Equal(4 * round, RedlineKit.Row(h, 0).Distance);
            h.Advance(h.Game.Rules.Reveal);
            if (round < 7) Assert.Equal(RacePhase.Throttle, h.Game.Phase);
        }

        var v = RedlineKit.View(h);
        Assert.Equal((RacePhase.Chequered, 7), (v.Phase, v.Round));
        Assert.Equal(28, v.FinishLine);
        Assert.Equal(h.Now + TimeSpan.FromSeconds(10), v.DeadlineUtc);
        Assert.NotNull(v.Finish);
        Assert.Null(v.Question);
        Assert.Null(v.CorrectIndex);
        Assert.Equal(22, v.Drivers.Count);
        Assert.Equal(1, RedlineKit.Row(h, 0).Position);
        Assert.Equal(v.Drivers.OrderByDescending(d => d.Distance).Select(d => d.Distance), v.Drivers.Select(d => d.Distance));

        var phone = Assert.IsType<WaitView>(h.PlayerView(0));
        Assert.Equal("You finished P1 of 22.", phone.Message);
        Assert.Equal("P1 · ROUND 7/15", phone.Header);
        Assert.Equal($"You finished P{RedlineKit.Row(h, 3).Position} of 22.", Assert.IsType<WaitView>(h.PlayerView(3)).Message);

        // Input during Chequered is ignored.
        RedlineKit.Throttle(h, 1, "push");
        Assert.Equal(RacePhase.Chequered, h.Game.Phase);

        h.Advance(TimeSpan.FromSeconds(9));
        Assert.Null(h.Results);
        h.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(h.Results);
        Assert.Equal("Player1 (NOR) wins the race!", h.Results.Message);
    }

    [Fact]
    public void Race_ends_after_round_15_without_a_finisher()
    {
        var (h, _) = RedlineKit.Start(rules: HopelessAi);
        RedlineKit.SeatAllAndGoToRound1(h, Codes);

        for (var round = 1; round <= 15; round++)
        {
            Assert.Equal((RacePhase.Throttle, round), (h.Game.Phase, h.Game.Round));
            PlayRound(h, ["cruise", "cruise", "cruise", "cruise"], [null, null, null, null]);
            h.Advance(h.Game.Rules.Reveal);
        }

        var v = RedlineKit.View(h);
        Assert.Equal((RacePhase.Chequered, 15), (v.Phase, v.Round));
        Assert.All(v.Drivers, d => Assert.Equal(0, d.Distance));
        Assert.All(v.Drivers, d => Assert.Equal(1, d.Position));                     // everyone shares P1
        Assert.NotNull(v.Finish);
        Assert.Empty(v.Finish.Awards);                                                // nobody earned anything
        Assert.Equal("You finished P1 of 22.", Assert.IsType<WaitView>(h.PlayerView(2)).Message);

        h.Advance(h.Game.Rules.Chequered);
        Assert.NotNull(h.Results);
        Assert.Equal(4, h.Results.Scores.Count);
        // All four humans share P1, named in race (display) order.
        var names = h.Results.Scores.Select(s => s.Name).ToList();
        Assert.Equal($"{names[0]}, {names[1]}, {names[2]} and {names[3]} share the win!", h.Results.Message);
    }

    [Fact]
    public void Results_list_only_humans_in_race_order()
    {
        var (h, _) = RedlineKit.Start(rules: HopelessAi);
        RedlineKit.SeatAllAndGoToRound1(h, Codes);

        // P1 first (redline, 28), P3 second (push, 21), P2 third (cruise, 7), P4 last (wrong cruise, 0).
        for (var round = 1; round <= 7; round++)
        {
            PlayRound(h, ["redline", "cruise", "push", "cruise"], [true, true, true, false]);
            h.Advance(h.Game.Rules.Reveal);
        }
        Assert.Equal(RacePhase.Chequered, h.Game.Phase);
        h.Advance(h.Game.Rules.Chequered);

        var r = h.Results;
        Assert.NotNull(r);
        Assert.Equal(4, r.Scores.Count);
        Assert.Equal(["Player1 (NOR)", "Player3 (LEC)", "Player2 (VER)", "Player4 (ANT)"], r.Scores.Select(s => s.Name));
        Assert.Equal([28, 21, 7, 0], r.Scores.Select(s => s.Score));
        Assert.Equal(new[] { 0, 2, 1, 3 }.Select(i => h.Players[i].Id), r.Scores.Select(s => s.PlayerId));
        Assert.Equal("Player1 (NOR) wins the race!", r.Message);
    }

    [Fact]
    public void Ai_winner_message_names_best_player()
    {
        var (h, _) = RedlineKit.Start(players: 2, rules: PerfectAi);
        RedlineKit.SeatAllAndGoToRound1(h, "NOR", "VER");

        // Player1 answers right on Cruise; Player2 never answers. AI is always right and gets away.
        while (h.Game.Phase != RacePhase.Chequered)
        {
            PlayRound(h, ["cruise", null], [true, null]);
            h.Advance(h.Game.Rules.Reveal);
        }

        var v = RedlineKit.View(h);
        var winner = v.Drivers[0];
        Assert.Null(winner.Player);
        Assert.Equal(1, winner.Position);
        Assert.True(winner.Distance >= h.Game.Rules.FinishLine);
        var best = RedlineKit.Row(h, 0);
        Assert.True(best.Position > 1);

        h.Advance(h.Game.Rules.Chequered);
        Assert.NotNull(h.Results);
        Assert.Equal($"{Surname(winner.Code)} (AI) wins the race · best player: Player1 (NOR), P{best.Position}", h.Results.Message);
        Assert.Equal(["Player1 (NOR)", "Player2 (VER)"], h.Results.Scores.Select(s => s.Name));
    }

    [Fact]
    public void Awards_go_to_humans_and_are_omitted_when_unearned()
    {
        var (h, _) = RedlineKit.Start(rules: PerfectAi);
        RedlineKit.SeatAllAndGoToRound1(h, Codes);

        // Player1: Push and right every round (best mind). Player2: Redline and right in rounds 1–2, then Cruise
        // without answering (boldest, ranked below Player1). Player3: Cruise, wrong. Player4: Cruise, no answer.
        // Nobody spins. AI (always right, so also never spinning) out-redlines and out-answers every human.
        while (h.Game.Phase != RacePhase.Chequered)
        {
            var early = h.Game.Round <= 2;
            PlayRound(h, ["push", early ? "redline" : "cruise", "cruise", "cruise"], [true, early ? true : null, false, null]);
            h.Advance(h.Game.Rules.Reveal);
        }

        var rounds = h.Game.Round;
        var v = RedlineKit.View(h);
        Assert.True(RedlineKit.Row(h, 0).Position < RedlineKit.Row(h, 1).Position);
        Assert.NotNull(v.Finish);
        Assert.Equal(
            [
                new Award("Boldest driver", h.Players[1].Id, "Player2", "2 Redlines"),
                new Award("Sharpest mind", h.Players[0].Id, "Player1", $"{rounds} of {rounds} right"),
            ],
            v.Finish.Awards);
        Assert.DoesNotContain(v.Finish.Awards, a => a.Title == "Biggest crash");
    }

    [Fact]
    public void Awards_ties_go_to_the_better_ranked_driver()
    {
        Driver[] field =
        [
            Ai("VER", 30, 9, redlines: 9, spinOuts: 5),                       // AI: never awarded
            Human("NOR", 1, 12, 4, redlines: 3, spinOuts: 2),
            Human("LEC", 2, 10, 5, redlines: 3, spinOuts: 2),
            Human("ANT", 3, 8, 5, redlines: 1, spinOuts: 1),
        ];
        var awards = Ranking.Awards(Ranking.Rank(field), rounds: 9);

        Assert.Equal(
            [
                new Award("Boldest driver", new PlayerId("p1"), "Player1", "3 Redlines"),
                new Award("Sharpest mind", new PlayerId("p2"), "Player2", "5 of 9 right"),
                new Award("Biggest crash", new PlayerId("p1"), "Player1", "2 spin-outs"),
            ],
            awards);
    }

    [Fact]
    public void Award_details_are_singular_for_one()
    {
        Driver[] field = [Human("NOR", 1, 12, 1, redlines: 1, spinOuts: 1)];
        var awards = Ranking.Awards(Ranking.Rank(field), rounds: 9);

        Assert.Equal(
            [
                new Award("Boldest driver", new PlayerId("p1"), "Player1", "1 Redline"),
                new Award("Sharpest mind", new PlayerId("p1"), "Player1", "1 of 9 right"),
                new Award("Biggest crash", new PlayerId("p1"), "Player1", "1 spin-out"),
            ],
            awards);
    }

    // Review Focus 4
    [Fact]
    public void Two_finishers_same_reveal_bigger_overshoot_wins_equal_shares()
    {
        var nor = Human("NOR", 1, 21, 6);
        var ver = Human("VER", 2, 22, 6);
        var overshoot = Ranking.Rank([nor, ver]);
        Assert.Equal([(ver, 1), (nor, 2)], overshoot);

        var a = Human("NOR", 1, 21, 6);
        var b = Human("VER", 2, 21, 6);
        var shared = Ranking.Rank([a, b]);
        Assert.Equal([(a, 1), (b, 1)], shared);
        Assert.Equal("Player1 (NOR) and Player2 (VER) share the win!", Ranking.Results(shared).Message);

        // Correct answers break a distance tie; full ties share and the next position skips (1, 2, 2, 4).
        var w = Ai("LEC", 21, 7);
        var x = Ai("ANT", 21, 6);
        var y = Ai("RUS", 21, 6);
        var z = Ai("HAM", 3, 9);
        Assert.Equal([(w, 1), (x, 2), (y, 2), (z, 4)], Ranking.Rank([z, x, w, y]));
        Assert.Equal([(w, 1), (y, 2), (x, 2), (z, 4)], Ranking.Rank([y, z, x, w]));   // full ties keep input order
    }

    [Fact]
    public void Same_seed_gives_identical_host_views_every_step()
    {
        var (a, _) = RedlineKit.Start(seed: 5);
        var (b, _) = RedlineKit.Start(seed: 5);
        Assert.Equal(Snapshot(a), Snapshot(b));

        void Both(Action<GameHarness<RedlineGame>> act)
        {
            act(a);
            act(b);
            Assert.Equal(Snapshot(a), Snapshot(b));
        }

        Both(h => RedlineKit.Claim(h, 0, "NOR"));
        Both(h => RedlineKit.Claim(h, 1, "VER"));
        Both(h => h.Advance(h.Game.Rules.DriverSelect));                              // players 3 and 4 get random seats
        Both(h => h.Advance(h.Game.Rules.Grid));

        var phases = new List<RacePhase>();
        while (!a.IsOver)
        {
            Assert.Equal(RacePhase.Throttle, a.Game.Phase);
            Both(h => RedlineKit.Throttle(h, 0, "redline"));
            Both(h => RedlineKit.Throttle(h, 1, "push"));
            Both(h => RedlineKit.Throttle(h, 2, "cruise"));
            Both(h => h.Advance(h.Game.Rules.Throttle));                              // player 4 never presses
            Both(h => RedlineKit.AnswerRight(h, 0));
            Both(h => RedlineKit.AnswerWrong(h, 1));
            Both(h => RedlineKit.AnswerRight(h, 2));
            Both(h => h.Advance(h.Game.Rules.Question));                              // player 4 never answers
            Both(h => h.Advance(h.Game.Rules.Reveal));
            phases.Add(a.Game.Phase);
            if (a.Game.Phase == RacePhase.Chequered) Both(h => h.Advance(h.Game.Rules.Chequered));
        }

        Assert.True(b.IsOver);
        Assert.Equal(RacePhase.Chequered, phases[^1]);
        Assert.Equal(JsonSerializer.Serialize(a.Results, ArcJson.Options), JsonSerializer.Serialize(b.Results, ArcJson.Options));
    }
}
