using ArcBox.Sdk;
using ArcBox.Sdk.Testing;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>A summon is played by its owner's phone while the owner is here, and by the companion AI when they aren't (#105).</summary>
public sealed class SummonControlTests
{
    private static readonly UnitId Hound = new("hero1.hound.7");
    private static readonly PlayerControlled Owner = new(new ProfileId("pr_member1"));
    private static readonly AiControlled Ai = new("companion");
    private static readonly string[] FourClasses = ["fighter", "cleric", "rogue", "wizard"];

    /// <summary>
    /// A battle saved with a hound of hero1 (player 0) whose turn it is, resumed with the players in <paramref name="here"/> back (they
    /// continue as soon as they can).
    /// </summary>
    private static Session HoundTurn(Controller? houndControl = null, params int[] here)
    {
        var s = new Session();
        s.ToBattle();
        var doc = TacticsDocument.Read(s.Lease.Document);
        var state = doc.Battle!.State;
        var owner = state.Units[new UnitId("hero1")];
        var taken = state.Units.Values.Select(u => u.Pos).ToHashSet();
        var at = Enumerable.Range(0, 12).SelectMany(x => Enumerable.Range(0, 12).Select(y => new Tile(x, y))).First(t => !taken.Contains(t));
        var sheet = owner.Sheet with { Name = "Hound" };
        var hound = owner with
        {
            Id = Hound, Sheet = sheet, SummonedBy = owner.Id, Pos = at, Economy = Economy.Fresh(sheet), Controller = houndControl ?? Owner,
        };
        state = state with
        {
            Units = state.Units.Add(Hound, hound), Order = [Hound, .. state.Order], TurnIndex = 0, Pending = null,
        };
        var saved = doc with { Battle = doc.Battle with { State = state } };
        var resumed = new Session(document: saved.ToJson());
        resumed.Harness.Resume(resumed.Lease);
        var connectedSeats = here.Length == 0 ? [0, 1, 2, 3] : here;
        foreach (var p in connectedSeats)
            resumed.Harness.SetConnected(p, true);
        if (resumed.Game.Phase == TacticsPhase.Resuming)
            resumed.Send(here.Length == 0 ? 0 : here[0], "continue");
        return resumed;
    }

    /// <summary>Lets the AI's paced steps pass until the hound's turn is over (or the budget runs out).</summary>
    private static void PastHoundTurn(Session s)
    {
        for (var i = 0; i < 100 && s.Game.Document.Battle!.State.Active == Hound; i++)
            s.Harness.Advance(TimeSpan.FromSeconds(7));
    }

    private static UnitState HoundOf(Session s) => s.Game.Document.Battle!.State.Units[Hound];

    [Fact]
    public void The_owners_phone_gets_the_hounds_turn_and_takes_its_command()
    {
        var s = HoundTurn();

        var screen = s.Screen(0)!;
        Assert.Equal(TacticsGame.Screens.Turn, screen.Screen);
        Assert.Equal("Hound", screen.Data.GetProperty("me").GetProperty("name").GetString());
        Assert.Equal(Hound.Value, screen.Data.GetProperty("me").GetProperty("id").GetString());
        Assert.True(screen.Data.GetProperty("summoned").GetBoolean());
        Assert.Contains("turn", s.Harness.ActiveTimers);
        Assert.NotEqual(TacticsGame.Screens.Turn, s.Screen(1)?.Screen);

        s.Send(0, "endTurn");

        Assert.NotEqual(Hound, s.Game.Document.Battle!.State.Active);
    }

    [Fact]
    public void A_timed_out_hound_is_played_by_the_ai()
    {
        var s = HoundTurn();
        Assert.Equal(Hound, s.Game.Document.Battle!.State.Active);

        s.Harness.Advance(TimeSpan.FromSeconds(61));
        PastHoundTurn(s);

        Assert.NotEqual(Hound, s.Game.Document.Battle!.State.Active);
    }

    [Fact]
    public void The_hound_follows_its_owner_off_the_phone_and_back()
    {
        var s = HoundTurn();
        Assert.Equal(Owner, HoundOf(s).Controller);

        s.Harness.SetConnected(0, false);
        Assert.Equal(Ai, HoundOf(s).Controller);

        s.Harness.SetConnected(0, true);
        Assert.Equal(Owner, HoundOf(s).Controller);
    }

    [Fact]
    public void A_hound_made_while_its_owner_is_away_acts_at_once_and_is_handed_back_when_they_return()
    {
        var s = HoundTurn(Ai, 1, 2, 3);

        Assert.DoesNotContain("turn", s.Harness.ActiveTimers);
        PastHoundTurn(s);
        Assert.NotEqual(Hound, s.Game.Document.Battle!.State.Active);
        Assert.Equal(Ai, HoundOf(s).Controller);

        s.Harness.SetConnected(0, true);
        Assert.Equal(Owner, HoundOf(s).Controller);
    }

    /// <summary>The real path: a present level-3 Beastmaster, whose hound the battle itself summons from the content.</summary>
    private static Session BeastmasterBattle()
    {
        var rules = Rules;
        var first = new Session();
        first.Harness.StartCampaign(first.Lease);
        for (var i = 0; i < 4; i++)
            first.Send(i, "pickClass", new { @class = i == 0 ? "ranger" : FourClasses[i] });
        var doc = TacticsDocument.Read(first.Lease.Document);
        var key = first.Profiles[0]!.Value.Value;
        var ranger = SheetBuilder.DefaultCharacter("Rue", "human", "ranger", "ranger.beastmaster", 3, rules);
        doc = doc with { Members = doc.Members.SetItem(key, doc.Members[key] with { Character = ranger }) };

        var s = new Session(document: doc.ToJson());
        s.Harness.StartCampaign(s.Lease);
        s.Send(0, "battles");
        s.Send(0, "pickEncounter", new { encounter = "river_crossing" });
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        if (s.Host.Battle!.Stage is not null)
            s.Harness.HostSignal("shown", new { seq = s.Host.Battle.Beats[^1].Seq });
        return s;
    }

    private static UnitState? RealHound(Session s)
        => s.Game.Document.Battle!.State.Units.Values.FirstOrDefault(u => u.SummonedBy == new UnitId("hero1"));

    [Fact]
    public void A_real_beastmasters_hound_is_the_owners_to_play_with_its_own_abilities()
    {
        var s = BeastmasterBattle();
        var hound = RealHound(s);
        Assert.NotNull(hound);
        Assert.Equal(new UnitId("hero1"), hound.SummonedBy);
        Assert.Equal(Owner, hound.Controller);

        // Play the others' turns (and the foes') until the hound's comes up on its owner's phone.
        CustomView? screen = null;
        for (var step = 0; step < 400 && screen is null; step++)
        {
            var state = s.Game.Document.Battle!.State;
            if (state.Outcome is not null)
                break;
            if (state.Active == hound.Id && s.Screen(0) is { Screen: TacticsGame.Screens.Turn } mine
                && mine.Data.GetProperty("summoned").GetBoolean())
                screen = mine;
            else if (Enumerable.Range(0, 4).FirstOrDefault(p => s.Screen(p) is { Screen: TacticsGame.Screens.Turn }, -1) is var p and >= 0)
                s.Send(p, "endTurn");
            else
                s.Harness.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.NotNull(screen);
        var data = screen.Data;
        Assert.Equal(hound.Id.Value, data.GetProperty("me").GetProperty("id").GetString());
        Assert.NotEmpty(data.GetProperty("choices").GetProperty("abilities").EnumerateArray());
        s.Harness.Advance(TimeSpan.FromSeconds(7));     // the TV's pacing for the beat before it is over: the turn clock runs
        Assert.Contains("turn", s.Harness.ActiveTimers);

        s.Send(0, "endTurn");
        Assert.NotEqual(hound.Id, s.Game.Document.Battle!.State.Active);
    }
}
