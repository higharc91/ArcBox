using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>#106: bosses are guarded against deep-tile pushes and can't skip a phase; everyone else dies in deep water as before.</summary>
public sealed class SteadfastTests
{
    // Row 0: a bank, then two tiles of river. Row 1: bank, bank, then river.
    private const string River = """
        0. 0. 0. 0x 0x
        0. 0. 0. 0. 0x
        """;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static readonly UnitSheet Goblin = TestSheets.Goblin with { MaxHp = 30 };

    private static UnitSheet Steadfast(UnitSheet sheet) => sheet with { Tags = sheet.Tags.Add(Tactics.Logic.Steadfast.Tag) };

    private static string Json(ImmutableArray<BattleEvent> events) => JsonSerializer.Serialize(events, ArcJson.Options);

    private static (BattleState State, ImmutableArray<BattleEvent> Events) Push(TestBattle battle, BattleState state, string source,
        string target, Tile aim, int distance)
        => battle.Run(state, source, target, aim, new PushEffect(distance));

    [Fact]
    public void A_guarded_unit_pushed_toward_deep_stops_on_the_last_tile_that_is_not_deep()
    {
        var battle = TestBattle.On(River)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("boss", Steadfast(Goblin), T(1, 0));
        var (state, _) = battle.Start();

        // Pushed 3: (2, 0) is dry, (3, 0) is deep. It stops on (2, 0) and the push ends there.
        var (after, events) = Push(battle, state, "kara", "boss", T(1, 0), 3);

        Assert.Equal(Json([new Pushed(U("boss"), T(1, 0), T(2, 0)), new PushResisted(U("boss"), T(2, 0))]), Json(events));
        var boss = after.Units[U("boss")];
        Assert.Equal(T(2, 0), boss.Pos);
        Assert.Equal(LifeState.Up, boss.Life);
        Assert.Equal(30, boss.Hp);
        Assert.DoesNotContain(events, e => e is Fell or Died);
    }

    [Fact]
    public void A_guarded_unit_on_the_edge_does_not_move_at_all()
    {
        var battle = TestBattle.On(River)
            .Party("kara", TestSheets.Fighter, T(1, 0))
            .Enemy("boss", Steadfast(Goblin), T(2, 0));
        var (state, _) = battle.Start();

        var (after, events) = Push(battle, state, "kara", "boss", T(2, 0), 2);

        Assert.Equal(Json([new PushResisted(U("boss"), T(2, 0))]), Json(events));
        Assert.Equal(T(2, 0), after.Units[U("boss")].Pos);
        Assert.Equal(LifeState.Up, after.Units[U("boss")].Life);
    }

    [Fact]
    public void A_pull_cannot_drag_a_guarded_unit_into_deep_either()
    {
        const string bay = """
            0. 0x 0. 0.
            0. 0. 0. 0.
            """;
        var battle = TestBattle.On(bay)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("boss", Steadfast(Goblin), T(2, 0));
        var (state, _) = battle.Start();

        var (after, events) = battle.Run(state, "kara", "boss", T(2, 0), new PullEffect(2));

        Assert.Equal(T(2, 0), after.Units[U("boss")].Pos);
        Assert.Contains(events, e => e is PushResisted);
        Assert.Equal(LifeState.Up, after.Units[U("boss")].Life);
    }

    [Fact]
    public void An_ordinary_enemy_and_a_hero_keep_the_old_rule_and_fall_in()
    {
        var enemyFalls = TestBattle.On(River)
            .Party("kara", TestSheets.Fighter, T(1, 0))
            .Enemy("gob", Goblin, T(2, 0));
        var (state, _) = enemyFalls.Start();
        var (after, events) = Push(enemyFalls, state, "kara", "gob", T(2, 0), 2);

        Assert.Contains(events, e => e is Fell { IntoDeep: true });
        Assert.Equal(LifeState.Dead, after.Units[U("gob")].Life);
        Assert.DoesNotContain(events, e => e is PushResisted);

        var heroFalls = TestBattle.On(River)
            .Enemy("gob", Goblin, T(1, 0))
            .Party("kara", TestSheets.Fighter, T(2, 0));
        (state, _) = heroFalls.Start();
        var (afterHero, heroEvents) = Push(heroFalls, state, "gob", "kara", T(2, 0), 2);
        Assert.Equal(LifeState.Downed, afterHero.Units[U("kara")].Life);
        Assert.DoesNotContain(heroEvents, e => e is PushResisted);
    }

