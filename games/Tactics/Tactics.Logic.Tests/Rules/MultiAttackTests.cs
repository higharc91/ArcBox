using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// #107: a weapon attack's attacks each pick their own target (<see cref="Use.Targets"/>, with retargeting when a target has
/// fallen), and a <see cref="AbilityDef.RiderOnce"/> ability's extras land on its first hit only.
/// </summary>
public sealed class MultiAttackTests
{
    // Kara stands at (2, 1) with four foes around her; (6, 1) is out of her reach.
    private const string Field = """
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        """;

    private static readonly AbilityId Attack = RulesPack.CommonAttack.Id;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>A fighter with three attacks who acts first.</summary>
    private static readonly UnitSheet Kara = TestSheets.Fighter with { Attacks = 3, InitiativeBonus = 40 };

    /// <summary>A goblin with <paramref name="hp"/> HP that is hit by anything but a natural 1.</summary>
    private static UnitSheet Gob(int hp, int ac = 0) => TestSheets.Goblin with { MaxHp = hp, Ac = ac };

    private static UnitId[] Struck(ImmutableArray<BattleEvent> events) => [.. events.OfType<AttackResolved>().Select(a => a.Target)];

    /// <summary>The first seed from 1 up whose first attack roll isn't a natural 1, so Kara's first attack hits.</summary>
    private static ulong FirstHitSeed(TestBattle battle)
    {
        for (ulong seed = 1; ; seed++)
            if (TestAbilities.Next(battle.Start(seed).State.Rng, 20)[0] != 1)
                return seed;
    }

