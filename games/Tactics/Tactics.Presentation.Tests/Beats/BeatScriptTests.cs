using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Beats;

/// <summary><see cref="BeatScript"/>: what a beat's events become on screen, and how long that takes.</summary>
public sealed class BeatScriptTests
{
    private static readonly Lazy<FxArt> Fx = new(() => FxArt.Load(Pack.Root));
    private static readonly UnitId Hero = new("hero1");
    private static readonly UnitId Foe = new("goblin.1");
    private static readonly AbilityId Attack = new("common.attack");

    private static UnitView Unit(UnitId id, string side, int x, int y, int hp = 10)
        => new(id, id.Value, side, "characters.class_fighter", new Tile(x, y), hp, 10, 0, "Up", 0, [], null);

    private static BattleView View(params UnitView[] units) => new("e", "m", 1, [], null, [.. units], [], null, BoardChanges.None);

    private static readonly BattleView Near = View(Unit(Hero, "Party", 3, 3), Unit(Foe, "Enemy", 4, 3));
    private static readonly BattleView Far = View(Unit(Hero, "Party", 3, 3), Unit(Foe, "Enemy", 9, 3));

    private static readonly ImmutableArray<UnitId> Targets = [.. Enumerable.Range(1, 5).Select(i => new UnitId($"goblin.{i}"))];
    private static readonly BattleView Crowd = View([Unit(Hero, "Party", 0, 3), .. Targets.Select((t, i) => Unit(t, "Enemy", 11 + i % 2, 2 + i))]);
    private static readonly AbilityUsed Fireball = new(Hero, Attack, new Target(Tile: new Tile(11, 3)), new PresentationDef("characters.cast", "fx.explosion.fire", "projectiles.fireball"));

    private static double LastHit(BeatScript script) => script.Cues.OfType<ApplyCue>().Where(a => a.Event is Damaged).Max(a => a.At);

    private static BeatScript Script(BattleView view, params BattleEvent[] events) => BeatScript.Of(new Beat(1, null, [.. events]), view, view, Fx.Value);

    private static AbilityUsed Used(string? anim, string? fx, string? projectile, UnitId target)
        => new(Hero, Attack, new Target(Unit: target), new PresentationDef(anim, fx, projectile));

    private static RollResult Roll(int die, bool success, bool crit = false) => new([die], die, 3, die + 3, 12, success, crit, false);

    private static BattleEvent[] Hit(int amount, int after, string? anim = "characters.attack", string? fx = "fx.slash.steel", string? projectile = null,
        bool crit = false) =>
    [
        Used(anim, fx, projectile, Foe),
        new Rolled(Hero, RollKind.Attack, Roll(14, true, crit)),
        new AttackResolved(Hero, Foe, true, crit),
        new Damaged(Foe, amount, "slashing", after),
    ];

    [Fact]
    public void A_walk_is_one_step_after_another()
    {
        var script = Script(Near, new Moved(Hero, [new Tile(3, 3), new Tile(3, 4)]), new Moved(Hero, [new Tile(3, 4), new Tile(3, 5)]));

        var steps = script.Cues.OfType<WalkCue>().ToArray();

        Assert.Equal(2, steps.Length);
        Assert.Equal(new Tile(3, 3), steps[0].From);
        Assert.Equal(steps[0].At + steps[0].Length, steps[1].At);
        Assert.Equal(BeatTiming.Step * 2, script.Length, 3);
    }

    [Fact]
    public void A_multi_tile_path_in_one_event_walks_each_leg()
        => Assert.Equal(3, Script(Near, new Moved(Hero, [new Tile(3, 3), new Tile(3, 4), new Tile(3, 5), new Tile(3, 6)])).Cues.OfType<WalkCue>().Count());

    [Fact]
    public void A_walks_position_lands_with_each_step_not_before_it()
    {
        var script = Script(Near, new Moved(Hero, [new Tile(3, 3), new Tile(3, 4)]));

        var landed = Assert.Single(script.Cues.OfType<ApplyCue>());

        Assert.Equal(BeatTiming.Step, landed.At, 3);
    }

