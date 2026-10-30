using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class DownedTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        """;

    private static readonly AbilityId Help = new("common.help");

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>Melee, no attack roll: 1d4 radiant.</summary>
    internal static readonly AbilityDef Smite = new(new AbilityId("test.smite"), "Smite", Tier: 0, new CostDef(Action: 1),
        new TargetDef(TargetKind.Enemy, Range: 1, Los: false, new AreaDef(AreaShape.Single)),
        [new DamageEffect(TestAbilities.Flat("1d4"), "radiant", [])]);

    /// <summary>A melee attack at +30 (only a natural 1 misses) that deals 1d4 slashing.</summary>
    private static readonly AbilityDef Strike = new(new AbilityId("test.strike"), "Strike", Tier: 0, new CostDef(Action: 1),
        new TargetDef(TargetKind.Enemy, Range: 1, Los: false, new AreaDef(AreaShape.Single)),
        [new AttackEffect("weapon", null, OnHit: [new DamageEffect(TestAbilities.Flat("1d4"), "slashing", [])], OnMiss: [], OnCrit: [], Bonus: 30)]);

    /// <summary>Touch heal: 1d4 + WIS modifier.</summary>
    private static readonly AbilityDef Mend = new(new AbilityId("test.mend"), "Mend", Tier: 0, new CostDef(Action: 1),
        new TargetDef(TargetKind.Ally, Range: 1, Los: false, new AreaDef(AreaShape.Single)),
        [new HealEffect(TestAbilities.Flat("1d4"), ["mod.wis"])]);

    /// <summary>gob (acts first), brin, kara, in that order; kara stands next to gob and brin.</summary>
    private static TestBattle Build(params AbilityDef[] abilities)
        => TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter.Knowing(Smite), T(2, 1))
            .Party("brin", (TestSheets.Cleric with { InitiativeBonus = 1 }).Knowing(Mend), T(1, 1))
            .Enemy("gob", (TestSheets.Goblin with { InitiativeBonus = 10 }).Knowing(Smite, Strike), T(3, 1))
            .With([Smite, Strike, Mend, .. abilities]);

    private static BattleState Down(BattleState s, string unit, int countdown = 3)
        => s.Change(unit, u => u with { Hp = 0, Life = LifeState.Downed, Countdown = countdown });

    /// <summary>The state with its RNG advanced until the next d20 satisfies <paramref name="ok"/>.</summary>
    internal static BattleState NextD20(BattleState s, Func<int, bool> ok)
    {
        var rng = s.Rng;
        while (!ok(TestAbilities.Next(rng, 20)[0]))
            rng = rng.Roll(20).Next;
        return s with { Rng = rng };
    }

    [Fact]
    public void Zero_hp_party_member_downed_with_countdown_3()
    {
        var battle = Build();
        var (state, _) = battle.Start(seed: 3);
        Assert.Equal(U("gob"), state.Active);
        state = state.Change("kara", u => u with { Hp = 1, Concentrating = new AbilityId("test.bless") });

        var (after, events) = battle.Apply(state, new Use(U("gob"), Smite.Id, new Target(Unit: U("kara"))));

        var kara = after.Units[U("kara")];
        Assert.Equal(LifeState.Downed, kara.Life);
        Assert.Equal(3, kara.Countdown);
        Assert.Equal(0, kara.Hp);
        Assert.Null(kara.Concentrating);
        Assert.Contains(kara.Conditions, c => c.Id == Motion.Prone);
        Assert.Contains(new Downed(U("kara")), events);
        Assert.Contains(new ConcentrationBroken(U("kara"), new AbilityId("test.bless")), events);
        Assert.Equal(0, Assert.Single(events.OfType<Damaged>()).HpAfter);
        Assert.Contains(U("kara"), after.Order);
        Assert.Null(after.Outcome);
    }

    [Fact]
    public void Downing_fires_on_downed_then_allies_on_ally_downed()
    {
        var lastStand = new PassiveDef("test.last_stand", HookKind.OnDowned, null,
            [new ApplyEffect(new ConditionId("marked"), 1)], null);
        var avenge = new PassiveDef("test.avenge", HookKind.OnAllyDowned, null,
            [new ApplyEffect(new ConditionId("vengeful"), 1)], null, Target: PassiveTarget.Self);
        var battle = Build().WithPassives(lastStand, avenge);
        var (state, _) = battle.Start(seed: 3);
        state = state.Change("kara", u => u with { Hp = 1, Sheet = u.Sheet with { Passives = ["test.last_stand"] } })
            .Change("brin", u => u with { Sheet = u.Sheet with { Passives = ["test.avenge"] } });

        var (_, events) = battle.Apply(state, new Use(U("gob"), Smite.Id, new Target(Unit: U("kara"))));

        Assert.Equal([new PassiveTriggered(U("kara"), "test.last_stand"), new PassiveTriggered(U("brin"), "test.avenge")],
            events.OfType<PassiveTriggered>());
        Assert.Contains(new ConditionApplied(U("gob"), new ConditionId("marked")), events);     // OnDowned's other is the attacker
        Assert.Contains(new ConditionApplied(U("brin"), new ConditionId("vengeful")), events);
    }

    [Fact]
    public void Countdown_ticks_at_round_start()
    {
        var battle = Build();
        var (state, _) = battle.Start(seed: 3);
        state = Down(state, "kara");
        (state, _) = battle.Apply(state, new EndTurn(U("gob")));

        var (after, events) = battle.Apply(state, new EndTurn(U("brin")));

        Assert.Equal(2, after.Units[U("kara")].Countdown);
        var round = events.IndexOf(events.OfType<RoundStarted>().Single());
        var tick = events.IndexOf(new CountdownTicked(U("kara"), 2));
        Assert.True(round >= 0 && tick > round, "the countdown ticks once the new round has started");
        Assert.Equal(2, after.Round);
        Assert.Equal(U("gob"), after.Active);
    }

    [Fact]
    public void Hit_while_downed_minus_1_crit_minus_2()
    {
        var battle = Build();
        var (start, _) = battle.Start(seed: 3);
        start = Down(start, "kara");
        var strike = new Use(U("gob"), Strike.Id, new Target(Unit: U("kara")));

        var (hit, hitEvents) = battle.Apply(NextD20(start, d => d is > 1 and < 20), strike);
        Assert.Equal(2, hit.Units[U("kara")].Countdown);
        Assert.Contains(new CountdownTicked(U("kara"), 2), hitEvents);
        Assert.Equal(0, hit.Units[U("kara")].Hp);
        Assert.DoesNotContain(hitEvents, e => e is Damaged);

        var (crit, critEvents) = battle.Apply(NextD20(start, d => d == 20), strike);
        Assert.True(TestConditions.FirstAttack(critEvents).Crit);
        Assert.Equal(1, crit.Units[U("kara")].Countdown);
        Assert.Contains(new CountdownTicked(U("kara"), 1), critEvents);
        Assert.Equal(LifeState.Downed, crit.Units[U("kara")].Life);
    }

    [Fact]
    public void Heal_revives_with_healed_hp()
    {
        var battle = Build();
        var (state, _) = battle.Start(seed: 3);
        state = Down(state, "kara", countdown: 2).Give("kara", TestConditions.Lasting(TestConditions.Prone));
        (state, _) = battle.Apply(state, new EndTurn(U("gob")));
        var healed = TestAbilities.Next(state.Rng, 4)[0] + 3;   // WIS 16

        var (after, events) = battle.Apply(state, new Use(U("brin"), Mend.Id, new Target(Unit: U("kara"))));

        var kara = after.Units[U("kara")];
        Assert.Equal(LifeState.Up, kara.Life);
        Assert.Equal(healed, kara.Hp);
        Assert.Equal(0, kara.Countdown);
        Assert.Contains(kara.Conditions, c => c.Id == Motion.Prone);   // stays prone
        Assert.Contains(new Healed(U("kara"), healed, healed), events);
        Assert.Contains(new Revived(U("kara"), healed), events);
    }

    [Fact]
    public void Help_revives_at_1hp()
    {
        var battle = Build();
        var (state, _) = battle.Start(seed: 3);
        state = Down(state, "kara");
        (state, _) = battle.Apply(state, new EndTurn(U("gob")));

        var (after, events) = battle.Apply(state, new Use(U("brin"), Help, new Target(Unit: U("kara"))));

        Assert.Equal(LifeState.Up, after.Units[U("kara")].Life);
        Assert.Equal(1, after.Units[U("kara")].Hp);
        Assert.Contains(new Revived(U("kara"), 1), events);

        // Help reaches only an adjacent ally.
        var far = state.Change("brin", u => u with { Pos = T(0, 0) });
        Assert.Equal(RejectCode.OutOfRange, Battle.Apply(far, new Use(U("brin"), Help, new Target(Unit: U("kara"))), battle.Rules).Rejected?.Code);
    }

    [Fact]
    public void Help_on_a_standing_ally_gives_advantage_on_its_next_roll()
    {
        var battle = Build();
        var (state, _) = battle.Start(seed: 3);
        (state, _) = battle.Apply(state, new EndTurn(U("gob")));
        var (helped, helpEvents) = battle.Apply(state, new Use(U("brin"), Help, new Target(Unit: U("kara"))));
        Assert.Contains(new ConditionApplied(U("kara"), new ConditionId("helped")), helpEvents);
        (state, _) = battle.Apply(helped, new EndTurn(U("brin")));

        var (after, events) = battle.Apply(state, new Use(U("kara"), new AbilityId("common.attack"), new Target(Unit: U("gob"))));

        var roll = TestConditions.FirstAttack(events);
        Assert.Equal(2, roll.D20s.Length);
        Assert.Equal(roll.D20s.Max(), roll.Kept);
        Assert.Contains(new ConditionRemoved(U("kara"), new ConditionId("helped")), events);
        Assert.DoesNotContain(after.Units[U("kara")].Conditions, c => c.Id.Value == "helped");
    }

    [Fact]
    public void Countdown_0_out_with_weighted_injury()
    {
        var ribs = new InjuryDef(new InjuryId("cracked_ribs"), "Cracked ribs", Weight: 1, new StaticMods());
        var concussion = new InjuryDef(new InjuryId("concussion"), "Concussion", Weight: 3, new StaticMods());
        var seen = new HashSet<InjuryId>();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var battle = Build().WithInjuries(ribs, concussion);
            var (state, _) = battle.Start(seed);
            state = Down(state, "kara", countdown: 1);
            (state, _) = battle.Apply(state, new EndTurn(U("gob")));
            // Injuries in id order: concussion (1–3), then cracked_ribs (4).
            var expected = TestAbilities.Next(state.Rng, 4)[0] <= 3 ? concussion.Id : ribs.Id;

            var (after, events) = battle.Apply(state, new EndTurn(U("brin")));

            Assert.Contains(new CountdownTicked(U("kara"), 0), events);
            Assert.Contains(new UnitOut(U("kara"), expected), events);
            Assert.Equal(LifeState.Out, after.Units[U("kara")].Life);
            Assert.Equal([new RecordedInjury(U("kara"), expected)], after.Injuries);
            Assert.DoesNotContain(U("kara"), after.Order);
            seen.Add(expected);
        }
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void Enemy_at_zero_dies()
    {
        var bloodlust = new PassiveDef("test.bloodlust", HookKind.OnKill, null,
            [new ApplyEffect(new ConditionId("bloodied"), 1)], null, Target: PassiveTarget.Self);
        var battle = TestBattle.On(Field)
            .Party("kara", (TestSheets.Fighter with { InitiativeBonus = 10, Passives = ["test.bloodlust"] }).Knowing(Smite), T(2, 1))
            .Enemy("gob", TestSheets.Goblin, T(3, 1))
            .Enemy("hob", TestSheets.Goblin, T(6, 1))
            .With(Smite).WithPassives(bloodlust);
        var (state, _) = battle.Start(seed: 3);
        state = state.Change("gob", u => u with { Hp = 1 });

        var (after, events) = battle.Apply(state, new Use(U("kara"), Smite.Id, new Target(Unit: U("gob"))));

        var gob = after.Units[U("gob")];
        Assert.Equal(LifeState.Dead, gob.Life);
        Assert.Equal(0, gob.Hp);
        Assert.Contains(new Died(U("gob")), events);
        Assert.DoesNotContain(events, e => e is Downed);
        Assert.DoesNotContain(U("gob"), after.Order);
        Assert.Contains(new PassiveTriggered(U("kara"), "test.bloodlust"), events);
        Assert.Contains(new ConditionApplied(U("kara"), new ConditionId("bloodied")), events);

        // The dead occupy no tile: kara can walk onto gob's.
        var (moved, _) = battle.Apply(after, new Move(U("kara"), [T(2, 1), T(3, 1)]));
        Assert.Equal(T(3, 1), moved.Units[U("kara")].Pos);
    }

    [Fact]
    public void Unit_leaving_before_the_current_turn_shifts_the_turn_index()
    {
        var battle = TestBattle.On(Field)
            .Enemy("gob", TestSheets.Goblin with { InitiativeBonus = 10 }, T(3, 1))
            .Party("kara", (TestSheets.Fighter with { InitiativeBonus = 5 }).Knowing(Smite), T(2, 1))
            .Party("brin", TestSheets.Cleric with { InitiativeBonus = 1 }, T(1, 1))
            .Enemy("hob", TestSheets.Goblin with { InitiativeBonus = -10 }, T(6, 1))
            .With(Smite);
        var (state, _) = battle.Start(seed: 3);
        Assert.Equal([U("gob"), U("kara"), U("brin"), U("hob")], state.Order);
        (state, _) = battle.Apply(state, new EndTurn(U("gob")));
        state = state.Change("gob", u => u with { Hp = 1 });

        (state, _) = battle.Apply(state, new Use(U("kara"), Smite.Id, new Target(Unit: U("gob"))));
        Assert.Equal([U("kara"), U("brin"), U("hob")], state.Order);
        Assert.Equal(U("kara"), state.Active);

        (state, _) = battle.Apply(state, new EndTurn(U("kara")));
        Assert.Equal(U("brin"), state.Active);
        (state, _) = battle.Apply(state, new EndTurn(U("brin")));
        Assert.Equal(U("hob"), state.Active);
    }

    [Fact]
    public void Unit_downed_on_its_own_turn_hands_the_turn_on()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter with { InitiativeBonus = 10 }, T(2, 1))
            .Party("brin", TestSheets.Cleric, T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(3, 1));
        var (state, _) = battle.Start(seed: 3);
        state = NextD20(state.Change("kara", u => u with { Hp = 1 }), d => d == 20);   // the goblin's opportunity attack crits

        var (after, events) = battle.Apply(state, new Move(U("kara"), [T(2, 1), T(1, 1), T(0, 1)]));

        Assert.Equal(LifeState.Downed, after.Units[U("kara")].Life);
        Assert.Equal(T(2, 1), after.Units[U("kara")].Pos);              // dropped before its first step
        Assert.Contains(new TurnEnded(U("kara")), events);
        Assert.Equal(U("gob"), after.Active);
        Assert.Equal(new TurnStarted(U("gob"), 1), events[^1]);
    }

    [Fact]
    public void Deep_water_downs_through_the_downed_rules_and_a_revived_unit_swims_out()
    {
        const string river = """
            0. 0. 0. 0x
            0. 0. 0. 0.
            """;
        var shove = new AbilityDef(new AbilityId("test.shove"), "Shove", Tier: 0, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, Range: 1, Los: false, new AreaDef(AreaShape.Single)), [new PushEffect(1)]);
        var battle = TestBattle.On(river)
            .Enemy("gob", (TestSheets.Goblin with { InitiativeBonus = 10 }).Knowing(shove), T(1, 0))
            .Party("kara", TestSheets.Fighter, T(2, 0))
            .Party("brin", TestSheets.Cleric, T(3, 1))
            .With(shove);
        var (state, _) = battle.Start(seed: 3);
        state = state.Change("kara", u => u with { Concentrating = new AbilityId("test.bless") });

        var (after, events) = battle.Apply(state, new Use(U("gob"), shove.Id, new Target(Unit: U("kara"))));

        var kara = after.Units[U("kara")];
        Assert.Equal((LifeState.Downed, 3, 0, T(3, 0)), (kara.Life, kara.Countdown, kara.Hp, kara.Pos));
        Assert.Contains(new Downed(U("kara")), events);
        Assert.True(events.IndexOf(new Downed(U("kara"))) > events.IndexOf(events.OfType<Fell>().Single()));
        Assert.Null(kara.Concentrating);

        // Helped up in the water, kara stands there and can swim out on its turn.
        (after, _) = battle.Apply(after, new EndTurn(U("gob")));
        (after, _) = battle.Apply(after, new Use(U("brin"), Help, new Target(Unit: U("kara"))));
        Assert.Equal((LifeState.Up, T(3, 0)), (after.Units[U("kara")].Life, after.Units[U("kara")].Pos));
        (after, _) = battle.Apply(after, new EndTurn(U("brin")));
        (after, _) = battle.Apply(after, new Move(U("kara"), [T(3, 0), T(2, 0)]));
        Assert.Equal(T(2, 0), after.Units[U("kara")].Pos);
    }
}
