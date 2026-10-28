using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Testing;
using Redline.Logic;

namespace Redline.Logic.Tests;

/// <summary>Podium spec §2: the Anthem and Champagne phases after the Chequered flag, and the shake inputs.</summary>
public class PodiumTests
{
    /// <summary>A one-round race in which AI never answers correctly, so only the players move.</summary>
    private static readonly RedlineRules OneRound = new()
    {
        MaxRounds = 1,
        Ai = new AiRules { Easy = 0, Medium = 0, Hard = 0, MinP = 0 },
    };

    /// <summary>A one-round race in which AI always answers correctly.</summary>
    private static readonly RedlineRules OneRoundPerfectAi = new() { MaxRounds = 1, Ai = new AiRules { MinP = 1, MaxP = 1 } };

    private const string AnthemPrompt = "Shake to build pressure!";
    private const string ChampagnePrompt = "Pop the cork! Shake again to reload";

    /// <summary>
    /// Seats players 0..n-1 in <paramref name="codes"/>, plays the single round with the given throttles and answers
    /// (true right, false wrong), and runs time on to the Chequered flag.
    /// </summary>
    private static GameHarness<RedlineGame> ToChequered(
        string[] codes, string[] throttles, bool[] answers, RedlineRules? rules = null, int seed = 1)
    {
        var (h, _) = RedlineKit.Start(players: codes.Length, seed: seed, rules: rules ?? OneRound);
        RedlineKit.SeatAllAndGoToRound1(h, codes);
        Assert.Equal(RacePhase.Throttle, h.Game.Phase);
        for (var i = 0; i < codes.Length; i++) RedlineKit.Throttle(h, i, throttles[i]);
        if (h.Game.Phase == RacePhase.Throttle) h.Advance(h.Game.Rules.Throttle);
        for (var i = 0; i < codes.Length; i++)
        {
            if (answers[i]) RedlineKit.AnswerRight(h, i);
            else RedlineKit.AnswerWrong(h, i);
        }
        if (h.Game.Phase == RacePhase.Question) h.Advance(h.Game.Rules.Question);
        h.Advance(h.Game.Rules.Reveal);
        Assert.Equal(RacePhase.Chequered, h.Game.Phase);
        return h;
    }

    /// <summary>Four players finishing NOR 4, VER 3, LEC 1, ANT 0 (Redline, Push, Cruise right; Cruise wrong).</summary>
    private static GameHarness<RedlineGame> FourPlayers(RedlineRules? rules = null) =>
        ToChequered(["NOR", "VER", "LEC", "ANT"], ["redline", "push", "cruise", "cruise"], [true, true, true, false], rules);

    private static GameHarness<RedlineGame> ToAnthem(GameHarness<RedlineGame> h)
    {
        h.Advance(h.Game.Rules.Chequered);
        Assert.Equal(RacePhase.Anthem, h.Game.Phase);
        return h;
    }

    private static GameHarness<RedlineGame> ToChampagne(GameHarness<RedlineGame> h)
    {
        ToAnthem(h).Advance(h.Game.Rules.Anthem);
        Assert.Equal(RacePhase.Champagne, h.Game.Phase);
        return h;
    }

    private static Podium Podium(GameHarness<RedlineGame> h) => RedlineKit.View(h).Podium ?? throw new InvalidOperationException("No podium.");

    private static PodiumStep Step(GameHarness<RedlineGame> h, int player) => Podium(h).Steps.Single(s => s.Player == h.Players[player].Id);

    private static void Shake(GameHarness<RedlineGame> h, int player, double charge, bool fire = false) =>
        RedlineKit.Shake(h, player, charge, fire);

    private static string Snapshot(GameHarness<RedlineGame> h) => JsonSerializer.Serialize(h.Game.HostView(), ArcJson.Options);

    // ---------------------------------------------------------------- Flow

