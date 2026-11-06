using System.Collections.Immutable;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>
/// #107: an ability that spends a resource on a weapon attack's rider lands it once, however many attacks the unit makes
/// (<see cref="AbilityDef.RiderOnce"/>); the shipped content says so wherever a unit with Extra Attack can use one.
/// </summary>
public sealed class RiderOnceTests
{
    private static RulesPack Base => BasePackTests.Rules;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static readonly BattleMap Field = AsciiMap.Parse("""
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """);

    private static RulesPack Rules { get; } = RulesPack.InMemory("riders", Base.Abilities.Values, Base.Passives.Values,
        Base.Conditions.Values, [Field], Base.Injuries.Values);

    private static UnitSheet Character(string cls, int level, string? subclass = null)
        => SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter(cls, "human", cls, subclass, level, Base), Base) with { InitiativeBonus = 50 };

    /// <summary>A goblin hit by anything but a natural 1, with HP to spare and no save worth the name.</summary>
    private static UnitSheet Dummy => SheetBuilder.ForMonster("goblin", Base) with
    {
        MaxHp = 400, Ac = 0, Scores = TestSheets.Scores(1, 1, 1, 1, 1, 1), SaveProficiencies = [],
    };

    private static ImmutableArray<BattleEvent> Use(ulong seed, UnitSheet user, string ability)
    {
        var setup = new BattleSetup(Field, [
                new UnitPlacement(U("hero"), user, new PlayerControlled(new ProfileId("pr_hero")), T(1, 1), []),
                new UnitPlacement(U("gob"), Dummy with { Side = Side.Enemy }, new AiControlled("brute"), T(2, 1), []),
            ], ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        var state = Battle.Start(setup, Rules, seed).State;
        var used = Battle.Apply(state, new Use(U("hero"), new AbilityId(ability), new Target(Unit: U("gob"))), Rules);
        Assert.True(used.Rejected is null, $"{ability}: {used.Rejected?.Reason}");
        return used.Events;
    }

    [Fact]
    public void Trip_attack_with_two_attacks_makes_at_most_one_prone_save()
    {
        var fighter = Character("fighter", 5);
        Assert.Equal(2, fighter.Attacks);
        Assert.Contains(new AbilityId("fighter.trip_attack"), fighter.Abilities);
        var bothHit = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var events = Use(seed, fighter, "fighter.trip_attack");
            var hits = events.OfType<AttackResolved>().Count(a => a.Hit);
            Assert.Equal(2, events.OfType<AttackResolved>().Count());
            Assert.Equal(hits > 0 ? 1 : 0, events.Count(e => e is Rolled { Kind: RollKind.Save }));
            bothHit += hits == 2 ? 1 : 0;
        }
        Assert.True(bothHit > 0);
    }

    [Fact]
    public void Quivering_palm_applies_its_4d10_once()
    {
        var monk = Character("monk", 10, "monk.open_hand") with { Resources = Ordinal.Map(("ki", 10)) };
        Assert.Equal(2, monk.Attacks);
        var many = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var events = Use(seed, monk, "open_hand.quivering_palm");
            var hits = events.OfType<AttackResolved>().Count(a => a.Hit);
            Assert.Equal(monk.Attacks, events.OfType<AttackResolved>().Count());
            Assert.Equal(hits > 0 ? 1 : 0, events.Count(e => e is Rolled { Kind: RollKind.Save }));
            Assert.True(events.Count(e => e is Damaged { Type: "force" }) <= 1);
            many += hits > 1 ? 1 : 0;
        }
        Assert.True(many > 0);
    }

    /// <summary>The most attacks any unit that can know each ability makes: classes (with subclasses), races and monsters.</summary>
    private static Dictionary<AbilityId, int> MostAttacks()
    {
        var most = new Dictionary<AbilityId, int>();
        void Note(IEnumerable<AbilityId> abilities, int attacks)
        {
            foreach (var id in abilities)
                most[id] = Math.Max(most.GetValueOrDefault(id), attacks);
        }
        static IEnumerable<AbilityId> Granted(IEnumerable<FeatureRef> features)
            => features.SelectMany(f => f.Ability is { } a ? [a] : f.From.IsDefault ? [] : f.From.Select(id => new AbilityId(id)));
        static int Attacks(IEnumerable<FeatureRef> features) => features.Select(f => Math.Max(f.Attacks ?? 1, f.RangedAttacks ?? 1)).DefaultIfEmpty(1).Max();

        var classAttacks = 1;
        foreach (var cls in Base.Classes.Values)
        {
            var own = cls.Features.Values.SelectMany(f => f).ToArray();
            classAttacks = Math.Max(classAttacks, Attacks(own));
            Note(Granted(own), Attacks(own));
            foreach (var sub in cls.Subclasses)
            {
                var all = own.Concat(sub.Features.Values.SelectMany(f => f)).ToArray();
                Note(Granted(all), Attacks(all));
            }
        }
        foreach (var race in Base.Races.Values)
            if (race.Ability is { } racial)
                Note([racial], classAttacks);
        foreach (var monster in Base.Monsters.Values)
            Note(monster.Abilities.Concat(monster.Phases.IsDefault ? [] : monster.Phases.SelectMany(p => p.Abilities)), monster.Attacks);
        return most;
    }

    [Fact]
    public void A_resource_costed_weapon_attack_rider_a_unit_with_extra_attacks_can_use_lands_once()
    {
        var most = MostAttacks();
        var missing = Base.Abilities.Values
            .Where(a => a.IsWeaponAttack && (a.Cost.Resource is { Count: > 0 } || a.Cost.Uses is not null) && !a.RiderOnce
                        && most.GetValueOrDefault(a.Id) > 1)
            .Select(a => a.Id.Value).Order(StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0,
            "These weapon attacks spend a resource and can be made with more than one attack, so their rider would repeat: set \"riderOnce\": true on "
            + string.Join(", ", missing));
    }

    [Fact]
    public void Rider_once_is_only_on_weapon_attacks_with_an_attack_roll()
        => Assert.All(Base.Abilities.Values.Where(a => a.RiderOnce), a =>
        {
            Assert.True(a.IsWeaponAttack, a.Id.Value);
            Assert.NotEmpty(Strikes.Plain(a));
        });
}