    [Fact]
    public void A_melee_attack_lunges_at_its_target_and_is_under_a_second()
    {
        var script = Script(Near, Hit(6, 4));

        var lunge = Assert.Single(script.Cues.OfType<LungeCue>());
        Assert.Equal(new Tile(4, 3), lunge.Toward);
        Assert.Equal("characters.attack", lunge.Anim);
        Assert.InRange(script.Length, 0.3, 1.0);
    }

    [Fact]
    public void A_ranged_attack_flies_and_is_under_a_second_even_across_the_map()
    {
        var script = Script(Far, Hit(6, 4, "characters.shoot", null, "projectiles.arrow"));

        var flight = Assert.Single(script.Cues.OfType<FlightCue>());
        Assert.Equal((new Tile(3, 3), new Tile(9, 3)), (flight.From, flight.To));
        Assert.Empty(script.Cues.OfType<LungeCue>());
        Assert.InRange(script.Length, 0.3, 1.0);
    }

    [Fact]
    public void A_plain_attack_from_range_is_a_bow_shot_though_the_ability_names_no_projectile()
    {
        var script = Script(Far, Hit(6, 4, fx: null));

        Assert.Equal("projectiles/arrow.png", Assert.Single(script.Cues.OfType<FlightCue>()).Art.Path);
        Assert.Contains(script.Cues.OfType<PoseCue>(), p => p.Anim == "shoot" && p.Unit == Hero);
    }

    [Fact]
    public void A_spell_from_range_with_no_projectile_is_not_an_arrow()
        => Assert.Empty(Script(Far, Hit(6, 4, "characters.cast", "fx.lightning.storm")).Cues.OfType<FlightCue>());

    [Fact]
    public void A_spell_with_a_projectile_flies_even_at_arms_length()
        => Assert.Single(Script(Near, Hit(6, 4, "characters.cast", "fx.explosion.fire", "projectiles.fireball")).Cues.OfType<FlightCue>());

    [Fact]
    public void The_damage_lands_after_the_blow_arrives()
    {
        var script = Script(Far, Hit(6, 4, "characters.shoot", null, "projectiles.arrow"));

        var flight = script.Cues.OfType<FlightCue>().Single();
        var pop = Assert.Single(script.Cues.OfType<PopCue>());
        var applied = script.Cues.OfType<ApplyCue>().Single(a => a.Event is Damaged);

        Assert.True(pop.At >= flight.At + flight.Length - 0.001);
        Assert.Equal(pop.At, applied.At);
        Assert.Equal("-6", pop.Text);
        Assert.Equal(PopStyle.Damage, pop.Style);
    }

    [Fact]
    public void Each_attack_of_a_double_attack_stays_under_a_second()
    {
        var script = Script(Near, [.. Hit(3, 7), .. Hit(3, 4)]);

        var lunges = script.Cues.OfType<LungeCue>().ToArray();

        Assert.Equal(2, lunges.Length);
        Assert.InRange(lunges[1].At - lunges[0].At, 0.3, 1.0);
        Assert.True(lunges[1].At >= lunges[0].At + lunges[0].Length - 0.001);   // the second doesn't start over the first
    }

    [Fact]
    public void Several_attacks_in_one_action_each_swing()
    {
        BattleEvent[] second = [new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, Foe, true, false), new Damaged(Foe, 3, "slashing", 4)];

        var script = Script(Near, [.. Hit(3, 7), .. second]);

