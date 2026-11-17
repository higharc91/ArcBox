using System.Text;
using System.Text.Json;

namespace Meridian.Game.Tests;

/// <summary>Meridian's snapshots (M6 plan, Task 3): when they're taken, what they hold, and that a restore plays on identically.</summary>
public sealed class SnapshotTests
{
    private static string Json(object value) => JsonSerializer.Serialize(value, ArcJson.Options);

    /// <summary>Plays to the first endgame turn start after <paramref name="turns"/> endgame turns.</summary>
    private static void ToEndgameTurn(Harness game, int turns)
    {
        var seen = 0;
        var active = "";
        game.PlayUntil(() =>
        {
            if (game.Session is not { Phase: SessionPhase.Endgame, Queue.IsEmpty: true } || game.State.Waiting is not null)
            {
                return false;
            }
            if (game.State.ActiveSeat.Value != active)
            {
                active = game.State.ActiveSeat.Value;
                seen++;
            }
            return seen > turns;
        });
        Assert.Equal(SessionPhase.Endgame, game.Session.Phase);
    }

    [Theory]
    [InlineData(ThePassenger.Id)]
    [InlineData(OneOfUsIsnt.Id)]
    [InlineData(TillerSaysNo.Id)]
    [InlineData(DecayingOrbit.Id)]
    [InlineData(SalvageRights.Id)]
    public void A_restored_game_plays_on_exactly_as_the_original(string scenario)
    {
        var original = new Harness(players: 4, seed: 3, scenario).Board();
        ToEndgameTurn(original, turns: 2);
        var snapshot = original.Game.TakeSnapshot()!.Value;

        var restored = Harness.Restored(snapshot, players: 4, seed: 3, scenario);
        Assert.Equal(Json(original.State), Json(restored.State));
        for (var p = 0; p < 4; p++)
        {
            restored.H.SetConnected(p, true);   // everyone back, as before the restart
        }

        // The same bots from here on: the same game, to the same end.
        original.PlayBots(seed: 11);
        restored.PlayBots(seed: 11);
        Assert.Equal(Json(original.State), Json(restored.State));
        Assert.Equal(original.Session.Ending, restored.Session.Ending);
        Assert.Equal(SessionPhase.Over, restored.Session.Phase);
    }

    [Fact]
    public void A_restored_game_comes_back_with_every_seat_offline_and_none_auto()
    {
        var original = new Harness(players: 4, seed: 2, ThePassenger.Id).Board();
        ToEndgameTurn(original, turns: 1);
        original.H.SetConnected(1, false);
        original.Advance(70);   // seat 1's pause runs out: a bot plays it
        var restored = Harness.Restored(original.Game.TakeSnapshot() ?? Snapshot(original), players: 4, seed: 2, ThePassenger.Id);
        Assert.All(restored.Session.Seats, s => Assert.False(s.Connected || s.Auto || s.PauseUsed));
    }

    /// <summary>A snapshot of the session as it is, whatever the moment (TakeSnapshot returns null within a turn).</summary>
    private static JsonElement Snapshot(Harness game)
        => JsonSerializer.SerializeToElement(new { version = MeridianGame.SnapshotVersion, session = game.Session }, ArcJson.Options);

    [Fact]
    public void One_snapshot_per_turn_and_phase_and_none_between()
    {
        var game = new Harness(players: 4, seed: 4, TillerSaysNo.Id);
        var moments = new HashSet<string>();
        var taken = 0;
        void Look()
        {
            if (game.Game.TakeSnapshot() is not null)
            {
                taken++;
            }
            if (game.Session.Phase != SessionPhase.Over)
            {
                moments.Add($"{game.Session.Phase}/{game.Session.Game?.Round}/{game.Session.Game?.ActiveSeat.Value}");
            }
        }
        Look();
        game.Board();
        Look();
        game.PlayUntil(() =>
        {
            Look();
            return false;
        });
        Assert.True(taken > 10);
        // Each moment is saved once. A moment can come back (a phase revisits a seat and round), and is saved again then.
        Assert.InRange(taken, moments.Count, moments.Count * 2);
    }

    [Fact]
    public void A_snapshot_stays_small_with_a_full_ship_and_six_players()
    {
        var game = new Harness(players: 6, seed: 2, ThePassenger.Id).Board();
        game.PlayUntil(() => game.Session.Phase == SessionPhase.Endgame);
        var state = game.State;
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
                var rotation = Placement.ValidRotations(tile, hatch.Side.Opposite())[0];
                state = state with { Ship = Placement.Place(state.Ship, tile, from.At.Step(hatch.Side), rotation, game.Pack.Rules.DeckRadius).Ship };
                break;
            }
        }
        game.Game.Load(game.Session with { Game = state with { TilePile = [] }, Log = Enumerable.Repeat("A long line of the log for size.", 8).ToEqArray() });
        var bytes = Encoding.UTF8.GetByteCount(Snapshot(game).GetRawText());
        Assert.True(state.Ship.Compartments.Count >= 45);
        Assert.True(bytes < 64 * 1024, $"{bytes} bytes");
    }

    [Theory]
    [InlineData("""{"version":2,"session":{}}""")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"from":"an older build"}""")]
    public void A_snapshot_of_another_version_or_shape_is_refused(string json)
    {
        var game = new MeridianGame(new Lazy<RulesPack>(() => BasePack.Pack));
        Assert.ThrowsAny<JsonException>(() => game.Restore(JsonDocument.Parse(json).RootElement));
    }

    [Fact]
    public void Meridian_says_it_resumes_after_a_restart()
        => Assert.True(new MeridianGame(new Lazy<RulesPack>(() => BasePack.Pack)).Manifest.ResumesAfterRestart);
}
