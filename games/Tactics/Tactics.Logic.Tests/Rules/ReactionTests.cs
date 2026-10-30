using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class ReactionTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static readonly AbilityId Attack = new("common.attack");
    private static readonly ReactionChoice Pass = new("pass", null, null);

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    /// <summary>A player fighter with two attacks per Attack action (an opportunity attack is still one).</summary>
    private static readonly UnitSheet Fighter = TestSheets.Fighter with { Attacks = 2 };

    /// <summary>A goblin with 30 HP that acts before the fighter (initiative 12 against 9).</summary>
    private static readonly UnitSheet Goblin = TestSheets.Goblin with { MaxHp = 30 };

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, ArcJson.Options);

    private static string Json(ImmutableArray<BattleEvent> events) => JsonSerializer.Serialize(events, ArcJson.Options);

    private static Rejection Rejected(TestBattle battle, BattleState state, Command command)
    {
        var resolution = Battle.Apply(state, command, battle.Rules);
        Assert.Same(state, resolution.State);
        Assert.Empty(resolution.Events);
        return Assert.IsType<Rejection>(resolution.Rejected);
    }

    private static ReactionOffer OpportunityOffer(string reactor, string mover)
        => new(U(reactor), [Pass, new ReactionChoice("opportunity", Attack, U(mover))], "leavesReach");

    /// <summary>The goblin (active) is next to the player fighter at (0, 1) and walks away east, two tiles.</summary>
    private static (TestBattle Battle, BattleState Paused, ImmutableArray<BattleEvent> Events) GoblinWalksAway(
        UnitSheet? goblin = null, ulong seed = 1)
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter, T(0, 1))
            .Enemy("gob", goblin ?? Goblin, T(1, 1));
        var (state, _) = battle.Start(seed);
        Assert.Equal(U("gob"), state.Active);
        var (paused, events) = battle.Apply(state, new Move(U("gob"), [T(1, 1), T(2, 1), T(3, 1)]));
        return (battle, paused, events);
    }

    [Fact]
    public void Leaving_reach_offers_player_reaction_and_pauses()
    {
        var (battle, paused, events) = GoblinWalksAway();

        var offer = Assert.IsType<ReactionOffer>(paused.Pending);
        Assert.Equal(Json<Pending>(OpportunityOffer("kara", "gob")), Json<Pending>(offer));
        Assert.Equal(Json([new ReactionOffered(offer)]), Json(events));

        // Nothing has moved or been spent yet; the rest of the move waits on the queue.
        var gob = paused.Units[U("gob")];
        Assert.Equal(T(1, 1), gob.Pos);
        Assert.Equal(4, gob.Economy.MoveLeft);
        Assert.NotEmpty(paused.Queue);
        Assert.Equal(U("gob"), paused.Active);

        Command[] others =
        [
            new EndTurn(U("gob")),
            new Move(U("gob"), [T(1, 1), T(1, 2)]),
            new Use(U("gob"), Attack, new Target(Unit: U("kara"))),
            new Delay(U("gob"), 1),
            new EndTurn(U("kara")),
        ];
        foreach (var command in others)
        {
            var rejection = Rejected(battle, paused, command);
            Assert.Equal(RejectCode.ReactionPending, rejection.Code);
            Assert.Equal("Answer the reaction first.", rejection.Reason);
        }
    }

    [Fact]
    public void React_attack_resolves_then_move_continues()
    {
        var (battle, paused, _) = GoblinWalksAway();

        // Seed 1: the attack d20 is 13; +3 STR +2 proficiency = 18 against AC 15: a hit for 1d8 + 3.
        var dice = TestAbilities.Next(paused.Rng, 20, 8);
        Assert.Equal(13, dice[0]);
        var damage = dice[1] + 3;

        var choice = new ReactionChoice("opportunity", Attack, U("gob"));
        var (after, events) = battle.Apply(paused, new React(U("kara"), choice));

        Assert.Collection(events,
            e => Assert.Equal(new Reacted(U("kara"), "opportunity"), e),
            e => Assert.Equal(new AbilityUsed(U("kara"), Attack, new Target(Unit: U("gob")), RulesPack.CommonAttack.Presentation), e),
            e =>
            {
                var rolled = Assert.IsType<Rolled>(e);
                Assert.Equal(U("kara"), rolled.Roller);
                Assert.Equal(18, rolled.Result.Total);
            },
            e => Assert.Equal(new AttackResolved(U("kara"), U("gob"), Hit: true, Crit: false), e),
            e => Assert.Equal(new Damaged(U("gob"), damage, "slashing", 30 - damage), e),
            e => Assert.Equal(Json<BattleEvent>(new Moved(U("gob"), [T(1, 1), T(2, 1)])), Json(e)),
            e => Assert.Equal(Json<BattleEvent>(new Moved(U("gob"), [T(2, 1), T(3, 1)])), Json(e)));

        Assert.Null(after.Pending);
        Assert.Empty(after.Queue);
        Assert.Equal(T(3, 1), after.Units[U("gob")].Pos);
        Assert.Equal(2, after.Units[U("gob")].Economy.MoveLeft);
        var kara = after.Units[U("kara")];
        Assert.False(kara.Economy.Reaction);
        Assert.True(kara.Economy.Action);                               // a reaction, not the Attack action
        Assert.Equal(U("gob"), after.Active);
    }

    [Fact]
    public void React_pass_clears_the_offer_and_the_move_continues()
    {
        var (battle, paused, _) = GoblinWalksAway();

        var (after, events) = battle.Apply(paused, new React(U("kara"), Pass));

        Assert.Equal(Json(
        [
            new Reacted(U("kara"), "pass"),
            new Moved(U("gob"), [T(1, 1), T(2, 1)]),
            new Moved(U("gob"), [T(2, 1), T(3, 1)]),
        ]), Json(events));
        Assert.Null(after.Pending);
        Assert.True(after.Units[U("kara")].Economy.Reaction);
        Assert.Equal(paused.Rng, after.Rng);
    }

    [Fact]
    public void Mover_dropped_by_the_opportunity_attack_stops()
    {
        // Two player reactors; kara's attack (seed 1: d20 13, a hit) drops the 1-HP goblin, so zed is never offered.
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter, T(0, 1))
            .Party("zed", TestSheets.Rogue, T(1, 0))
            .Enemy("gob", Goblin with { MaxHp = 1, InitiativeBonus = 10 }, T(1, 1));
        var (state, _) = battle.Start(seed: 1);
        var (paused, _) = battle.Apply(state, new Move(U("gob"), [T(1, 1), T(2, 1), T(3, 1)]));
        Assert.Equal(U("kara"), Assert.IsType<ReactionOffer>(paused.Pending).Reactor);

        var (after, events) = battle.Apply(paused, new React(U("kara"), new ReactionChoice("opportunity", Attack, U("gob"))));

        Assert.IsType<Damaged>(events[^1]);
        Assert.DoesNotContain(events, e => e is Moved or ReactionOffered);
        var gob = after.Units[U("gob")];
        Assert.Equal(0, gob.Hp);
        Assert.Equal(T(1, 1), gob.Pos);
        Assert.Equal(4, gob.Economy.MoveLeft);
        Assert.Empty(after.Queue);
        Assert.Null(after.Pending);
        Assert.True(after.Units[U("zed")].Economy.Reaction);
    }

    [Fact]
    public void Mover_dropped_on_an_allys_tile_returns_to_the_last_tile_it_held_alone()
    {
        // The goblin walks through its ally pal at (2, 1); leaving pal's tile provokes kara, whose hit drops it there.
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter, T(2, 0))
            .Enemy("gob", Goblin with { MaxHp = 1 }, T(1, 1))
            .Enemy("pal", Goblin, T(2, 1));
        var (state, _) = battle.Start(seed: 1);
        Assert.Equal(U("gob"), state.Active);

        var (paused, events) = battle.Apply(state, new Move(U("gob"), [T(1, 1), T(2, 1), T(3, 1), T(4, 1)]));
        Assert.Equal(Json<BattleEvent>(new Moved(U("gob"), [T(1, 1), T(2, 1)])), Json(events[0]));
        Assert.Equal(U("kara"), Assert.IsType<ReactionOffer>(paused.Pending).Reactor);

        var (after, reacted) = battle.Apply(paused, new React(U("kara"), new ReactionChoice("opportunity", Attack, U("gob"))));

        Assert.Equal(0, after.Units[U("gob")].Hp);
        Assert.Equal(Json<BattleEvent>(new Moved(U("gob"), [T(2, 1), T(1, 1)])), Json(reacted[^1]));
        Assert.Equal(T(1, 1), after.Units[U("gob")].Pos);
        Assert.Equal(T(2, 1), after.Units[U("pal")].Pos);
        Assert.DoesNotContain(after.Queue, s => s is MoveStep);
    }

    [Fact]
    public void Halted_move_leaves_no_move_steps_for_the_unit()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter, T(0, 1))
            .Enemy("gob", Goblin, T(1, 1));
        var (state, _) = battle.Start();
        state = state with { Units = state.Units.SetItem(U("gob"), state.Units[U("gob")] with { Hp = 0 }) };

        var w = new BattleWriter(state, battle.Rules);
        w.PushFront(
            new MoveStep(U("gob"), T(1, 1), T(2, 1), T(1, 1)),
            new MoveStep(U("gob"), T(2, 1), T(3, 1), T(2, 1)),
            new EndTurnStep(U("gob")),
            new MoveStep(U("gob"), T(3, 1), T(4, 1), T(3, 1)));
        w.PopFront()!.Run(w, battle.Rules);

        Assert.Equal([new EndTurnStep(U("gob"))], w.State.Queue);
        Assert.Empty(w.Events);
    }

    [Fact]
    public void Ai_reactor_takes_the_opportunity_attack_without_an_offer()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter with { InitiativeBonus = 10 }, T(0, 1))
            .Enemy("gob", Goblin, T(1, 1));
        var (state, _) = battle.Start();
        Assert.Equal(U("kara"), state.Active);

        var (after, events) = battle.Apply(state, new Move(U("kara"), [T(0, 1), T(0, 0)]));

        Assert.Equal(new Reacted(U("gob"), "opportunity"), events[0]);
        Assert.Equal(new AbilityUsed(U("gob"), Attack, new Target(Unit: U("kara")), RulesPack.CommonAttack.Presentation), events[1]);
        Assert.Equal(U("gob"), Assert.IsType<Rolled>(events[2]).Roller);
        Assert.Equal(Json<BattleEvent>(new Moved(U("kara"), [T(0, 1), T(0, 0)])), Json(events[^1]));
        Assert.DoesNotContain(events, e => e is ReactionOffered);
        Assert.Null(after.Pending);
        Assert.False(after.Units[U("gob")].Economy.Reaction);
        Assert.Equal(T(0, 0), after.Units[U("kara")].Pos);
    }

    [Fact]
    public void Disengage_prevents_offer()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter, T(0, 1))
            .Enemy("gob", Goblin, T(1, 1));
        var (state, _) = battle.Start();
        var disengaged = new ActiveCondition(new ConditionId("disengaged"), U("gob"), 1, false, 0, Stat.Con, false);
        state = state with { Units = state.Units.SetItem(U("gob"), state.Units[U("gob")] with { Conditions = [disengaged] }) };

        var (after, events) = battle.Apply(state, new Move(U("gob"), [T(1, 1), T(2, 1), T(3, 1)]));

        Assert.Equal(Json(
        [
            new Moved(U("gob"), [T(1, 1), T(2, 1)]),
            new Moved(U("gob"), [T(2, 1), T(3, 1)]),
        ]), Json(events));
        Assert.Null(after.Pending);
        Assert.True(after.Units[U("kara")].Economy.Reaction);
    }

    [Fact]
    public void Moving_within_reach_does_not_provoke()
    {
        var glaive = Fighter.Weapon with { Id = "glaive", Reach = true };
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter with { Weapon = glaive }, T(0, 1))
            .Enemy("gob", Goblin, T(1, 1));
        var (state, _) = battle.Start();

        // (2, 1) is still in reach (2 tiles in a line); (3, 1) is not.
        var (paused, events) = battle.Apply(state, new Move(U("gob"), [T(1, 1), T(2, 1), T(3, 1)]));

        Assert.Equal(2, events.Length);
        Assert.Equal(Json<BattleEvent>(new Moved(U("gob"), [T(1, 1), T(2, 1)])), Json(events[0]));
        Assert.IsType<ReactionOffered>(events[1]);
        Assert.Equal(T(2, 1), paused.Units[U("gob")].Pos);
    }

    [Fact]
    public void Reactors_are_offered_one_at_a_time_in_sorted_order()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter, T(0, 1))
            .Party("ari", TestSheets.Rogue, T(1, 0))
            .Enemy("pal", Goblin, T(1, 2))                              // the mover's ally: never offered
            .Enemy("gob", Goblin with { InitiativeBonus = 10 }, T(1, 1));
        var (state, _) = battle.Start();
        Assert.Equal(U("gob"), state.Active);

        var (first, firstEvents) = battle.Apply(state, new Move(U("gob"), [T(1, 1), T(2, 1), T(3, 1)]));
        Assert.Equal(Json<Pending>(OpportunityOffer("ari", "gob")), Json(first.Pending));
        Assert.Single(firstEvents);

        // Only the offered unit may answer, and only with an offered choice.
        Assert.Equal(RejectCode.NotYourTurn, Rejected(battle, first, new React(U("kara"), Pass)).Code);
        Assert.Equal(RejectCode.InvalidTarget,
            Rejected(battle, first, new React(U("ari"), new ReactionChoice("fireball", null, null))).Code);

        var (second, secondEvents) = battle.Apply(first, new React(U("ari"), Pass));
        var offer = OpportunityOffer("kara", "gob");
        Assert.Equal(Json([new Reacted(U("ari"), "pass"), new ReactionOffered(offer)]), Json(secondEvents));
        Assert.Equal(T(1, 1), second.Units[U("gob")].Pos);

        var (done, doneEvents) = battle.Apply(second, new React(U("kara"), Pass));
        Assert.Equal(new Reacted(U("kara"), "pass"), doneEvents[0]);
        Assert.Equal(2, doneEvents.Count(e => e is Moved));
        Assert.Null(done.Pending);
        Assert.Equal(T(3, 1), done.Units[U("gob")].Pos);
    }

    [Fact]
    public void Spent_reaction_is_not_offered()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter, T(0, 1))
            .Enemy("gob", Goblin, T(1, 1));
        var (state, _) = battle.Start();
        var kara = state.Units[U("kara")];
        state = state with { Units = state.Units.SetItem(kara.Id, kara with { Economy = kara.Economy with { Reaction = false } }) };

        var (after, events) = battle.Apply(state, new Move(U("gob"), [T(1, 1), T(2, 1)]));

        Assert.Null(after.Pending);
        Assert.IsType<Moved>(Assert.Single(events));
    }

    [Fact]
    public void React_without_pending_rejected_NothingToReact()
    {
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter, T(0, 1))
            .Enemy("gob", Goblin, T(4, 1));
        var (state, _) = battle.Start();

        var rejection = Rejected(battle, state, new React(U("kara"), Pass));
        Assert.Equal(RejectCode.NothingToReact, rejection.Code);
        Assert.Equal("There's nothing to react to.", rejection.Reason);
    }

    [Fact]
    public void Pending_survives_serialisation()
    {
        var (battle, paused, _) = GoblinWalksAway();

        var json = JsonSerializer.Serialize(paused, ArcJson.Options);
        var loaded = JsonSerializer.Deserialize<BattleState>(json, ArcJson.Options)!;
        Assert.Equal(json, JsonSerializer.Serialize(loaded, ArcJson.Options));
        Assert.Contains("\"$type\":\"ReactionOffer\"", json);
        Assert.Contains("\"$type\":\"MoveStep\"", json);

        var react = new React(U("kara"), new ReactionChoice("opportunity", Attack, U("gob")));
        var (original, originalEvents) = battle.Apply(paused, react);
        var (resumed, resumedEvents) = battle.Apply(loaded, react);

        Assert.Equal(Json(originalEvents), Json(resumedEvents));
        Assert.Contains(resumedEvents, e => e is Moved);
        Assert.Equal(JsonSerializer.Serialize(original, ArcJson.Options), JsonSerializer.Serialize(resumed, ArcJson.Options));
    }

    [Fact]
    public void Reaction_steps_and_events_round_trip()
    {
        var trigger = new ReactionTrigger(ReactionTriggerKind.LeavesReach, U("gob"), null, null, null, T(2, 1));
        ImmutableList<Step> queue =
        [
            new MoveStep(U("gob"), T(1, 1), T(2, 1), T(1, 1)),
            new MoveStep(U("gob"), T(1, 1), T(2, 1), T(1, 1), Checked: true),
            new OfferReactionStep(U("kara"), trigger),
            new ReactionStep(U("kara"), new ReactionChoice("opportunity", Attack, U("gob"))),
        ];
        ImmutableArray<BattleEvent> events =
        [
            new Moved(U("gob"), [T(1, 1), T(2, 1)]),
            new Pushed(U("gob"), T(2, 1), T(3, 1)),
            new Fell(U("gob"), 4, 3, false),
            new Collided(U("gob"), null, 2),
            new Teleported(U("kara"), T(0, 1), T(4, 1)),
            new ReactionOffered(OpportunityOffer("kara", "gob")),
            new Reacted(U("kara"), "pass"),
        ];

        var queueJson = JsonSerializer.Serialize(queue, ArcJson.Options);
        var eventJson = JsonSerializer.Serialize(events, ArcJson.Options);

        Assert.Contains("\"kind\":\"leavesReach\"", queueJson);
        Assert.Equal(queueJson, JsonSerializer.Serialize(JsonSerializer.Deserialize<ImmutableList<Step>>(queueJson, ArcJson.Options), ArcJson.Options));
        Assert.Equal(eventJson, JsonSerializer.Serialize(JsonSerializer.Deserialize<ImmutableArray<BattleEvent>>(eventJson, ArcJson.Options), ArcJson.Options));
    }

    [Fact]
    public void OffersFor_lists_reaction_abilities_by_their_reactsTo_tag()
    {
        var shield = new AbilityDef(new AbilityId("test.shield"), "Shield", Tier: 1, new CostDef(Reaction: 1),
            new TargetDef(TargetKind.Self, Range: 0, Los: false, new AreaDef(AreaShape.Self)), [],
            Ai: new AiHints(["defense", "reactsTo:beforeHit"]));
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter with { Abilities = [shield.Id] }, T(0, 1))
            .Party("ari", TestSheets.Rogue with { Abilities = [shield.Id] }, T(0, 2))
            .Enemy("gob", Goblin, T(1, 1))
            .With(shield);
        var (state, _) = battle.Start();
        var w = new BattleWriter(state, battle.Rules);

        var offers = Reactions.OffersFor(w, new ReactionTrigger(ReactionTriggerKind.BeforeHit, U("gob"), U("kara"), Attack, null), battle.Rules);

        var offer = Assert.Single(offers);
        Assert.Equal(Json<Pending>(new ReactionOffer(U("kara"), [Pass, new ReactionChoice("test.shield", shield.Id, U("kara"))], "beforeHit")),
            Json<Pending>(offer));

        // A leavesReach trigger doesn't match the tag, and only the opportunity attack is offered.
        var leaving = Reactions.OffersFor(w, new ReactionTrigger(ReactionTriggerKind.LeavesReach, U("gob"), null, null, null, T(2, 1)), battle.Rules);
        Assert.Equal(Json<Pending>(OpportunityOffer("kara", "gob")), Json<Pending>(Assert.Single(leaving)));

        // No reaction left: nothing.
        var kara = w.Unit(U("kara"));
        w.Set(kara with { Economy = kara.Economy with { Reaction = false } });
        Assert.Empty(Reactions.OffersFor(w, new ReactionTrigger(ReactionTriggerKind.BeforeHit, U("gob"), U("kara"), Attack, null), battle.Rules));
    }

    /// <summary>Shield: a tier-1 reaction spell that gives the caster +5 AC (the "shielded" condition) for a round.</summary>
    private static readonly ConditionDef Shielded = new(new ConditionId("shielded"), "Shielded", new StaticMods(Ac: 5), [],
        Ordinal.Set("magical"));

    private static readonly AbilityDef ShieldSpell = new(new AbilityId("test.shield"), "Shield", Tier: 1, new CostDef(Reaction: 1),
        new TargetDef(TargetKind.Self, Range: 0, Los: false, new AreaDef(AreaShape.Self)), [new ApplyEffect(Shielded.Id, 1)],
        Ai: new AiHints(["defense", "reactsTo:beforeHit"]));

    [Fact]
    public void Shield_before_hit_turns_a_hit_into_a_miss()
    {
        // The goblin (DEX +2, +2 proficiency) hits the fighter's AC 18 on 14-18, which AC 23 turns into a miss.
        var seed = Enumerable.Range(1, 200).Select(s => (ulong)s).First(s => TestAbilities.Next(Rng.Seeded(s), 20)[0] is >= 14 and <= 18);
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter with { Abilities = [ShieldSpell.Id] }, T(0, 1))
            .Enemy("gob", Goblin, T(1, 1))
            .With(ShieldSpell).WithConditions(Shielded);
        var (state, _) = battle.Start(seed);
        Assert.Equal(U("gob"), state.Active);

        var (paused, events) = battle.Apply(state, new Use(U("gob"), Attack, new Target(Unit: U("kara"))));

        Assert.True(Assert.IsType<Rolled>(events[1]).Result.Success);
        var shield = new ReactionChoice("test.shield", ShieldSpell.Id, U("kara"));
        var offer = new ReactionOffer(U("kara"), [Pass, shield], "beforeHit");
        Assert.Equal(Json<BattleEvent>(new ReactionOffered(offer)), Json(events[2]));
        Assert.Equal(Json<Pending>(offer), Json<Pending>(paused.Pending!));
        Assert.DoesNotContain(events, e => e is Damaged);

        // Taking the reaction raises AC before the hit lands: now a miss, and no damage.
        var (after, reacted) = battle.Apply(paused, new React(U("kara"), shield));
        Assert.Equal<BattleEvent>(
        [
            new Reacted(U("kara"), "test.shield"),
            new AbilityUsed(U("kara"), ShieldSpell.Id, new Target(Unit: U("kara")), null),
            new ConditionApplied(U("kara"), Shielded.Id),
            new AttackResolved(U("gob"), U("kara"), Hit: false, Crit: false),
        ], reacted);
        Assert.Equal(12, after.Units[U("kara")].Hp);
        Assert.False(after.Units[U("kara")].Economy.Reaction);
        Assert.Null(after.Pending);
        Assert.Empty(after.Queue);

        // The pause (the rolled attack waiting behind the offer) survives a save and resumes identically.
        var json = Json(paused);
        Assert.Contains("\"$type\":\"AttackStep\"", json);
        var loaded = JsonSerializer.Deserialize<BattleState>(json, ArcJson.Options)!;
        Assert.Equal(json, Json(loaded));
        var fromSave = Battle.Apply(loaded, new React(U("kara"), shield), battle.Rules);
        Assert.Equal(Json(after), Json(fromSave.State));
        Assert.Equal(Json(reacted), Json(fromSave.Events));

        // Passing lets the hit land.
        var (_, passed) = battle.Apply(paused, new React(U("kara"), Pass));
        Assert.Equal(new AttackResolved(U("gob"), U("kara"), Hit: true, Crit: false), passed[1]);
        Assert.IsType<Damaged>(passed[^1]);

        // An AI target with the same reaction isn't offered it (Task 15 decides): the attack resolves at once.
        var reversed = TestBattle.On(Field)
            .Party("kara", Fighter with { InitiativeBonus = 10 }, T(0, 1))
            .Enemy("gob", Goblin with { Abilities = [ShieldSpell.Id] }, T(1, 1))
            .With(ShieldSpell).WithConditions(Shielded);
        (state, _) = reversed.Start(seed);
        (after, events) = reversed.Apply(state, new Use(U("kara"), Attack, new Target(Unit: U("gob"))));
        Assert.Null(after.Pending);
        Assert.DoesNotContain(events, e => e is ReactionOffered or Reacted);
    }

    [Fact]
    public void Spell_cast_offers_reaction_before_its_effects()
    {
        AbilityDef Hex(string id, int tier) => new(new AbilityId(id), id, tier, new CostDef(Action: 1),
            new TargetDef(TargetKind.Enemy, Range: 6, Los: false, new AreaDef(AreaShape.Single)),
            [new DamageEffect(TestAbilities.Flat("3"), "force", [])]);
        var hex = Hex("test.hex", 1);
        var jinx = Hex("test.jinx", 0);                                  // a cantrip: no spellCast window
        var counter = new AbilityDef(new AbilityId("test.counter"), "Counter", 3, new CostDef(Reaction: 1),
            new TargetDef(TargetKind.Enemy, Range: 6, Los: false, new AreaDef(AreaShape.Single)), [],
            Ai: new AiHints(["reactsTo:spellCast"]));
        var battle = TestBattle.On(Field)
            .Party("kara", Fighter with { Abilities = [counter.Id] }, T(0, 1))
            .Enemy("gob", Goblin with { Abilities = [hex.Id, jinx.Id] }, T(4, 1))
            .With(hex, jinx, counter);
        var (state, _) = battle.Start();

        var (paused, events) = battle.Apply(state, new Use(U("gob"), hex.Id, new Target(Unit: U("kara"))));
        var offer = new ReactionOffer(U("kara"), [Pass, new ReactionChoice("test.counter", counter.Id, U("gob"))], "spellCast");
        Assert.Equal(Json(
        [
            new AbilityUsed(U("gob"), hex.Id, new Target(Unit: U("kara")), null),
            new ReactionOffered(offer),
        ]), Json(events));
        Assert.False(paused.Units[U("gob")].Economy.Action);              // the cost is paid; the effects wait

        var (_, passed) = battle.Apply(paused, new React(U("kara"), Pass));
        Assert.Equal(new Damaged(U("kara"), 3, "force", 9), passed[^1]);

        (_, events) = battle.Apply(state, new Use(U("gob"), jinx.Id, new Target(Unit: U("kara"))));
        Assert.DoesNotContain(events, e => e is ReactionOffered);
    }
}
