using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;

namespace Tactics.Presentation.Tests.Beats;

/// <summary><see cref="DisplayedBattle"/>: the battle as the player has seen it, a step behind the state.</summary>
public sealed class DisplayedBattleTests
{
    private static readonly UnitId A = new("a");
    private static readonly UnitId S = new("summon");

    private static UnitView Unit(UnitId id, int x = 1, int y = 1, int hp = 10, int tempHp = 0, string life = "Up", int countdown = 0, params string[] conditions)
        => new(id, id.Value, "Party", "characters.class_fighter", new Tile(x, y), hp, 10, tempHp, life, countdown, [.. conditions], null);

    private static BattleView View(BoardChanges? board = null, params UnitView[] units)
        => new("e", "m", 1, [], null, [.. units], [], null, board);

    private static UnitView After(BattleEvent e, UnitView unit) => DisplayedBattle.Apply(View(null, unit), e, View(null, unit)).Units.Single();

    [Fact]
    public void A_step_moves_the_unit_to_the_end_of_its_path()
        => Assert.Equal(new Tile(1, 3), After(new Moved(A, [new Tile(1, 1), new Tile(1, 2), new Tile(1, 3)]), Unit(A)).At);

    [Fact]
    public void A_push_and_a_teleport_move_it_too()
    {
        Assert.Equal(new Tile(2, 1), After(new Pushed(A, new Tile(1, 1), new Tile(2, 1)), Unit(A)).At);
        Assert.Equal(new Tile(7, 7), After(new Teleported(A, new Tile(1, 1), new Tile(7, 7)), Unit(A)).At);
    }

    [Fact]
    public void Damage_takes_temporary_hp_first()
    {
        var hit = After(new Damaged(A, 7, "fire", 8), Unit(A, tempHp: 5));

        Assert.Equal(8, hit.Hp);
        Assert.Equal(0, hit.TempHp);
    }

    [Fact]
    public void Healing_and_temporary_hp_set_what_they_say()
    {
        Assert.Equal(9, After(new Healed(A, 4, 9), Unit(A, hp: 5)).Hp);
        Assert.Equal(6, After(new TempHpGained(A, 6), Unit(A)).TempHp);
    }

    [Fact]
    public void Conditions_come_and_go()
    {
        var poisoned = new ConditionId("poisoned");

        Assert.Equal(["poisoned"], After(new ConditionApplied(A, poisoned), Unit(A)).Conditions);
        Assert.Equal(["poisoned"], After(new ConditionApplied(A, poisoned), Unit(A, conditions: "poisoned")).Conditions);
        Assert.Empty(After(new ConditionRemoved(A, poisoned), Unit(A, conditions: "poisoned")).Conditions);
    }

    [Fact]
    public void Going_down_and_coming_back()
    {
        var downed = After(new Downed(A), Unit(A, hp: 0));
        Assert.Equal("Downed", downed.Life);
        Assert.Equal(DownedRules.StartingCountdown, downed.Countdown);

        Assert.Equal(2, After(new CountdownTicked(A, 2), downed).Countdown);

        var back = After(new Revived(A, 4), downed);
        Assert.Equal(("Up", 4, 0), (back.Life, back.Hp, back.Countdown));
    }

    [Fact]
    public void Out_and_dead_leave_the_board()
    {
        Assert.Equal("Out", After(new UnitOut(A, new InjuryId("scar")), Unit(A, hp: 0, life: "Downed")).Life);
        Assert.Equal("Dead", After(new Died(A), Unit(A, hp: 0)).Life);
    }

    [Fact]
    public void A_summon_arrives_from_the_live_view_on_the_tile_of_its_event()
    {
        var live = View(null, Unit(A), Unit(S, x: 9, y: 9, hp: 3));

        var shown = DisplayedBattle.Apply(View(null, Unit(A)), new Spawned(S, new Tile(4, 4), "beasts.wolf"), live);

        var summon = Assert.Single(shown.Units, u => u.Id == S);
        Assert.Equal(new Tile(4, 4), summon.At);
        Assert.Equal(3, summon.Hp);
    }

    [Fact]
    public void A_summon_that_has_left_the_live_view_too_is_skipped()
        => Assert.Single(DisplayedBattle.Apply(View(null, Unit(A)), new Spawned(S, new Tile(4, 4), null), View(null, Unit(A))).Units);

    [Fact]
    public void A_summon_leaves()
        => Assert.Equal([A], DisplayedBattle.Apply(View(null, Unit(A), Unit(S)), new Despawned(S), View()).Units.Select(u => u.Id));

    [Fact]
    public void A_new_form_with_a_sprite_key_shows_at_once()
        => Assert.Equal("beasts.wolf.grey", After(new Transformed(A, "beasts.wolf.grey"), Unit(A)).Sprite);

