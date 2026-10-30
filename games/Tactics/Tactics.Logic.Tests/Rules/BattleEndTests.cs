namespace Tactics.Logic.Tests;

public sealed class BattleEndTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>A burst of 2 at range 6 that hits allies too: 10d6 fire, no save.</summary>
    private static readonly AbilityDef Fireball = new(new AbilityId("test.fireball"), "Fireball", Tier: 0, new CostDef(Action: 1),
        new TargetDef(TargetKind.Tile, Range: 6, Los: false, new AreaDef(AreaShape.Burst, Size: 2, FriendlyFire: true)),
        [new DamageEffect(TestAbilities.Flat("10d6"), "fire", [])]);

    /// <summary>kara (acts first) next to the last goblin, which has 1 HP; comp is an AI-played party member.</summary>
    private static (TestBattle Battle, BattleState State) LastEnemyAt1Hp()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", (TestSheets.Fighter with { InitiativeBonus = 10 }).Knowing(DownedTests.Smite), T(2, 1))
            .Party("comp", TestSheets.Cleric, T(0, 0), new AiControlled("companion"))
            .Enemy("gob", TestSheets.Goblin, T(3, 1))
            .With(DownedTests.Smite);
        var (state, _) = battle.Start(seed: 4);
        return (battle, state.Change("gob", u => u with { Hp = 1 }));
    }

    [Fact]
    public void Last_enemy_dies_won()
    {
        var (battle, state) = LastEnemyAt1Hp();

        var (after, events) = battle.Apply(state, new Use(U("kara"), DownedTests.Smite.Id, new Target(Unit: U("gob"))));

        Assert.Equal(BattleOutcome.Won, after.Outcome);
        Assert.Equal(new BattleEnded(BattleOutcome.Won), events[^1]);
        Assert.Single(events.OfType<BattleEnded>());
        Assert.Empty(after.Queue);
        Assert.Null(after.Pending);
        Assert.Null(after.Active);
    }

    [Fact]
    public void Fireball_downing_last_two_party_members_ends_once_and_drops_queue()
    {
        var battle = TestBattle.On(Field)
            .Enemy("cult", (TestSheets.Goblin with { InitiativeBonus = 10 }).Knowing(Fireball), T(5, 2))
            .Party("ana", TestSheets.Fighter with { MaxHp = 5 }, T(1, 1))
            .Party("bo", TestSheets.Rogue with { MaxHp = 5 }, T(2, 1))
            .Enemy("gob", TestSheets.Goblin, T(1, 0))
            .With(Fireball);
        var (state, _) = battle.Start(seed: 8);

        var (after, events) = battle.Apply(state, new Use(U("cult"), Fireball.Id, new Target(Tile: T(1, 1))));

        Assert.Equal(BattleOutcome.Lost, after.Outcome);
        Assert.Equal([new Downed(U("ana")), new Downed(U("bo"))], events.OfType<Downed>());
        Assert.Equal(new BattleEnded(BattleOutcome.Lost), Assert.Single(events.OfType<BattleEnded>()));
        Assert.Equal(new BattleEnded(BattleOutcome.Lost), events[^1]);
        // gob was the third target in the area: its effects were dropped with the queue.
        Assert.DoesNotContain(events, e => e is Damaged { Unit.Value: "gob" });
        Assert.Equal(TestSheets.Goblin.MaxHp, after.Units[U("gob")].Hp);
        Assert.Empty(after.Queue);
    }

    [Fact]
    public void Commands_after_end_rejected_BattleOver()
    {
        var (battle, state) = LastEnemyAt1Hp();
        var (ended, _) = battle.Apply(state, new Use(U("kara"), DownedTests.Smite.Id, new Target(Unit: U("gob"))));

        foreach (Command command in new Command[] { new EndTurn(U("kara")), new Move(U("kara"), [T(2, 1), T(2, 2)]),
                     new React(U("kara"), new ReactionChoice("pass", null, null)) })
        {
            var resolution = Battle.Apply(ended, command, battle.Rules);
            Assert.Same(ended, resolution.State);
            Assert.Empty(resolution.Events);
            Assert.Equal(Rejection.Of(RejectCode.BattleOver), resolution.Rejected);
        }
    }

    [Fact]
    public void Result_gives_full_xp_to_every_party_member_including_ai_controlled()
    {
        var (battle, state) = LastEnemyAt1Hp();
        Assert.Null(Battle.Result(state, battle.Rules));

        var (ended, _) = battle.Apply(state, new Use(U("kara"), DownedTests.Smite.Id, new Target(Unit: U("gob"))));
        var result = Battle.Result(ended, battle.Rules);

        Assert.NotNull(result);
        Assert.Equal(BattleOutcome.Won, result.Outcome);
        Assert.Equal([(U("comp"), 250), (U("kara"), 250)], result.Xp.Select(p => (p.Key, p.Value)));
        Assert.Empty(result.Injuries);
        Assert.Empty(result.ItemsUsed);
        Assert.Empty(result.Loot);
        Assert.Empty(result.FlagsSet);
    }

    [Fact]
    public void Result_groups_injuries_and_items_by_unit()
    {
        var (battle, state) = LastEnemyAt1Hp();
        state = state with
        {
            Injuries = [new RecordedInjury(U("kara"), new InjuryId("concussion")), new RecordedInjury(U("comp"), new InjuryId("cracked_ribs"))],
            ItemsUsed = [new ItemUse(U("comp"), new ItemId("potion"), ByAi: true), new ItemUse(U("comp"), new ItemId("bomb"), ByAi: true)],
            Loot = [new LootTableId("goblin_cache")],
            Flags = Ordinal.Set("bridge_down"),
        };
        var (ended, _) = battle.Apply(state, new Use(U("kara"), DownedTests.Smite.Id, new Target(Unit: U("gob"))));

        var result = Battle.Result(ended, battle.Rules)!;

        Assert.Equal([new InjuryId("cracked_ribs")], result.Injuries[U("comp")]);
        Assert.Equal([new InjuryId("concussion")], result.Injuries[U("kara")]);
        Assert.Equal([new ItemId("potion"), new ItemId("bomb")], result.ItemsUsed[U("comp")].Select(i => i.Item));
        Assert.False(result.ItemsUsed.ContainsKey(U("kara")));
        Assert.Equal([new LootTableId("goblin_cache")], result.Loot);
        Assert.Equal(["bridge_down"], result.FlagsSet);
    }
}
