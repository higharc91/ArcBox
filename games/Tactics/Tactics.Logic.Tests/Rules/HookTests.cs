using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class HookTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        """;

    /// <summary>The spec §4.4 passive, verbatim.</summary>
    private const string SneakAttackJson = """
        { "id": "rogue.sneak_attack", "hook": "onAttackHit", "oncePerTurn": true,
          "when": { "all": [ { "weapon": ["finesse", "ranged"] }, { "any": [ { "advantage": true }, { "flanked": true } ] } ] },
          "effects": [ { "damage": { "dice": { "byLevel": { "1": "1d6", "3": "2d6", "5": "3d6", "7": "4d6", "9": "5d6" } } } } ] }
        """;

    private static readonly AbilityId Attack = new("common.attack");

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>A goblin with 30 HP, so damage doesn't drop it.</summary>
    private static readonly UnitSheet Goblin = TestSheets.Goblin with { MaxHp = 30 };

    private static PassiveDef SneakAttack() => JsonSerializer.Deserialize<PassiveDef>(SneakAttackJson, ArcJson.Options)!;

    [Fact]
    public void Sneak_attack_passive_reads_from_the_spec_json()
    {
        var sneak = SneakAttack();

        Assert.Equal(("rogue.sneak_attack", HookKind.OnAttackHit, true), (sneak.Id, sneak.Hook, sneak.OncePerTurn));
        var when = Assert.IsType<WhenDef>(sneak.When);
        Assert.Equal(["finesse", "ranged"], when.All[0].Weapon);
        Assert.Equal([true, null], when.All[1].Any.Select(w => w.Advantage));
        Assert.Equal([null, true], when.All[1].Any.Select(w => w.Flanked));
        var damage = Assert.IsType<DamageEffect>(Assert.Single(sneak.Effects));
        Assert.Equal(Dice.Parse("3d6"), damage.Dice!.At(6));

        var json = JsonSerializer.Serialize(sneak, ArcJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<PassiveDef>(json, ArcJson.Options), ArcJson.Options));
        Assert.Contains("\"hook\":\"onAttackHit\"", json);
    }

    [Fact]
    public void Sneak_attack_once_per_turn_with_advantage_or_flank()
    {
        // A rogue with two attacks a turn (shortsword, finesse: DEX +3, +2 proficiency) acts first (initiative 13); the
        // fighter (9) stands opposite it across the goblin (12) and flanks.
        var rogue = TestSheets.Rogue with { Passives = ["rogue.sneak_attack"], Attacks = 2 };
        TestBattle Build(Tile ally) => TestBattle.On(Field)
            .Party("ari", rogue, T(2, 1))
            .Party("kara", TestSheets.Fighter, ally)
            .Enemy("gob", Goblin, T(3, 1))
            .WithPassives(SneakAttack()).WithConditions([.. TestConditions.All]);

        // The first seed whose two flanked attacks (two d20s each, then 1d6 and sneak attack's 1d6 after the first) both hit
        // AC 15 without a crit, and whose first d20 alone also hits.
        static bool BothHit(Rng rng)
        {
            var d = TestAbilities.Next(rng, 20, 20, 6, 6, 20, 20);
            return d[0] is >= 10 and < 20 && d[1] < 20 && Math.Max(d[4], d[5]) is >= 10 and < 20;
        }
        var seed = Enumerable.Range(1, 200).Select(s => (ulong)s).First(s => BothHit(Rng.Seeded(s)));

        var flanking = Build(T(4, 1));
        var (state, _) = flanking.Start(seed);
        Assert.Equal(U("ari"), state.Active);
        var dice = TestAbilities.Next(state.Rng, 20, 20, 6, 6, 20, 20, 6);

        var (after, events) = flanking.Apply(state, new Use(U("ari"), Attack, new Target(Unit: U("gob"))));

        // Weapon damage, then sneak attack's 1d6 (level 1), once; the second hit adds weapon damage only.
        var first = dice[2] + 3;
        var sneak = dice[3];
        var second = dice[6] + 3;
        Assert.Equal(1, events.Count(e => e is PassiveTriggered));
        Assert.Equal<BattleEvent>(
        [
            new Damaged(U("gob"), first, "piercing", 30 - first),
            new PassiveTriggered(U("ari"), "rogue.sneak_attack"),
            new Damaged(U("gob"), sneak, null, 30 - first - sneak),
        ], events.SkipWhile(e => e is not Damaged).TakeWhile(e => e is not Rolled));
        Assert.Equal(new Damaged(U("gob"), second, "piercing", 30 - first - sneak - second), events[^1]);
        Assert.Contains("rogue.sneak_attack", after.Units[U("ari")].UsedThisTurn);

        // Next round it fires again (once per turn, not once per battle).
        foreach (var unit in new[] { "ari", "gob", "kara" })
            (after, _) = flanking.Apply(after, new EndTurn(U(unit)));
        Assert.Equal(U("ari"), after.Active);
        Assert.Empty(after.Units[U("ari")].UsedThisTurn);
        (_, events) = flanking.Apply(after, new Use(U("ari"), Attack, new Target(Unit: U("gob"))));
        var hits = events.OfType<Rolled>().Count(r => r.Result.Success);
        Assert.Equal(hits > 0 ? 1 : 0, events.Count(e => e is PassiveTriggered));

        // Not flanked and no advantage: no sneak attack. Advantage alone (a prone goblin) is enough.
        var apart = Build(T(0, 0));
        (state, _) = apart.Start(seed);
        (_, events) = apart.Apply(state, new Use(U("ari"), Attack, new Target(Unit: U("gob"))));
        Assert.DoesNotContain(events, e => e is PassiveTriggered);
        Assert.Contains(events, e => e is Rolled { Result.Success: true });

        (state, _) = apart.Start(seed);
        state = state.Give("gob", TestConditions.Lasting(TestConditions.Prone));
        (_, events) = apart.Apply(state, new Use(U("ari"), Attack, new Target(Unit: U("gob"))));
        Assert.Equal(1, events.Count(e => e is PassiveTriggered));
    }

    [Fact]
    public void Static_resist_halves_fire()
    {
        var burn = new AbilityDef(new AbilityId("test.burn"), "Burn", 1, new CostDef(),
            new TargetDef(TargetKind.Enemy, Range: 6, Los: false, new AreaDef(AreaShape.Single)),
            [new DamageEffect(TestAbilities.Flat("9"), "fire", [])]);
        var fireSoul = new PassiveDef("test.fire_soul", null, null, [], new StaticMods(Resist: Ordinal.Set("fire")));
        var warded = new ConditionDef(new ConditionId("warded"), "Warded", new StaticMods(Immune: Ordinal.Set("fire")), [], Ordinal.Set("magical"));
        var battle = TestBattle.On(Field)
            .Party("cleric", TestSheets.Cleric with { Abilities = [burn.Id], InitiativeBonus = 5 }, T(0, 1))
            .Enemy("gob", Goblin with { Passives = [fireSoul.Id] }, T(4, 1))
            .With(burn).WithPassives(fireSoul).WithConditions(warded);
        var (state, _) = battle.Start();

        var (_, events) = battle.Apply(state, new Use(U("cleric"), burn.Id, new Target(Unit: U("gob"))));
        Assert.Equal(new Damaged(U("gob"), 4, "fire", 26), events[1]);                 // 9 halved, rounded down

        state = state.Give("gob", TestConditions.Lasting(warded));
        (_, events) = battle.Apply(state, new Use(U("cleric"), burn.Id, new Target(Unit: U("gob"))));
        Assert.Equal(new Damaged(U("gob"), 0, "fire", 30), events[1]);                 // immune
    }

    [Fact]
    public void Hook_recursion_cap_throws_invariant()
    {
        // Thorns: when damaged, deal 1 piercing back to whoever did it.
        var thorns = new PassiveDef("test.thorns", HookKind.OnDamaged, null, [new DamageEffect(null, "piercing", ["level"])], null);
        var poke = new AbilityDef(new AbilityId("test.poke"), "Poke", 0, new CostDef(),
            new TargetDef(TargetKind.Enemy, Range: 6, Los: false, new AreaDef(AreaShape.Single)),
            [new DamageEffect(null, "piercing", ["level"])]);
        TestBattle Build(ImmutableArray<string> clericPassives) => TestBattle.On(Field)
            .Party("cleric", TestSheets.Cleric with { Abilities = [poke.Id], Passives = clericPassives, MaxHp = 100, InitiativeBonus = 5 }, T(0, 1))
            .Enemy("gob", Goblin with { Passives = [thorns.Id], MaxHp = 100 }, T(4, 1))
            .With(poke).WithPassives(thorns);

        // Only the goblin has thorns: one hook, one point back.
        var once = Build([]);
        var (state, _) = once.Start();
        var (_, events) = once.Apply(state, new Use(U("cleric"), poke.Id, new Target(Unit: U("gob"))));
        Assert.Equal<BattleEvent>(
        [
            new Damaged(U("gob"), 1, "piercing", 99),
            new PassiveTriggered(U("gob"), "test.thorns"),
            new Damaged(U("cleric"), 1, "piercing", 99),
        ], events[1..]);

        // Both have thorns: they trade points until the hooks nest deeper than 8, which breaks an invariant.
        var both = Build(["test.thorns"]);
        (state, _) = both.Start();
        var error = Assert.Throws<RulesInvariantException>(
            () => Battle.Apply(state, new Use(U("cleric"), poke.Id, new Target(Unit: U("gob"))), both.Rules));
        Assert.Contains("test.thorns", error.Message);
        Assert.Equal(8, Hooks.MaxDepth);
    }

    [Fact]
    public void Battle_round_turn_start_and_end_hooks_fire_in_order_and_not_on_a_resumed_turn()
    {
        // Each passive marks its owner with a condition named after the hook, so the events show what fired when.
        PassiveDef Marker(HookKind hook, string name)
            => new("test." + name, hook, null, [new ApplyEffect(new ConditionId(name), null)], null, Target: PassiveTarget.Self);
        var passives = new[]
        {
            Marker(HookKind.OnBattleStart, "battle"), Marker(HookKind.OnRoundStart, "round"),
            Marker(HookKind.OnTurnStart, "turn"), Marker(HookKind.OnTurnEnd, "ending"),
        };
        var sheet = TestSheets.Fighter with { Passives = [.. passives.Select(p => p.Id)] };
        var battle = TestBattle.On(Field)
            .Party("kara", sheet with { InitiativeBonus = 10 }, T(0, 0))   // 19
            .Party("ari", TestSheets.Rogue, T(0, 2))                      // 13
            .Enemy("gob", Goblin, T(6, 1))                                // 12
            .WithPassives(passives);

        BattleEvent Fired(string name) => new PassiveTriggered(U("kara"), "test." + name);
        BattleEvent Marked(string name) => new ConditionApplied(U("kara"), new ConditionId(name));

        var (state, events) = battle.Start();
        Assert.Equal<BattleEvent>(
        [
            Fired("battle"), Marked("battle"), Fired("round"), Marked("round"), Fired("turn"), Marked("turn"),
        ], events.SkipWhile(e => e is not PassiveTriggered));
        Assert.IsType<TurnStarted>(events[1]);

        // Delaying past ari: kara's turn resumes later with no second turn-start hook. The turn-end hook runs before
        // TurnEnded.
        (state, _) = battle.Apply(state, new Delay(U("kara"), 1));
        (state, events) = battle.Apply(state, new EndTurn(U("ari")));
        Assert.Equal(U("kara"), state.Active);
        Assert.DoesNotContain(events, e => e is PassiveTriggered);
        (_, events) = battle.Apply(state, new EndTurn(U("kara")));
        Assert.Equal<BattleEvent>([Fired("ending"), Marked("ending"), new TurnEnded(U("kara"))], events[..3]);
    }

    [Fact]
    public void Retaliation_needs_another_unit_and_condition_passives_come_from_their_source()
    {
        // Thorns (once per battle) hits back whoever damaged its owner; it never turns on the owner.
        var thorns = new PassiveDef("test.thorns", HookKind.OnDamaged, null, [new DamageEffect(null, "piercing", ["level"])], null,
            UsesPerBattle: 1);
        var shove = new AbilityDef(new AbilityId("test.shove"), "Shove", 0, new CostDef(),
            new TargetDef(TargetKind.Enemy, Range: 1, Los: false, new AreaDef(AreaShape.Single)), [new PushEffect(1)]);
        const string wall = """
            0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0# 0.
            0. 0. 0. 0. 0. 0.
            """;
        TestBattle Build() => TestBattle.On(wall)
            .Party("kara", TestSheets.Fighter with { Abilities = [shove.Id], InitiativeBonus = 10 }, T(2, 1))
            .Enemy("gob", Goblin with { Passives = [thorns.Id], MaxHp = 100 }, T(3, 1))
            .With(shove).WithPassives(thorns).WithConditions([.. TestConditions.All]);

        // A collision (no source) and burning nobody applied (it comes from the goblin itself): no thorns, nothing spent.
        var battle = Build();
        var (state, _) = battle.Start();
        state = state.Give("gob", TestConditions.Lasting(TestConditions.Burning));
        var (after, events) = battle.Apply(state, new Use(U("kara"), shove.Id, new Target(Unit: U("gob"))));
        Assert.Contains(events, e => e is Collided);
        (after, var burnt) = battle.Apply(after, new EndTurn(U("kara")));
        Assert.Contains(new PassiveTriggered(U("gob"), "burning.tick"), burnt);
        Assert.DoesNotContain(events.AddRange(burnt), e => e is PassiveTriggered { Passive: "test.thorns" });
        Assert.Equal(12, after.Units[U("kara")].Hp);
        Assert.False(after.Units[U("gob")].UsesLeft.ContainsKey("test.thorns"));

        // Burning the fighter applied comes from the fighter: thorns answers it, once.
        (state, _) = battle.Start();
        state = state.Give("gob", TestConditions.Lasting(TestConditions.Burning, source: U("kara")));
        (after, events) = battle.Apply(state, new EndTurn(U("kara")));
        Assert.Equal<BattleEvent>(
            [new PassiveTriggered(U("gob"), "test.thorns"), new Damaged(U("kara"), 1, "piercing", 11)],
            events.SkipWhile(e => e is not PassiveTriggered { Passive: "test.thorns" }));
        Assert.Equal(0, after.Units[U("gob")].UsesLeft["test.thorns"]);
    }

    [Fact]
    public void Turn_end_save_hooks_resolve_before_the_next_turn_starts()
    {
        var frightened = new ConditionId("frightened");
        var steadied = new ConditionId("steadied");
        var steady = new PassiveDef("test.steady", HookKind.OnSave, null, [new ApplyEffect(steadied, null)], null,
            Target: PassiveTarget.Self);
        var battle = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter, T(0, 1))                                        // 9
            .Enemy("gob", Goblin with { Passives = [steady.Id] }, T(5, 1))                    // 12
            .WithPassives(steady);
        var (state, _) = battle.Start();
        Assert.Equal(U("gob"), state.Active);
        state = state.Give("gob", new ActiveCondition(frightened, U("kara"), null, true, 30, Stat.Wis, false));

        var (_, events) = battle.Apply(state, new EndTurn(U("gob")));

        Assert.Equal(RollKind.Save, Assert.IsType<Rolled>(events[0]).Kind);
        Assert.Equal<BattleEvent>(
        [
            new TurnEnded(U("gob")),
            new PassiveTriggered(U("gob"), "test.steady"),
            new ConditionApplied(U("gob"), steadied),
            new TurnStarted(U("kara"), 1),
        ], events[1..]);
    }
}
