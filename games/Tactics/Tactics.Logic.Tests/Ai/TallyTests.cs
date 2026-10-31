using Tactics.Sim;

namespace Tactics.Logic.Tests;

/// <summary>The simulator's <see cref="Tally"/>: who each event's damage, healing and mitigation is credited to.</summary>
public sealed class TallyTests
{
    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>A wizard and a fighter (party members, by class tag) against one goblin, read into a fresh tally.</summary>
    private static (Tally Tally, BattleState State) Fresh()
    {
        static UnitSheet Member(UnitSheet sheet, string cls) => sheet with { Tags = sheet.Tags.Add(SheetBuilder.ClassTag + cls) };
        var (state, _) = TestBattle.On("""
            0. 0. 0. 0. 0.
            """)
            .Party("wiz", Member(TestSheets.Fighter, "wizard"), T(0, 0))
            .Party("kara", Member(TestSheets.Fighter, "fighter"), T(1, 0))
            .Enemy("gob", TestSheets.Goblin, T(4, 0))
            .Start();
        var tally = new Tally(BasePackTests.Rules);
        tally.Read([], state);
        return (tally, state);
    }

    [Fact]
    public void Zone_damage_on_a_foes_turn_is_credited_to_the_zones_caster()
    {
        var (tally, state) = Fresh();
        var hp = state.Units[U("gob")].Hp;

        // The goblin starts its turn in the wizard's fire: the zone's passive is the goblin's, its source the wizard.
        tally.Read([new TurnStarted(U("gob"), 1), new PassiveTriggered(U("gob"), "wall_of_fire.burn", Source: U("wiz")),
            new Damaged(U("gob"), 5, "fire", hp - 5)], state);

        var stats = tally.PerClass(rounds: 1);
        Assert.Equal(5, stats["wizard"].DamagePerRound);
    }

    [Fact]
    public void A_passive_with_no_other_source_still_belongs_to_its_owner()
    {
        var (tally, state) = Fresh();
        var hp = state.Units[U("gob")].Hp;

        tally.Read([new TurnStarted(U("kara"), 1), new PassiveTriggered(U("kara"), "fighter.riposte"),
            new Damaged(U("gob"), 4, "slashing", hp - 4)], state);

        Assert.Equal(4, tally.PerClass(rounds: 1)["fighter"].DamagePerRound);
    }

    [Fact]
    public void Temporary_hp_soak_is_the_targets_mitigation_not_the_attackers_damage()
    {
        var (tally, state) = Fresh();
        var hp = state.Units[U("kara")].Hp;

        // Kara has 6 temporary HP; a 10-damage hit spends them and takes 4 HP.
        tally.Read([new TempHpGained(U("kara"), 6), new TurnStarted(U("gob"), 1), new AttackResolved(U("gob"), U("kara"), Hit: true, Crit: false),
            new Damaged(U("kara"), 10, "slashing", hp - 4)], state);

        var fighter = tally.PerClass(rounds: 1)["fighter"];
        Assert.Equal(10, fighter.MitigatedPerRound);
    }
}