        var lunges = script.Cues.OfType<LungeCue>().ToArray();
        Assert.Equal(2, lunges.Length);
        Assert.True(lunges[1].At >= lunges[0].At + lunges[0].Length - 0.001);
    }

    [Fact]
    public void An_area_spell_lands_on_all_its_targets_within_a_second()
    {
        var script = Script(Crowd, [Fireball, .. Targets.SelectMany(t => new BattleEvent[]
        {
            new Rolled(t, RollKind.Save, Roll(4, false)), new Damaged(t, 3, "fire", 7),
        })]);

        Assert.InRange(LastHit(script), 0.3, 1.0);
    }

    [Theory]
    [InlineData(2, 0.78, 1.02)]    // the longest flight's 0.52 to arrive, the first save's 0.2, and 0.06 for each target after
    [InlineData(3, 0.84, 1.08)]
    [InlineData(5, 0.96, 1.20)]    // with the impact's 0.24 held once, after the last hit
    public void An_areas_timing_at_the_longest_range(int targets, double lastHit, double length)
    {
        var script = Script(Crowd, [Fireball, .. Targets.Take(targets).SelectMany(t => new BattleEvent[]
        {
            new Rolled(t, RollKind.Save, Roll(4, false)), new Damaged(t, 3, "fire", 7),
        })]);

        Assert.Equal((lastHit, length), (Math.Round(LastHit(script), 3), Math.Round(script.Length, 3)));
    }

    [Fact]
    public void An_area_spell_that_also_lays_conditions_and_kills_lands_within_a_second_too()
    {
        var script = Script(Crowd, [Fireball, .. Targets.SelectMany(t => new BattleEvent[]
        {
            new Rolled(t, RollKind.Save, Roll(4, false)), new Damaged(t, 10, "fire", 0), new ConditionApplied(t, new ConditionId("burning")), new Died(t),
        })]);

        Assert.InRange(LastHit(script), 0.3, 1.0);
    }

    [Fact]
    public void An_area_spell_with_no_saves_lands_within_a_second()
    {
        var script = Script(Crowd, [Fireball, .. Targets.Select(t => new Damaged(t, 3, "fire", 7))]);

        Assert.InRange(LastHit(script), 0.3, 1.0);
    }

    [Fact]
    public void A_sweep_that_rolls_to_hit_each_target_is_one_swing()
    {
        var sweep = new AbilityUsed(Hero, Attack, new Target(Tile: new Tile(5, 3)), new PresentationDef("characters.spin", "fx.slash.steel", null));
        var script = Script(Crowd, [sweep, .. Targets.SelectMany(t => new BattleEvent[]
        {
            new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, t, true, false), new Damaged(t, 3, "slashing", 7),
        })]);

        Assert.True(script.Cues.OfType<LungeCue>().Count() + script.Cues.OfType<PoseCue>().Count(p => p.Unit == Hero) <= 1);
        Assert.InRange(LastHit(script), 0.2, 1.0);
    }

    [Fact]
    public void An_area_still_shows_every_targets_roll_and_number()
    {
        var script = Script(Crowd, [Fireball, .. Targets.SelectMany(t => new BattleEvent[]
        {
            new Rolled(t, RollKind.Save, Roll(4, false)), new Damaged(t, 3, "fire", 7),
        })]);

        Assert.Equal(Targets, script.Cues.OfType<BadgeCue>().Select(b => b.Unit));
        Assert.Equal(Targets, script.Cues.OfType<PopCue>().Select(p => p.Unit));
    }

    [Fact]
    public void A_target_killed_in_an_area_stays_until_its_fall_has_played()
    {
        var script = Script(Crowd, [Fireball, .. Targets.SelectMany(t => new BattleEvent[] { new Damaged(t, 10, "fire", 0), new Died(t) })]);

        foreach (var target in Targets)
        {
            var pose = script.Cues.OfType<PoseCue>().Single(p => p.Unit == target && p.Anim == "down");
            var gone = script.Cues.OfType<ApplyCue>().Single(a => a.Event is Died d && d.Unit == target);
            Assert.True(gone.At >= pose.At + pose.Length - 0.001);
            Assert.True(gone.At <= script.Length);
        }
    }

    [Fact]
    public void The_stage_is_still_held_for_a_moment_after_an_areas_last_hit()
        => Assert.True(Script(Crowd, [Fireball, .. Targets.Select(t => new Damaged(t, 3, "fire", 7))]) is var script
            && script.Length >= LastHit(script) + BeatTiming.Impact - 0.001);

    [Theory]
    [InlineData(false, 0.46)]    // a swing's 0.22, then the impact's 0.24
    [InlineData(true, 0.74)]     // the same, with 0.28 of flight over six tiles
    public void A_single_target_attack_times_out_as_it_did(bool shot, double seconds)
        => Assert.Equal(seconds, (shot ? Script(Far, Hit(6, 4, "characters.shoot", null, "projectiles.arrow")) : Script(Near, Hit(6, 4))).Length, 3);

    [Fact]
    public void A_single_target_spell_with_a_save_and_a_condition_times_out_as_it_did()
    {
        var script = Script(Far, Used("characters.cast", "fx.hex.shadow", "projectiles.eldritch", Foe), new Rolled(Foe, RollKind.Save, Roll(4, false)),
            new Damaged(Foe, 3, "necrotic", 7), new ConditionApplied(Foe, new ConditionId("cursed")));

        Assert.Equal(0.22 + 0.28 + BeatTiming.BadgeHold + BeatTiming.Impact + BeatTiming.Condition, script.Length, 3);
    }

    [Fact]
    public void Two_attacks_on_one_foe_with_thorns_biting_the_attacker_after_each_hit_are_still_two_swings()
    {
        BattleEvent[] thorns = [new PassiveTriggered(Foe, "thorns"), new Damaged(Hero, 2, "piercing", 8)];

        var script = Script(Near, [.. Hit(3, 7), .. thorns, new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, Foe, true, false),
            new Damaged(Foe, 3, "slashing", 4), .. thorns]);

        Assert.Equal(2, script.Cues.OfType<LungeCue>().Count());
    }

    [Fact]
    public void A_drain_is_single_target_timing()
        => Assert.Equal(BeatTiming.Windup + BeatTiming.Impact + BeatTiming.Stagger, Script(Near, [.. Hit(6, 4), new Healed(Hero, 3, 10)]).Length, 3);

    [Fact]
    public void A_condition_landing_on_the_caster_does_not_make_an_area()
        => Assert.Equal(BeatTiming.Windup + BeatTiming.Impact + BeatTiming.Condition,
            Script(Near, [.. Hit(6, 4), new ConditionApplied(Hero, new ConditionId("blessed"))]).Length, 3);

    [Fact]
    public void A_foe_shoved_into_another_is_not_an_area()
    {
        var other = new UnitId("goblin.2");
        var view = View(Unit(Hero, "Party", 3, 3), Unit(Foe, "Enemy", 4, 3), Unit(other, "Enemy", 6, 3));

        var script = Script(view, [.. Hit(3, 7), new Pushed(Foe, new Tile(4, 3), new Tile(5, 3)), new Collided(Foe, other, 2),
            new Damaged(Foe, 2, null, 5), new Damaged(other, 2, null, 8)]);

        Assert.True(script.Length >= BeatTiming.Windup + BeatTiming.Impact + BeatTiming.Slide * 2);
        Assert.Equal(BeatTiming.Windup, script.Cues.OfType<PopCue>().First().At, 3);
        Assert.True(script.Cues.OfType<ApplyCue>().Single(a => a.Event is Pushed).At >= BeatTiming.Windup + BeatTiming.Impact - 0.001);
    }

    [Fact]
    public void A_real_three_target_area_is_still_an_area()
    {
        var script = Script(Crowd, [Fireball, .. Targets.Take(3).SelectMany(t => new BattleEvent[]
        {
            new Rolled(t, RollKind.Save, Roll(4, false)), new Damaged(t, 3, "fire", 7),
        })]);

        Assert.Equal(0.84, LastHit(script), 3);
    }

    // The next few are the engine's own event order, from real battles (see Real_battles below).

    [Fact]
    public void A_hobgoblins_martial_advantage_then_a_second_attack_is_two_swings()
    {
        var script = Script(Near, new AbilityUsed(Hero, Attack, new Target(Unit: Foe), new PresentationDef("characters.attack", null, null)),
            new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, Foe, true, false), new Damaged(Foe, 3, "slashing", 7),
            new PassiveTriggered(Hero, "hobgoblin.martial_advantage"), new Damaged(Foe, 7, "slashing", 0),
            new Rolled(Hero, RollKind.Attack, Roll(2, false)), new AttackResolved(Hero, Foe, false, false));

        Assert.Equal(2, script.Cues.OfType<LungeCue>().Count());
    }

    [Fact]
    public void A_wolfs_knockdown_save_on_the_same_target_is_not_an_area()
    {
        var script = Script(Near, [.. Hit(3, 7), new PassiveTriggered(Hero, "wolf.knockdown"), new Rolled(Foe, RollKind.Save, Roll(4, false)),
            new ConditionApplied(Foe, new ConditionId("prone"))]);

        Assert.Equal(BeatTiming.Windup + BeatTiming.Impact + BeatTiming.BadgeHold + BeatTiming.Condition, script.Length, 3);
    }

    [Fact]
    public void A_repelling_blast_knocking_a_foe_into_another_is_not_an_area()
    {
        var other = new UnitId("goblin.2");
        var view = View(Unit(Hero, "Party", 3, 3), Unit(Foe, "Enemy", 4, 3), Unit(other, "Enemy", 6, 3));

        var script = Script(view, [.. Hit(3, 7), new PassiveTriggered(Hero, "warlock.repelling_blast"), new Damaged(Foe, 2, null, 5), new Damaged(other, 2, null, 8),
            new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, Foe, true, false), new Damaged(Foe, 2, "slashing", 3)]);

        Assert.Equal(2, script.Cues.OfType<LungeCue>().Count());
    }

    [Fact]
    public void A_passive_on_the_first_target_of_a_fireball_leaves_it_an_area()
    {
        var script = Script(Crowd, [Fireball, new Rolled(Targets[0], RollKind.Save, Roll(4, false)), new Damaged(Targets[0], 3, "fire", 7),
            new PassiveTriggered(Targets[0], "fighter.indomitable"), .. Targets.Skip(1).SelectMany(t => new BattleEvent[]
            {
                new Rolled(t, RollKind.Save, Roll(4, false)), new Damaged(t, 3, "fire", 7),
            })]);

        Assert.InRange(LastHit(script), 0.3, 1.0);
    }

    [Fact]
    public void A_mass_heal_aimed_at_one_ally_is_an_area()
    {
        var ally = new UnitId("hero2");
        var view = View([Unit(Hero, "Party", 0, 3), Unit(ally, "Party", 1, 3), .. Targets.Select((t, i) => Unit(t, "Party", 2, 2 + i))]);
        var heal = new AbilityUsed(Hero, Attack, new Target(Unit: ally), new PresentationDef("characters.cast", "fx.heal.holy", null));

        var script = Script(view, [heal, .. Targets.Select(t => new Healed(t, 3, 10))]);

        Assert.InRange(script.Cues.OfType<ApplyCue>().Where(a => a.Event is Healed).Max(a => a.At), 0.2, 1.0);
    }

    [Fact]
    public void A_double_attack_on_one_target_is_still_two_swings_after_all_this()
        => Assert.Equal(2, Script(Near, [.. Hit(3, 7), new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, Foe, true, false),
            new Damaged(Foe, 3, "slashing", 4)]).Cues.OfType<LungeCue>().Count());

    [Fact]
    public void A_miss_says_so()
    {
        var script = Script(Near, Used("characters.attack", "fx.slash.steel", null, Foe), new Rolled(Hero, RollKind.Attack, Roll(2, false)),
            new AttackResolved(Hero, Foe, false, false));

        Assert.Equal(("MISS", Foe), Assert.Single(script.Cues.OfType<PopCue>()) is { } pop ? (pop.Text, pop.Unit) : default);
    }

    [Fact]
    public void A_crit_says_so_with_the_damage()
    {
        var script = Script(Near, Hit(12, 0, crit: true));

        Assert.Equal(["CRIT", "-12"], script.Cues.OfType<PopCue>().Select(p => p.Text));
    }

    [Fact]
    public void Healing_pops_green()
    {
        var pop = Assert.Single(Script(Near, new Healed(Hero, 5, 9)).Cues.OfType<PopCue>());

        Assert.Equal(("+5", PopStyle.Heal), (pop.Text, pop.Style));
    }

    [Fact]
    public void A_save_shows_its_roll_on_the_roller()
    {
        var badge = Assert.Single(Script(Near, new Rolled(Foe, RollKind.Save, Roll(15, true))).Cues.OfType<BadgeCue>());

        Assert.Equal((Foe, 15, true), (badge.Unit, badge.Die, badge.Success));
    }

    [Fact]
    public void An_attack_roll_gets_no_badge()
        => Assert.Empty(Script(Near, Hit(1, 9)).Cues.OfType<BadgeCue>());

    [Fact]
    public void A_condition_saved_shows_its_roll_too()
        => Assert.Single(Script(Near, new ConditionSaved(Hero, new ConditionId("poisoned"), Roll(16, true))).Cues.OfType<BadgeCue>());

    [Fact]
    public void A_push_slides_rather_than_walks()
    {
        var script = Script(Near, new Pushed(Foe, new Tile(4, 3), new Tile(5, 3)));

        Assert.Equal((new Tile(4, 3), new Tile(5, 3)), Assert.Single(script.Cues.OfType<SlideCue>()) is { } s ? (s.From, s.To) : default);
    }

    [Fact]
    public void Someone_who_dies_stays_until_their_fall_has_played()
    {
        var script = Script(Near, new Damaged(Foe, 10, null, 0), new Died(Foe));

        var pose = script.Cues.OfType<PoseCue>().Single(p => p.Anim == "down");
        var gone = script.Cues.OfType<ApplyCue>().Single(a => a.Event is Died);

        Assert.True(gone.At >= pose.At + pose.Length - 0.001);
        Assert.True(pose.Length >= 0.2);
    }

    [Fact]
    public void A_new_unit_arrives_in_a_puff()
    {
        var script = Script(Near, new Spawned(new UnitId("x"), new Tile(6, 6), null));

        Assert.Equal(new Tile(6, 6), Assert.Single(script.Cues.OfType<BurstCue>()).Tile);
    }

    [Fact]
    public void A_line_is_spoken_and_held_long_enough_to_catch_its_start()
    {
        var script = Script(Near, new Said("narrator", "The ropes snap."));

        var line = Assert.Single(script.Cues.OfType<LineCue>());
        Assert.Equal(("narrator", "The ropes snap."), (line.Speaker, line.Line));
        Assert.True(script.Length >= 0.5);
    }

    [Fact]
    public void The_bridge_goes_after_the_walk_and_the_line_that_trigger_it()
    {
        Tile[] bridge = [new(5, 5), new(6, 5)];
        var script = Script(Near, new Moved(Hero, [new Tile(4, 5), new Tile(5, 5)]), new TriggerFired("bridge_collapse"),
            new Said("narrator", "The ropes snap!"), new TerrainAltered([.. bridge], -9), new Fell(Hero, 1, 0, true), new Downed(Hero));

        var walked = script.Cues.OfType<WalkCue>().Single();
        var said = script.Cues.OfType<LineCue>().Single();
        var sunk = script.Cues.OfType<ApplyCue>().Single(a => a.Event is TerrainAltered);
        var fall = script.Cues.OfType<FallCue>().Single();

        Assert.True(walked.At + walked.Length <= said.At + 0.001);
        Assert.True(said.At < sunk.At);
        Assert.Equal(sunk.At, fall.At);            // the ground goes, and they go with it
        Assert.Equal(1, fall.Levels);
        Assert.Contains(script.Cues.OfType<ShakeCue>(), s => s.At <= sunk.At && s.At + s.Length >= sunk.At);
        Assert.Equal(2, script.Cues.OfType<BurstCue>().Count(b => b.At == sunk.At && bridge.Contains(b.Tile)));
    }

    [Fact]
    public void A_bridge_that_goes_a_tile_at_a_time_is_still_one_collapse()
    {
        BattleEvent[] tiles = [.. Enumerable.Range(5, 4).SelectMany(x => new[] { new Tile(x, 5), new Tile(x, 6) }).Select(t => new TerrainAltered([t], -9))];

        var script = Script(Near, tiles);

        Assert.Single(script.Cues.OfType<ShakeCue>());
        Assert.Equal(8, script.Cues.OfType<ApplyCue>().Count(a => a.Event is TerrainAltered));
        Assert.InRange(script.Length, 1, 2);
    }

    [Fact]
    public void Someone_on_a_bridge_goes_down_with_it_and_the_rest_of_it_keeps_going()
    {
        var script = Script(Near, new TerrainAltered([new Tile(3, 3)], -9), new Fell(Hero, 2, 0, true), new Downed(Hero),
            new TerrainAltered([new Tile(4, 3)], -9), new TerrainAltered([new Tile(5, 3)], -9), new Said("warlord", "Ha!"));

        var altered = script.Cues.OfType<ApplyCue>().Where(a => a.Event is TerrainAltered).Select(a => a.At).ToArray();
        var line = script.Cues.OfType<LineCue>().Single();

        Assert.True(altered[2] - altered[0] < 0.6);        // the fall doesn't stop the collapse
        Assert.True(line.At >= altered[2] + BeatTiming.Collapse - 0.001);    // and the jeer waits for the dust
    }

    [Fact]
    public void A_boss_phase_is_marked()
    {
        var script = Script(Near, new PhaseEntered(Foe, 1));

        Assert.NotEmpty(script.Cues.OfType<ShakeCue>());
        Assert.Single(script.Cues.OfType<BurstCue>());
    }

    [Fact]
    public void A_beat_with_nothing_to_show_still_takes_a_moment()
    {
        var script = Script(Near, new TurnEnded(Hero), new TurnStarted(Foe, 1));

        Assert.InRange(script.Length, 0.1, 0.5);
    }

    [Fact]
    public void An_ability_with_no_presentation_still_works()
    {
        var script = Script(Near, new AbilityUsed(Hero, Attack, new Target(Unit: Foe), null), new Damaged(Foe, 1, null, 9));

        Assert.Single(script.Cues.OfType<PopCue>());
    }

    [Fact]
    public void An_ability_on_a_unit_the_view_has_lost_is_played_where_it_can_be()
    {
        var script = Script(Near, new AbilityUsed(new UnitId("gone"), Attack, new Target(Unit: new UnitId("also-gone")), null));

        Assert.True(script.Length >= 0);
    }

    [Fact]
    public void A_self_buff_is_a_pose_and_no_lunge()
    {
        var script = Script(Near, new AbilityUsed(Hero, Attack, new Target(Tile: new Tile(3, 3)), new PresentationDef("characters.cast", "fx.aura.holy", null)));

        Assert.Empty(script.Cues.OfType<LungeCue>());
        Assert.Single(script.Cues.OfType<PoseCue>(), p => p.Anim == "characters.cast");
        Assert.Single(script.Cues.OfType<BurstCue>());
    }

    [Fact]
    public void A_key_the_art_does_not_have_is_left_out()
        => Assert.Empty(Script(Near, Used("characters.attack", "fx.nonsense", "projectiles.nonsense", Foe)).Cues.OfType<BurstCue>());

    [Fact]
    public void Events_the_tv_has_no_use_for_are_skipped()
    {
        var script = Script(Near, new CameraHint(new Tile(1, 1)), new PassiveTriggered(Hero, "x"), new Marked(Foe, Hero));

        Assert.Empty(script.Cues.OfType<PopCue>());
    }

    [Fact]
    public void Everything_is_in_time_order_and_within_the_beat_or_fades_beyond_it_only_if_it_does_not_block()
    {
        var script = Script(Near, [.. Hit(3, 7), new ConditionApplied(Foe, new ConditionId("prone")), new TurnEnded(Hero)]);

        Assert.True(script.Cues.Select(c => c.At).SequenceEqual(script.Cues.Select(c => c.At).Order()));
        Assert.All(script.Cues, c => Assert.True(c.At <= script.Length));
    }

    [Fact]
    public void A_zone_appears_with_a_moment_for_the_camera()
    {
        var zone = new Zone(1, null, "fire", [new Tile(2, 2)], 2, false, false, []);

        Assert.Contains(Script(Near, new ZoneCreated(zone)).Cues.OfType<MomentCue>(), m => m.Label == "zone");
    }

    [Fact]
    public void An_offer_is_a_moment_worth_a_shot_once_it_has_been_applied()
    {
        var offer = new ReactionOffered(new ReactionOffer(new UnitId("hero1"), [], "leavesReach"));

        var cues = BeatScript.Of(new Beat(1, null, [offer]), new BattleView("e", "m", 1, [], null, [], [], null), new BattleView("e", "m", 1, [], null, [], [], null), FxArt.Load(Tactics.Presentation.Tests.Board.Pack.Root)).Cues;

        var applied = cues.Select((c, i) => (c, i)).Single(x => x.c is ApplyCue { Event: ReactionOffered }).i;
        var moment = cues.Select((c, i) => (c, i)).Single(x => x.c is MomentCue { Label: BeatPlayer.ReactionMoment }).i;
        Assert.True(applied < moment);
    }
}
