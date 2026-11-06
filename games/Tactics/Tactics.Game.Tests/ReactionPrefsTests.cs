using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>Per-player reaction preferences and the shorter clock once a turn's Move and Action are spent (#113).</summary>
public sealed class ReactionPrefsTests
{
    private static readonly UnitId Fighter = new("hero1");
    private static readonly UnitId Wizard = new("hero4");
    private static readonly UnitId Hound = new("hero1.hound.7");
    private static readonly AbilityId ShieldId = new("wizard.shield");

    private static ReactionChoice Pass => new(Reactions.Pass, null, null);

    /// <summary>A battle saved mid-way with <paramref name="shape"/> applied to its state, resumed with all four players back.</summary>
    private static Session Resumed(Func<BattleState, BattleState> shape, ReactionPrefs? fighter = null, ReactionPrefs? wizard = null)
    {
        var s = new Session();
        s.ToBattle();
        var doc = TacticsDocument.Read(s.Lease.Document);
        var members = doc.Members;
        if (fighter is not null)
            members = members.SetItem("pr_member1", members["pr_member1"] with { Reactions = fighter });
        if (wizard is not null)
            members = members.SetItem("pr_member4", members["pr_member4"] with { Reactions = wizard });
        var saved = doc with { Members = members, Battle = doc.Battle! with { State = shape(doc.Battle.State) } };
        var resumed = new Session(document: saved.ToJson());
        resumed.Harness.Resume(resumed.Lease);
        for (var p = 0; p < 4; p++)
            resumed.Harness.SetConnected(p, true);
        if (resumed.Game.Phase == TacticsPhase.Resuming)
            resumed.Send(0, "continue");
        return resumed;
    }

    private static BattleState State(Session s) => s.Game.Document.Battle!.State;

    private static UnitState FirstFoe(BattleState state) => state.Units.Values.First(u => !u.Id.Value.StartsWith("hero"));

    private static Tile FreeBeside(BattleState state, Tile at)
    {
        var taken = state.Units.Values.Select(u => u.Pos).ToHashSet();
        return new[] { new Tile(at.X + 1, at.Y), new Tile(at.X - 1, at.Y), new Tile(at.X, at.Y + 1), new Tile(at.X, at.Y - 1) }
            .First(t => t.X >= 0 && t.Y >= 0 && !taken.Contains(t));
    }

    /// <summary>The foe's turn, <paramref name="reactor"/> beside it, and an opportunity attack on offer.</summary>
    private static BattleState OpportunityOffer(BattleState state, UnitId reactor)
    {
        var foe = FirstFoe(state);
        var unit = state.Units[reactor];
        var offer = new ReactionOffer(reactor, [Pass, new ReactionChoice(Reactions.Opportunity, RulesPack.CommonAttack.Id, foe.Id)], "leavesReach",
            new ReactionTrigger(ReactionTriggerKind.LeavesReach, foe.Id, null, null, null, new Tile(foe.Pos.X + 5, foe.Pos.Y)));
        return state with
        {
            Units = state.Units.SetItem(reactor, unit with { Pos = FreeBeside(state, foe.Pos), Economy = unit.Economy with { Reaction = true } }),
            TurnIndex = state.Order.IndexOf(foe.Id), Queue = [], Pending = offer,
        };
    }

    /// <summary>The foe's hit on the wizard, <paramref name="overAc"/> over its AC, with Shield (+5 AC) on offer.</summary>
    private static Func<BattleState, BattleState> ShieldOffer(int overAc) => state =>
    {
        var foe = FirstFoe(state);
        var wizard = state.Units[Wizard];
        var sheet = wizard.Sheet with { Abilities = wizard.Sheet.Abilities.Contains(ShieldId) ? wizard.Sheet.Abilities : wizard.Sheet.Abilities.Add(ShieldId) };
        var roll = new RollResult([10], 10, 3, wizard.Sheet.Ac + overAc, wizard.Sheet.Ac, true, false, false);
        var offer = new ReactionOffer(Wizard, [Pass, new ReactionChoice(ShieldId.Value, ShieldId, Wizard)], "beforeHit",
            new ReactionTrigger(ReactionTriggerKind.BeforeHit, foe.Id, Wizard, RulesPack.CommonAttack.Id, roll));
        return state with
        {
            Units = state.Units.SetItem(Wizard, wizard with { Sheet = sheet, Economy = wizard.Economy with { Reaction = true } }),
            TurnIndex = state.Order.IndexOf(foe.Id), Queue = [], Pending = offer,
        };
    };

    private static bool Prompted(Session s, int player) => s.Screen(player)?.Screen == TacticsGame.Screens.React;

    [Fact]
    public void Ask_keeps_todays_prompt_and_its_clock()
    {
        var s = Resumed(st => OpportunityOffer(st, Fighter));

        Assert.True(Prompted(s, 0));
        Assert.IsType<ReactionOffer>(State(s).Pending);
        Assert.Contains("react", s.Harness.ActiveTimers);
    }