    [Fact]
    public void Chequered_is_followed_by_anthem_then_champagne_then_end()
    {
        var h = FourPlayers();
        Assert.Null(RedlineKit.View(h).Podium);
        Assert.Equal(h.Now + TimeSpan.FromSeconds(10), RedlineKit.View(h).DeadlineUtc);

        h.Advance(TimeSpan.FromSeconds(10));
        var anthem = RedlineKit.View(h);
        Assert.Equal(RacePhase.Anthem, anthem.Phase);
        Assert.Equal(h.Now + TimeSpan.FromSeconds(16), anthem.DeadlineUtc);
        Assert.NotNull(anthem.Podium);
        Assert.Null(anthem.Finish);
        Assert.Null(anthem.Question);
        Assert.Equal(22, anthem.Drivers.Count);
        Assert.Null(h.Results);

        h.Advance(TimeSpan.FromSeconds(16));
        var champagne = RedlineKit.View(h);
        Assert.Equal(RacePhase.Champagne, champagne.Phase);
        Assert.Equal(h.Now + TimeSpan.FromSeconds(16), champagne.DeadlineUtc);
        Assert.NotNull(champagne.Podium);
        Assert.Null(h.Results);

        h.Advance(TimeSpan.FromSeconds(15));
        Assert.Null(h.Results);
        h.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(h.Results);
        Assert.Equal("Player1 (NOR) wins the race!", h.Results.Message);
        Assert.Equal(["Player1 (NOR)", "Player2 (VER)", "Player3 (LEC)", "Player4 (ANT)"], h.Results.Scores.Select(s => s.Name));
    }

    [Fact]
    public void Celebrate_false_ends_after_chequered()
    {
        var h = FourPlayers(OneRound with { Celebrate = false });
        h.Advance(TimeSpan.FromSeconds(10));
        Assert.NotNull(h.Results);
        Assert.Equal(RacePhase.Chequered, h.Game.Phase);
    }

    [Fact]
    public void Ai_only_room_skips_celebration()
    {
        var (h, _) = RedlineKit.Start(players: 0, rules: OneRound);
        RedlineKit.SeatAllAndGoToRound1(h);
        h.Advance(h.Game.Rules.Throttle + h.Game.Rules.Question + h.Game.Rules.Reveal);
        Assert.Equal(RacePhase.Chequered, h.Game.Phase);

        h.Advance(h.Game.Rules.Chequered);
        Assert.NotNull(h.Results);
        Assert.Empty(h.Results.Scores);
        Assert.Equal(RacePhase.Chequered, h.Game.Phase);
    }

    // ---------------------------------------------------------------- Who stands on the podium

    [Fact]
    public void Podium_is_top_three_players_even_behind_ai()
    {
        var h = ToAnthem(ToChequered(["NOR", "VER", "LEC", "ANT"], ["redline", "push", "cruise", "cruise"], [true, true, true, false],
            OneRoundPerfectAi));
        var view = RedlineKit.View(h);
        var steps = Podium(h).Steps;

        Assert.Equal([1, 2, 3], steps.Select(s => s.Step));
        Assert.Equal(new[] { 0, 1, 2 }.Select(i => h.Players[i].Id), steps.Select(s => s.Player));
        Assert.Equal(["Player1", "Player2", "Player3"], steps.Select(s => s.Name));
        Assert.Equal(["NOR", "VER", "LEC"], steps.Select(s => s.Code));
        Assert.Equal(["mclaren", "red-bull", "ferrari"], steps.Select(s => s.TeamId));
        Assert.Equal(new[] { 0, 1, 2 }.Select(i => RedlineKit.Row(h, i).Position), steps.Select(s => s.RacePosition));
        // An AI car finished ahead of the third player and still has no step.
        Assert.Contains(view.Drivers, d => d.Player is null && d.Position < steps[2].RacePosition);
        Assert.All(steps, s => Assert.Equal(0, s.Charge));
        Assert.All(steps, s => Assert.Empty(s.Sprays));
    }

    [Fact]
    public void Fewer_than_three_players_all_get_steps()
    {
        var h = ToAnthem(ToChequered(["HAM", "ALO"], ["cruise", "push"], [true, true]));
        Assert.Equal([(1, "ALO"), (2, "HAM")], Podium(h).Steps.Select(s => (s.Step, s.Code)));
    }