    [Fact]
    public void A_unit_with_a_phase_trigger_is_guarded_without_the_flag()
    {
        var phase = new PhaseDef(50, [new AbilityId("boss.warcry")], [], null);
        var battle = TestBattle.On(River)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("boss", Goblin with { Tags = Goblin.Tags.Add("warlord") }, T(2, 0))
            .Enemy("grunt", Goblin, T(2, 1))
            .WithTriggers(new TriggerDef("enraged", new TriggerWhen(HpBelow: new HpWatch("warlord", 50)), Once: true,
                [new TriggerAction(Phase: new PhaseAction("warlord", 1, phase))]));
        var (state, _) = battle.Start();

        var (after, events) = Push(battle, state, "kara", "boss", T(1, 0), 2);
        Assert.Equal(T(2, 0), after.Units[U("boss")].Pos);
        Assert.Contains(events, e => e is PushResisted);
        Assert.Equal(LifeState.Up, after.Units[U("boss")].Life);

        // The grunt, with no phase and no flag, is not guarded: pushed east along row 1 it ends in the river at (4, 1).
        var (afterGrunt, _) = Push(battle, state, "kara", "grunt", T(1, 1), 3);
        Assert.Equal(LifeState.Dead, afterGrunt.Units[U("grunt")].Life);
    }

    private static (TestBattle Battle, BattleState State) Phased(int maxHp, int hp)
    {
        var phase = new PhaseDef(50, [new AbilityId("boss.warcry")], [], null);
        var battle = TestBattle.On(River)
            .Party("kara", TestSheets.Fighter with { InitiativeBonus = 20 }, T(0, 0))
            .Enemy("boss", Goblin with { MaxHp = maxHp, Tags = Goblin.Tags.Add("warlord") }, T(2, 0))
            .WithTriggers(new TriggerDef("enraged", new TriggerWhen(HpBelow: new HpWatch("warlord", 50)), Once: true,
                [new TriggerAction(Phase: new PhaseAction("warlord", 1, phase))]));
        var (state, _) = battle.Start();
        return (battle, state.Change("boss", u => u with { Hp = hp }));
    }

    private static int Hit(TestBattle battle, BattleState state, int amount, out BattleState after)
    {
        var w = new BattleWriter(state, battle.Rules);
        var dealt = Hp.Damage(w, U("boss"), amount, "slashing", U("kara"));
        after = w.Freeze();
        return dealt;
    }

    [Fact]
    public void A_hit_that_would_cross_the_phase_threshold_leaves_the_unit_just_below_it()
    {
        var (battle, state) = Phased(maxHp: 100, hp: 60);

        Hit(battle, state, 500, out var after);

        // The most HP that is still below 50% of 100: 49. The excess damage is discarded.
        Assert.Equal(49, after.Units[U("boss")].Hp);
        Assert.Equal(LifeState.Up, after.Units[U("boss")].Life);
    }

    [Fact]
    public void A_held_hit_reports_only_the_damage_it_dealt()
    {
        var (battle, state) = Phased(maxHp: 100, hp: 60);
        var w = new BattleWriter(state, battle.Rules);

        var dealt = Hp.Damage(w, U("boss"), 500, "slashing", U("kara"));

        Assert.Equal(11, dealt);   // 60 down to 49
        Assert.Equal(new Damaged(U("boss"), 11, "slashing", 49), Assert.Single(w.Events.OfType<Damaged>()));
    }

