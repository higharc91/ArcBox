using System.Collections.Immutable;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// The base pack's ranger, paladin and monk features doing what their tooltips say, as default characters on a small
/// field (the content is the shipped JSON; the field and the foe are the test's).
/// </summary>
public sealed class MartialClassTests
{
    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static readonly BattleMap Field = AsciiMap.Parse("""
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0. 0. 0.
        """);

    private static RulesPack Rules { get; } = RulesPack.InMemory("martial", BasePackTests.Rules.Abilities.Values,
        BasePackTests.Rules.Passives.Values, BasePackTests.Rules.Conditions.Values, [Field], BasePackTests.Rules.Injuries.Values);

    /// <summary>A default human of <paramref name="cls"/> at <paramref name="level"/>, built from the base pack.</summary>
    private static UnitSheet Character(string cls, int level)
        => SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter(cls, "human", cls, null, level, BasePackTests.Rules), BasePackTests.Rules);

    /// <summary>A goblin that can't be missed except on a natural 1, with HP to spare.</summary>
    private static UnitSheet Dummy => SheetBuilder.ForMonster("goblin", BasePackTests.Rules) with { MaxHp = 300, Ac = 0 };

    private static BattleState Start(ulong seed, params (string Id, UnitSheet Sheet, Tile At)[] units)
    {
        var setup = new BattleSetup(Field, [.. units.Select(u => new UnitPlacement(U(u.Id), u.Sheet,
                u.Sheet.Side == Side.Party ? new PlayerControlled(new ProfileId("pr_" + u.Id)) : new AiControlled("brute"), u.At, []))],
            ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        return Battle.Start(setup, Rules, seed).State;
    }

    private static Resolution Use(BattleState state, string actor, string ability, string target)
    {
        var used = Battle.Apply(state, new Use(U(actor), new AbilityId(ability), new Target(Unit: U(target))), Rules);
        Assert.True(used.Rejected is null, $"{ability}: {used.Rejected?.Reason}");
        return used;
    }

    private static bool Has(BattleState state, string unit, string condition)
        => state.Units[U(unit)].Conditions.Any(c => c.Id.Value == condition);

    [Fact]
    public void Hunters_mark_adds_its_damage_to_every_hit_on_the_quarry()
    {
        var ranger = Character("ranger", 5) with { InitiativeBonus = 50 };
        var state = Start(4, ("ranger", ranger, T(0, 1)), ("gob", Dummy, T(5, 1)));

        var marked = Use(state, "ranger", "ranger.hunters_mark", "gob");
        Assert.True(Has(marked.State, "gob", "hunters_mark"));
        Assert.Equal(state.Units[U("ranger")].Resources["focus"] - 1, marked.State.Units[U("ranger")].Resources["focus"]);
        Assert.Equal(new AbilityId("ranger.hunters_mark"), marked.State.Units[U("ranger")].Concentrating);

        var shot = Use(marked.State, "ranger", RulesPack.CommonAttack.Id.Value, "gob");
        var hits = shot.Events.OfType<AttackResolved>().Count(a => a.Hit);
        Assert.Equal(2, shot.Events.OfType<AttackResolved>().Count());   // Extra Attack
        Assert.True(hits > 0);
        Assert.Equal(hits, shot.Events.Count(e => e == new PassiveTriggered(U("ranger"), "ranger.hunters_mark_bonus")));
    }

    [Fact]
    public void Divine_smite_spends_itself_on_the_next_melee_hit_on_its_target()
    {
        var paladin = Character("paladin", 5) with { InitiativeBonus = 50 };
        var state = Start(2, ("paladin", paladin, T(1, 1)), ("gob", Dummy, T(2, 1)));

        var readied = Use(state, "paladin", "paladin.divine_smite", "gob");
        Assert.True(Has(readied.State, "gob", "divine_smite"));
        Assert.Equal(state.Units[U("paladin")].Resources["oath"] - 1, readied.State.Units[U("paladin")].Resources["oath"]);

        var struck = Use(readied.State, "paladin", RulesPack.CommonAttack.Id.Value, "gob");
        Assert.Contains(struck.Events.OfType<AttackResolved>(), a => a.Hit);
        Assert.Single(struck.Events, e => e == new PassiveTriggered(U("paladin"), "paladin.smite_damage"));
        Assert.False(Has(struck.State, "gob", "divine_smite"));
    }

    [Fact]
    public void Aura_of_protection_covers_the_paladin_and_allies_within_2_tiles_from_the_start()
    {
        var state = Start(1,
            ("paladin", Character("paladin", 6), T(2, 1)),
            ("near", Character("fighter", 6), T(4, 1)),
            ("far", Character("rogue", 6), T(7, 2)),
            ("gob", Dummy, T(2, 0)));

        Assert.True(Has(state, "paladin", "aura_of_protection"));
        Assert.True(Has(state, "near", "aura_of_protection"));
        Assert.False(Has(state, "far", "aura_of_protection"));
        Assert.False(Has(state, "gob", "aura_of_protection"));
        Assert.Equal(2, Modifiers.For(state, U("near"), Rules).Saves![Stat.Wis]);

        // Below level 6 there is no aura.
        Assert.False(Has(Start(1, ("paladin", Character("paladin", 5), T(2, 1)), ("gob", Dummy, T(2, 0))), "paladin", "aura_of_protection"));
    }

    [Fact]
    public void Flurry_of_blows_strikes_twice_for_one_ki_and_martial_arts_once_for_free()
    {
        var monk = Character("monk", 2) with { InitiativeBonus = 50 };
        var state = Start(5, ("monk", monk, T(1, 1)), ("gob", Dummy, T(2, 1)));

        var flurry = Use(state, "monk", "monk.flurry_of_blows", "gob");
        Assert.Equal(2, flurry.Events.OfType<AttackResolved>().Count());
        var ki = state.Units[U("monk")].Resources["ki"];
        Assert.Equal(ki - 1, flurry.State.Units[U("monk")].Resources["ki"]);

        var free = Use(state, "monk", "monk.martial_arts", "gob");
        Assert.Single(free.Events.OfType<AttackResolved>());
        Assert.Equal(ki, free.State.Units[U("monk")].Resources["ki"]);
    }

    [Fact]
    public void Stunning_strike_stuns_on_a_failed_con_save()
    {
        var monk = Character("monk", 5) with { InitiativeBonus = 50 };
        var weak = Dummy with { Scores = Dummy.Scores.SetItem(Stat.Con, 1), SaveProficiencies = [] };
        var stunned = Enumerable.Range(1, 10).Select(seed => Use(Start((ulong)seed, ("monk", monk, T(1, 1)), ("gob", weak, T(2, 1))),
                "monk", "monk.stunning_strike", "gob"))
            .First(r => r.Events.OfType<AttackResolved>().Any(a => a.Hit));

        Assert.True(Has(stunned.State, "gob", "stunned"));
    }
}