    [Fact]
    public void Always_takes_the_opportunity_attack_with_no_prompt_and_no_pause()
    {
        var s = Resumed(st => OpportunityOffer(st, Fighter), new ReactionPrefs(OpportunityPref.Always, DefencePref.Ask));

        Assert.False(Prompted(s, 0));
        Assert.DoesNotContain("react", s.Harness.ActiveTimers);
        Assert.Null(State(s).Pending);
        Assert.False(State(s).Units[Fighter].Economy.Reaction);     // the attack was made: the Reaction is spent
    }

    [Fact]
    public void Never_declines_the_opportunity_attack_with_no_prompt()
    {
        var s = Resumed(st => OpportunityOffer(st, Fighter), new ReactionPrefs(OpportunityPref.Never, DefencePref.Ask));

        Assert.False(Prompted(s, 0));
        Assert.Null(State(s).Pending);
        Assert.True(State(s).Units[Fighter].Economy.Reaction);     // kept
    }

    [Fact]
    public void An_opportunity_setting_does_not_answer_a_defensive_offer()
    {
        var s = Resumed(ShieldOffer(2), wizard: new ReactionPrefs(OpportunityPref.Always, DefencePref.Ask));

        Assert.True(Prompted(s, 3));
    }

    [Fact]
    public void Miss_only_casts_shield_when_the_roll_then_misses()
    {
        var s = Resumed(ShieldOffer(2), wizard: new ReactionPrefs(OpportunityPref.Ask, DefencePref.Miss));

        Assert.False(Prompted(s, 3));
        Assert.Null(State(s).Pending);
        Assert.False(State(s).Units[Wizard].Economy.Reaction);     // Shield was cast
    }

    [Fact]
    public void Miss_only_declines_shield_without_a_prompt_when_the_hit_would_land_anyway()
    {
        var s = Resumed(ShieldOffer(5), wizard: new ReactionPrefs(OpportunityPref.Ask, DefencePref.Miss));     // AC + 5 still hits

        Assert.False(Prompted(s, 3));
        Assert.Null(State(s).Pending);
        Assert.True(State(s).Units[Wizard].Economy.Reaction);     // not spent
    }

    [Fact]
    public void Never_declines_shield_without_a_prompt()
    {
        var s = Resumed(ShieldOffer(2), wizard: new ReactionPrefs(OpportunityPref.Ask, DefencePref.Never));

        Assert.False(Prompted(s, 3));
        Assert.Null(State(s).Pending);
        Assert.True(State(s).Units[Wizard].Economy.Reaction);
    }

    [Fact]
    public void A_summons_reaction_follows_its_owners_preferences()
    {
        BattleState WithHound(BattleState st)
        {
            var owner = st.Units[Fighter];
            var sheet = owner.Sheet with { Name = "Hound" };
            var hound = owner with { Id = Hound, Sheet = sheet, SummonedBy = Fighter, Economy = Economy.Fresh(sheet) };
            var shaped = OpportunityOffer(st with { Units = st.Units.Add(Hound, hound), Order = [.. st.Order, Hound] }, Hound);
            return shaped with { TurnIndex = st.Order.IndexOf(FirstFoe(st).Id) };
        }

        var always = Resumed(WithHound, new ReactionPrefs(OpportunityPref.Always, DefencePref.Ask));
        Assert.False(Prompted(always, 0));
        Assert.False(State(always).Units[Hound].Economy.Reaction);

        var ask = Resumed(WithHound);
        Assert.True(Prompted(ask, 0));
    }

    [Fact]
    public void Preferences_are_set_from_the_phone_and_saved_on_the_member()
    {
        var s = Resumed(st => st);
        Assert.Null(TacticsDocument.Read(s.Lease.Document).Members["pr_member1"].Reactions);

        s.Send(0, "reactionPrefs", new { opportunity = "always", defence = "miss" });

        Assert.Equal(new ReactionPrefs(OpportunityPref.Always, DefencePref.Miss), TacticsDocument.Read(s.Lease.Document).Members["pr_member1"].Reactions);
        Assert.Null(TacticsDocument.Read(s.Lease.Document).Members["pr_member2"].Reactions);
        s.Send(0, "reactionPrefs", new { opportunity = "nonsense" });     // nonsense changes nothing
        Assert.Equal(new ReactionPrefs(OpportunityPref.Always, DefencePref.Miss), TacticsDocument.Read(s.Lease.Document).Members["pr_member1"].Reactions);
    }

    [Fact]
    public void A_prompt_is_answered_at_once_by_a_new_choice_and_the_screen_shows_the_choices()
    {
        var s = Resumed(st => OpportunityOffer(st, Fighter));
        Assert.True(Prompted(s, 0));

        s.Send(0, "reactionPrefs", new { opportunity = "never" });

        Assert.Null(State(s).Pending);
        var status = s.Screen(0)!;
        Assert.DoesNotContain("react", s.Harness.ActiveTimers);
        Assert.Equal("never", status.Data.GetProperty("reactions").GetProperty("opportunity").GetString());
        Assert.Equal("ask", status.Data.GetProperty("reactions").GetProperty("defence").GetString());
    }

