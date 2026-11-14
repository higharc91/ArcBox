namespace Meridian.Logic.Tests.Endgame;

// The test board: s1 (Ivo: Brawn 3) and s3 (Dex: Brawn 3) in the airlock c1 with s2's brood (Brawn 4); s1 to act.
public sealed class CombatTests
{
    private static Play Endgame()
    {
        var game = Play.Game(StubScenario.Pack((new StubScenario("stub"), 10)));
        game.State = EndgameStateTests.WithBoard(game);
        return game;
    }

    [Fact]
    public void Attacks_are_offered_against_crew_and_other_peoples_monsters_alongside()
    {
        var legal = Endgame().Legal(Play.S1);
        Assert.Contains(new Attack(Play.S1, "s3"), legal);
        Assert.Contains(new Attack(Play.S1, "brood1"), legal);
        Assert.DoesNotContain(new Attack(Play.S1, "s1"), legal);
    }

    [Fact]
    public void A_winning_attacker_deals_the_difference_and_the_defender_splits_it()
    {
        var game = Endgame().RigFight(3, 3, (a, d) => a - d == 2);
        game.Do(new Attack(Play.S1, "s3"));
        var allocate = Assert.IsType<AllocatePending>(game.State.Waiting);
        Assert.Equal((Play.S3, 2, TraitGroup.Physical), (allocate.From, allocate.Steps, allocate.Group));
        Assert.Equal([new Allocate(Play.S3, 0), new Allocate(Play.S3, 1), new Allocate(Play.S3, 2), new Logic.Timeout(Play.S3)], game.Legal(Play.S3));
        game.Do(new Allocate(Play.S3, 2));
        Assert.Equal(new TraitSteps(2, 4, 4, 4), game.Crew(Play.S3).Steps);
        Assert.Equal(2, game.Crew(Play.S1).Counted.Damage);
        Assert.DoesNotContain(game.Legal(Play.S1), c => c is Attack);   // once per turn
    }

    [Fact]
    public void A_timeout_splits_the_damage_evenly_higher_trait_first()
    {
        var game = Endgame().Steps(Play.S3, new TraitSteps(4, 6, 4, 4)).RigFight(3, 3, (a, d) => a - d == 3);
        game.Do(new Attack(Play.S1, "s3"));
        game.Do(new Logic.Timeout(Play.S3));
        Assert.Equal(new TraitSteps(3, 4, 4, 4), game.Crew(Play.S3).Steps);
    }

    [Fact]
    public void A_winning_defender_staggers_the_attacker()
    {
        var game = Endgame().RigFight(3, 3, (a, d) => d > a);
        game.Do(new Attack(Play.S1, "s3"));
        Assert.Equal(0, game.State.MovesLeft);
        Assert.Contains(game.Events, e => e is Attacked { Result: "staggered" });
        Assert.Null(game.State.Waiting);
    }

    [Fact]
    public void A_tie_does_nothing()
    {
        var game = Endgame().RigFight(3, 3, (a, d) => a == d);
        var moves = game.State.MovesLeft;
        game.Do(new Attack(Play.S1, "s3"));
        Assert.Equal(moves, game.State.MovesLeft);
        Assert.Contains(game.Events, e => e is Attacked { Result: "tie" });
    }

    [Fact]
    public void A_beaten_monster_is_stunned_and_misses_its_next_activation()
    {
        var game = Endgame().RigFight(3, 4, (a, d) => a > d);
        game.Do(new Attack(Play.S1, "brood1"));
        Assert.True(game.State.Board!.Tokens.Single(t => t.Id == "brood1").Stunned);
        game.Do(new EndTurn(Play.S1));
        Assert.False(game.State.Board!.Tokens.Single(t => t.Id == "brood1").Stunned);
        Assert.DoesNotContain(game.Legal(Play.S2), c => c is MonsterMove or MonsterAttack);
        Assert.Contains(new MonsterRecovered("brood1"), game.Events);
    }

    [Fact]
    public void A_monster_moves_and_attacks_on_its_controllers_turn()
    {
        var game = Endgame();
        game.Do(new EndTurn(Play.S1));
        var legal = game.Legal(Play.S2);
        Assert.Contains(new MonsterMove(Play.S2, "brood1", Play.C(2)), legal);
        Assert.Contains(new MonsterAttack(Play.S2, "brood1", Play.S1), legal);
        game.RigFight(4, 3, (a, d) => a - d == 1).Do(new MonsterAttack(Play.S2, "brood1", Play.S1));
        Assert.Equal(Play.S1, game.State.Waiting!.From);   // the defender owes the split, not the active seat
        game.Do(new Allocate(Play.S1, 1));
        Assert.Equal(Play.S2, game.State.ActiveSeat);
        Assert.DoesNotContain(game.Legal(Play.S2), c => c is MonsterAttack);
    }

    [Fact]
    public void Explore_has_no_combat()
        => Assert.DoesNotContain(Play.Game().Legal(Play.S1), c => c is Attack);
}
