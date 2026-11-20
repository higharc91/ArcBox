namespace Meridian.Logic.Tests.Scenarios;

/// <summary>
/// The owner's decision on the M7 balance report: a side that only held out to the round limit gets an active way to win.
/// </summary>
public sealed class SecondLossPathTests
{
    private static Play Begin(string id, int players = 4) => Play.From(ScenarioRun.Pack(id), ScenarioRun.Begun(id, players));

    /// <summary>Kills every crew member but <paramref name="keep"/> of those <paramref name="crew"/> picks, then lets a step run.</summary>
    private static void KillAllBut(Play game, Func<GameState, IEnumerable<SeatId>> crew, int keep)
    {
        foreach (var seat in crew(game.State).Skip(keep).ToList())
        {
            game.Resolve(r => r.Kill(seat));
        }
        game.Do(new EndTurn(game.State.ActiveSeat));
    }

    [Fact]
    public void The_mimic_wins_when_one_crew_member_is_left()
    {
        var game = Begin(OneOfUsIsnt.Id);
        var mimic = game.State.Board!.Roles.Single(r => r.Role == OneOfUsIsnt.Mimic).Seat;
        KillAllBut(game, s => s.Board!.Roles.Where(r => r.Role == OneOfUsIsnt.Crew).Select(r => r.Seat), keep: 1);
        Assert.Equal([mimic], game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void The_captain_wins_when_one_crew_member_is_left()
    {
        var game = Begin(CaptainOnDeck.Id);
        var captain = game.State.Board!.Roles.Single(r => r.Role == CaptainOnDeck.Captain).Seat;
        KillAllBut(game, s => s.Board!.Roles.Where(r => r.Role == CaptainOnDeck.Crew).Select(r => r.Seat), keep: 1);
        Assert.Equal([captain], game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void A_vent_kills_someone_already_hurt_who_fails_the_save_and_only_hurts_the_rest()
    {
        var game = Begin(TillerSaysNo.Id);
        var pack = game.Pack;
        var (hurt, fresh) = (game.State.Crew[0], game.State.Crew[1]);
        game.Put(fresh.Seat, hurt.At);
        var wounded = game.State.CrewOf(hurt.Seat) with { Steps = game.State.CrewOf(hurt.Seat).Steps.With(Trait.Brawn, game.State.CrewOf(hurt.Seat).Steps.Brawn - 1) };
        game.State = game.State.WithCrew(wounded);
        Assert.True(Primitives.Hurt(pack, game.Crew(hurt.Seat)));
        Assert.False(Primitives.Hurt(pack, game.Crew(fresh.Seat)));

        var vented = Primitives.Vent(pack, game.State, hurt.At, save: 99, [], lethal: true);
        Assert.Equal(0, vented.CrewOf(hurt.Seat).Steps.Brawn);   // the rules deal the death
        Assert.Equal(Total(game.Crew(fresh.Seat)) - 1, Total(vented.CrewOf(fresh.Seat)));
        // Without lethal (every other venting scenario), the hurt only lose a step.
        Assert.Equal(Total(game.Crew(hurt.Seat)) - 1, Total(Primitives.Vent(pack, game.State, hurt.At, save: 99, []).CrewOf(hurt.Seat)));
    }

    [Fact]
    public void The_crew_win_once_the_saboteur_cant_reach_the_target()
    {
        var game = Begin(ScuttleCharge.Id);
        var needed = ScuttleCharge.Needed(game.Pack, game.State);
        // Two charges down: armed none, and a kit one short of the target.
        game.State = game.State.WithBoard(game.State.Board!.WithCounter("kit", needed - 1));
        game.Do(new EndTurn(game.State.ActiveSeat));
        Assert.Equal(Phase.Over, game.State.Phase);
        Assert.DoesNotContain(game.State.Board!.Roles.Single(r => r.Role == ScuttleCharge.Saboteur).Seat, game.State.Board.Outcome!.Winners);
    }

    [Fact]
    public void While_the_target_is_reachable_the_game_goes_on()
    {
        var game = Begin(ScuttleCharge.Id);
        game.Do(new EndTurn(game.State.ActiveSeat));
        Assert.Equal(Phase.Endgame, game.State.Phase);
    }

    private static int Total(CrewState c) => c.Steps.Brawn + c.Steps.Agility;
}
