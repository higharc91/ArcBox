using ArcBox.Sdk.Testing;
using Redline.Logic;

namespace Redline.Logic.Tests;

/// <summary>Shared helpers for driving a <see cref="RedlineGame"/> through a <see cref="GameHarness{TGame}"/>.</summary>
public static class RedlineKit
{
    public static (GameHarness<RedlineGame> H, FakeQuestionSource Q) Start(int players = 4, int seed = 1, RedlineRules? rules = null)
    {
        var q = new FakeQuestionSource();
        var h = new GameHarness<RedlineGame>(new RedlineGame(q, rules), players, seed);
        h.Start();
        return (h, q);
    }

    public static void Claim(GameHarness<RedlineGame> h, int player, string code) =>
        h.Input(player, new { selected = new[] { code } });

    /// <summary>Players 0..n-1 claim <paramref name="codes"/> in order; then time runs through the Grid phase into round 1.</summary>
    public static void SeatAllAndGoToRound1(GameHarness<RedlineGame> h, params string[] codes)
    {
        for (var i = 0; i < codes.Length; i++) Claim(h, i, codes[i]);
        if (View(h).Phase == RacePhase.DriverSelect) h.Advance(h.Game.Rules.DriverSelect);   // not everyone claimed
        h.Advance(h.Game.Rules.Grid);
    }

    public static RedlineHostView View(GameHarness<RedlineGame> h) => h.HostView<RedlineHostView>();

    /// <summary>The row driven by <c>h.Players[player]</c>.</summary>
    public static DriverRow Row(GameHarness<RedlineGame> h, int player) =>
        View(h).Drivers.Single(d => d.Player == h.Players[player].Id);
}
