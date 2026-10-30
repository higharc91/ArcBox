using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary>Save modifiers beyond plain numbers: advantage and disadvantage per stat, extra save proficiencies and Evasion.</summary>
public sealed class SaveModsTests
{
    private const string Field = """
        0. 0. 0. 0.
        0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static readonly ConditionDef Nimble = new(new ConditionId("nimble"), "Nimble",
        new StaticMods(SaveEdges: ImmutableSortedDictionary<Stat, Edge>.Empty.Add(Stat.Dex, Edge.Advantage)), [], Ordinal.Set());

    private static readonly ConditionDef Tangled = new(new ConditionId("tangled"), "Tangled",
        new StaticMods(SaveEdges: ImmutableSortedDictionary<Stat, Edge>.Empty.Add(Stat.Dex, Edge.Disadvantage)), [], Ordinal.Set());

    private static readonly PassiveDef Slippery = new("test.slippery", null, null, [], new StaticMods(SaveProficiencies: [Stat.Wis]));

    private static readonly PassiveDef Evasion = new("test.evasion", null, null, [], new StaticMods(Evasion: true));

    private static TestBattle Build(UnitSheet rogue)
        => TestBattle.On(Field)
            .Party("vex", rogue with { MaxHp = 40, InitiativeBonus = 10 }, new Tile(0, 0))
            .Enemy("gob", TestSheets.Goblin, new Tile(3, 1))
            .WithConditions(Nimble, Tangled)
            .WithPassives(Slippery, Evasion);

    private static BattleState With(BattleState state, params ConditionDef[] conditions)
        => state.Change("vex", u => u with
        {
            Conditions = [.. conditions.Select(c => new ActiveCondition(c.Id, null, null, false, 0, Stat.Con, false))],
        });

    private static RollResult SaveOf(TestBattle battle, BattleState state, Stat stat)
    {
        var (_, events) = battle.Run(state, "gob", "vex", null, new SaveEffect(stat, 10, [], []));
        return Assert.IsType<Rolled>(events[0]).Result;
    }

    [Fact]
    public void Save_edges_roll_two_dice_for_their_stat_and_cancel_out()
    {
        var battle = Build(TestSheets.Rogue);
        var (state, _) = battle.Start();

        var advantage = SaveOf(battle, With(state, Nimble), Stat.Dex);
        Assert.Equal(2, advantage.D20s.Length);
        Assert.Equal(advantage.D20s.Max(), advantage.Kept);
        var disadvantage = SaveOf(battle, With(state, Tangled), Stat.Dex);
        Assert.Equal(disadvantage.D20s.Min(), disadvantage.Kept);
        Assert.Single(SaveOf(battle, With(state, Nimble, Tangled), Stat.Dex).D20s);   // they cancel
        Assert.Single(SaveOf(battle, With(state, Nimble), Stat.Str).D20s);            // only DEX saves
        Assert.Equal(Edge.Advantage, Modifiers.For(With(state, Nimble), U("vex"), battle.Rules).SaveEdges![Stat.Dex]);
    }

    [Fact]
    public void Extra_save_proficiencies_add_proficiency_once()
    {
        // The rogue has DEX and INT save proficiency; WIS 10 (+0) gains +2 from the passive, DEX stays +3 + 2.
        var plain = Build(TestSheets.Rogue);
        var (state, _) = plain.Start();
        Assert.Equal(0, SaveOf(plain, state, Stat.Wis).Modifier);

        var slippery = Build(TestSheets.Rogue with { Passives = ["test.slippery"] });
        (state, _) = slippery.Start();
        Assert.Equal(2, SaveOf(slippery, state, Stat.Wis).Modifier);
        var dexOnly = Build(TestSheets.Rogue with { Passives = ["test.slippery"] });
        Assert.Equal(5, SaveOf(dexOnly, dexOnly.Start().State, Stat.Dex).Modifier);
    }

    [Fact]
    public void Evasion_takes_no_damage_on_a_dex_save_and_half_on_a_failure()
    {
        var blast = new SaveEffect(Stat.Dex, 0, [new DamageEffect(TestAbilities.Flat("10"), "fire", [])], [], HalfOnSuccess: true);
        var failBlast = blast with { Dc = 40 };
        var battle = Build(TestSheets.Rogue with { Passives = ["test.evasion"] });
        var (state, _) = battle.Start();

        var (_, events) = battle.Run(state, "gob", "vex", null, blast);
        Assert.DoesNotContain(events, e => e is Damaged);
        (_, events) = battle.Run(state, "gob", "vex", null, failBlast);
        Assert.Equal(5, Assert.Single(events.OfType<Damaged>()).Amount);

        // Without Evasion: half on a success, all on a failure.
        var plain = Build(TestSheets.Rogue);
        var (start, _) = plain.Start();
        Assert.Equal(5, Assert.Single(plain.Run(start, "gob", "vex", null, blast).Events.OfType<Damaged>()).Amount);
        Assert.Equal(10, Assert.Single(plain.Run(start, "gob", "vex", null, failBlast).Events.OfType<Damaged>()).Amount);
    }
}
