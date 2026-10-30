using System.Text.Json;

namespace Tactics.Logic.Tests;

public sealed class NamedHookTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static readonly ConditionId Transformed = new("transformed");

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static AbilityDef Ability(string id, int tier, CostDef cost, TargetDef target, string hook, string args = "{}",
        AiHints? ai = null)
        => new(new AbilityId(id), id, tier, cost, target, [new TransformEffect(hook, Args(args))], Ai: ai);

    private static readonly TargetDef Self = new(TargetKind.Self, 0, false, new AreaDef(AreaShape.Self));

    private static readonly TargetDef Foe = new(TargetKind.Enemy, 6, false, new AreaDef(AreaShape.Single));

    private static readonly AbilityDef WildShape = Ability("test.wild_shape", 0, new CostDef(Bonus: 1), Self, "wild_shape",
        """{"monster":"wolf"}""");

    private static readonly AbilityDef Counterspell = Ability("test.counterspell", 3,
        new CostDef(Reaction: 1, Resource: Ordinal.Map(("mana", 3))), Foe, "counterspell", ai: new AiHints(["reactsTo:spellCast"]));

    private static readonly AbilityDef Polymorph = Ability("test.polymorph", 4, new CostDef(Action: 1, Concentration: true), Foe,
        "polymorph");

    private static readonly AbilityDef AnimateDead = Ability("test.animate_dead", 0, new CostDef(Action: 1),
        new TargetDef(TargetKind.Tile, 6, false, new AreaDef(AreaShape.Single)), "animate_dead");

    private static AbilityDef Scorch(int tier) => new(new AbilityId($"test.scorch{tier}"), "Scorch", tier, new CostDef(Action: 1), Foe,
        [new DamageEffect(TestAbilities.Flat("2d6"), "fire", [])]);

    /// <summary>A caster (cleric numbers: WIS 16, 10 HP, 4 Mana) who acts first (initiative 15).</summary>
    private static UnitSheet Caster(params AbilityDef[] abilities) => (TestSheets.Cleric with { InitiativeBonus = 5 }).Knowing(abilities);

    [Fact]
    public void Wild_shape_reverts_at_zero_temp_hp()
    {
        var battle = TestBattle.On(Field)
            .Party("druid", Caster(WildShape), T(0, 1))
            .Enemy("gob", TestSheets.Goblin, T(4, 1))
            .With(WildShape)
            .WithMonsters(("wolf", TestSheets.Wolf));
        var (state, _) = battle.Start();
        var druid = state.Units[U("druid")].Sheet;

        var (after, events) = battle.Apply(state, new Use(U("druid"), WildShape.Id, new Target()));
        Assert.Equal(new Transformed(U("druid"), "beasts.wolf"), events[^1]);
        var wolf = after.Units[U("druid")];
        Assert.Same(druid, wolf.TrueSheet);
        Assert.Equal(("Wolf", "bite", Side.Party, 10), (wolf.Sheet.Name, wolf.Sheet.Weapon.Id, wolf.Sheet.Side, wolf.Sheet.MaxHp));
        Assert.Equal([12, 15, 12, 10, 16, 8],                         // the beast's STR, DEX, CON; the druid's INT, WIS, CHA
            new[] { Stat.Str, Stat.Dex, Stat.Con, Stat.Int, Stat.Wis, Stat.Cha }.Select(wolf.Sheet.Score));
        Assert.Equal((10, 11), (wolf.Hp, wolf.TempHp));                // the beast's HP are temporary HP

        // Damage eats the beast's HP first; when they run out the druid reverts and the rest carries over.
        (after, events) = battle.Run(after, "gob", "druid", null, new DamageEffect(TestAbilities.Flat("8"), "slashing", []));
        Assert.Equal<BattleEvent>([new Damaged(U("druid"), 8, "slashing", 10)], events);
        Assert.Equal(3, after.Units[U("druid")].TempHp);
        (after, events) = battle.Run(after, "gob", "druid", null, new DamageEffect(TestAbilities.Flat("5"), "slashing", []));
        Assert.Equal<BattleEvent>([new Damaged(U("druid"), 5, "slashing", 8), new Transformed(U("druid"), "cleric")], events);
        var back = after.Units[U("druid")];
        Assert.Same(druid, back.Sheet);
        Assert.Null(back.TrueSheet);
        Assert.Equal((8, 0), (back.Hp, back.TempHp));
    }

    /// <summary>A wizard with Counterspell at (0, 1) and an enemy mage (initiative 22) who knows the scorch spells.</summary>
    private static TestBattle CounterBattle(params AbilityDef[] spells)
        => TestBattle.On(Field)
            .Party("wiz", Caster(Counterspell), T(0, 1))
            .Enemy("mage", (TestSheets.Goblin with { InitiativeBonus = 10 }).Knowing(spells), T(4, 1))
            .With([Counterspell, .. spells]);

    [Fact]
    public void Counterspell_cancels_tier_2()
    {
        var scorch = Scorch(2);
        var battle = CounterBattle(scorch);
        var (state, _) = battle.Start();
        Assert.Equal(U("mage"), state.Active);

        var (offered, events) = battle.Apply(state, new Use(U("mage"), scorch.Id, new Target(Unit: U("wiz"))));
        Assert.Equal(new AbilityUsed(U("mage"), scorch.Id, new Target(Unit: U("wiz")), null), events[0]);
        Assert.IsType<ReactionOffered>(events[^1]);
        var offer = Assert.IsType<ReactionOffer>(offered.Pending);
        Assert.Equal(("wiz", "spellCast"), (offer.Reactor.Value, offer.Trigger));
        var choice = offer.Options.Single(o => o.Id == Counterspell.Id.Value);

        var (after, reacted) = battle.Apply(offered, new React(U("wiz"), choice));
        Assert.Equal<BattleEvent>(
        [
            new Reacted(U("wiz"), Counterspell.Id.Value),
            new AbilityUsed(U("wiz"), Counterspell.Id, new Target(Unit: U("mage")), null),
            new Countered(U("mage"), scorch.Id, U("wiz")),
        ], reacted);
        Assert.Equal(10, after.Units[U("wiz")].Hp);
        Assert.False(after.Units[U("wiz")].Economy.Reaction);
        Assert.Equal(1, after.Units[U("wiz")].Resources["mana"]);
        Assert.Equal(U("mage"), after.Active);
        Assert.Empty(after.Queue);
    }

    [Fact]
    public void Counterspell_against_tier_4_needs_a_check()
    {
        var scorch = Scorch(4);
        var outcomes = new SortedSet<bool>();
        for (ulong seed = 1; seed <= 16; seed++)
        {
            var battle = CounterBattle(scorch);
            var (state, _) = battle.Start(seed);
            (state, _) = battle.Apply(state, new Use(U("mage"), scorch.Id, new Target(Unit: U("wiz"))));
            var choice = ((ReactionOffer)state.Pending!).Options.Single(o => o.Id == Counterspell.Id.Value);

            // d20 + the casting modifier (WIS +3) against 10 + tier.
            var d20 = TestAbilities.Next(state.Rng, 20)[0];
            var (_, events) = battle.Apply(state, new React(U("wiz"), choice));
            var check = Assert.Single(events.OfType<Rolled>(), r => r.Kind == RollKind.Check);
            Assert.Equal((U("wiz"), d20 + 3, 14), (check.Roller, check.Result.Total, check.Result.Vs));
            var countered = d20 + 3 >= 14;
            Assert.Equal(countered, check.Result.Success);
            Assert.Equal(countered, events.Contains(new Countered(U("mage"), scorch.Id, U("wiz"))));
            Assert.Equal(!countered, events.Any(e => e is Damaged { Unit.Value: "wiz" }));
            outcomes.Add(countered);
        }
        Assert.Equal([false, true], outcomes);
    }

    [Fact]
    public void Polymorph_makes_a_sheep_until_a_wis_save_or_the_concentration_ends()
    {
        var outcomes = new SortedSet<bool>();
        for (ulong seed = 1; seed <= 16; seed++)
        {
            var battle = TestBattle.On(Field)
                .Party("wiz", Caster(Polymorph), T(0, 1))
                .Enemy("gob", TestSheets.Goblin, T(4, 1))
                .With(Polymorph)
                .WithMonsters(("sheep", TestSheets.Sheep));
            var (state, _) = battle.Start(seed);

            var (after, events) = battle.Apply(state, new Use(U("wiz"), Polymorph.Id, new Target(Unit: U("gob"))));
            Assert.Contains(new Transformed(U("gob"), "beasts.sheep"), events);
            var sheep = after.Units[U("gob")];
            Assert.Equal(("Sheep", Side.Enemy, 7, 7, 10), (sheep.Sheet.Name, sheep.Sheet.Side, sheep.Sheet.MaxHp, sheep.Hp, sheep.TempHp));
            var marker = Assert.Single(sheep.Conditions);
            Assert.Equal((Transformed, U("wiz"), true, Stat.Wis, 13, true),
                (marker.Id, marker.Source, marker.UntilSave, marker.SaveStat, marker.SaveDc, marker.Concentration));

            if (seed == 1)
            {
                // Ending the concentration ends it at once.
                var (dispelled, dispelEvents) = battle.Run(after, "gob", "wiz", null, new DispelEffect(Concentration: true));
                Assert.Contains(new Transformed(U("gob"), "goblin"), dispelEvents);
                Assert.Equal(("Goblin", 0), (dispelled.Units[U("gob")].Sheet.Name, dispelled.Units[U("gob")].TempHp));
            }

            // A WIS save (the sheep's WIS 8: −1) at the end of each of its turns.
            (after, _) = battle.Apply(after, new EndTurn(U("wiz")));
            var d20 = TestAbilities.Next(after.Rng, 20)[0];
            (after, events) = battle.Apply(after, new EndTurn(U("gob")));
            var saved = d20 - 1 >= 13;
            Assert.Equal(saved, events.Contains(new Transformed(U("gob"), "goblin")));
            Assert.Equal(saved ? "Goblin" : "Sheep", after.Units[U("gob")].Sheet.Name);
            outcomes.Add(saved);
        }
        Assert.Equal([false, true], outcomes);
    }

    [Fact]
    public void A_transformed_unit_dropping_by_any_means_turns_back_first()
    {
        const string pool = """
            0. 0. 0. 0x 0.
            0. 0. 0. 0. 0.
            """;
        var battle = TestBattle.On(pool)
            .Party("wiz", Caster(Polymorph), T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(2, 0))
            .Enemy("hob", TestSheets.Goblin, T(4, 1))
            .With(Polymorph)
            .WithMonsters(("sheep", TestSheets.Sheep));
        var (state, _) = battle.Start();
        (state, _) = battle.Apply(state, new Use(U("wiz"), Polymorph.Id, new Target(Unit: U("gob"))));

        // Pushed into deep water: the sheep goes back to a goblin, which dies.
        var (after, events) = battle.Run(state, "wiz", "gob", null, new PushEffect(1));
        var turned = events.IndexOf(new Transformed(U("gob"), "goblin"));
        Assert.True(turned > 0 && turned < events.IndexOf(new Died(U("gob"))));
        Assert.Equal(("Goblin", LifeState.Dead), (after.Units[U("gob")].Sheet.Name, after.Units[U("gob")].Life));
        Assert.Null(after.Units[U("gob")].TrueSheet);
    }

    [Fact]
    public void Animate_dead_raises_a_skeleton_from_an_enemy_corpse()
    {
        var battle = TestBattle.On(Field)
            .Party("necro", Caster(AnimateDead), T(0, 1))
            .Enemy("gob", TestSheets.Goblin, T(3, 1))
            .Enemy("hob", TestSheets.Goblin with { MaxHp = 30 }, T(5, 1))
            .With(AnimateDead)
            .WithMonsters(("skeleton", TestSheets.Skeleton));
        var (state, _) = battle.Start();
        (state, _) = battle.Run(state, "necro", "gob", null, new DamageEffect(TestAbilities.Flat("30"), "necrotic", []));
        Assert.Equal(LifeState.Dead, state.Units[U("gob")].Life);
        var skeleton = U("necro.skeleton.0");

        // No corpse on the tile: nothing.
        var (after, events) = battle.Apply(state, new Use(U("necro"), AnimateDead.Id, new Target(Tile: T(2, 2))));
        Assert.DoesNotContain(events, e => e is Spawned);

        after = after.Change("necro", u => u with { Economy = Economy.Fresh(u.Sheet) });
        (after, events) = battle.Apply(after, new Use(U("necro"), AnimateDead.Id, new Target(Tile: T(3, 1))));
        Assert.Equal(new Spawned(skeleton, T(3, 1), "undead.skeleton"), events[^1]);
        Assert.Equal((U("necro"), Side.Party), (after.Units[skeleton].SummonedBy, after.Units[skeleton].Sheet.Side));

        // The corpse is used up: once the skeleton falls there is nothing left to raise.
        (after, _) = battle.Run(after, "hob", skeleton.Value, null, new DamageEffect(TestAbilities.Flat("30"), "bludgeoning", []));
        after = after.Change("necro", u => u with { Economy = Economy.Fresh(u.Sheet) });
        (_, events) = battle.Apply(after, new Use(U("necro"), AnimateDead.Id, new Target(Tile: T(3, 1))));
        Assert.DoesNotContain(events, e => e is Spawned);
    }

    [Fact]
    public void Unknown_hook_throws_naming_it()
    {
        var battle = TestBattle.On(Field)
            .Party("wiz", Caster(), T(0, 1))
            .Enemy("gob", TestSheets.Goblin, T(4, 1));
        var (state, _) = battle.Start();

        var error = Assert.Throws<InvalidOperationException>(() =>
            battle.Run(state, "wiz", "gob", null, new TransformEffect("time_stop", Args("{}"))));
        Assert.Contains("time_stop", error.Message);
        Assert.Equal(["animate_dead", "counterspell", "polymorph", "wild_shape"], NamedHooks.Names);
    }
}
