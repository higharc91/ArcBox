using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// How AI-controlled units react (spec §5.5, <see cref="Ai.React"/>): an opportunity attack when its expected value is above
/// 0; a defensive reaction when it turns the pending hit into a miss or saves at least 25% of max HP; otherwise pass.
/// </summary>
public sealed class AiReactionTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static RulesPack Base => BasePackTests.Rules;

    private static UnitId U(string id) => new(id);

    private static AbilityDef A(string id) => Base.Abilities[new AbilityId(id)];

    private static Tile T(int x, int y) => new(x, y);

    private static readonly AbilityId Attack = RulesPack.CommonAttack.Id;

    private static TestBattle On() => TestBattle.On(Field).WithAiProfiles(Base.AiProfiles.Values);

    /// <summary>The offer <paramref name="reactor"/> would get for <paramref name="trigger"/>.</summary>
    private static ReactionOffer OfferTo(string reactor, BattleState state, ReactionTrigger trigger, RulesPack rules)
        => Reactions.OffersFor(new BattleWriter(state, rules), trigger, rules).Single(o => o.Reactor == U(reactor));

    /// <summary>A d20 roll of <paramref name="d20"/> + <paramref name="modifier"/> against <paramref name="vs"/>: a hit.</summary>
    private static RollResult Hit(int d20, int modifier, int vs)
        => new([d20], d20, modifier, d20 + modifier, vs, Success: true, Crit: d20 == 20, Fumble: false);

    // ---- Opportunity attacks ----------------------------------------------------------------------------------------

    [Fact]
    public void Takes_opportunity_attack()
    {
        TestBattle Battle(UnitSheet kara) => On()
            .Party("kara", kara with { InitiativeBonus = 10 }, T(0, 1))
            .Enemy("gob", TestSheets.Goblin with { MaxHp = 30 }, T(1, 1), new AiControlled("brute"));
        var leaving = new ReactionTrigger(ReactionTriggerKind.LeavesReach, U("kara"), null, null, null, T(0, 0));

        // Kara steps out of the goblin's reach: its swing is worth something, so it takes it.
        var battle = Battle(TestSheets.Fighter);
        var (state, _) = battle.Start();
        var offer = OfferTo("gob", state, leaving, battle.Rules);
        Assert.Equal(Reactions.Opportunity, Ai.React(state, offer, battle.Rules).Id);
        var (after, events) = battle.Apply(state, new Move(U("kara"), [T(0, 1), T(0, 0)]));
        Assert.Equal(new Reacted(U("gob"), Reactions.Opportunity), events[0]);
        Assert.False(after.Units[U("gob")].Economy.Reaction);

        // Immune to its scimitar, she isn't worth the swing (EV 0): it passes, silently, and keeps its Reaction.
        var immune = Battle(TestSheets.Fighter with { Immune = Ordinal.Set("slashing") });
        (state, _) = immune.Start();
        offer = OfferTo("gob", state, leaving, immune.Rules);
        Assert.Equal(Reactions.Pass, Ai.React(state, offer, immune.Rules).Id);
        (after, events) = immune.Apply(state, new Move(U("kara"), [T(0, 1), T(0, 0)]));
        Assert.DoesNotContain(events, e => e is Reacted or ReactionOffered or Rolled);
        Assert.True(after.Units[U("gob")].Economy.Reaction);
        Assert.Equal(T(0, 0), after.Units[U("kara")].Pos);
    }

    // ---- Shield -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// An AI mage (AC 12, 40 HP, Shield and 1 Mana) next to Kara, a player fighter (+5 to hit, longsword 1d8+3), whose turn
    /// it is. <paramref name="seed"/> picks her first d20.
    /// </summary>
    private static (TestBattle Battle, BattleState State) MageAttacked(ulong seed = 1)
    {
        var shield = A("wizard.shield");
        var mage = TestSheets.Cleric with
        {
            Ac = 12, MaxHp = 40, CastingStat = "Int", Resources = Ordinal.Map(("mana", 1)), Abilities = [shield.Id], AiProfile = "caster",
        };
        var battle = On().With(shield)
            .Party("kara", TestSheets.Fighter with { InitiativeBonus = 10 }, T(0, 1))
            .Enemy("mage", mage, T(1, 1), new AiControlled("caster"));
        return (battle, battle.Start(seed).State);
    }

    private static ulong SeedRolling(Func<int, bool> d20)
        => Enumerable.Range(1, 500).Select(s => (ulong)s).First(s => d20(TestAbilities.Next(Rng.Seeded(s), 20)[0]));

    [Fact]
    public void Shield_when_it_turns_hit_to_miss()
    {
        // A 15 against AC 12 hits by 3: +5 AC turns it into a miss.
        var (battle, state) = MageAttacked();
        var trigger = new ReactionTrigger(ReactionTriggerKind.BeforeHit, U("kara"), U("mage"), Attack, Hit(10, 5, 12));
        var choice = Ai.React(state, OfferTo("mage", state, trigger, battle.Rules), battle.Rules);
        Assert.Equal("wizard.shield", choice.Id);

        // In play: Kara rolls 8-11 (13-16 against AC 12), the mage casts Shield without being asked and the hit misses.
        var (rolled, start) = MageAttacked(SeedRolling(d20 => d20 is >= 8 and <= 11));
        var (after, events) = rolled.Apply(start, new Use(U("kara"), Attack, new Target(Unit: U("mage"))));
        Assert.True(Assert.IsType<Rolled>(events[1]).Result.Success);
        Assert.DoesNotContain(events, e => e is ReactionOffered);
        Assert.Contains(new Reacted(U("mage"), "wizard.shield"), events);
        Assert.Contains(new AttackResolved(U("kara"), U("mage"), Hit: false, Crit: false), events);
        Assert.Equal((40, 0), (after.Units[U("mage")].Hp, after.Units[U("mage")].Resources["mana"]));
        Assert.Null(after.Pending);
    }

    [Fact]
    public void Passes_when_shield_wouldnt_matter()
    {
        var (battle, state) = MageAttacked();
        ReactionChoice React(RollResult roll)
        {
            var trigger = new ReactionTrigger(ReactionTriggerKind.BeforeHit, U("kara"), U("mage"), Attack, roll);
            return Ai.React(state, OfferTo("mage", state, trigger, battle.Rules), battle.Rules);
        }

        // Hit by 5 or more (+5 AC still hits), or a natural 20: Shield would change nothing, and a longsword (about 7.5,
        // or 12 on a crit) is well under a quarter of the mage's 40 HP.
        Assert.Equal(Reactions.Pass, React(Hit(12, 5, 12)).Id);
        Assert.Equal(Reactions.Pass, React(Hit(15, 5, 12)).Id);
        Assert.Equal(Reactions.Pass, React(Hit(20, 5, 12)).Id);

        // In play: Kara rolls 12-19, the mage keeps its Mana and Reaction and the hit lands.
        var (rolled, start) = MageAttacked(SeedRolling(d20 => d20 is >= 12 and <= 19));
        var (after, events) = rolled.Apply(start, new Use(U("kara"), Attack, new Target(Unit: U("mage"))));
        Assert.DoesNotContain(events, e => e is Reacted or ReactionOffered);
        Assert.Contains(new AttackResolved(U("kara"), U("mage"), Hit: true, Crit: false), events);
        Assert.Equal(1, after.Units[U("mage")].Resources["mana"]);
        Assert.True(after.Units[U("mage")].Economy.Reaction);
        Assert.True(after.Units[U("mage")].Hp < 40);
    }

    [Fact]
    public void A_saved_offer_is_answered_the_same_after_reloading()
    {
        // A player's mage (Shield, 1 Mana) is hit by a goblin roll of 8-12 (12-16 against AC 12): the battle pauses on the
        // offer. Saved and reloaded, the offer still carries its trigger, so the adapter's fallback (Ai.React on the pending
        // offer) answers it as before: Shield, and the hit misses.
        var shield = A("wizard.shield");
        var mage = TestSheets.Cleric with
        {
            Ac = 12, MaxHp = 40, CastingStat = "Int", Resources = Ordinal.Map(("mana", 1)), Abilities = [shield.Id],
        };
        var battle = On().With(shield)
            .Party("mage", mage, T(0, 1))
            .Enemy("gob", TestSheets.Goblin, T(1, 1), new AiControlled("brute"));
        var (state, _) = battle.Start(SeedRolling(d20 => d20 is >= 8 and <= 12));
        var (paused, _) = battle.Apply(state, new Use(U("gob"), Attack, new Target(Unit: U("mage"))));
        var offer = Assert.IsType<ReactionOffer>(paused.Pending);
        Assert.Equal(ReactionTriggerKind.BeforeHit, offer.Cause?.Kind);

        var json = JsonSerializer.Serialize(paused, ArcJson.Options);
        var loaded = JsonSerializer.Deserialize<BattleState>(json, ArcJson.Options)!;
        Assert.Equal(json, JsonSerializer.Serialize(loaded, ArcJson.Options));
        var before = Ai.React(paused, offer, battle.Rules);
        var after = Ai.React(loaded, Assert.IsType<ReactionOffer>(loaded.Pending), battle.Rules);

        Assert.Equal("wizard.shield", before.Id);
        Assert.Equal(before, after);
        var (_, events) = battle.Apply(loaded, new React(U("mage"), after));
        Assert.Contains(new AttackResolved(U("gob"), U("mage"), Hit: false, Crit: false), events);
    }

    // ---- Damage-reducing reactions ----------------------------------------------------------------------------------

    [Fact]
    public void Uncanny_dodge_when_it_saves_a_quarter_of_max_hp()
    {
        // Uncanny Dodge at level 5: 1d6 + 5 temporary HP (8.5 on average) against a rogue with 32 HP (a quarter is 8).
        var dodge = A("rogue.uncanny_dodge");
        var rogue = TestSheets.Rogue with { Level = 5, MaxHp = 32, Abilities = [dodge.Id], AiProfile = "skirmisher" };
        TestBattle Battle(string weapon) => On().With(dodge)
            .Party("kara", TestSheets.Fighter with
            {
                InitiativeBonus = 10, Weapon = TestSheets.Fighter.Weapon with { Damage = Dice.Parse(weapon) },
            }, T(0, 1))
            .Enemy("vex", rogue, T(1, 1), new AiControlled("skirmisher"));
        ReactionChoice React(string weapon)
        {
            var battle = Battle(weapon);
            var state = battle.Start().State;
            var trigger = new ReactionTrigger(ReactionTriggerKind.BeforeHit, U("kara"), U("vex"), Attack, Hit(16, 5, 14));
            return Ai.React(state, OfferTo("vex", state, trigger, battle.Rules), battle.Rules);
        }

        // A longsword hit (1d8 + 3, 7.5) is less than 8 HP saved: pass. A greataxe-sized blow (2d12 + 3, 16) loses 8.5 of
        // it to the temporary HP: take it.
        Assert.Equal(Reactions.Pass, React("1d8").Id);
        Assert.Equal("rogue.uncanny_dodge", React("2d12").Id);
    }

    // ---- Counterspell -----------------------------------------------------------------------------------------------

    [Fact]
    public void Counterspells_a_spell_worth_more_than_its_cost()
    {
        // A player cleric's Guiding Bolt (4d6) at a goblin with 7 HP, within range of an AI mage with Counterspell.
        var bolt = A("cleric.guiding_bolt");
        var counter = A("wizard.counterspell");
        var mage = TestSheets.Cleric with
        {
            Level = 5, CastingStat = "Int", Scores = TestSheets.Scores(8, 14, 12, 16, 10, 10),
            Resources = Ordinal.Map(("mana", 11)), Abilities = [counter.Id],
        };
        var battle = TestBattle.On("""
            0. 0. 0. 0. 0. 0. 0. 0.
            0. 0. 0. 0. 0. 0. 0. 0.
            """).WithAiProfiles(Base.AiProfiles.Values).With(bolt, counter)
            .WithConditions(Base.Conditions[new ConditionId("lit")])
            .Party("priest", TestSheets.Cleric.Knowing(bolt) with { InitiativeBonus = 10, Resources = Ordinal.Map(("devotion", 2)) }, T(0, 0))
            .Enemy("gob", TestSheets.Goblin, T(3, 0))
            .Enemy("mage", mage, T(5, 1), new AiControlled("caster"));
        var (state, _) = battle.Start();

        var (after, events) = battle.Apply(state, new Use(U("priest"), bolt.Id, new Target(Unit: U("gob"))));

        Assert.Contains(new Reacted(U("mage"), "wizard.counterspell"), events);
        Assert.Contains(events, e => e is Countered);
        Assert.Equal(7, after.Units[U("gob")].Hp);
        Assert.Equal(8, after.Units[U("mage")].Resources["mana"]);
    }
}
