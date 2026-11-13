using System.Text;
using System.Text.Json;
using ArcBox.Sdk.Protocol;

namespace Meridian.Game.Tests;

public sealed class HostViewTests
{
    private static JsonElement Json(object view) => JsonSerializer.SerializeToElement(view, ArcJson.Options);
    private static int Bytes(JsonElement e) => Encoding.UTF8.GetByteCount(e.GetRawText());

    [Fact]
    public void HostView_is_under_8_kb_with_every_compartment_placed()
    {
        var game = new Harness(players: 6).Board();
        var state = game.State;
        var rng = Rng.Seeded(4);
        // Place every tile left in the pile wherever the ship can take it.
        foreach (var tileId in state.TilePile)
        {
            var tile = game.Pack.Tile(tileId);
            foreach (var hatch in state.Ship.Frontier(game.Pack.Rules.DeckRadius))
            {
                var from = state.Ship.Get(hatch.Compartment);
                if (!tile.Decks.Contains(from.At.Deck))
                {
                    continue;
                }
                var cell = from.At.Step(hatch.Side);
                var rotation = Placement.ValidRotations(tile, hatch.Side.Opposite())[0];
                state = state with { Ship = Placement.Place(state.Ship, tile, cell, rotation, game.Pack.Rules.DeckRadius).Ship };
                break;
            }
        }
        state = state with { TilePile = [] };
        Assert.True(state.Ship.Compartments.Count >= 45);
        game.Game.Load(game.Session with { Game = state, Log = Enumerable.Repeat("Dr. Anselm Varga found the Chief Engineer's Office.", 8).ToEqArray() });
        var size = Bytes(Json(game.Game.HostView()));
        Assert.True(size < 8 * 1024, $"{size} bytes");
    }

    [Fact]
    public void An_explore_step_patches_in_under_one_packet()
    {
        var game = new Harness(players: 6, seed: 2).Board();
        var previous = Json(game.Game.HostView());
        var worst = 0;
        var rng = Rng.Seeded(8, 99);
        for (var i = 0; i < 3000 && game.Session.Phase == SessionPhase.Explore; i++)
        {
            if (!game.Session.Queue.IsEmpty)
            {
                game.Presented(game.Session.Queue[0].Seq);
            }
            else
            {
                var seat = BotRunner.Actor(game.State);
                game.Do(ExploreBot.Instance.Choose(game.Pack, game.State, seat, Rules.Legal(game.Pack, game.State, seat), ref rng));
            }
            var next = Json(game.Game.HostView());
            if (HostViewPatch.Diff(previous, next) is { } patch)
            {
                worst = Math.Max(worst, Encoding.UTF8.GetByteCount(patch.ToJsonString()));
            }
            previous = next;
        }
        Assert.True(worst < 1420, $"worst patch {worst} bytes");
    }

    [Fact]
    public void Arrays_of_objects_are_keyed_by_id()
    {
        var game = new Harness().Board();
        var view = (MeridianHostView)game.Game.HostView();
        Assert.Equal(view.Compartments.Count, view.Compartments.Select(c => c.Id).Distinct().Count());
        Assert.Equal(3, view.Seats.Select(s => s.Id).Distinct().Count());
        Assert.Equal(3, view.Crew.Select(c => c.Id).Distinct().Count());
    }
}
