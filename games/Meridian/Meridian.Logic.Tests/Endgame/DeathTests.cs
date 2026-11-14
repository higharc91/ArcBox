namespace Meridian.Logic.Tests.Endgame;

public sealed class DeathTests
{
    private static Play Endgame()
    {
        var game = Play.Game();
        game.State = EndgameStateTests.WithBoard(game);
        return game;
    }

    [Fact]
    public void Damage_below_step_one_kills_and_drops_the_items()
    {
        var game = Endgame().Steps(Play.S3, new TraitSteps(2, 2, 4, 4)).Give(Play.S3, "torch");
        game.State = game.State with { Salvage = game.State.Salvage with { Draw = game.State.Salvage.Draw.Remove(new CardId("torch")) } };
        game.RigFight(3, 3, (a, d) => a - d >= 2).Do(new Attack(Play.S1, "s3"));
        game.Do(new Allocate(Play.S3, 2));
        var dead = game.Crew(Play.S3);
        Assert.False(dead.Alive);
        Assert.Equal(0, dead.Steps.Brawn);
        Assert.Empty(dead.Items);
        Assert.Contains(game.State.Board!.Tokens, t => t.Card == new CardId("torch") && t.At == Play.C(1));
        Assert.Contains(new Died(Play.S3), game.Events);
        Assert.Empty(Invariants.Check(game.Pack, game.State));
    }

    [Fact]
    public void A_loss_from_a_card_kills_after_the_breach()
    {
        var game = Endgame().Steps(Play.S1, new TraitSteps(1, 1, 4, 4)).Check(Play.S1, "jammed_panel");   // the loss spreads, so both must be low
        game.Rig(game.Dice, t => t <= 1).Do(new Roll(Play.S1));
        Assert.False(game.Crew(Play.S1).Alive);
    }

    [Fact]
    public void A_death_mid_wait_resolves_what_the_seat_owed()
    {
        // M3 review focus 2.
        var game = Endgame().Give(Play.S1, "pry_bar");
        game.State = game.State with { Waiting = new TradePending(Play.S3, Play.S1, new TradeOffer([new CardId("pry_bar")], [])) };
        game.Resolve(r => r.Kill(Play.S3));
        Assert.Null(game.State.Waiting);
        Assert.Contains(new TradeAnswered(Play.S1, Play.S3, false), game.Events);
    }

    [Fact]
    public void An_echo_lends_a_die_for_the_next_check_only()
    {
        var game = Endgame().Resolve(r => r.Kill(Play.S1));
        Assert.Equal([new Lend(Play.S1, Play.S2), new Lend(Play.S1, Play.S3), new EndTurn(Play.S1), new Logic.Timeout(Play.S1)], game.Legal(Play.S1));
        game.Do(new Lend(Play.S1, Play.S3));
        game.Do(new EndTurn(Play.S1));
        game.Do(new EndTurn(Play.S2));
        var plain = Rules.Value(game.Pack, game.Crew(Play.S3), Trait.Nerve);
        game.Check(Play.S3, "power_flicker");
        Assert.Equal(plain + 1, game.Dice);
        Assert.Equal(0, game.Crew(Play.S3).Lent);
    }

    [Fact]
    public void A_dead_controllers_monsters_still_act()
    {
        // M3 review focus 3.
        var game = Endgame().Resolve(r => r.Kill(Play.S2));
        game.Do(new EndTurn(Play.S1));
        Assert.Contains(game.Legal(Play.S2), c => c is MonsterMove { Monster: "brood1" });
        Assert.DoesNotContain(game.Legal(Play.S2), c => c is MoveTo or Open);
    }

    [Fact]
    public void Escaped_seats_leave_the_turn_order()
    {
        var game = Endgame();
        game.State = game.State.WithCrew(game.Crew(Play.S2) with { Escaped = true });
        game.Do(new EndTurn(Play.S1));
        Assert.Equal(Play.S3, game.State.ActiveSeat);
        game.Do(new EndTurn(Play.S3));
        Assert.Equal(Play.S1, game.State.ActiveSeat);
        Assert.Equal(2, game.State.Round);
    }
}