    [Fact]
    public void Tied_players_follow_classification_order()
    {
        foreach (var seed in new[] { 1, 2, 3 })
        {
            // All four players answer right on Cruise: a full tie, shared P1 among the players.
            var h = ToAnthem(ToChequered(["NOR", "VER", "LEC", "ANT"], ["cruise", "cruise", "cruise", "cruise"], [true, true, true, true],
                seed: seed));
            var view = RedlineKit.View(h);
            var classification = view.Drivers.Where(d => d.Player is not null).Select(d => d.Player!.Value).ToList();
            Assert.Equal(classification.Take(3), Podium(h).Steps.Select(s => s.Player));
            Assert.All(Podium(h).Steps, s => Assert.Equal(1, s.RacePosition));
        }
    }

    // ---------------------------------------------------------------- Phones

    [Fact]
    public void Podium_phones_get_shake_others_wait()
    {
        var h = ToAnthem(FourPlayers());
        var position = RedlineKit.Row(h, 3).Position;

        for (var i = 0; i < 3; i++)
        {
            var shake = Assert.IsType<ShakeView>(h.PlayerView(i));
            Assert.Equal(AnthemPrompt, shake.Prompt);
            Assert.False(shake.CanFire);
            Assert.Equal(0, shake.InitialCharge);
            Assert.Equal(RedlineKit.View(h).DeadlineUtc, shake.DeadlineUtc);
        }
        Assert.Equal("#ff8000", Assert.IsType<ShakeView>(h.PlayerView(0)).Color);   // McLaren
        Assert.Equal("#dc0000", Assert.IsType<ShakeView>(h.PlayerView(2)).Color);   // Ferrari
        Assert.Equal($"P{position}. Watch the podium!", Assert.IsType<WaitView>(h.PlayerView(3)).Message);

        h.Advance(h.Game.Rules.Anthem);
        for (var i = 0; i < 3; i++)
        {
            var shake = Assert.IsType<ShakeView>(h.PlayerView(i));
            Assert.Equal(ChampagnePrompt, shake.Prompt);
            Assert.True(shake.CanFire);
        }
        Assert.Equal($"P{position}. Watch the podium!", Assert.IsType<WaitView>(h.PlayerView(3)).Message);
    }

    [Fact]
    public void Shake_view_stable_while_charging()
    {
        var h = ToAnthem(FourPlayers());
        var anthem = h.PlayerView(0);
        foreach (var c in new[] { 0.1, 0.35, 0.8 }) Shake(h, 0, c);
        Assert.Same(anthem, h.PlayerView(0));
        Assert.Equal(0.8, Step(h, 0).Charge);

        h.Advance(h.Game.Rules.Anthem);
        var champagne = h.PlayerView(0);
        Assert.NotSame(anthem, champagne);
        Shake(h, 0, 0.9);
        Shake(h, 0, 0.9, fire: true);
        h.Advance(TimeSpan.FromSeconds(1));
        Shake(h, 0, 0.4);
        Assert.Same(champagne, h.PlayerView(0));
    }

    [Fact]
    public void Champagne_view_carries_initial_charge_and_can_fire()
    {
        var h = ToAnthem(FourPlayers());
        Shake(h, 1, 0.62);
        h.Advance(h.Game.Rules.Anthem);

        var shake = Assert.IsType<ShakeView>(h.PlayerView(1));
        Assert.True(shake.CanFire);
        Assert.Equal(0.6, shake.InitialCharge);                      // quantised to 0.05
        Assert.Equal(0, Assert.IsType<ShakeView>(h.PlayerView(0)).InitialCharge);
    }

    // ---------------------------------------------------------------- Inputs

    [Fact]
    public void Fire_in_anthem_is_ignored()
    {
        var h = ToAnthem(FourPlayers());
        Shake(h, 0, 0.5, fire: true);
        Assert.Empty(Step(h, 0).Sprays);
        Assert.Equal(0.5, Step(h, 0).Charge);                       // the charge still counts
    }

