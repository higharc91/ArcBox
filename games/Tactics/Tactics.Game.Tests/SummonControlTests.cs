using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>A summon is played by its owner's phone while the owner is here, and by the companion AI when they aren't (#105).</summary>
public sealed class SummonControlTests
{
    private static readonly UnitId Hound = new("hero1.hound.7");
    private static readonly PlayerControlled Owner = new(new ProfileId("pr_member1"));
    private static readonly AiControlled Ai = new("companion");

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
        foreach (var p in here.DefaultIfEmpty(0).Concat(here.Length == 0 ? [1, 2, 3] : []))
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
}
