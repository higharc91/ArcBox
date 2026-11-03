using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class SurfaceRulesTests
{
    private static Tile T(int x, int y) => new(x, y);
    private static UnitId U(string id) => new(id);
    private static readonly UnitSheet Tough = TestSheets.Goblin with { MaxHp = 100, Move = 8 };
    private static AbilityDef Blast(string type, int radius = 0) => new(new AbilityId("test." + type), type, 0,
        new CostDef(Action: 1), new TargetDef(TargetKind.Tile, 6, false,
            new AreaDef(radius == 0 ? AreaShape.Single : AreaShape.Burst, radius)),
        [new DamageEffect(TestAbilities.Flat("8"), type, [])]);
    private static UnitSheet Mage(params AbilityDef[] spells) => (TestSheets.Cleric with { MaxHp = 100, InitiativeBonus = 30 }).Knowing(spells);
    private static TestBattle On(string picture, bool enabled = true) => TestBattle.On(AsciiMap.Parse(picture))
        .WithEnvironment(BasePackTests.Rules.Environment, enabled)
        .WithConditions(BasePackTests.Rules.Conditions[new ConditionId("prone")], BasePackTests.Rules.Conditions[new ConditionId("burning")],
            BasePackTests.Rules.Conditions[new ConditionId("restrained")]);
    private static TestBattle OnFuelLine() => TestBattle.On(Paint(AsciiMap.Parse("0. 0. 0. 0. 0.\n0. 0. 0. 0. 0."),
        t => t.Y == 0 && t.X > 0 ? "planks/oak" : "grass/lush"))
        .WithEnvironment(BasePackTests.Rules.Environment).WithConditions(BasePackTests.Rules.Conditions[new ConditionId("prone")]);
    private static BattleMap Paint(BattleMap source, Func<Tile, string> material, Func<Tile, TileFlags, TileFlags>? flags = null)
    {
        string[] materials = ["grass/lush", "grass/dry", "planks/oak", "water/clear"];
        var bytes = new byte[source.Width * source.Height * 3];
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
            {
                var t = T(x, y); var i = (y * source.Width + x) * 3;
                bytes[i] = (byte)source[t].Height; bytes[i + 1] = (byte)Array.IndexOf(materials, material(t));
                bytes[i + 2] = (byte)(flags?.Invoke(t, source[t].Flags) ?? source[t].Flags);
            }
        return BattleMap.FromBytes(source.Id, source.Width, source.Height, bytes, [.. materials], source.Props, source.Zones);
    }
    private static BattleState Round(TestBattle battle, BattleState state, int target)
    {
        for (var n = 0; state.Round < target && n < 30; n++)
            state = battle.Apply(state, new EndTurn(state.Active!.Value)).State;
        Assert.Equal(target, state.Round);
        return state;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lightning_conducts_to_all_standing_sides_and_caster_but_skips_downed(bool enabled)
    {
        var bolt = Blast("lightning");
        var battle = On("0~ 0~ 0~ 0~ 0~", enabled).With(bolt)
            .Party("mage", Mage(bolt), T(0, 0)).Enemy("direct", Tough, T(1, 0))
            .Party("ally", TestSheets.Fighter with { MaxHp = 100 }, T(2, 0))
            .Enemy("extra", Tough, T(3, 0)).Party("downed", TestSheets.Fighter, T(4, 0));
        var state = battle.Start().State.Change("downed", u => u with { Hp = 0, Life = LifeState.Downed, Countdown = 3 });
        var result = battle.Apply(state, new Use(U("mage"), bolt.Id, new Target(Tile: T(1, 0))));
        Assert.Equal(92, result.State.Units[U("direct")].Hp);
        Assert.Equal(3, result.State.Units[U("downed")].Countdown);
        foreach (var id in new[] { "mage", "ally", "extra" })
            if (enabled) Assert.Contains(100 - result.State.Units[U(id)].Hp, new[] { 4, 8 });
            else Assert.Equal(100, result.State.Units[U(id)].Hp);
        Assert.Equal(enabled ? 1 : 0, result.Events.OfType<TileReacted>().Count(e => e.Rule == "conduct"));
        if (enabled)
        {
            var order = result.Events.OfType<Damaged>().Where(e => e.Unit != U("direct")).Select(e => e.Unit.Value);
            Assert.Equal(new[] { "ally", "extra", "mage" }, order);
        }
    }

    [Fact]
    public void Conduction_is_bounded_to_six_steps_and_ice_blocks_the_fill()
    {
        var bolt = Blast("lightning");
        var battle = On("0~ 0~ 0~ 0~ 0~ 0~ 0~ 0~ 0~\n0. 0. 0. 0. 0. 0. 0. 0. 0.").With(bolt)
            .Party("mage", Mage(bolt), T(0, 1)).Enemy("near", Tough, T(6, 0)).Enemy("far", Tough, T(7, 0));
        var state = battle.Start().State;
        var result = battle.Apply(state, new Use(U("mage"), bolt.Id, new Target(Tile: T(0, 0))));
        Assert.True(result.State.Units[U("near")].Hp < 100); Assert.Equal(100, result.State.Units[U("far")].Hp);
        var ice = new Zone(0, U("mage"), "ice_sheet", [T(3, 0)], 3, false, true, [], Surface: true, Source: "freeze");
        state = state with { Zones = [ice], NextSerial = 1 };
        result = battle.Apply(state, new Use(U("mage"), bolt.Id, new Target(Tile: T(0, 0))));
        Assert.Equal(100, result.State.Units[U("near")].Hp);
        Assert.Equal(new[] { T(0, 0), T(1, 0), T(2, 0) }, Assert.Single(result.Events.OfType<TileReacted>()).Tiles);
    }

    [Fact]
    public void Fire_on_empty_fuel_spreads_one_tile_per_round_then_scorches_and_cannot_relit()
    {
        var fire = Blast("fire");
        var battle = OnFuelLine().With(fire).Party("mage", Mage(fire), T(0, 0)).Enemy("gob", Tough, T(0, 1));
        var result = battle.Apply(battle.Start().State, new Use(U("mage"), fire.Id, new Target(Tile: T(1, 0))));
        var state = result.State;
        var first = Assert.Single(state.Zones.Where(z => z.Kind == "burning"));
        Assert.True(first.Surface); Assert.Equal("ignite", first.Source); Assert.Equal(2, first.RoundsLeft);
        Assert.DoesNotContain(result.Events, e => e is Damaged);
        var tileRounds = 0;
        for (var round = 1; round <= 6; round++)
        {
            state = Round(battle, state, round);
            tileRounds += state.Zones.Where(z => z.Kind == "burning").Sum(z => z.Tiles.Length);
            var lit = state.Zones.Where(z => z.Kind is "burning" or "scorched").SelectMany(z => z.Tiles).ToHashSet();
            Assert.Equal(Math.Min(4, round), lit.Count);
            Invariants.Check(state, battle.Rules);
        }
        Assert.Equal(8, tileRounds); Assert.DoesNotContain(state.Zones, z => z.Kind == "burning");
        result = battle.Apply(state, new Use(U("mage"), fire.Id, new Target(Tile: T(1, 0))));
        Assert.DoesNotContain(result.State.Zones, z => z.Kind == "burning");
    }

    [Fact]
    public void Cold_freezes_only_shallow_water_and_fire_thaws_just_the_touched_tile()
    {
        var cold = Blast("cold", 1); var fire = Blast("fire");
        var map = Paint(AsciiMap.Parse("0. 0~ 0~ 0~ 0."), _ => "grass/lush",
            (t, f) => t == T(2, 0) ? f | TileFlags.Deep : f);
        var battle = TestBattle.On(map).WithEnvironment(BasePackTests.Rules.Environment).With(cold, fire)
            .WithConditions(BasePackTests.Rules.Conditions[new ConditionId("prone")])
            .Party("mage", Mage(cold, fire), T(0, 0)).Enemy("gob", Tough, T(4, 0));
        var state = battle.Apply(battle.Start().State, new Use(U("mage"), cold.Id, new Target(Tile: T(2, 0)))).State;
        Assert.Equal(new[] { T(1, 0), T(3, 0) }, state.Zones.Where(z => z.Kind == "ice_sheet").SelectMany(z => z.Tiles).Order());
        state = state.Change("mage", u => u with { Economy = u.Economy with { Action = true } });
        var result = battle.Apply(state, new Use(U("mage"), fire.Id, new Target(Tile: T(1, 0))));
        Assert.Equal(new[] { T(3, 0) }, result.State.Zones.Where(z => z.Kind == "ice_sheet").SelectMany(z => z.Tiles));
        Assert.DoesNotContain(result.State.Zones, z => z.Kind == "burning");
        Assert.Contains(result.Events, e => e is TileReacted { Rule: "thaw" });
    }

    [Fact]
    public void Entering_water_removes_burning_and_emits_the_cause_first()
    {
        var battle = On("0. 0~ 0. 0.").Party("mage", Mage(), T(0, 0)).Enemy("gob", Tough, T(3, 0));
        var state = battle.Start().State;
        state = battle.Run(state, "mage", "mage", T(0, 0), new ApplyEffect(new ConditionId("burning"), 3)).State;
        var result = battle.Apply(state, new Move(U("mage"), [T(0, 0), T(1, 0)]));
        Assert.DoesNotContain(result.State.Units[U("mage")].Conditions, c => c.Id == new ConditionId("burning"));
        Assert.True(result.Events.IndexOf(result.Events.OfType<TileReacted>().Single()) < result.Events.IndexOf(result.Events.OfType<ConditionRemoved>().Single()));
    }

    [Fact]
    public void Surface_and_reaction_queue_metadata_round_trip()
    {
        Step step = new ReactStep(U("mage"), new AbilityId("test.fire"), T(1, 0), [T(1, 0)], 7, Hit: true,
            Observed: [new ReactionDamage(U("gob"), new DamageEffect(TestAbilities.Flat("8"), "fire", []))]);
        var json = JsonSerializer.Serialize(step, ArcJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<Step>(json, ArcJson.Options), ArcJson.Options));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Fire_removes_only_burnt_web_tiles_and_only_their_own_restraints(bool fromWeb)
    {
        var fire = Blast("fire");
        var battle = On("0. 0. 0. 0.\n0. 0. 0. 0.").With(fire)
            .Party("mage", Mage(fire), T(0, 1)).Enemy("burnt", Tough, T(1, 0)).Enemy("spared", Tough, T(2, 0));
        var state = battle.Start().State;
        var webDef = BasePackTests.Rules.Abilities[new AbilityId("wizard.web")].Effects.OfType<ZoneEffect>().Single();
        var web = new Zone(0, U("mage"), "web", [T(1, 0), T(2, 0)], 10, false, true, webDef.Passives);
        state = state with { Zones = [web], NextSerial = 1 };
        var restraint = new ActiveCondition(new ConditionId("restrained"), U("mage"), null, false, 0, Stat.Dex, false,
            SourceZone: fromWeb ? 0 : null);
        state = state.Change("burnt", u => u with { Conditions = [restraint] })
            .Change("spared", u => u with { Conditions = [restraint with { SourceZone = 0 }] });
        var result = battle.Apply(state, new Use(U("mage"), fire.Id, new Target(Tile: T(1, 0))));
        var remaining = Assert.Single(result.State.Zones.Where(z => z.Kind == "web"));
        Assert.Equal(new[] { T(2, 0) }, remaining.Tiles);
        Assert.Equal(!fromWeb, result.State.Units[U("burnt")].Conditions.Any(c => c.Id.Value == "restrained"));
        Assert.Equal(remaining.Serial, Assert.Single(result.State.Units[U("spared")].Conditions).SourceZone);
        var flames = Assert.Single(result.State.Zones.Where(z => z.Kind == "burning"));
        Assert.Equal(1, flames.RoundsLeft); Assert.Equal("burn_web", flames.Source);
        Assert.Equal("2d4", Assert.IsType<DamageEffect>(flames.Passives.Single().Effects.Single()).Dice!.At(1).ToString());
    }

    [Fact]
    public void Restraint_from_a_zone_save_retains_its_origin_across_nested_effects()
    {
        var battle = On("0. 0. 0.\n0. 0. 0.").Party("mage", Mage(), T(0, 1)).Enemy("gob", Tough, T(0, 0));
        var seed = Enumerable.Range(1, 100).Select(n => (ulong)n).First(n => TestAbilities.Next(Rng.Seeded(n), 20)[0] == 1);
        var state = battle.Start(seed).State;
        var webDef = BasePackTests.Rules.Abilities[new AbilityId("wizard.web")].Effects.OfType<ZoneEffect>().Single();
        state = state with { Zones = [new Zone(0, U("mage"), "web", [T(1, 0)], 10, false, true, webDef.Passives)], NextSerial = 1 };
        state = battle.Apply(state, new EndTurn(U("mage"))).State;
        var result = battle.Apply(state, new Move(U("gob"), [T(0, 0), T(1, 0)]));
        while (result.State.Pending is ReactionOffer offer)
        {
            var saved = JsonSerializer.Deserialize<BattleState>(JsonSerializer.Serialize(result.State, ArcJson.Options), ArcJson.Options)!;
            result = battle.Apply(saved, new React(offer.Reactor, offer.Options[0]));
        }
        Assert.Equal(0, Assert.Single(result.State.Units[U("gob")].Conditions.Where(c => c.Id.Value == "restrained")).SourceZone);
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("conflict")]
    [InlineData("water")]
    [InlineData("scorched")]
    [InlineData("deep_ice")]
    [InlineData("unknown")]
    public void Surface_invariants_reject_invalid_saved_states(string invalid)
    {
        var battle = On("0. 0~ 0~ 0.\n0. 0. 0. 0.").Party("mage", Mage(), T(0, 0)).Enemy("gob", Tough, T(3, 1));
        var state = battle.Start().State;
        Zone Surface(string kind, Tile tile, int serial = 0) => new(serial, U("mage"), kind, [tile], 2, false, false, [], Surface: true);
        state = invalid switch
        {
            "outside" => state with { Zones = [Surface("burning", T(99, 0))] },
            "conflict" => state with { Zones = [Surface("burning", T(1, 1)), Surface("oil", T(1, 1), 1)] },
            "water" => state with { Zones = [Surface("burning", T(1, 0))] },
            "scorched" => state with { Zones = [Surface("burning", T(1, 1)), Surface("scorched", T(1, 1), 1)] },
            "deep_ice" => state with { Zones = [Surface("ice_sheet", T(2, 0))], Sunk = [T(2, 0)] },
            _ => state with { Zones = [Surface("__missing__", T(1, 1))] },
        };
        Assert.Throws<RulesInvariantException>(() => Invariants.Check(state, battle.Rules));
    }

    [Fact]
    public void Killing_the_last_direct_target_still_resolves_conduction_before_the_battle_ends()
    {
        var bolt = Blast("lightning");
        var battle = On("0~ 0~").With(bolt).Party("mage", Mage(bolt), T(0, 0))
            .Enemy("last", Tough with { MaxHp = 1 }, T(1, 0));
        var result = battle.Apply(battle.Start().State, new Use(U("mage"), bolt.Id, new Target(Tile: T(1, 0))));
        Assert.True(result.State.Units[U("mage")].Hp < 100);
        var damage = result.Events.OfType<Damaged>().Single(d => d.Unit == U("mage"));
        Assert.True(result.Events.IndexOf(damage) < result.Events.IndexOf(result.Events.OfType<BattleEnded>().Single()));
    }

    [Fact]
    public void A_missed_single_target_attack_does_not_conduct()
    {
        var bolt = Blast("lightning") with
        {
            Target = new TargetDef(TargetKind.Enemy, 6, false, new AreaDef(AreaShape.Single)),
            Effects = [new AttackEffect("spell", null, [new DamageEffect(TestAbilities.Flat("8"), "lightning", [])], [], [])],
        };
        var battle = On("0~ 0~ 0~").With(bolt).Party("mage", Mage(bolt), T(0, 0))
            .Enemy("direct", Tough with { Ac = 100 }, T(1, 0)).Enemy("extra", Tough, T(2, 0));
        var seed = Enumerable.Range(1, 100).Select(n => (ulong)n).First(n => TestAbilities.Next(Rng.Seeded(n), 20)[0] == 1);
        var result = battle.Apply(battle.Start(seed).State, new Use(U("mage"), bolt.Id, new Target(Unit: U("direct"))));
        Assert.Contains(result.Events, e => e is AttackResolved { Hit: false });
        Assert.DoesNotContain(result.Events, e => e is TileReacted);
        Assert.Equal(100, result.State.Units[U("extra")].Hp);
    }

    [Fact]
    public void Grease_is_fuel_and_only_the_lit_portion_is_consumed()
    {
        var fire = Blast("fire");
        var battle = On("0. 0. 0. 0.\n0. 0. 0. 0.").With(fire)
            .Party("mage", Mage(fire), T(0, 1)).Enemy("gob", Tough, T(3, 1));
        var state = battle.Start().State;
        state = state with { Zones = [new Zone(0, U("mage"), "grease", [T(1, 0), T(2, 0)], 10, false, true, [])], NextSerial = 1 };
        var result = battle.Apply(state, new Use(U("mage"), fire.Id, new Target(Tile: T(1, 0))));
        Assert.Equal(new[] { T(2, 0) }, Assert.Single(result.State.Zones.Where(z => z.Kind == "grease")).Tiles);
        Assert.Equal(new[] { T(1, 0) }, Assert.Single(result.State.Zones.Where(z => z.Kind == "burning")).Tiles);
    }

    [Fact]
    public void Fire_condition_ticks_do_not_ignite_fuel_or_create_cascades()
    {
        var battle = OnFuelLine().WithConditions(BasePackTests.Rules.Conditions[new ConditionId("burning")])
            .Party("mage", Mage(), T(1, 0)).Enemy("gob", Tough, T(0, 1));
        var state = battle.Start().State;
        state = battle.Run(state, "mage", "mage", T(1, 0), new ApplyEffect(new ConditionId("burning"), 3)).State;
        state = Round(battle, state, 2);
        Assert.DoesNotContain(state.Zones, z => z.Surface);
        Assert.True(state.Units[U("mage")].Hp < 100);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_the_selected_random_or_conditional_damage_type_reacts(bool randomBranch)
    {
        var lightning = new DamageEffect(TestAbilities.Flat("8"), "lightning", []);
        var cold = new DamageEffect(TestAbilities.Flat("8"), "cold", []);
        var spell = Blast("lightning") with { Effects = randomBranch
            ? [new RandomEffect([new RandomEntry(0, [lightning]), new RandomEntry(1, [cold])])]
            : [new IfEffect(new WhenDef(TargetHasCondition: new ConditionId("prone")), [lightning], [cold])] };
        var battle = On("0~ 0~ 0~").With(spell).Party("mage", Mage(spell), T(0, 0)).Enemy("gob", Tough, T(1, 0));
        var result = battle.Apply(battle.Start().State, new Use(U("mage"), spell.Id, new Target(Tile: T(1, 0))));
        Assert.DoesNotContain(result.Events, e => e is TileReacted { Rule: "conduct" });
        Assert.Contains(result.Events, e => e is TileReacted { Rule: "freeze" });
        Assert.Equal(100, result.State.Units[U("mage")].Hp);
    }
    [Fact]
    public void Burning_enter_and_turn_start_share_one_limit_and_credit_the_original_source()
    {
        var battle = On("0. 0. 0. 0. 0.\n0. 0. 0. 0. 0.").Party("mage", Mage(), T(0, 0)).Enemy("gob", Tough, T(4, 1));
        var state = battle.Start().State;
        var def = battle.Rules.Environment.Surfaces["burning"];
        state = state with { Zones = [new Zone(0, U("gob"), "burning", [T(1, 0), T(2, 0)], 2, false, false, def.Passives, Surface: true, Source: "ignite")], NextSerial = 1 };
        var result = battle.Apply(state, new Move(U("mage"), [T(0, 0), T(1, 0), T(2, 0)]));
        Assert.Single(result.Events.OfType<Damaged>());
        Assert.Equal(U("gob"), Assert.Single(result.Events.OfType<PassiveTriggered>()).Source);
        var hp = result.State.Units[U("mage")].Hp;
        state = Round(battle, result.State, 2);
        Assert.True(state.Units[U("mage")].Hp < hp);
        var again = battle.Apply(state, new Move(U("mage"), [T(2, 0), T(1, 0)]));
        Assert.DoesNotContain(again.Events, e => e is Damaged);
    }

    [Theory]
    [InlineData("push")]
    [InlineData("teleport")]
    [InlineData("fall")]
    public void Water_douses_on_forced_arrivals_and_collapsing_shallows(string arrival)
    {
        var battle = On("0. 0. 0~ 0.").Party("mage", Mage(), T(0, 0)).Enemy("gob", Tough, arrival == "fall" ? T(2, 0) : T(1, 0));
        var state = battle.Run(battle.Start().State, "mage", "gob", T(1, 0), new ApplyEffect(new ConditionId("burning"), 3)).State;
        EffectDef effect = arrival switch
        {
            "push" => new PushEffect(1),
            "teleport" => new TeleportEffect(4, Self: false),
            _ => new AlterTerrainEffect(new AreaDef(AreaShape.Single), 0, Deep: true),
        };
        var result = battle.Run(state, "mage", "gob", T(2, 0), effect);
        Assert.Equal(T(2, 0), result.State.Units[U("gob")].Pos);
        Assert.DoesNotContain(result.State.Units[U("gob")].Conditions, c => c.Id.Value == "burning");
        Assert.Contains(result.Events, e => e is TileReacted { Rule: "douse" });
    }

    [Fact]
    public void Surfaces_age_at_round_start_even_after_their_owner_dies_and_cannot_be_dispelled()
    {
        var cold = Blast("cold");
        var battle = On("0. 0~ 0~ 0.").With(cold).Party("mage", Mage(cold), T(0, 0)).Enemy("gob", Tough, T(3, 0));
        var state = battle.Apply(battle.Start().State, new Use(U("mage"), cold.Id, new Target(Tile: T(1, 0)))).State;
        state = state with { Zones = [.. state.Zones.Select(z => z with { Owner = null })] };
        state = battle.Run(state, "mage", null, T(1, 0), new DispelEffect(Zones: true)).State;
        Assert.Equal(3, Assert.Single(state.Zones).RoundsLeft);
        state = battle.Apply(state, new EndTurn(U("mage"))).State;
        Assert.Equal(3, Assert.Single(state.Zones).RoundsLeft);
        state = Round(battle, state, 2);
        Assert.Equal(2, Assert.Single(state.Zones).RoundsLeft);
        state = Round(battle, state, 4);
        Assert.DoesNotContain(state.Zones, z => z.Kind == "ice_sheet");
    }

    [Fact]
    public void Environmental_tooltips_follow_content_and_are_absent_without_opt_in()
    {
        var rules = BasePackTests.Rules;
        var bolt = Blast("lightning");
        var legacy = Tooltips.For(bolt, 1, rules);
        Assert.DoesNotContain("connected water", legacy);
        Assert.Contains("connected water (6 tiles)", Tooltips.For(bolt, 1, rules, environment: true));
        var altered = rules.Environment with { Reactions = [rules.Environment.Reactions[0] with { Conduct = new ConductDef(4, Stat.Dex, true) }] };
        var other = RulesPack.InMemory("tooltip", environment: altered);
        Assert.Contains("connected water (4 tiles)", Tooltips.For(bolt, 1, other, environment: true));
        foreach (var reaction in rules.Environment.Reactions)
            Assert.False(string.IsNullOrWhiteSpace(Tooltips.For(reaction, 1, rules)));
        Assert.Contains("2d4", Tooltips.For(rules.Environment.Reactions.Single(r => r.Id == "burn_web"), 1, rules));
        Assert.Contains("DC 10", Tooltips.For(rules.Environment.Reactions.Single(r => r.Id == "freeze"), 1, rules));
    }

    [Fact]
    public void Conduction_event_uses_breadth_first_layers_with_tile_order_within_a_layer()
    {
        var bolt = Blast("lightning");
        var battle = On("0~ 0~ 0~\n0~ 0~ 0~\n0~ 0~ 0~").With(bolt)
            .Party("mage", Mage(bolt), T(0, 0)).Enemy("gob", Tough, T(2, 2));
        var result = battle.Apply(battle.Start().State, new Use(U("mage"), bolt.Id, new Target(Tile: T(1, 1))));
        var expected = new[] { T(1, 1) }.Concat(new[] { T(0, 1), T(1, 0), T(1, 2), T(2, 1) }.Order())
            .Concat(new[] { T(0, 0), T(2, 0), T(0, 2), T(2, 2) }.Order());
        Assert.Equal(expected, Assert.Single(result.Events.OfType<TileReacted>()).Tiles);
    }

    [Fact]
    public void Countering_a_spell_cancels_its_saved_reaction_step_too()
    {
        var bolt = Blast("lightning") with { Tier = 1, Target = new TargetDef(TargetKind.Enemy, 6, false, new AreaDef(AreaShape.Single)) };
        var counter = new AbilityDef(new AbilityId("test.counter"), "Counter", 0, new CostDef(Reaction: 1),
            new TargetDef(TargetKind.Enemy, 6, false, new AreaDef(AreaShape.Single)),
            [new TransformEffect("counterspell", JsonDocument.Parse("{}").RootElement.Clone())], Ai: new AiHints(["reactsTo:spellCast"]));
        var battle = On("0~ 0~ 0~").With(bolt, counter).Party("wiz", Mage(counter) with { InitiativeBonus = -30 }, T(0, 0))
            .Enemy("caster", Mage(bolt), T(1, 0));
        var offered = battle.Apply(battle.Start().State, new Use(U("caster"), bolt.Id, new Target(Unit: U("wiz")))).State;
        var offer = Assert.IsType<ReactionOffer>(offered.Pending);
        Assert.Contains(offered.Queue, step => step is ReactStep);
        var loaded = JsonSerializer.Deserialize<BattleState>(JsonSerializer.Serialize(offered, ArcJson.Options), ArcJson.Options)!;
        var result = battle.Apply(loaded, new React(offer.Reactor, offer.Options.Single(o => o.Ability == counter.Id)));
        Assert.Contains(result.Events, e => e is Countered);
        Assert.DoesNotContain(result.Events, e => e is TileReacted or Damaged);
        Assert.Empty(result.State.Queue);
    }

    [Fact]
    public void Sinking_a_frozen_tile_removes_ice_before_deep_water_is_checked()
    {
        var cold = Blast("cold");
        var battle = On("0. 0~ 0~ 0.").With(cold).Party("mage", Mage(cold), T(0, 0)).Enemy("gob", Tough, T(3, 0));
        var state = battle.Apply(battle.Start().State, new Use(U("mage"), cold.Id, new Target(Tile: T(1, 0)))).State;
        state = battle.Run(state, "mage", null, T(1, 0), new AlterTerrainEffect(new AreaDef(AreaShape.Single), 0, Deep: true)).State;
        Assert.DoesNotContain(state.Zones, z => z.Kind == "ice_sheet");
        Assert.Contains(T(1, 0), state.Sunk!);
    }

    [Fact]
    public void Fire_consumes_oil_and_keeps_the_igniter_as_the_new_surface_owner()
    {
        var fire = Blast("fire");
        var battle = On("0. 0. 0. 0.").With(fire).Party("mage", Mage(fire), T(0, 0)).Enemy("gob", Tough, T(3, 0));
        var state = battle.Start().State;
        state = state with { Zones = [new Zone(0, U("gob"), "oil", [T(1, 0)], 10, false, false, [], Surface: true, Source: "oil_flask")], NextSerial = 1 };
        state = battle.Apply(state, new Use(U("mage"), fire.Id, new Target(Tile: T(1, 0)))).State;
        var surface = Assert.Single(state.Zones);
        Assert.Equal("burning", surface.Kind);
        Assert.Equal(U("mage"), surface.Owner);
        Assert.Equal(2, surface.RoundsLeft);
    }

    [Fact]
    public void Ice_slips_on_first_entry_only_until_the_units_next_turn()
    {
        var sheet = Mage() with { Scores = Mage().Scores.SetItem(Stat.Dex, -30) };
        var battle = On("0. 0~ 0~ 0. 0.\n0. 0. 0. 0. 0.").Party("mage", sheet, T(0, 0)).Enemy("gob", Tough, T(4, 1));
        var state = battle.Start().State;
        var ice = battle.Rules.Environment.Surfaces["ice_sheet"];
        state = state with { Zones = [new Zone(0, U("gob"), "ice_sheet", [T(1, 0), T(2, 0)], 3, false, true, ice.Passives, Surface: true, Source: "freeze")], NextSerial = 1 };
        var result = battle.Apply(state, new Move(U("mage"), [T(0, 0), T(1, 0)]));
        Assert.Equal(10, Assert.Single(result.Events.OfType<Rolled>()).Result.Vs);
        Assert.Contains(result.State.Units[U("mage")].Conditions, c => c.Id.Value == "prone");
        state = battle.Run(result.State, "mage", "mage", T(1, 0), new RemoveEffect(new ConditionId("prone"))).State;
        result = battle.Apply(state, new Move(U("mage"), [T(1, 0), T(2, 0)]));
        Assert.DoesNotContain(result.Events, e => e is Rolled);
        state = Round(battle, result.State, 2);
        result = battle.Apply(state, new Move(U("mage"), [T(2, 0), T(1, 0)]));
        Assert.Single(result.Events.OfType<Rolled>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_interrupted_use_reacts_only_if_its_damage_already_landed(bool landed)
    {
        var fire = Blast("fire");
        var battle = OnFuelLine().With(fire).Party("mage", Mage(fire), T(0, 0)).Enemy("gob", Tough, T(0, 1));
        var state = battle.Start().State.Change("mage", u => u with { Hp = 0, Life = LifeState.Downed, Countdown = 3 });
        var step = new ReactStep(U("mage"), fire.Id, T(1, 0), [T(1, 0)], 1,
            Observed: landed ? [new ReactionDamage(U("gob"), (DamageEffect)fire.Effects[0])] : default);
        var saved = JsonSerializer.Deserialize<Step>(JsonSerializer.Serialize<Step>(step, ArcJson.Options), ArcJson.Options)!;
        var writer = new BattleWriter(state, battle.Rules);
        saved.Run(writer, battle.Rules);
        Assert.Equal(landed, writer.State.Zones.Any(z => z.Kind == "burning"));
    }

}
