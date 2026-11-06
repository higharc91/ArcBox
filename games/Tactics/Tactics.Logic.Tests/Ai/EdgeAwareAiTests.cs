using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

/// <summary>#106: the AI scores a push by where it lands and keeps off an edge a foe could shove it over.</summary>
public sealed class EdgeAwareAiTests
{
    private static RulesPack Base => BasePackTests.Rules;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static AbilityDef Shove => Base.Abilities[new AbilityId("common.shove")];

    private static TestBattle On(string ascii) => TestBattle.On(ascii).WithAiProfiles(Base.AiProfiles.Values).With(Shove);

    private static UnitSheet Steadfast(UnitSheet sheet) => sheet with { Tags = sheet.Tags.Add(Tactics.Logic.Steadfast.Tag) };

    private static UnitSheet First(UnitSheet sheet) => sheet with { InitiativeBonus = 40 };

    // Row 0 is a lava bank; the goblin stands south of the victim, who stands on the edge.
    private const string Edge = """
        0x 0x 0x 0x 0x
        0. 0. 0. 0. 0.
        0. 0. 0. 0. 0.
        0. 0. 0. 0. 0.
        """;

    private const string Flat = """
        0. 0. 0. 0. 0.
        0. 0. 0. 0. 0.
        0. 0. 0. 0. 0.
        0. 0. 0. 0. 0.
        """;

    private static readonly UnitSheet Hero = TestSheets.Fighter with { MaxHp = 40, Move = 0 };

    private static TurnPlan Plan(TestBattle battle, BattleState state, string unit) => Ai.Plan(state, U(unit), battle.Rules);

    private static bool Shoves(TurnPlan plan) => plan.Commands.OfType<Use>().Any(u => u.Ability.Value == "common.shove");

    [Fact]
    public void The_AI_shoves_a_foe_standing_on_an_edge_over_it_but_attacks_when_nothing_is_there_to_fall_into()
    {
        var onEdge = On(Edge).Enemy("gob", First(TestSheets.Goblin with { Move = 0 }), T(2, 2)).Party("kara", Hero, T(2, 1));
        Assert.True(Shoves(Plan(onEdge, onEdge.Start().State, "gob")), "should shove a hero toward the lava");

        var onFlat = On(Flat).Enemy("gob", First(TestSheets.Goblin with { Move = 0 }), T(2, 2)).Party("kara", Hero, T(2, 1));
        Assert.False(Shoves(Plan(onFlat, onFlat.Start().State, "gob")), "no point shoving on flat ground");
    }

    [Fact]
    public void A_guarded_boss_on_the_edge_is_not_worth_shoving()
    {
        var boss = On(Edge).Party("kara", First(Hero with { Move = 0 }), T(2, 2))
            .Enemy("boss", Steadfast(TestSheets.Goblin with { MaxHp = 40, Move = 0 }), T(2, 1));
        var plan = Plan(boss, boss.Start().State, "kara");
        Assert.False(Shoves(plan), "a steadfast boss only braces: shoving it gains nothing");

        var grunt = On(Edge).Party("kara", First(Hero with { Move = 0 }), T(2, 2))
            .Enemy("grunt", TestSheets.Goblin with { MaxHp = 40, Move = 0 }, T(2, 1));
        Assert.True(Shoves(Plan(grunt, grunt.Start().State, "kara")), "an ordinary enemy dies in the lava");
    }

    /// <summary>
    /// The goblin may strike the hero from the edge tile (2, 1) (a step up, so it would otherwise like it) or from (1, 2), safe.
    /// The hero can shove, so the edge costs it.
    /// </summary>
    private static (TestBattle Battle, BattleState State) EdgeChoice(UnitSheet gob, UnitSheet hero, bool deepBehind)
    {
        var map = (deepBehind ? "0x 0x 0x 0x 0x\n" : "0. 0. 0. 0. 0.\n") + """
            0. 0. 1. 0. 0.
            0. 0. 0. 0. 0.
            0. 0. 0. 0. 0.
            """;
        var battle = On(map).Enemy("gob", First(gob), T(0, 3)).Party("kara", hero, T(2, 2));
        return (battle, battle.Start().State);
    }

    private static Tile EndTile(BattleState state, TurnPlan plan) => plan.Commands.OfType<Move>().LastOrDefault()?.Path[^1] ?? state.Units[U("gob")].Pos;

    [Fact]
    public void The_AI_does_not_end_its_turn_on_an_edge_beside_a_foe_that_can_shove_it()
    {
        var gob = TestSheets.Goblin with { MaxHp = 40, Move = 4 };
        var (battle, state) = EdgeChoice(gob, Hero, deepBehind: true);
        var edge = EndTile(state, Plan(battle, state, "gob"));
        Assert.NotEqual(T(2, 1), edge);
        Assert.Equal(1, Reach.Distance(edge, T(2, 2)));   // it still closes in to strike

        // Without the lava behind that tile the step up is the better place to stand.
        var (safeBattle, safeState) = EdgeChoice(gob, Hero, deepBehind: false);
        Assert.Equal(T(2, 1), EndTile(safeState, Plan(safeBattle, safeState, "gob")));

        // A guarded unit has nothing to fear from the edge.
        var (bossBattle, bossState) = EdgeChoice(Steadfast(gob), Hero, deepBehind: true);
        Assert.Equal(T(2, 1), EndTile(bossState, Plan(bossBattle, bossState, "gob")));
    }
}
