using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class SupportTargetingTests
{
    private static RulesPack Base => BasePackTests.Rules;
    private static UnitId U(string id) => new(id);
    private static Tile T(int x, int y) => new(x, y);
    private const string Field = "0. 0. 0. 0. 0. 0. 0. 0. 0.\n0. 0. 0. 0. 0. 0. 0. 0. 0.\n0. 0. 0. 0. 0. 0. 0. 0. 0.\n0. 0. 0. 0. 0. 0. 0. 0. 0.";

    private static (TestBattle Battle, BattleState State) Start(string ability, string cls, bool enemyCaster = false)
    {
        var id = new AbilityId(ability);
        var actor = SheetBuilder.ForCharacter(SheetBuilder.DefaultCharacter("Caster", "human", cls, null, 10, Base), Base)
            with { InitiativeBonus = 100, Abilities = [id], Passives = [], MaxHp = 100 };
        var other = TestSheets.Fighter with { InitiativeBonus = -100, MaxHp = 100, Passives = [], Ac = 10 };
        var battle = TestBattle.On(Field).With(Base.Abilities[id]).WithConditions([.. Base.Conditions.Values]);
        if (enemyCaster)
            battle.Enemy("caster", actor, T(1, 1)).Enemy("ally", other, T(2, 1))
                .Party("foe", other, T(1, 2)).Enemy("outside", other, T(8, 1));
        else
            battle.Party("caster", actor, T(1, 1)).Party("ally", other, T(2, 1))
                .Enemy("foe", other, T(1, 2)).Party("outside", other, T(8, 1));
        var state = battle.Start().State;
        state = state with { Units = state.Units.ToImmutableSortedDictionary(p => p.Key, p => p.Value with { Hp = 20 }) };
        return (battle, state);
    }

    [Theory]
    [InlineData("goblin_boss.warcry", true)]
    [InlineData("goblin_boss.rally", true)]
    [InlineData("bandit_captain.rally_cry", true)]
    [InlineData("cult_leader.fanatic_zeal", true)]
    [InlineData("mother_skarrow.old_song", true)]
    [InlineData("warden.guardian_tree", false)]
    public void Rally_buffs_apply_only_to_the_casters_side_and_stay_in_range(string ability, bool enemyCaster)
    {
        var (battle, state) = Start(ability, "ranger", enemyCaster);
        var (after, events) = battle.Apply(state, new Use(U("caster"), new(ability), new Target(Unit: U("caster"))));
        var buffed = events.OfType<ConditionApplied>().Select(e => e.Unit).Distinct().Order().ToArray();
        Assert.Equal([U("ally"), U("caster")], buffed);
        Assert.Empty(after.Units[U("foe")].Conditions);
        Assert.Equal(0, after.Units[U("foe")].TempHp);
        Assert.Empty(after.Units[U("outside")].Conditions);
    }

    [Theory]
    [InlineData("warlock.vampiric_touch")]
    [InlineData("hexblade.cursed_slash")]
    public void Drains_heal_only_the_caster_on_hits_and_never_on_misses(string ability)
    {
        var (battle, initial) = Start(ability, "warlock");
        var sawHit = false;
        var sawMiss = false;
        for (ulong seed = 1; seed <= 60 && !(sawHit && sawMiss); seed++)
        {
            var state = initial with { Rng = Rng.Seeded(seed) };
            var (after, events) = battle.Apply(state, new Use(U("caster"), new(ability), new Target(Unit: U("foe"))));
            var roll = Assert.Single(events.OfType<AttackResolved>());
            Assert.DoesNotContain(events.OfType<Healed>(), e => e.Unit != U("caster"));
            if (roll.Hit)
            {
                sawHit = true;
                Assert.True(after.Units[U("caster")].Hp > 20);
                Assert.True(after.Units[U("foe")].Hp < 20);
                Assert.Single(events.OfType<Healed>());
            }
            else
            {
                sawMiss = true;
                Assert.Equal(20, after.Units[U("caster")].Hp);
                Assert.Empty(events.OfType<Healed>());
            }
        }
        Assert.True(sawHit && sawMiss);
    }

    [Theory]
    [InlineData("druid.mass_cure_wounds")]
    [InlineData("grovekeeper.bloom")]
    [InlineData("grovekeeper.sanctuary_grove")]
    public void Tile_heals_preview_and_heal_allies_without_healing_foes(string ability)
    {
        var (battle, state) = Start(ability, "druid");
        var choices = Choices.For(state, U("caster"), battle.Rules);
        var cast = Assert.Single(choices.Abilities, a => a.Ability.Value == ability);
        var target = Assert.Single(cast.Targets, t => t.Target.Tile == T(1, 1));
        Assert.Equal([U("ally"), U("caster")], target.Affected.Select(p => p.Unit).Order());
        var (after, events) = battle.Apply(state, new Use(U("caster"), new(ability), target.Target));
        Assert.Equal([U("ally"), U("caster")], events.OfType<Healed>().Select(h => h.Unit).Order());
        Assert.Equal(20, after.Units[U("foe")].Hp);
        Assert.Equal(20, after.Units[U("outside")].Hp);
    }

    [Theory]
    [InlineData("grovekeeper.sanctuary_grove", "druid")]
    [InlineData("crusader.consecrated_ground", "paladin")]
    public void Healing_zones_tick_only_for_allies_and_preserve_the_filter_across_serialization(string ability, string cls)
    {
        var (battle, state) = Start(ability, cls);
        state = state.Change("foe", u => u with { Hp = 100 });
        var target = cls == "druid" ? new Target(Tile: T(1, 1)) : new Target();
        (state, _) = battle.Apply(state, new Use(U("caster"), new(ability), target));
        state = JsonSerializer.Deserialize<BattleState>(JsonSerializer.Serialize(state, ArcJson.Options), ArcJson.Options)!;
        Assert.Equal(AffectedSide.Allies, Assert.Single(state.Zones).Affects);
        var healed = new List<UnitId>();
        // One complete turn cycle: each unit starts a turn inside or outside the same zone.
        do
        {
            var (after, events) = battle.Apply(state, new EndTurn(state.Active!.Value));
            healed.AddRange(events.OfType<Healed>().Select(e => e.Unit));
            state = after;
        } while (state.Active != U("caster"));
        Assert.Equal([U("ally"), U("caster")], healed.Order());
    }

    [Theory]
    [InlineData("warlock.vampiric_touch")]
    [InlineData("hexblade.cursed_slash")]
    public void Drain_previews_attribute_healing_to_the_caster_and_weight_it_by_hit_chance(string ability)
    {
        var (battle, state) = Start(ability, "warlock");
        var choice = Choices.For(state, U("caster"), battle.Rules).Abilities.Single(a => a.Ability.Value == ability);
        var preview = choice.Targets.Single(t => t.Target.Unit == U("foe")).Affected;
        Assert.Equal(0, preview.Single(p => p.Unit == U("foe")).Heal);
        Assert.True(preview.Single(p => p.Unit == U("caster")).Heal > 0);
        var harder = state.Change("foe", u => u with { Sheet = u.Sheet with { Ac = 100 } });
        var harderChoice = Choices.For(harder, U("caster"), battle.Rules).Abilities.Single(a => a.Ability.Value == ability);
        var harderHeal = harderChoice.Targets.Single(t => t.Target.Unit == U("foe")).Affected.Single(p => p.Unit == U("caster")).Heal;
        Assert.True(harderHeal < preview.Single(p => p.Unit == U("caster")).Heal);
    }
    [Fact]
    public void Caster_only_heal_and_temporary_hp_do_not_benefit_the_aimed_foe()
    {
        var (battle, state) = Start("warlock.vampiric_touch", "warlock");
        var (after, events) = battle.Run(state, "caster", "foe", T(1, 2),
            new HealEffect(TestAbilities.Flat("3"), [], Self: true),
            new TempHpEffect(TestAbilities.Flat("5"), [], Self: true));
        Assert.Equal(23, after.Units[U("caster")].Hp);
        Assert.Equal(5, after.Units[U("caster")].TempHp);
        Assert.Equal(20, after.Units[U("foe")].Hp);
        Assert.Equal(0, after.Units[U("foe")].TempHp);
        Assert.Single(events.OfType<Healed>(), e => e.Unit == U("caster"));
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(AffectedSide.All, 2)]
    [InlineData(AffectedSide.Allies, 1)]
    [InlineData(AffectedSide.Enemies, 1)]
    public void Zone_filters_apply_to_static_modifiers_and_survive_partial_removal(AffectedSide? filter, int recipients)
    {
        var (battle, state) = Start("warlock.vampiric_touch", "warlock");
        var passive = new PassiveDef("ward", null, null, [], new StaticMods(Ac: 2));
        var writer = new BattleWriter(state, battle.Rules);
        ZoneRules.Create(writer, U("caster"), "ward", [T(1, 1), T(1, 2), T(0, 0)], 3, false, false, [passive], affects: filter);
        Surfaces.RemoveTiles(writer, _ => true, [T(0, 0)]);
        state = writer.Freeze();
        Assert.Equal(filter, Assert.Single(state.Zones).Affects);
        var caster = ZoneRules.PassivesAt(state, state.Units[U("caster")]).Count();
        var foe = ZoneRules.PassivesAt(state, state.Units[U("foe")]).Count();
        Assert.Equal(recipients, caster + foe);
        if (filter == AffectedSide.Allies) Assert.Equal(0, foe);
        if (filter == AffectedSide.Enemies) Assert.Equal(0, caster);
        var json = JsonSerializer.Serialize(state.Zones[0], ArcJson.Options);
        if (filter is null) Assert.DoesNotContain("affects", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ai_values_drain_healing_for_a_hurt_caster()
    {
        const string ability = "warlock.vampiric_touch";
        var (battle, state) = Start(ability, "warlock");
        battle.WithAiProfiles([new AiProfileDef("drainer", Damage: 1, Heal: 10)]);
        state = state.Change("caster", u => u with { Controller = new AiControlled("drainer"),
            Economy = u.Economy with { MoveLeft = 0, Bonus = false } });
        var plan = Ai.Plan(state, U("caster"), battle.Rules);
        Assert.True(plan.Commands.OfType<Use>().Any(u => u.Ability.Value == ability), plan.Explain);
    }

}