    [Fact]
    public void The_phase_fires_after_a_clamped_hit_and_a_second_hit_is_free_to_kill()
    {
        var (battle, state) = Phased(maxHp: 100, hp: 60);
        Hit(battle, state, 500, out var clamped);

        // The next checkpoint fires the trigger and the phase.
        var (afterTurn, events) = battle.Apply(clamped, new EndTurn(U("kara")));
        Assert.Contains(new PhaseEntered(U("boss"), 1), events);

        Hit(battle, afterTurn, 500, out var dead);
        Assert.Equal(LifeState.Dead, dead.Units[U("boss")].Life);
    }

    [Fact]
    public void Several_damage_effects_in_one_hit_still_trigger_only_one_phase()
    {
        var (battle, state) = Phased(maxHp: 100, hp: 60);

        Hit(battle, state, 20, out var first);            // 40 would be below 50: clamped to 49
        Hit(battle, first, 20, out var second);           // same hit, the trigger has not run yet: nothing more is taken

        Assert.Equal(49, first.Units[U("boss")].Hp);
        Assert.Equal(49, second.Units[U("boss")].Hp);
    }

    [Fact]
    public void Damage_that_stays_above_the_threshold_is_untouched_and_so_is_a_unit_without_a_phase()
    {
        var (battle, state) = Phased(maxHp: 100, hp: 90);
        Hit(battle, state, 30, out var after);
        Assert.Equal(60, after.Units[U("boss")].Hp);

        var plain = TestBattle.On(River)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("boss", Goblin, T(2, 0));
        var (start, _) = plain.Start();
        var w = new BattleWriter(start, plain.Rules);
        Hp.Damage(w, U("boss"), 500, "slashing", U("kara"));
        Assert.Equal(LifeState.Dead, w.Freeze().Units[U("boss")].Life);
    }

    [Fact]
    public void Steadfast_loads_from_content_for_the_villains_and_Ashmaw()
    {
        var rules = BasePackTests.Rules;
        foreach (var id in new[] { "maren_sorrel", "vorga", "halvard", "ivo_rime", "mother_skarrow", "ashmaw", "ashmaw_party" })
        {
            Assert.True(rules.Monsters[id].Steadfast, id);
            Assert.Contains(Tactics.Logic.Steadfast.Tag, SheetBuilder.ForMonster(id, rules).Tags);
        }
        Assert.False(rules.Monsters["goblin"].Steadfast);
        Assert.DoesNotContain(Tactics.Logic.Steadfast.Tag, SheetBuilder.ForMonster("goblin", rules).Tags);
    }

    [Fact]
    public void Hall_of_the_Flame_cannot_be_won_by_a_round_one_shove_on_Maren()
    {
        var rules = BasePackTests.Rules;
        var pushed = false;
        for (ulong seed = 1; seed <= 24; seed++)
        {
            var setup = Encounters.Setup("hall_of_the_flame", BasePackTests.Party(4, rules), Difficulty.Normal, rules);
            // The altar fills (9, 5), so no one can shove Maren north from her start tile: she stands one tile east on the
            // plateau, as she may after a step (her guards are left out of the way), with (10, 3) lava to her north and the fighter, with the first turn, south of her.
            setup = setup with { Units = [.. setup.Units.Where(u => u.Sheet.Side == Side.Party || u.Id == U("maren_sorrel")).Select(u => u.Id == U("fighter")
                ? u with { At = T(10, 5), Sheet = u.Sheet with { InitiativeBonus = 100 } }
                : u.Id == U("maren_sorrel") ? u with { At = T(10, 4) } : u)] };
            var state = Battle.Start(setup, rules, seed).State;
            Assert.Equal(U("fighter"), state.Active);

            var result = Battle.Apply(state, new Use(U("fighter"), new AbilityId("common.shove"), new Target(U("maren_sorrel"))), rules);
            Assert.Null(result.Rejected);

            var maren = result.State.Units[U("maren_sorrel")];
            Assert.Equal(LifeState.Up, maren.Life);
            Assert.Null(result.State.Outcome);
            if (result.Events.Any(e => e is PushResisted))
            {
                pushed = true;
                Assert.Equal(T(10, 4), maren.Pos);
            }
            Assert.DoesNotContain(result.Events, e => e is Fell or Died);
        }
        Assert.True(pushed, "no seed failed Maren's Strength save in 24 tries");
    }

