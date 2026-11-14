using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;

namespace Meridian.Game.Tests;

/// <summary>
/// The M3 plan's review focus 1 (spec success criterion 2): no viewer ever receives a secret outside its audience. Whole
/// bot games in every scenario at every player count; after every step, the TV's view, every phone's and every bot's
/// input are serialized and searched for what that viewer may not know.
/// </summary>
public sealed class SecrecyTests
{
    public static TheoryData<string, int> Games()
    {
        var data = new TheoryData<string, int>();
        foreach (var scenario in ScenarioRegistry.Default.All.Select(s => s.Info.Id))
        {
            for (var players = 3; players <= 6; players++)
            {
                data.Add(scenario, players);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void No_view_holds_a_secret_outside_its_audience(string scenario, int players)
    {
        for (var seed = 1; seed <= 2; seed++)
        {
            var game = new Harness(players, seed, scenario).Board();
            var checkedSteps = 0;
            game.PlayUntil(() =>
            {
                checkedSteps += Check(game) ? 1 : 0;
                return false;
            }, steps: 6000, seed: (ulong)seed);
            Assert.True(checkedSteps > 0, $"{scenario} at {players} never reached the endgame");
        }
    }

    /// <summary>Checks every viewer; returns whether there was a scenario to keep secret.</summary>
    private static bool Check(Harness game)
    {
        if (game.Session.Game is not { Board: { } board } real || real.Phase == Phase.Over)
        {
            return false;   // nothing secret yet, or the Debrief, which reveals everything
        }
        var tv = Serialize(game.Game.HostView());
        AssertNone(Leaks(game, real, null, tv), "the TV", tv);
        for (var p = 0; p < game.H.Players.Count; p++)
        {
            var seat = game.Seat(p);
            var phone = Serialize(game.H.PlayerView(p));
            var bot = JsonSerializer.Serialize(Visibility.For(real, seat), ArcJson.Options);
            AssertNone(Leaks(game, real, seat, phone), $"phone {seat}", phone);
            AssertNone(Leaks(game, real, seat, bot), $"bot {seat}", bot);
            if (Visibility.For(real, seat).Waiting is VotePending vote)
            {
                Assert.All(vote.Cast, b => Assert.Equal(seat, b.Voter));   // nobody sees another's ballot
            }
        }
        return true;
    }

    private static void AssertNone(IReadOnlyList<string> leaks, string viewer, string json)
        => Assert.True(leaks.Count == 0, $"{viewer} saw {string.Join(", ", leaks)} in {json[..Math.Min(json.Length, 600)]}");

    private static string Serialize(object view) => JsonSerializer.Serialize(view, ArcJson.Options);

    /// <summary>
    /// Everything about the board the viewer is outside the audience of, minus anything that also reaches them through an
    /// audience they are in (a crew member's own role is "crew", so another crew member's isn't a secret from them).
    /// </summary>
    private static IReadOnlyList<string> Leaks(Harness game, GameState real, SeatId? viewer, string json)
    {
        var board = real.Board!;
        var def = game.Pack.ScenarioDef(board.Scenario);
        var secret = new HashSet<string>();
        var seen = new HashSet<string>();
        void Sort(bool visible, IEnumerable<string> patterns)
        {
            foreach (var p in patterns)
            {
                (visible ? seen : secret).Add(p);
            }
        }
        foreach (var r in board.Roles)
        {
            Sort(r.Audience.Includes(real, viewer), [$"\"role\":\"{r.Role}\"", $"\"name\":\"{def.Role(r.Role).Name}\""]);
        }
        foreach (var t in board.Tokens)
        {
            Sort(t.Audience.Includes(real, viewer), [$"\"id\":\"{t.Id}\""]);
        }
        foreach (var c in board.Counters)
        {
            Sort(c.Audience.Includes(real, viewer), [$"\"id\":\"{c.Id}\""]);
        }
        foreach (var o in board.Objectives)
        {
            Sort(o.Audience.Includes(real, viewer), [JsonSerializer.Serialize(o.Text, ArcJson.Options)]);
        }
        return secret.Except(seen).Where(json.Contains).ToList();
    }
}