    [Fact]
    public void A_form_named_without_a_sprite_takes_the_live_views_key()
    {
        var live = View(null, Unit(A) with { Sprite = "characters.class_druid" });

        var shown = DisplayedBattle.Apply(View(null, Unit(A) with { Sprite = "beasts.wolf" }), new Transformed(A, "Ana"), live);

        Assert.Equal("characters.class_druid", shown.Units.Single().Sprite);
    }

    [Fact]
    public void The_turn_and_round_follow_the_events()
    {
        var shown = DisplayedBattle.Apply(View(), new RoundStarted(4, [A]), View());
        shown = DisplayedBattle.Apply(shown, new TurnStarted(A, 4), View());

        Assert.Equal((4, A), (shown.Round, shown.Active));
        Assert.Equal([A], shown.Order);
    }

    [Fact]
    public void A_zone_comes_from_its_event_and_ends_by_serial()
    {
        var zone = new Zone(7, null, "fire", [new Tile(2, 2)], 3, false, false, []);

        var shown = DisplayedBattle.Apply(View(BoardChanges.None), new ZoneCreated(zone), View());
        Assert.Equal(7, Assert.Single(shown.Board!.Zones).Serial);

        Assert.Empty(DisplayedBattle.Apply(shown, new ZoneEnded(7), View()).Board!.Zones);
    }

    [Fact]
    public void A_battle_with_no_board_yet_can_still_take_a_zone()
    {
        var zone = new Zone(7, null, "fire", [new Tile(2, 2)], 3, false, false, []);

        Assert.Single(DisplayedBattle.Apply(View(), new ZoneCreated(zone), View()).Board!.Zones);
    }

    [Fact]
    public void Altered_tiles_take_their_height_and_depth_from_the_live_view_because_the_event_has_neither_clamp_nor_depth()
    {
        Tile[] bridge = [new(5, 5), new(6, 5)];
        var live = View(new BoardChanges([], [new HeightChange(bridge[0], -1), new HeightChange(bridge[1], -1), new HeightChange(new Tile(1, 1), 2)],
            [bridge[0], bridge[1]]));

        var shown = DisplayedBattle.Apply(View(BoardChanges.None), new TerrainAltered([.. bridge], -9), live);

        Assert.Equal([new HeightChange(bridge[0], -1), new HeightChange(bridge[1], -1)], shown.Board!.Heights);   // not (1, 1): that one hasn't happened yet
        Assert.Equal(bridge, shown.Board.Sunk);
    }

    [Fact]
    public void A_tile_raised_back_to_its_own_height_drops_its_change()
    {
        var tile = new Tile(5, 5);
        var was = new BoardChanges([], [new HeightChange(tile, 2)], []);

        var shown = DisplayedBattle.Apply(View(was), new TerrainAltered([tile], -2), View(BoardChanges.None));

        Assert.Empty(shown.Board!.Heights);
    }

    [Fact]
    public void An_event_for_a_unit_it_does_not_know_changes_nothing()
    {
        var view = View(null, Unit(A));

        Assert.Equal(view.Units, DisplayedBattle.Apply(view, new Damaged(S, 3, null, 1), view).Units);
    }

    [Fact]
    public void Same_board_compares_contents()
    {
        var one = new BoardChanges([new ZoneView(1, "fire", [new Tile(1, 1)])], [new HeightChange(new Tile(2, 2), 1)], [new Tile(3, 3)]);
        var two = new BoardChanges([new ZoneView(1, "fire", ImmutableArray.Create(new Tile(1, 1)))], [new HeightChange(new Tile(2, 2), 1)], [new Tile(3, 3)]);

        Assert.True(DisplayedBattle.SameBoard(one, two));
        Assert.True(DisplayedBattle.SameBoard(null, BoardChanges.None));
        Assert.False(DisplayedBattle.SameBoard(one, BoardChanges.None));
    }

    [Fact]
    public void Same_board_does_not_care_what_order_things_happened_in()
    {
        var one = new BoardChanges([], [new HeightChange(new Tile(1, 1), 1), new HeightChange(new Tile(2, 2), 1)], [new Tile(1, 1), new Tile(2, 2)]);
        var two = new BoardChanges([], [new HeightChange(new Tile(2, 2), 1), new HeightChange(new Tile(1, 1), 1)], [new Tile(2, 2), new Tile(1, 1)]);

        Assert.True(DisplayedBattle.SameBoard(one, two));
    }

    [Fact]
    public void A_changed_objective_shows_when_its_event_plays()
    {
        var next = new ObjectivesDef(ObjectiveExpr.Of("survive:3"), ObjectiveExpr.Of("party_out"));

        var after = DisplayedBattle.Apply(View(), new ObjectiveChanged(next), View());

        Assert.Same(next, after.Objectives);
    }

    [Fact]
    public void An_offer_marks_who_can_react_and_an_answer_clears_it()
    {
        var offer = new ReactionOffer(A, [], "leavesReach");

        var offered = DisplayedBattle.Apply(View(), new ReactionOffered(offer), View());
        Assert.Equal(A, offered.Reacting);

        Assert.Null(DisplayedBattle.Apply(offered, new Reacted(A, "pass"), View()).Reacting);
    }