    // A bridge over a chasm: the middle tile is five levels up and about to collapse.
    private const string Bridge = "0. 0. 5. 0. 0.";

    private static readonly AlterTerrainEffect Collapse = new(new AreaDef(AreaShape.Single), -9, Deep: true);

    [Fact]
    public void A_guarded_unit_on_a_collapsing_tile_lands_on_the_nearest_free_bank()
    {
        var battle = TestBattle.On(Bridge)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("gob", Goblin, T(1, 0))
            .Enemy("boss", Steadfast(Goblin with { MaxHp = 60 }), T(2, 0));
        var (state, _) = battle.Start();

        var (after, events) = battle.Run(state, "kara", null, T(2, 0), Collapse);

        // (1, 0) is taken, so the nearest free bank is (3, 0).
        var boss = after.Units[U("boss")];
        Assert.Equal(T(3, 0), boss.Pos);
        Assert.Equal(LifeState.Up, boss.Life);
        Assert.Contains(new PushResisted(U("boss"), T(3, 0)), events);
        var fell = Assert.Single(events.OfType<Fell>());
        Assert.False(fell.IntoDeep);
        Assert.Equal(5, fell.Levels);
        Assert.True(boss.Hp < 60 && boss.Hp > 0);
        Assert.DoesNotContain(events, e => e is Died);
        Assert.Contains(T(2, 0), after.Sunk!);
    }

    [Fact]
    public void An_ordinary_unit_on_a_collapsing_tile_still_falls_in_and_dies()
    {
        var battle = TestBattle.On(Bridge)
            .Party("kara", TestSheets.Fighter, T(0, 0))
            .Enemy("gob", Goblin, T(2, 0));
        var (state, _) = battle.Start();

        var (after, events) = battle.Run(state, "kara", null, T(2, 0), Collapse);

        Assert.Equal(LifeState.Dead, after.Units[U("gob")].Life);
        Assert.Contains(events, e => e is Fell { IntoDeep: true });
        Assert.DoesNotContain(events, e => e is PushResisted);
    }

    [Fact]
    public void Vorga_on_the_Warlord_Bridge_survives_the_collapse_with_his_phase_intact()
    {
        var rules = BasePackTests.Rules;
        var setup = Encounters.Setup("warlord_bridge", BasePackTests.Party(4, rules), Difficulty.Normal, rules);
        var bridge = setup.Map.Zones["bridge"];
        setup = setup with { Units = [.. setup.Units.Select(u => u.Id == U("fighter") ? u with { At = bridge[0] }
            : u.Id == U("warlord") ? u with { At = bridge[^1] } : u)] };

        var (state, events) = (Battle.Start(setup, rules, 3).State, Battle.Start(setup, rules, 3).Events);

        Assert.Contains(new TriggerFired("bridge_collapse"), events);
        var vorga = state.Units[U("warlord")];
        Assert.Equal(LifeState.Up, vorga.Life);
        Assert.Contains(events, e => e is PushResisted { Unit.Value: "warlord" });
        Assert.DoesNotContain(events, e => e is Died { Unit.Value: "warlord" });
        Assert.DoesNotContain(vorga.Pos, state.Sunk!);

        // His phase is still ahead of him: a huge hit holds at the threshold and the phase then fires.
        var w = new BattleWriter(state, rules);
        Hp.Damage(w, U("warlord"), 5000, "slashing", U("fighter"));
        var held = w.Freeze();
        Assert.Equal(LifeState.Up, held.Units[U("warlord")].Life);
        var (_, later) = (held, Battle.Apply(held, new EndTurn(held.Active!.Value), rules).Events);
        Assert.Contains(new PhaseEntered(U("warlord"), 1), later);
    }
}
