using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class ConditionTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        """;

    private static readonly AbilityId Attack = new("common.attack");

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>A fighter who acts first (initiative 19).</summary>
    private static readonly UnitSheet Fighter = TestSheets.Fighter with { InitiativeBonus = 10 };

    /// <summary>An archer with a longbow (DEX 12: +1, +2 proficiency) who acts first.</summary>
    private static readonly UnitSheet Archer = Fighter with { Weapon = TestConditions.Longbow };

    /// <summary>A goblin with 30 HP, so damage doesn't drop it.</summary>
    private static readonly UnitSheet Goblin = TestSheets.Goblin with { MaxHp = 30 };

    private static TestBattle Build(UnitSheet attacker, Tile at, Tile gob, params AbilityDef[] abilities)
        => TestBattle.On(Field).Party("kara", attacker, at).Enemy("gob", Goblin, gob)
            .WithConditions([.. TestConditions.All]).With(abilities);

    private static Rejection Rejected(TestBattle battle, BattleState state, Command command)
    {
        var resolution = Battle.Apply(state, command, battle.Rules);
        Assert.Same(state, resolution.State);
        return Assert.IsType<Rejection>(resolution.Rejected);
    }

    /// <summary>The first attack roll when kara attacks gob, with the given conditions on each.</summary>
    private static RollResult AttackRoll(UnitSheet attacker, Tile at, Tile gob, ConditionDef[] onKara, ConditionDef[] onGob)
    {
        var battle = Build(attacker, at, gob);
        var (state, _) = battle.Start(seed: 7);
        state = state.Give("kara", [.. onKara.Select(c => TestConditions.Lasting(c))])
            .Give("gob", [.. onGob.Select(c => TestConditions.Lasting(c))]);
        var (_, events) = battle.Apply(state, new Use(U("kara"), Attack, new Target(Unit: U("gob"))));
        return TestConditions.FirstAttack(events);
    }

    private static void AssertEdge(Edge edge, RollResult roll)
    {
        switch (edge)
        {
            case Edge.None:
                Assert.Single(roll.D20s);
                break;
            case Edge.Advantage:
                Assert.Equal(2, roll.D20s.Length);
                Assert.Equal(roll.D20s.Max(), roll.Kept);
                break;
            case Edge.Disadvantage:
                Assert.Equal(2, roll.D20s.Length);
                Assert.Equal(roll.D20s.Min(), roll.Kept);
                break;
        }
    }

    [Fact]
    public void Prone_melee_adv_ranged_disadv()
    {
        var prone = TestConditions.Prone;

        AssertEdge(Edge.None, AttackRoll(Fighter, T(2, 1), T(3, 1), [], []));
        AssertEdge(Edge.Advantage, AttackRoll(Fighter, T(2, 1), T(3, 1), [], [prone]));      // melee against prone
        AssertEdge(Edge.Disadvantage, AttackRoll(Archer, T(0, 1), T(5, 1), [], [prone]));    // ranged against prone
        AssertEdge(Edge.Disadvantage, AttackRoll(Fighter, T(2, 1), T(3, 1), [prone], []));   // a prone attacker
        AssertEdge(Edge.None, AttackRoll(Fighter, T(2, 1), T(3, 1), [prone], [prone]));      // one of each cancels
    }

    [Fact]
    public void Stunned_skips_actions_and_autofails_str_dex()
    {
        AbilityDef SaveAbility(Stat stat) => new(new AbilityId("test.save_" + stat), "Save", 0, new CostDef(),
            new TargetDef(TargetKind.Enemy, Range: 6, Los: false, new AreaDef(AreaShape.Single)),
            [new SaveEffect(stat, Dc: 1, OnFail: [], OnSuccess: [])]);
        var saves = new[] { SaveAbility(Stat.Str), SaveAbility(Stat.Dex), SaveAbility(Stat.Wis) };
        var battle = Build(Fighter with { Abilities = [.. saves.Select(a => a.Id)] }, T(2, 1), T(3, 1), saves);
        var (state, _) = battle.Start();
        var stunned = TestConditions.Lasting(TestConditions.Stunned);

        // DC 1 saves: STR and DEX fail whatever the d20; WIS still succeeds.
        var onGob = state.Give("gob", stunned);
        foreach (var (ability, success) in saves.Zip([false, false, true]))
        {
            var (_, events) = battle.Apply(onGob, new Use(U("kara"), ability.Id, new Target(Unit: U("gob"))));
            var save = Assert.Single(events.OfType<Rolled>());
            Assert.Equal((RollKind.Save, success), (save.Kind, save.Result.Success));
        }

        // A stunned unit can't use abilities or move on its turn, only end it.
        var (gobTurn, _) = battle.Apply(state, new EndTurn(U("kara")));
        Assert.Equal(U("gob"), gobTurn.Active);
        gobTurn = gobTurn.Give("gob", stunned);
        Assert.Equal(RejectCode.Incapacitated, Rejected(battle, gobTurn, new Use(U("gob"), Attack, new Target(Unit: U("kara")))).Code);
        var moving = Rejected(battle, gobTurn, new Move(U("gob"), [T(3, 1), T(4, 1)]));
        Assert.Equal((RejectCode.Incapacitated, "You can't act right now."), (moving.Code, moving.Reason));
        battle.Apply(gobTurn, new EndTurn(U("gob")));

        // Nor take reactions: a stunned fighter gets no opportunity attack when the goblin walks away.
        var (walked, walkEvents) = battle.Apply(state.Give("kara", stunned) with { TurnIndex = 1 },
            new Move(U("gob"), [T(3, 1), T(4, 1), T(5, 1)]));
        Assert.DoesNotContain(walkEvents, e => e is ReactionOffered);
        Assert.Equal(T(5, 1), walked.Units[U("gob")].Pos);
    }

    [Fact]
    public void Restrained_cant_move()
    {
        var battle = Build(Fighter, T(0, 1), T(5, 1));
        var (state, _) = battle.Start();
        state = state.Give("kara", TestConditions.Lasting(TestConditions.Restrained));

        var rejection = Rejected(battle, state, new Move(U("kara"), [T(0, 1), T(1, 1)]));
        Assert.Equal((RejectCode.NoMove, "You don't have enough movement."), (rejection.Code, rejection.Reason));

        // The next turn starts with no Move at all, but the unit can still act.
        var (next, _) = battle.Apply(state, new EndTurn(U("kara")));
        (next, _) = battle.Apply(next, new EndTurn(U("gob")));
        Assert.Equal(U("kara"), next.Active);
        Assert.Equal(0, next.Units[U("kara")].Economy.MoveLeft);
        Assert.True(next.Units[U("kara")].Economy.Action);
    }

    [Fact]
    public void Invisible_attacks_have_advantage()
    {
        var invisible = TestConditions.Invisible;

        AssertEdge(Edge.Advantage, AttackRoll(Fighter, T(2, 1), T(3, 1), [invisible], []));
        AssertEdge(Edge.Disadvantage, AttackRoll(Fighter, T(2, 1), T(3, 1), [], [invisible]));
        AssertEdge(Edge.None, AttackRoll(Fighter, T(2, 1), T(3, 1), [invisible], [invisible]));
    }

    [Fact]
    public void Rounds_tick_on_source_turn_end()
    {
        var blessed = new ConditionId("blessed");
        var guarded = new ConditionId("guarded");
        var hexed = new ConditionId("hexed");
        var dazed = new ConditionId("dazed");
        AbilityDef Grant(string id, ConditionId condition, int rounds, CostDef cost) => new(new AbilityId(id), id, 1, cost,
            new TargetDef(TargetKind.Ally, Range: 4, Los: false, new AreaDef(AreaShape.Single)), [new ApplyEffect(condition, rounds)]);
        var bless = Grant("test.bless", blessed, 1, new CostDef(Action: 1));       // until the end of the cleric's next turn
        var guard = Grant("test.guard", guarded, 0, new CostDef(Bonus: 1));        // until the end of this turn
        var battle = TestBattle.On(Field)
            .Party("cleric", TestSheets.Cleric with { Abilities = [bless.Id, guard.Id], InitiativeBonus = 5 }, T(0, 1))   // 15
            .Party("kara", TestSheets.Fighter, T(1, 1))                                                                  // 9
            .Enemy("gob", Goblin, T(6, 1))                                                                               // 12
            .With(bless, guard);
        var (state, _) = battle.Start();
        Assert.Equal([U("cleric"), U("gob"), U("kara")], state.Order);
        state = state.Give("gob",
            new ActiveCondition(dazed, null, 1, false, 0, Stat.Con, false),                          // no source: ticks on gob's turn
            TestConditions.Lasting(TestConditions.Prone));                                          // no duration: never ticks

        int? Left(BattleState s, string unit, ConditionId id) => s.Units[U(unit)].Conditions.Single(c => c.Id == id).RoundsLeft;

        (state, _) = battle.Apply(state, new Use(U("cleric"), bless.Id, new Target(Unit: U("kara"))));
        (state, _) = battle.Apply(state, new Use(U("cleric"), guard.Id, new Target(Unit: U("kara"))));
        Assert.True(state.Units[U("kara")].Conditions.All(c => c.AppliedThisTurn));

        // The turn they were applied in: rounds 0 ends; rounds 1 doesn't tick.
        (state, var events) = battle.Apply(state, new EndTurn(U("cleric")));
        Assert.Equal<BattleEvent>([new ConditionRemoved(U("kara"), guarded), new TurnEnded(U("cleric"))], events[..2]);
        Assert.Equal(1, Left(state, "kara", blessed));
        Assert.False(state.Units[U("kara")].Conditions.Single().AppliedThisTurn);

        // During the goblin's turn the cleric hexes it for 1 round (as a reaction would): off the cleric's turn, so it
        // ends with blessed, at the end of the cleric's next turn.
        state = state.Give("gob", new ActiveCondition(hexed, U("cleric"), 1, false, 0, Stat.Con, false));
        (state, events) = battle.Apply(state, new EndTurn(U("gob")));
        Assert.Equal<BattleEvent>([new ConditionRemoved(U("gob"), dazed), new TurnEnded(U("gob"))], events[..2]);
        Assert.Equal(1, Left(state, "kara", blessed));                 // not the source's turn
        Assert.Contains(state.Units[U("gob")].Conditions, c => c.Id == TestConditions.Prone.Id);

        (state, _) = battle.Apply(state, new EndTurn(U("kara")));      // kara's own turn: still not the source's
        Assert.Equal(1, Left(state, "kara", blessed));

        (state, events) = battle.Apply(state, new EndTurn(U("cleric")));
        Assert.Equal<BattleEvent>(
            [new ConditionRemoved(U("gob"), hexed), new ConditionRemoved(U("kara"), blessed), new TurnEnded(U("cleric"))],
            events[..3]);
        Assert.Empty(state.Units[U("kara")].Conditions);
    }

    [Fact]
    public void Until_save_rolls_each_turn_end()
    {
        var frightened = new ConditionId("frightened");
        var battle = Build(TestSheets.Fighter, T(0, 1), T(5, 1));      // goblin 12 acts before the fighter's 9
        var (state, _) = battle.Start(seed: 3);
        Assert.Equal(U("gob"), state.Active);
        state = state.Give("gob", new ActiveCondition(frightened, U("kara"), null, true, 30, Stat.Wis, false));

        // DC 30: the goblin (WIS −1) can't make it, and the condition stays.
        var d20 = TestAbilities.Next(state.Rng, 20)[0];
        (state, var events) = battle.Apply(state, new EndTurn(U("gob")));
        var failed = Assert.IsType<Rolled>(events[0]);
        Assert.Equal((U("gob"), RollKind.Save), (failed.Roller, failed.Kind));
        Assert.Equal((d20, -1, 30, false), (failed.Result.Kept, failed.Result.Modifier, failed.Result.Vs, failed.Result.Success));
        Assert.Equal(new TurnEnded(U("gob")), events[1]);
        Assert.Contains(state.Units[U("gob")].Conditions, c => c.Id == frightened);

        // Its next turn end, against DC 1: saved, and the condition ends.
        (state, _) = battle.Apply(state, new EndTurn(U("kara")));
        state = state.Change("gob", g => g with { Conditions = [g.Conditions.Single() with { SaveDc = 1 }] });
        Assert.True(TestAbilities.Next(state.Rng, 20)[0] >= 2);          // seed 3: d20 − 1 reaches DC 1
        (state, events) = battle.Apply(state, new EndTurn(U("gob")));
        var saved = Assert.IsType<Rolled>(events[0]).Result;
        Assert.True(saved.Success);
        Assert.Equal<BattleEvent>(
            [new ConditionSaved(U("gob"), frightened, saved), new ConditionRemoved(U("gob"), frightened), new TurnEnded(U("gob"))],
            events[1..4]);
        Assert.Empty(state.Units[U("gob")].Conditions);
    }

    [Fact]
    public void Burning_deals_1d6_at_turn_start()
    {
        var battle = Build(TestSheets.Fighter, T(0, 1), T(5, 1));
        var (state, _) = battle.Start(seed: 2);
        Assert.Equal(U("gob"), state.Active);
        state = state.Give("kara", TestConditions.Lasting(TestConditions.Burning, source: U("gob")));
        var d6 = TestAbilities.Next(state.Rng, 6)[0];

        var (after, events) = battle.Apply(state, new EndTurn(U("gob")));

        Assert.Equal<BattleEvent>(
        [
            new TurnEnded(U("gob")),
            new TurnStarted(U("kara"), 1),
            new PassiveTriggered(U("kara"), "burning.tick", U("gob")),
            new Damaged(U("kara"), d6, "fire", 12 - d6),
        ], events);
        Assert.Equal(12 - d6, after.Units[U("kara")].Hp);
        Assert.Empty(after.Queue);
    }

    [Fact]
    public void Move_and_initiative_modifiers_shape_the_turn_and_still_allow_delay()
    {
        var slowed = new ConditionDef(new ConditionId("slowed"), "Slowed", new StaticMods(Move: -1, Initiative: -5), [], Ordinal.Set("magical"));
        var hasted = new ConditionDef(new ConditionId("hasted"), "Hasted", new StaticMods(Move: 1, Initiative: 5, ExtraActions: 1), [],
            Ordinal.Set("magical"));
        var battle = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter, T(0, 0))                 // 9
            .Party("ari", TestSheets.Rogue, T(0, 2))                    // 13
            .Enemy("gob", Goblin, T(6, 1))                              // 12
            .WithConditions(slowed, hasted);
        var (state, _) = battle.Start();
        Assert.Equal([U("ari"), U("gob"), U("kara")], state.Order);
        state = state.Give("ari", TestConditions.Lasting(slowed)).Give("kara", TestConditions.Lasting(hasted));

        // Next round: ari 13 − 5 = 8 drops behind kara 9 + 5 = 14; each turn's Move and actions follow the modifiers.
        foreach (var unit in new[] { "ari", "gob", "kara" })
            (state, _) = battle.Apply(state, new EndTurn(U(unit)));
        Assert.Equal([U("kara"), U("gob"), U("ari")], state.Order);
        Assert.Equal((5, 1), (state.Units[U("kara")].Economy.MoveLeft, state.Units[U("kara")].Economy.ExtraActions));
        (state, _) = battle.Apply(state, new EndTurn(U("kara")));
        (state, _) = battle.Apply(state, new EndTurn(U("gob")));
        Assert.Equal(U("ari"), state.Active);
        Assert.Equal(3, state.Units[U("ari")].Economy.MoveLeft);

        // A slowed unit that has spent nothing may still delay (its full Move is 3, not the sheet's 4).
        var slow = TestBattle.On(Field)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("gob", Goblin, T(6, 1))
            .WithConditions(slowed);
        var (first, _) = slow.Start();
        first = first.Give("gob", TestConditions.Lasting(slowed));
        (first, _) = slow.Apply(first, new EndTurn(U("gob")));
        (first, _) = slow.Apply(first, new EndTurn(U("kara")));        // round 2: gob 12 − 5 = 7 now goes last
        (first, _) = slow.Apply(first, new EndTurn(U("kara")));        // round 3: kara, then gob
        Assert.Equal([U("kara"), U("gob")], first.Order);
        var (delayed, _) = slow.Apply(first with { TurnIndex = 0 } , new Delay(U("kara"), 1));
        Assert.Equal(U("gob"), delayed.Active);
    }

    [Fact]
    public void Condition_defs_read_from_content_json()
    {
        const string json = """
            {"id":"stunned","name":"Stunned",
             "static":{"saves":{"wis":-1},"attackedMelee":"advantage","attackedRanged":"advantage","autoFailSaves":["str","dex"],"resist":["poison"]},
             "passives":[{"id":"stunned.shake","hook":"onTurnEnd","effects":[{"remove":"prone"}],"oncePerRound":true}],
             "tags":["magical","Fear"],"implies":"incapacitated"}
            """;
        var def = System.Text.Json.JsonSerializer.Deserialize<ConditionDef>(json, ArcBox.Sdk.ArcJson.Options)!;

        Assert.Equal((new ConditionId("stunned"), new ConditionId("incapacitated")), (def.Id, def.Implies));
        var mods = def.Static!;
        Assert.Equal((Edge.Advantage, Edge.Advantage, Edge.None), (mods.AttackedMelee, mods.AttackedRanged, mods.OwnAttacks));
        Assert.Equal([Stat.Str, Stat.Dex], mods.AutoFailSaves!);
        Assert.Equal(-1, mods.Saves![Stat.Wis]);
        var passive = Assert.Single(def.Passives);
        Assert.Equal((HookKind.OnTurnEnd, true), (passive.Hook, passive.OncePerRound));
        Assert.Equal(["Fear", "magical"], def.Tags);                    // ordinal: upper case first

        var written = System.Text.Json.JsonSerializer.Serialize(def, ArcBox.Sdk.ArcJson.Options);
        Assert.Contains("\"autoFailSaves\":[\"str\",\"dex\"]", written);
        Assert.Contains("\"saves\":{\"wis\":-1}", written);
        Assert.DoesNotContain("\"ac\"", written);                       // defaults are left out
        Assert.Equal(written, System.Text.Json.JsonSerializer.Serialize(
            System.Text.Json.JsonSerializer.Deserialize<ConditionDef>(written, ArcBox.Sdk.ArcJson.Options), ArcBox.Sdk.ArcJson.Options));
    }

    [Fact]
    public void Crit_when_adjacent_turns_an_adjacent_hit_into_a_crit()
    {
        var helpless = new ConditionDef(new ConditionId("helpless"), "Helpless", new StaticMods(CritWhenAdjacent: true), [], Ordinal.Set());
        // A seed whose first d20 hits AC 15 (+5) without being a natural 20.
        var seed = Enumerable.Range(1, 200).Select(s => (ulong)s).First(s => TestAbilities.Next(Rng.Seeded(s), 20)[0] is >= 10 and < 20);

        (ImmutableArray<BattleEvent> Events, int[] Dice) Attack(UnitSheet attacker, Tile at)
        {
            var battle = TestBattle.On(Field).Party("kara", attacker, at).Enemy("gob", Goblin, T(3, 1)).WithConditions(helpless);
            var (state, _) = battle.Start(seed);
            state = state.Give("gob", TestConditions.Lasting(helpless));
            var dice = TestAbilities.Next(state.Rng, 20, 8, 8);
            return (battle.Apply(state, new Use(U("kara"), new AbilityId("common.attack"), new Target(Unit: U("gob")))).Events, dice);
        }

        // Adjacent: the hit is a crit (the d20 itself wasn't), so the weapon dice roll twice.
        var (adjacent, dice) = Attack(Fighter, T(2, 1));
        Assert.False(TestConditions.FirstAttack(adjacent).Crit);
        Assert.Contains(new AttackResolved(U("kara"), U("gob"), Hit: true, Crit: true), adjacent);
        Assert.Equal(dice[1] + dice[2] + 3, adjacent.OfType<Damaged>().Single().Amount);

        // From range: an ordinary hit (a DEX 16 archer's +5 also reaches AC 15 on this d20).
        var (ranged, _) = Attack(Archer with { Scores = Archer.Scores.SetItem(Stat.Dex, 16) }, T(0, 1));
        Assert.Contains(new AttackResolved(U("kara"), U("gob"), Hit: true, Crit: false), ranged);
    }
}
