using System.Text.Json;
using ArcBox.Sdk.Protocol;

namespace Meridian.Game.Tests;

/// <summary>
/// M7 review focus 2: a hidden act traced by timing. On the turn a charge is planted, nothing public changes: not the TV's
/// view, a byte of it, nor any other seat's phone. Only the planter's own view may move.
/// </summary>
public sealed class ScuttleChargeTimingTests
{
    [Fact]
    public void Planting_a_charge_changes_no_view_but_the_planters()
    {
        var game = new Harness(players: 4, seed: 2, ScuttleCharge.Id).Board();
        game.PlayUntil(() => game.Session is { Phase: SessionPhase.Endgame, Queue.IsEmpty: true } && game.State.Waiting is null
                             && game.State.Board!.Roles.Single(r => r.Role == ScuttleCharge.Saboteur).Seat == game.State.ActiveSeat);
        var saboteur = game.State.ActiveSeat;
        var empty = game.State.Ship.Compartments.First(c => game.State.Crew.All(x => x.At != c.Id)).Id;
        game.Game.Load(game.Session with { Game = game.State.WithCrew(game.State.CrewOf(saboteur) with { At = empty }) });

        string Host() => JsonSerializer.Serialize(game.Game.HostView(), ArcJson.Options);
        string Phone(int p) => JsonSerializer.Serialize(game.H.PlayerView(p), ArcJson.Options);
        var others = Enumerable.Range(0, 4).Where(p => game.Seat(p) != saboteur).ToList();
        var (host, phones) = (Host(), others.Select(Phone).ToList());

        game.Send(game.Index(saboteur), "act", new { action = "plant" });
        Assert.Single(game.State.Board!.Tokens, t => t.Kind == ScuttleCharge.Charge);   // it really was planted
        Assert.Equal(host, Host());
        Assert.Equal(phones, others.Select(Phone).ToList());
    }
}