    [Fact]
    public void Fire_records_spray_and_resets_charge()
    {
        var h = ToChampagne(ToAnthem(FourPlayers()));
        Shake(h, 0, 0.8);
        Assert.Equal(0.8, Step(h, 0).Charge);
        Shake(h, 0, 0.8, fire: true);
        Assert.Equal([new Spray(1, 0.8)], Step(h, 0).Sprays);
        Assert.Equal(0, Step(h, 0).Charge);

        // No charge: the weakest spray. Seq is shared by the whole podium.
        Shake(h, 2, 0, fire: true);
        Assert.Equal([new Spray(2, 0.15)], Step(h, 2).Sprays);
        h.Advance(TimeSpan.FromMilliseconds(400));
        Shake(h, 0, 0.33, fire: true);
        Assert.Equal([new Spray(1, 0.8), new Spray(3, 0.33)], Step(h, 0).Sprays);

        // A non-podium player cannot spray.
        Shake(h, 3, 1, fire: true);
        Assert.Equal(3, Podium(h).Steps.Sum(s => s.Sprays.Count));
    }

    [Fact]
    public void Spray_throttled_to_400ms()
    {
        var h = ToChampagne(ToAnthem(FourPlayers()));
        Shake(h, 0, 0.5, fire: true);
        h.Advance(TimeSpan.FromMilliseconds(399));
        Shake(h, 0, 0.7, fire: true);                                // too soon: ignored, but the charge is kept
        Assert.Single(Step(h, 0).Sprays);
        Assert.Equal(0.7, Step(h, 0).Charge);

        Shake(h, 1, 0.6, fire: true);                                // another player is not throttled by player 0
        Assert.Single(Step(h, 1).Sprays);

        h.Advance(TimeSpan.FromMilliseconds(1));
        Shake(h, 0, 0.7, fire: true);
        Assert.Equal([1, 3], Step(h, 0).Sprays.Select(s => s.Seq));
    }

    [Fact]
    public void Charge_updates_are_quantised()
    {
        var h = ToAnthem(FourPlayers());
        var snapshots = new HashSet<string>(StringComparer.Ordinal) { Snapshot(h) };
        var phone = h.PlayerView(0);

        // A buggy phone floods 40 inputs a second for 4 s, the charge creeping up by 0.006 each time.
        for (var i = 1; i <= 160; i++)
        {
            Shake(h, 0, i * 0.006);
            h.Advance(TimeSpan.FromMilliseconds(25));
            snapshots.Add(Snapshot(h));
            Assert.Equal(0, Math.Round(Step(h, 0).Charge * 20) - Step(h, 0).Charge * 20, 9);
        }

        Assert.InRange(snapshots.Count, 2, 21);                      // 0.00 … 0.95: one view per 0.05 step at most
        Assert.Equal(0.95, Step(h, 0).Charge);                       // 0.96 rounds to 0.95
        Assert.Same(phone, h.PlayerView(0));

        Shake(h, 0, 7);
        Assert.Equal(1, Step(h, 0).Charge);                          // clamped
        Shake(h, 0, -3);
        Assert.Equal(0, Step(h, 0).Charge);
        Shake(h, 0, 0.124);
        Assert.Equal(0.1, Step(h, 0).Charge);
        Shake(h, 0, 0.126);
        Assert.Equal(0.15, Step(h, 0).Charge);
    }

    [Fact]
    public void Malformed_shake_input_ignored()
    {
        var h = ToAnthem(FourPlayers());
        Shake(h, 0, 0.5);
        var before = Snapshot(h);

        h.Input(0, new { pressed = "push" });
        h.Input(0, new { selected = new[] { "a" } });
        h.Input(0, new { fire = true });
        h.Input(0, new { charge = "lots" });
        h.Input(0, new { charge = "NaN" });
        h.Input(0, "shake");
        h.Input(0, new[] { 0.9 });
        Assert.Equal(before, Snapshot(h));

        h.Advance(h.Game.Rules.Anthem);
        h.Input(0, new { charge = (double?)null, fire = true });
        h.Input(0, new { fire = true });
        Assert.Empty(Step(h, 0).Sprays);
        Assert.Equal(RacePhase.Champagne, h.Game.Phase);
    }