    [Fact]
    public void Each_attack_goes_to_its_listed_target_in_order()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Kara, T(2, 1))
            .Enemy("a", Gob(40), T(3, 1)).Enemy("b", Gob(40), T(2, 0)).Enemy("c", Gob(40), T(2, 2));
        var (state, _) = battle.Start(seed: 3);

        var use = new Use(U("kara"), Attack, new Target(Unit: U("a"))) { Targets = [U("a"), U("b"), U("c")] };
        var (_, events) = battle.Apply(state, use);

        Assert.Equal([U("a"), U("b"), U("c")], Struck(events));
        Assert.Equal([new Retargeted(U("kara"), U("b")), new Retargeted(U("kara"), U("c"))], events.OfType<Retargeted>());
        Assert.Equal(new Target(Unit: U("a")), Assert.Single(events.OfType<AbilityUsed>()).Target);
    }

    [Fact]
    public void A_single_target_still_takes_every_attack()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Kara, T(2, 1))
            .Enemy("a", Gob(60), T(3, 1)).Enemy("b", Gob(1), T(2, 0));
        var (state, _) = battle.Start(seed: 3);

        var (_, events) = battle.Apply(state, new Use(U("kara"), Attack, new Target(Unit: U("a"))));

        Assert.Equal([U("a"), U("a"), U("a")], Struck(events));
        Assert.Empty(events.OfType<Retargeted>());
    }

    [Fact]
    public void A_short_list_repeats_its_last_target()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Kara, T(2, 1))
            .Enemy("a", Gob(60), T(3, 1)).Enemy("b", Gob(60), T(2, 0));
        var (state, _) = battle.Start(seed: 3);

        var use = new Use(U("kara"), Attack, new Target(Unit: U("a"))) { Targets = [U("a"), U("b")] };
        Assert.Equal([U("a"), U("b"), U("b")], Struck(battle.Apply(state, use).Events));
    }

    [Fact]
    public void After_a_kill_the_next_attack_goes_to_the_next_listed_target()
    {
        // c is closer to dead than b, but b is the one listed.
        var battle = TestBattle.On(Field)
            .Party("kara", Kara, T(2, 1))
            .Enemy("a", Gob(1), T(3, 1)).Enemy("b", Gob(60), T(2, 0)).Enemy("c", Gob(2), T(2, 2));
        var (state, _) = battle.Start(FirstHitSeed(battle));

        var use = new Use(U("kara"), Attack, new Target(Unit: U("a"))) { Targets = [U("a"), U("a"), U("b")] };
        var (after, events) = battle.Apply(state, use);

        Assert.Equal(LifeState.Dead, after.Units[U("a")].Life);
        Assert.Equal([U("a"), U("b"), U("b")], Struck(events));
        Assert.Equal([new Retargeted(U("kara"), U("b"))], events.OfType<Retargeted>());
    }

    [Fact]
    public void With_no_listed_target_left_the_rest_go_to_the_hostile_in_reach_with_the_least_hp_ties_by_id()
    {
        // d ties c on HP and loses on id; "far" has the least HP but is out of reach; the ally is never a choice.
        var battle = TestBattle.On(Field)
            .Party("kara", Kara, T(2, 1))
            .Party("ally", TestSheets.Cleric with { MaxHp = 1 }, T(1, 0))
            .Enemy("a", Gob(1), T(3, 1)).Enemy("b", Gob(40), T(2, 0)).Enemy("d", Gob(25), T(1, 1)).Enemy("c", Gob(25), T(2, 2))
            .Enemy("far", Gob(1), T(6, 1));
        var (state, _) = battle.Start(FirstHitSeed(battle));

        var (_, events) = battle.Apply(state, new Use(U("kara"), Attack, new Target(Unit: U("a"))));

        Assert.Equal([U("a"), U("c"), U("c")], Struck(events));
        Assert.Equal([new Retargeted(U("kara"), U("c"))], events.OfType<Retargeted>());
    }

    [Fact]
    public void An_attack_whose_target_was_pushed_out_of_reach_turns_to_a_foe_still_in_reach()
    {
        var shove = new AbilityDef(new AbilityId("test.bash"), "Bash", 0, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, 1, false, new AreaDef(AreaShape.Single)),
            [new AttackEffect("weapon", null, [new DamageEffect(null, null, [], Weapon: true), new PushEffect(2)], [], [])],
            IsWeaponAttack: true);
        var battle = TestBattle.On(Field)
            .Party("kara", Kara with { Attacks = 2, Abilities = [shove.Id] }, T(2, 1))
            .Enemy("a", Gob(60), T(3, 1)).Enemy("b", Gob(60), T(2, 0))
            .With(shove);
        var (state, _) = battle.Start(FirstHitSeed(battle));

        var (after, events) = battle.Apply(state, new Use(U("kara"), shove.Id, new Target(Unit: U("a"))));

        Assert.Equal(T(5, 1), after.Units[U("a")].Pos);
        Assert.Equal([U("a"), U("b")], Struck(events));
    }

    [Fact]
    public void An_attack_with_nobody_left_in_reach_does_nothing()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Kara, T(2, 1))
            .Enemy("a", Gob(1), T(3, 1)).Enemy("far", Gob(1), T(6, 1));
        var (state, _) = battle.Start(FirstHitSeed(battle));

        var (after, events) = battle.Apply(state, new Use(U("kara"), Attack, new Target(Unit: U("a"))));

        Assert.Equal([U("a")], Struck(events));
        Assert.Equal(LifeState.Up, after.Units[U("far")].Life);
        Assert.Single(events.OfType<Rolled>(), r => r.Kind == RollKind.Attack);
    }

    [Fact]
    public void The_targets_list_is_checked_when_the_command_is()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Kara with { Abilities = [TestAbilities.GuidingBolt.Id], Resources = Ordinal.Map(("devotion", 1)), CastingStat = "Wis" }, T(2, 1))
            .Party("ally", TestSheets.Cleric, T(1, 1))
            .Enemy("a", Gob(40), T(3, 1)).Enemy("b", Gob(40), T(2, 0)).Enemy("far", Gob(40), T(6, 1))
            .With(TestAbilities.GuidingBolt);
        var (state, _) = battle.Start(seed: 3);
        Rejection? Check(Use use) => Battle.Check(state, use, battle.Rules);
        Use Hit(params string[] targets) => new(U("kara"), Attack, new Target(Unit: U("a"))) { Targets = [.. targets.Select(U)] };

        Assert.Null(Check(Hit("a", "b", "b")));
        Assert.Null(Check(Hit("a")));
        Assert.Equal(RejectCode.InvalidTarget, Check(Hit("a", "b", "b", "b"))?.Code);     // more than her three attacks
        Assert.Equal(RejectCode.InvalidTarget, Check(Hit("b", "a"))?.Code);               // the first is the command's target
        Assert.Equal(RejectCode.OutOfRange, Check(Hit("a", "far"))?.Code);
        Assert.Equal(RejectCode.InvalidTarget, Check(Hit("a", "ally"))?.Code);
        Assert.Equal(RejectCode.InvalidTarget, Check(Hit("a", "ghost"))?.Code);
        var bolt = new Use(U("kara"), TestAbilities.GuidingBolt.Id, new Target(Unit: U("a"))) { Targets = [U("a"), U("b")] };
        Assert.Equal(RejectCode.InvalidTarget, Check(bolt)?.Code);                         // only a weapon attack takes a list
    }

    [Fact]
    public void A_use_without_targets_reads_and_writes_as_before()
    {
        var plain = new Use(U("kara"), Attack, new Target(Unit: U("a")));
        var json = JsonSerializer.Serialize<Command>(plain, ArcJson.Options);
        Assert.DoesNotContain("targets", json);
        var old = JsonSerializer.Deserialize<Command>("""{"$type":"Use","actor":"kara","ability":"common.attack","target":{"unit":"a"}}""", ArcJson.Options);
        Assert.Equal(plain, old);
        Assert.True(((Use)old!).Targets.IsDefaultOrEmpty);

        var listed = plain with { Targets = [U("a"), U("b")] };
        var back = JsonSerializer.Deserialize<Command>(JsonSerializer.Serialize<Command>(listed, ArcJson.Options), ArcJson.Options);
        Assert.Equal(listed, back);
        Assert.NotEqual(plain, back);
        Assert.Equal(plain, plain with { Targets = [] });
    }

    // ---- Riders --------------------------------------------------------------------------------------------------------

    /// <summary>An attack whose hit deals the weapon's damage plus 1d4 + 1 more and 1 radiant, with a save to knock prone.</summary>
    private static AbilityDef Strike(bool once) => new(new AbilityId("test.rider"), "Rider", 0,
        new CostDef(Action: 1, Resource: Ordinal.Map(("second_wind", 1))),
        new TargetDef(TargetKind.Enemy, 1, false, new AreaDef(AreaShape.Single)),
        [
            new AttackEffect("weapon", null,
                [
                    new DamageEffect(TestAbilities.Flat("1d4+1"), null, [], Weapon: true),
                    new DamageEffect(TestAbilities.Flat("1"), "radiant", []),
                    new SaveEffect(Stat.Str, 30, [new ApplyEffect(new ConditionId("prone"), 1)], []),
                ], [], [], Bonus: 2),
        ],
        IsWeaponAttack: true, RiderOnce: once);

    [Fact]
    public void A_rider_once_ability_lands_its_extras_on_the_first_hit_and_plain_hits_after()
    {
        var seen = 0;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var ability = Strike(once: true);
            var battle = TestBattle.On(Field)
                .Party("kara", Kara with { Abilities = [ability.Id] }, T(2, 1))
                .Enemy("a", Gob(200, ac: 16), T(3, 1))
                .With(ability);
            var (state, _) = battle.Start(seed);
            var (_, events) = battle.Apply(state, new Use(U("kara"), ability.Id, new Target(Unit: U("a"))));

            var list = events.ToList();
            var attacks = list.OfType<AttackResolved>().ToArray();
            // Events are records, equal by value: find each attack by its position, not by IndexOf.
            var places = Enumerable.Range(0, list.Count).Where(i => list[i] is AttackResolved).ToArray();
            Assert.Equal(3, attacks.Length);
            var rolls = list.OfType<Rolled>().Where(r => r.Kind == RollKind.Attack).ToArray();
            var first = Array.FindIndex(attacks, a => a.Hit);
            // The +2 counts until the rider lands, and not after.
            for (var i = 0; i < rolls.Length; i++)
                Assert.Equal(first < 0 || i <= first ? 7 : 5, rolls[i].Result.Modifier);
            if (first < 0)
            {
                Assert.DoesNotContain(list, e => e is Damaged { Type: "radiant" });
                continue;
            }
            seen += attacks.Count(a => a.Hit) > 1 && first > 0 ? 1 : 0;
            // One radiant hit and one save, both straight after the first hit; the later hits are the weapon's alone.
            var at = places[first];
            var next = first + 1 < places.Length ? places[first + 1] : list.Count;
            var radiant = list.FindIndex(e => e is Damaged { Type: "radiant" });
            Assert.InRange(radiant, at, next);
            Assert.Single(list, e => e is Damaged { Type: "radiant" });
            Assert.Single(list, e => e is Rolled { Kind: RollKind.Save });
            for (var i = first + 1; i < attacks.Length; i++)
                if (attacks[i] is { Hit: true, Crit: false })
                    Assert.InRange(((Damaged)list[places[i] + 1]).Amount, 1 + 3, 8 + 3);   // 1d8 + STR, no 1d4 + 1
        }
        Assert.True(seen > 0, "some seed should miss first and hit twice after");
    }

    [Fact]
    public void Without_rider_once_every_hit_carries_the_extras()
    {
        var ability = Strike(once: false);
        var battle = TestBattle.On(Field)
            .Party("kara", Kara with { Abilities = [ability.Id] }, T(2, 1))
            .Enemy("a", Gob(200), T(3, 1))
            .With(ability);
        var (state, _) = battle.Start(seed: 3);
        var (_, events) = battle.Apply(state, new Use(U("kara"), ability.Id, new Target(Unit: U("a"))));

        var hits = events.OfType<AttackResolved>().Count(a => a.Hit);
        Assert.True(hits >= 2);
        Assert.Equal(hits, events.Count(e => e is Damaged { Type: "radiant" }));
    }

    [Fact]
    public void A_rider_follows_its_attack_to_a_new_target()
    {
        // The first hit kills a; the rider has landed, so b takes plain hits.
        var ability = Strike(once: true);
        var battle = TestBattle.On(Field)
            .Party("kara", Kara with { Abilities = [ability.Id] }, T(2, 1))
            .Enemy("a", Gob(1), T(3, 1)).Enemy("b", Gob(200), T(2, 0))
            .With(ability);
        var (state, _) = battle.Start(FirstHitSeed(battle));
        var (_, events) = battle.Apply(state, new Use(U("kara"), ability.Id, new Target(Unit: U("a"))));

        Assert.Equal([U("a"), U("b"), U("b")], Struck(events));
        Assert.DoesNotContain(events, e => e is Damaged { Type: "radiant", Unit.Value: "b" });
        Assert.DoesNotContain(events, e => e is Rolled { Kind: RollKind.Save, Roller.Value: "b" });
    }

    [Fact]
    public void A_battle_saved_between_attacks_resumes_with_the_same_targets_and_rider()
    {
        // A reaction offer to a player pauses the attacks between the first and the second; the save is loaded and answered.
        var ability = Strike(once: true);
        var shield = new AbilityDef(new AbilityId("test.parry"), "Parry", 0, new CostDef(Reaction: 1),
            new TargetDef(TargetKind.Self, 0, false, new AreaDef(AreaShape.Self)),
            [new StatModEffect(new StaticMods(Ac: 0), 0)], Ai: new AiHints(["reactsTo:beforeHit"]));
        var battle = TestBattle.On(Field)
            .Party("kara", Kara with { Abilities = [ability.Id] }, T(2, 1))
            .Enemy("a", Gob(1), T(3, 1))
            .Enemy("b", Gob(200) with { Abilities = [shield.Id] }, T(2, 0), new PlayerControlled(new ProfileId("pr_b")))
            .With(ability, shield);
        var (state, _) = battle.Start(FirstHitSeed(battle));
        var use = new Use(U("kara"), ability.Id, new Target(Unit: U("a"))) { Targets = [U("a"), U("b"), U("b")] };
        var resolution = Battle.Apply(state, use, battle.Rules);
        Assert.Null(resolution.Rejected);
        var events = resolution.Events.ToList();
        var paused = resolution.State;
        while (paused.Pending is ReactionOffer offer)
        {
            var saved = JsonSerializer.Deserialize<BattleState>(JsonSerializer.Serialize(paused, ArcJson.Options), ArcJson.Options)!;
            var next = Battle.Apply(saved, new React(offer.Reactor, new ReactionChoice("pass", null, null)), battle.Rules);
            Assert.Null(next.Rejected);
            events.AddRange(next.Events);
            paused = next.State;
        }

        // The rider landed on the blow that killed a (its extras find nobody there); b's hits, the last after the load, are plain.
        Assert.True(events.Count(e => e is ReactionOffered) >= 1, "b should have been offered a reaction between attacks");
        Assert.Equal([U("a"), U("b"), U("b")], Struck([.. events]));
        Assert.DoesNotContain(events, e => e is Damaged { Type: "radiant" });
        Assert.DoesNotContain(events, e => e is Rolled { Kind: RollKind.Save });
    }
}