    [Fact]
    public void An_old_member_record_reads_as_ask_and_ask_is_not_written()
    {
        var doc = TacticsDocument.Read(new Session().Lease.Document);
        var character = SheetBuilder.DefaultCharacter("Ana", "dwarf", "cleric", null, 1, Rules);
        var plain = new MemberRecord(character, 0);
        Assert.Null(plain.Reactions);
        Assert.DoesNotContain("reactions", System.Text.Json.JsonSerializer.Serialize(plain, ArcJson.Options));

        var set = plain with { Reactions = new ReactionPrefs(OpportunityPref.Never, DefencePref.Miss) };
        var again = TacticsDocument.Read((doc with { Members = doc.Members.SetItem("pr_x", set) }).ToJson()).Members["pr_x"];
        Assert.Equal(set.Reactions, again.Reactions);
    }

    // ---- The clock ----------------------------------------------------------------------------------------------------

    private static BattleState HeroTurn(BattleState state, bool moveSpent, bool actionSpent)
    {
        var hero = state.Units[Fighter];
        var fresh = Economy.Fresh(hero.Sheet);
        var economy = fresh with
        {
            MoveLeft = moveSpent ? 0 : fresh.MoveLeft, Action = !actionSpent, ExtraActions = actionSpent ? 0 : fresh.ExtraActions,
        };
        return state with
        {
            Units = state.Units.SetItem(Fighter, hero with { Economy = economy }), TurnIndex = state.Order.IndexOf(Fighter), Queue = [], Pending = null,
        };
    }

    [Fact]
    public void A_turn_with_move_and_action_spent_runs_fifteen_seconds()
    {
        var s = Resumed(st => HeroTurn(st, moveSpent: true, actionSpent: true));
        Assert.Equal(Fighter, State(s).Active);

        s.Harness.Advance(TimeSpan.FromSeconds(14));
        Assert.Equal(Fighter, State(s).Active);

        s.Harness.Advance(TimeSpan.FromSeconds(2));
        Assert.NotEqual(Fighter, State(s).Active);
    }

    [Fact]
    public void A_fresh_turn_still_runs_a_minute()
    {
        var s = Resumed(st => HeroTurn(st, moveSpent: false, actionSpent: false));

        s.Harness.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(Fighter, State(s).Active);
        Assert.Contains("turn", s.Harness.ActiveTimers);
    }

    [Fact]
    public void A_turn_with_only_the_action_spent_runs_a_minute()
    {
        var s = Resumed(st => HeroTurn(st, moveSpent: false, actionSpent: true));

        s.Harness.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(Fighter, State(s).Active);
    }

    private static object MovePath(Session s)
    {
        var state = State(s);
        var left = state.Units[Fighter].Economy.MoveLeft;
        var move = Choices.For(state, Fighter, Rules).Moves.First(m => m.Cost == left);
        return new { path = move.Path.Select(t => new[] { t.X, t.Y }).ToArray() };
    }

    [Fact]
    public void Spending_the_last_move_after_the_action_cuts_the_clock_to_fifteen_seconds()
    {
        var s = Resumed(st => HeroTurn(st, moveSpent: false, actionSpent: true));
        s.Harness.Advance(TimeSpan.FromSeconds(30));     // 30 s left

        s.Send(0, "move", MovePath(s));
        Assert.Equal(0, State(s).Units[Fighter].Economy.MoveLeft);
        Assert.Equal(Fighter, State(s).Active);

        s.Harness.Advance(TimeSpan.FromSeconds(14));
        Assert.Equal(Fighter, State(s).Active);
        s.Harness.Advance(TimeSpan.FromSeconds(2));
        Assert.NotEqual(Fighter, State(s).Active);
    }

    [Fact]
    public void Having_walked_after_acting_counts_as_the_move_spent_even_with_some_left()
    {
        var s = Resumed(st => HeroTurn(st, moveSpent: false, actionSpent: true));
        s.Harness.Advance(TimeSpan.FromSeconds(10));
        var one = Choices.For(State(s), Fighter, Rules).Moves.OrderBy(m => m.Cost).First();

        s.Send(0, "move", new { path = one.Path.Select(t => new[] { t.X, t.Y }).ToArray() });
        Assert.True(State(s).Units[Fighter].Economy.MoveLeft > 0);

        s.Harness.Advance(TimeSpan.FromSeconds(14));
        Assert.Equal(Fighter, State(s).Active);
        s.Harness.Advance(TimeSpan.FromSeconds(8));     // time's up at 15 s, and the AI then finishes what's left (walks, ends the turn)
        Assert.NotEqual(Fighter, State(s).Active);
    }

    [Fact]
    public void The_short_clock_never_lengthens_what_is_left()
    {
        var s = Resumed(st => HeroTurn(st, moveSpent: false, actionSpent: true));
        s.Harness.Advance(TimeSpan.FromSeconds(52));     // 8 s left

        s.Send(0, "move", MovePath(s));
        s.Harness.Advance(TimeSpan.FromSeconds(9));

        Assert.NotEqual(Fighter, State(s).Active);
    }
}
