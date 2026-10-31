using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Hud;

/// <summary><see cref="BattleHud"/>: what the battle HUD says, from the battle as the player has seen it.</summary>
public sealed class BattleHudTests
{
    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));

    private static UnitId Id(string id) => new(id);

    private static UnitView Unit(string id, string side = "Party", string life = "Up", string? player = null, string? sprite = "characters.class_fighter")
        => new(Id(id), id, side, sprite, new Tile(5, 5), 10, 10, 0, life, 0, [], player);

    private static BattleView Battle(UnitId? active, int round, UnitView[] units, string[]? order = null, UnitId? reacting = null, ObjectivesDef? objectives = null)
        => new("warlord_bridge", "warlord_bridge", round, [.. (order ?? units.Select(u => u.Id.Value).ToArray()).Select(Id)], active, [.. units], [], null, BoardChanges.None,
            objectives, reacting);

    private static readonly UnitView[] Four =
    [
        Unit("hero1", player: "Ana"),
        Unit("hero2", player: "Bo"),
        Unit("goblin.1", "Enemy"),
        Unit("goblin.2", "Enemy"),
    ];

    private static BattleHud Hud(BattleView shown, BattleView? live = null, string banner = "", BattleHud? previous = null, UnitId? aiFinished = null)
        => BattleHud.Of(shown, live ?? shown, banner, previous, aiFinished);

    [Fact]
    public void The_order_starts_with_the_active_unit_and_wraps()
    {
        var hud = Hud(Battle(Id("goblin.1"), 2, Four));

        Assert.Equal(["goblin.1", "goblin.2", "hero1", "hero2"], hud.Order.Select(s => s.Id.Value));
        Assert.Equal([true, false, false, false], hud.Order.Select(s => s.Active));
    }

    [Fact]
    public void Between_two_turns_the_strip_stays_where_it_was_with_nobody_marked()
    {
        var during = Hud(Battle(Id("goblin.1"), 2, Four));

        var between = Hud(Battle(null, 2, Four), previous: during);

        Assert.Equal("goblin.1", between.Order[0].Id.Value);
        Assert.DoesNotContain(between.Order, s => s.Active);
    }

    [Fact]
    public void Units_out_of_the_fight_leave_the_strip_at_once()
    {
        UnitView[] units = [Unit("hero1", player: "Ana"), Unit("hero2", life: "Out"), Unit("goblin.1", "Enemy", "Dead"), Unit("goblin.2", "Enemy")];

        var hud = Hud(Battle(Id("hero1"), 1, units));

        Assert.Equal(["hero1", "goblin.2"], hud.Order.Select(s => s.Id.Value));
    }

    [Fact]
    public void When_the_lead_falls_the_strip_goes_on_from_its_place()
    {
        var during = Hud(Battle(Id("hero2"), 1, Four));
        UnitView[] fallen = [Four[0], Unit("hero2", life: "Dead"), Four[2], Four[3]];

        var after = Hud(Battle(null, 1, fallen), previous: during);

        Assert.Equal(["goblin.1", "goblin.2", "hero1"], after.Order.Select(s => s.Id.Value));
    }

    [Fact]
    public void A_downed_unit_keeps_its_place_and_says_so()
    {
        UnitView[] units = [Four[0], Unit("hero2", life: "Downed"), Four[2]];

        var hud = Hud(Battle(Id("hero1"), 1, units));

        Assert.Equal([false, true, false], hud.Order.Select(s => s.Downed));
    }

    [Fact]
    public void A_unit_in_the_order_but_not_in_the_list_is_left_out()
        => Assert.Equal(2, Hud(Battle(Id("hero1"), 1, [Four[0], Four[1]], ["hero1", "ghost", "hero2"])).Order.Length);

    [Fact]
    public void A_long_order_shows_the_next_few_and_counts_the_rest()
    {
        UnitView[] units = [.. Enumerable.Range(1, BattleHud.Slots + 6).Select(i => Unit($"goblin.{i}", "Enemy"))];

        var hud = Hud(Battle(units[3].Id, 1, units));

        Assert.Equal(BattleHud.Slots, hud.Order.Length);
        Assert.Equal(6, hud.More);
        Assert.Equal("goblin.4", hud.Order[0].Id.Value);
    }

    [Fact]
    public void Only_a_character_whose_player_is_away_gets_the_ai_badge()
    {
        UnitView[] units =
        [
            Unit("hero1", player: "Ana"),
            Unit("hero2"),                      // nobody at this phone
            Unit("ally1"),                      // a companion is always the AI, and nobody's absent
            Unit("hound.1", sprite: "bosses.hound.tan"),
            Unit("goblin.1", "Enemy"),
        ];

        var hud = Hud(Battle(Id("hero1"), 1, units));

        Assert.Equal([false, true, false, false, false], hud.Order.Select(s => s.Ai));
    }

    [Theory]
    [InlineData("hero1", "Party", "stripe_p1")]
    [InlineData("hero6", "Party", "stripe_p6")]
    [InlineData("hero7", "Party", "stripe_neutral")]
    [InlineData("ally1", "Party", "stripe_neutral")]
    [InlineData("hound.1", "Party", "stripe_neutral")]
    [InlineData("goblin.1", "Enemy", "stripe_enemy")]
    [InlineData("farmer", "Neutral", "stripe_neutral")]
    public void Each_unit_has_its_stripe(string id, string side, string stripe)
        => Assert.Equal(stripe, Hud(Battle(null, 1, [Unit(id, side)])).Order[0].Stripe);

    [Fact]
    public void The_turn_and_round_come_from_the_battle_shown_not_the_live_one()
    {
        var shown = Battle(Id("hero1"), 2, Four);
        var live = Battle(Id("goblin.2"), 3, Four);

        var hud = Hud(shown, live, "goblin.2 is acting");

        Assert.Equal("Round 2", hud.Round);
        Assert.Equal("Ana's turn", hud.Turn);
        Assert.Equal("hero1", hud.Order[0].Id.Value);
    }

    [Fact]
    public void An_ai_turn_says_who_is_acting()
        => Assert.Equal("goblin.1 is acting", Hud(Battle(Id("goblin.1"), 1, Four)).Turn);

    [Fact]
    public void A_turn_the_ai_is_finishing_is_not_the_players_any_more()
        => Assert.Equal("hero1 is acting", Hud(Battle(Id("hero1"), 1, Four), aiFinished: Id("hero1")).Turn);

    [Fact]
    public void Between_turns_no_turn_is_named()
        => Assert.Null(Hud(Battle(null, 1, Four)).Turn);

    [Fact]
    public void The_objective_is_the_one_on_screen()
    {
        var shown = Battle(null, 1, Four, objectives: new ObjectivesDef(ObjectiveExpr.Of("defeat_all"), ObjectiveExpr.Of("party_out")));
        var live = Battle(null, 1, Four, objectives: new ObjectivesDef(ObjectiveExpr.Of("survive:3"), ObjectiveExpr.Of("party_out")));

        Assert.Equal("Defeat every enemy", Hud(shown, live).Objective);
    }

    [Fact]
    public void The_title_is_the_encounter()
        => Assert.Equal("Warlord Bridge", Hud(Battle(null, 1, Four)).Title);

    [Fact]
    public void A_waiting_reaction_comes_straight_from_the_live_view()
    {
        var shown = Battle(Id("goblin.1"), 1, Four);
        var live = Battle(Id("goblin.1"), 1, Four, reacting: Id("hero2"));

        Assert.Equal("Bo can react", Hud(shown, live, "Bo can react").Prompt);
    }

    [Fact]
    public void A_shown_view_left_over_from_a_prompt_does_not_bring_it_back()
    {
        var shown = Battle(Id("goblin.1"), 1, Four, reacting: Id("hero2"));
        var live = Battle(Id("goblin.1"), 1, Four);

        Assert.Null(Hud(shown, live, "goblin.1 is acting").Prompt);
    }

    [Fact]
    public void The_badge_stands_over_the_units_marks()
    {
        var fit = new BoardFit(new Vector2(800, 300), 2);
        var units = UnitPlan.Build(Battle(Id("hero1"), 1, [Four[0]]), new BoardTerrain(Pack.Map("river_crossing"), null), Art.Value, fit);
        var unit = units[0];

        var at = BattleHud.BadgeAt(unit, fit, new Vector2(120, 40));

        Assert.Equal(fit.ToScreen(unit.Feet).X - 60, at.X, 1);
        Assert.True(at.Y + 40 <= fit.ToScreen(unit.ArrowAt!.Value).Y - 10, $"{at.Y}");
    }

    [Theory]
    [InlineData(-400, 300, BattleHud.ScreenGutter, 300)]
    [InlineData(3000, 300, 1920 - 120 - BattleHud.ScreenGutter, 300)]
    [InlineData(800, -400, 800, BattleHud.ScreenGutter)]
    public void The_badge_stays_on_the_screen(float x, float y, float expectedX, float expectedY)
        => Assert.Equal(new Vector2(expectedX, expectedY), BattleHud.Clamp(new Vector2(x, y), new Vector2(120, 40)));

    [Fact]
    public void A_panel_over_a_unit_knows_it()
    {
        var fit = new BoardFit(new Vector2(800, 300), 2);
        var units = UnitPlan.Build(Battle(null, 1, [Four[0]]), new BoardTerrain(Pack.Map("river_crossing"), null), Art.Value, fit);
        var feet = fit.ToScreen(units[0].Feet);

        Assert.True(BattleHud.Covers(new BoardArea(feet.X - 10, feet.Y - 30, 20, 20), units, fit));
        Assert.False(BattleHud.Covers(new BoardArea(feet.X + 200, feet.Y, 20, 20), units, fit));
    }
}