    [Fact]
    public void Someone_elses_answer_leaves_the_offer_up()
    {
        var offered = DisplayedBattle.Apply(View(), new ReactionOffered(new ReactionOffer(A, [], "leavesReach")), View());

        Assert.Equal(A, DisplayedBattle.Apply(offered, new Reacted(S, "pass"), View()).Reacting);
    }

    private static UnitView Undone(UnitView unit, params BattleEvent[] events)
        => DisplayedBattle.Before(View(null, unit), new Beat(1, null, [.. events])).Units.Single();

    [Fact]
    public void Undoing_a_walk_a_push_and_a_teleport_stands_a_unit_where_it_began()
    {
        Assert.Equal(new Tile(1, 1), Undone(Unit(A, 1, 3), new Moved(A, [new Tile(1, 1), new Tile(1, 2), new Tile(1, 3)])).At);
        Assert.Equal(new Tile(1, 1), Undone(Unit(A, 2, 1), new Pushed(A, new Tile(1, 1), new Tile(2, 1))).At);
        Assert.Equal(new Tile(1, 1), Undone(Unit(A, 7, 7), new Teleported(A, new Tile(1, 1), new Tile(7, 7))).At);
    }

    [Fact]
    public void Undoing_runs_backwards_through_the_beat()
    {
        var walked = Undone(Unit(A, 3, 1), new Moved(A, [new Tile(1, 1), new Tile(2, 1)]), new Moved(A, [new Tile(2, 1), new Tile(3, 1)]));

        Assert.Equal(new Tile(1, 1), walked.At);
    }

    [Fact]
    public void Undoing_a_killing_blow_stands_the_unit_back_up_with_its_hp()
    {
        var back = Undone(Unit(A, hp: 0, life: "Dead"), new Damaged(A, 7, null, 0), new Died(A));

        Assert.Equal("Up", back.Life);
        Assert.Equal(7, back.Hp);
    }

    [Fact]
    public void Undoing_a_downing_takes_the_countdown_too()
    {
        var back = Undone(Unit(A, hp: 0, life: "Downed", countdown: 3), new Damaged(A, 4, null, 0), new Downed(A));

        Assert.Equal("Up", back.Life);
        Assert.Equal(0, back.Countdown);
        Assert.Equal(4, back.Hp);
    }

    [Fact]
    public void Undoing_damage_never_passes_full_health()
        => Assert.Equal(10, Undone(Unit(A, hp: 8), new Damaged(A, 30, null, 8)).Hp);

    [Fact]
    public void Undoing_a_heal_and_a_revival()
    {
        Assert.Equal(4, Undone(Unit(A, hp: 9), new Healed(A, 5, 9)).Hp);
        var fallen = Undone(Unit(A, hp: 6), new Revived(A, 6));
        Assert.Equal(("Downed", 0), (fallen.Life, fallen.Hp));
    }

    [Fact]
    public void Undoing_going_out_leaves_the_unit_on_the_ground_as_only_a_downed_unit_goes_out()
        => Assert.Equal("Downed", Undone(Unit(A, hp: 0, life: "Out"), new UnitOut(A, new InjuryId("scar"))).Life);

    [Fact]
    public void Conditions_stay_as_the_state_has_them()
        => Assert.Equal(["prone"], Undone(Unit(A, conditions: "prone"), new ConditionApplied(A, new ConditionId("prone"))).Conditions);

    [Fact]
    public void Undoing_a_spawn_takes_the_unit_off_until_it_arrives()
    {
        var view = View(null, Unit(A), Unit(S));

        var before = DisplayedBattle.Before(view, new Beat(1, null, [new Spawned(S, new Tile(1, 1), null)]));

        Assert.Equal([A], before.Units.Select(u => u.Id));
    }

    [Fact]
    public void Undoing_turns_puts_the_active_unit_back()
    {
        var view = View(null, Unit(A), Unit(S)) with { Active = S };

        var before = DisplayedBattle.Before(view, new Beat(1, null, [new TurnEnded(A), new TurnStarted(S, 1)]));

        Assert.Equal(A, before.Active);
    }

    [Fact]
    public void Undoing_an_offer_takes_it_down_until_its_beat_raises_it()
    {
        var view = View() with { Reacting = A };

        Assert.Null(DisplayedBattle.Before(view, new Beat(1, null, [new ReactionOffered(new ReactionOffer(A, [], "leavesReach"))])).Reacting);
    }

    [Fact]
    public void Undoing_a_broken_beat_changes_nothing_and_does_not_throw()
    {
        var unit = Unit(A, 3, 3);

        Assert.Equal(unit, Undone(unit, new Moved(A, [])));
    }

    [Fact]
    public void Undoing_an_event_about_nobody_here_changes_nothing()
    {
        var unit = Unit(A);

        Assert.Equal(unit, Undone(unit, new Moved(S, [new Tile(4, 4), new Tile(5, 5)]), new Died(S)));
    }
}
