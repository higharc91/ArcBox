using ArcBox.Sdk;
using ArcBox.Sdk.Testing;
using Redline.Logic;

namespace Redline.Logic.Tests;

/// <summary>Shared helpers for driving a <see cref="RedlineGame"/> through a <see cref="GameHarness{TGame}"/>.</summary>
public static class RedlineKit
{
    /// <param name="reading">
    /// Sets <see cref="RedlineRules.ReadQuestions"/>. Off by default, so the round tests written before the Reading phase
    /// keep going straight from Throttle to Question; the Reading tests turn it on.
    /// </param>
    /// <param name="qualify">
    /// Sets <see cref="RedlineRules.Qualify"/>. Off by default, so the tests written before qualifying keep going straight
    /// from Driver Select to the Grid with the seeded shuffle; the qualifying tests turn it on.
    /// </param>
    public static (GameHarness<RedlineGame> H, FakeQuestionSource Q) Start(
        int players = 4, int seed = 1, RedlineRules? rules = null, bool reading = false, FakeQuestionSource? questions = null,
        bool qualify = false)
    {
        var q = questions ?? new FakeQuestionSource();
        var h = new GameHarness<RedlineGame>(
            new RedlineGame(q, (rules ?? new RedlineRules()) with { ReadQuestions = reading, Qualify = qualify }), players, seed);
        h.Start();
        return (h, q);
    }

    public static void Claim(GameHarness<RedlineGame> h, int player, string code) =>
        h.Input(player, new { selected = new[] { code } });

    /// <summary>
    /// Players 0..n-1 claim <paramref name="codes"/> in order; then time runs through qualifying (if the rules have it on;
    /// nobody answers) and the Grid phase into round 1.
    /// </summary>
    public static void SeatAllAndGoToRound1(GameHarness<RedlineGame> h, params string[] codes)
    {
        for (var i = 0; i < codes.Length; i++) Claim(h, i, codes[i]);
        if (View(h).Phase == RacePhase.DriverSelect) h.Advance(h.Game.Rules.DriverSelect);   // not everyone claimed
        while (View(h) is { Phase: RacePhase.QualifyingReading or RacePhase.QualifyingQuestion or RacePhase.QualifyingResult } v)
            h.Advance(v.DeadlineUtc!.Value - h.Now);
        h.Advance(h.Game.Rules.Grid);
    }

    /// <summary>
    /// Players 0..n-1 claim <paramref name="codes"/> in order (time runs out Driver Select if not everyone claimed), so the
    /// game is in lap 1 of qualifying: QualifyingReading, or QualifyingQuestion with ReadQuestions off.
    /// </summary>
    public static void SeatAllAndGoToQualifying(GameHarness<RedlineGame> h, params string[] codes)
    {
        for (var i = 0; i < codes.Length; i++) Claim(h, i, codes[i]);
        if (View(h).Phase == RacePhase.DriverSelect) h.Advance(h.Game.Rules.DriverSelect);
    }

    /// <summary>
    /// QualifyingQuestion: picks the right option ("right…") or a wrong one ("w1") and sends it with the phone's
    /// <paramref name="elapsedMs"/> (null: an older phone that does not measure).
    /// </summary>
    public static void QualiAnswer(GameHarness<RedlineGame> h, int player, bool right, double? elapsedMs)
    {
        var view = h.PlayerView(player) as ChoiceView
            ?? throw new InvalidOperationException($"Player {player} is not looking at a question.");
        var option = view.Options.First(o => right ? o.Label.StartsWith("right", StringComparison.Ordinal) : o.Label == "w1");
        h.Input(player, new { selected = new[] { option.Id }, elapsedMs });
    }

    /// <summary>Throttle phase: presses <paramref name="id"/> (cruise / push / redline).</summary>
    public static void Throttle(GameHarness<RedlineGame> h, int player, string id) =>
        h.Input(player, new { pressed = id });

    /// <summary>Question phase: picks the option whose Label starts with "right" (FakeQuestionSource's correct answer).</summary>
    public static void AnswerRight(GameHarness<RedlineGame> h, int player) =>
        Answer(h, player, o => o.Label.StartsWith("right", StringComparison.Ordinal));

    /// <summary>Question phase: picks the option labelled "w1" (always wrong).</summary>
    public static void AnswerWrong(GameHarness<RedlineGame> h, int player) =>
        Answer(h, player, o => o.Label == "w1");

    private static void Answer(GameHarness<RedlineGame> h, int player, Func<Option, bool> pick)
    {
        var view = h.PlayerView(player) as ChoiceView
            ?? throw new InvalidOperationException($"Player {player} is not looking at a question.");
        h.Input(player, new { selected = new[] { view.Options.First(pick).Id } });
    }

    /// <summary>Anthem and Champagne: sends a <see cref="ShakeInput"/> as the phone does.</summary>
    public static void Shake(GameHarness<RedlineGame> h, int player, double charge, bool fire = false) =>
        h.Input(player, new { charge, fire });

    /// <summary>From the Chequered flag: runs the clock to the end of the game, through the podium ceremony when there is one.</summary>
    public static void RunToEnd(GameHarness<RedlineGame> h) =>
        h.Advance(h.Game.Rules.Chequered + h.Game.Rules.Anthem + h.Game.Rules.Champagne);

    public static RedlineHostView View(GameHarness<RedlineGame> h) => h.HostView<RedlineHostView>();

    /// <summary>The row driven by <c>h.Players[player]</c>.</summary>
    public static DriverRow Row(GameHarness<RedlineGame> h, int player) =>
        View(h).Drivers.Single(d => d.Player == h.Players[player].Id);
}