    [Fact]
    public void Disconnected_podium_player_keeps_step()
    {
        var h = ToAnthem(FourPlayers());
        Shake(h, 1, 0.4);
        h.SetConnected(1, false);

        Assert.Equal(new[] { 0, 1, 2 }.Select(i => h.Players[i].Id), Podium(h).Steps.Select(s => s.Player));
        Assert.False(RedlineKit.Row(h, 1).Connected);
        Assert.Equal(0.4, Step(h, 1).Charge);

        h.Advance(h.Game.Rules.Anthem);
        Assert.Equal(RacePhase.Champagne, h.Game.Phase);
        Assert.Equal(2, Step(h, 1).Step);
        Shake(h, 0, 0.5, fire: true);

        h.SetConnected(1, true);
        var shake = Assert.IsType<ShakeView>(h.PlayerView(1));
        Assert.True(shake.CanFire);
        Assert.Equal(0.4, shake.InitialCharge);

        h.Advance(h.Game.Rules.Champagne);
        Assert.NotNull(h.Results);
    }

    // ---------------------------------------------------------------- Anthem

    [Fact]
    public void Anthem_follows_best_player_driver_nationality()
    {
        // VER (NL) is the best player.
        var h = ToAnthem(ToChequered(["NOR", "VER"], ["cruise", "redline"], [true, true]));
        Assert.Equal(("Netherlands", "nl"), (Podium(h).AnthemCountry, Podium(h).AnthemId));

        // An AI car wins; the anthem is still the best player's: LEC (MC).
        var ai = ToAnthem(ToChequered(["LEC", "HAM"], ["push", "cruise"], [true, false], OneRoundPerfectAi));
        Assert.Null(RedlineKit.View(ai).Drivers[0].Player);
        Assert.Equal(("Monaco", "mc"), (Podium(ai).AnthemCountry, Podium(ai).AnthemId));
    }

    [Fact]
    public void Fallback_nationality_has_null_anthem_id()
    {
        var mexico = ToAnthem(ToChequered(["PER", "NOR"], ["redline", "cruise"], [true, true]));
        Assert.Equal("Mexico", Podium(mexico).AnthemCountry);
        Assert.Null(Podium(mexico).AnthemId);

        var thailand = ToAnthem(ToChequered(["ALB", "NOR"], ["redline", "cruise"], [true, true]));
        Assert.Equal("Thailand", Podium(thailand).AnthemCountry);
        Assert.Null(Podium(thailand).AnthemId);
    }

    [Fact]
    public void Countries_table_names_every_grid_nationality_with_its_anthem_id()
    {
        Assert.Equal(new Country("GB", "Great Britain", "gb"), Countries.Find("GB"));
        Assert.Equal(new Country("MX", "Mexico", null), Countries.Find("MX"));
        Assert.Equal(new Country("TH", "Thailand", null), Countries.Find("TH"));
        Assert.Equal(new Country("BR", "Brazil", null), Countries.Find("BR"));   // Lei 5.700/1971 art. 34
        Assert.Null(Countries.Find("XX"));
        Assert.All(Countries.All.Where(c => c.AnthemId is not null), c => Assert.Equal(c.Code.ToLowerInvariant(), c.AnthemId));
        Assert.Equal(15, Countries.All.Count);
    }

    [Fact]
    public void Same_seed_gives_identical_podium_views()
    {
        string Run()
        {
            var h = ToChampagne(ToAnthem(FourPlayers()));
            Shake(h, 0, 0.7, fire: true);
            h.Advance(TimeSpan.FromSeconds(1));
            Shake(h, 2, 0.2, fire: true);
            return Snapshot(h);
        }
        Assert.Equal(Run(), Run());
    }
}
